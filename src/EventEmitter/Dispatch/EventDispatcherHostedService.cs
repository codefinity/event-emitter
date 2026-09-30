using EventEmitter.Listeners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventEmitter.Dispatch;

/// <summary>
/// Runs <see cref="ApplicationModuleListenerAttribute"/> listeners on background workers, each in its own DI scope.
/// </summary>
internal sealed class EventDispatcherHostedService(
    AsyncEventDispatcher dispatcher,
    ListenerRegistry registry,
    IEventPublicationRepository repository,
    IServiceScopeFactory scopeFactory,
    IOptions<EventEmitterOptions> options,
    TimeProvider time,
    ILogger<EventDispatcherHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task[] _workers = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.RepublishOutstandingEventsOnStartup)
        {
            var outstanding = await repository.FindIncompleteAsync(cancellationToken).ConfigureAwait(false);
            var republished = outstanding.Count(dispatcher.TryEnqueue);
            if (republished > 0)
            {
                logger.LogInformation("Republished {Count} outstanding event publication(s).", republished);
            }
        }

        _workers = Enumerable.Range(0, options.Value.MaxDegreeOfParallelism)
            .Select(_ => Task.Run(RunWorkerAsync, CancellationToken.None))
            .ToArray();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        dispatcher.Complete();

        var drained = Task.WhenAll(_workers);
        await Task.WhenAny(drained, Task.Delay(options.Value.ShutdownTimeout, cancellationToken)).ConfigureAwait(false);

        if (!drained.IsCompleted)
        {
            logger.LogWarning(
                "Event listeners did not finish within the shutdown timeout; cancelling them. Unfinished publications stay incomplete.");
            await _stopping.CancelAsync().ConfigureAwait(false);

            // Let cancelled listeners record their failure, unless the host itself stops waiting.
            await Task.WhenAny(drained, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
    }

    public void Dispose() => _stopping.Dispose();

    private async Task RunWorkerAsync()
    {
        await foreach (var publication in dispatcher.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_stopping.IsCancellationRequested)
            {
                dispatcher.Release(publication.Id);
                continue;
            }

            await ProcessAsync(publication).ConfigureAwait(false);
        }
    }

    private async Task ProcessAsync(EventPublication publication)
    {
        var failure = await InvokeListenerAsync(publication).ConfigureAwait(false);

        try
        {
            if (failure is not null)
            {
                // Release first, so the publication can be resubmitted as soon as it shows up as failed.
                dispatcher.Release(publication.Id);
                await repository.MarkFailedAsync(publication.Id, failure.ToString()).ConfigureAwait(false);
            }
            else if (options.Value.CompletionMode == CompletionMode.Delete)
            {
                await repository.DeleteAsync([publication.Id]).ConfigureAwait(false);
            }
            else
            {
                await repository.MarkCompletedAsync(publication.Id, time.GetUtcNow()).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not update publication {PublicationId}.", publication.Id);
        }
        finally
        {
            // A delivered publication stays reserved until it is marked completed, so it can't be resubmitted.
            dispatcher.Release(publication.Id);
        }
    }

    /// <returns>The exception thrown by the listener, or <c>null</c> if it succeeded.</returns>
    private async Task<Exception?> InvokeListenerAsync(EventPublication publication)
    {
        try
        {
            if (!registry.TryGetById(publication.ListenerId, out var listener))
            {
                throw new InvalidOperationException($"No listener with id '{publication.ListenerId}' is registered.");
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var instance = scope.ServiceProvider.GetRequiredService(listener.ListenerType);
            await listener.Invoker(instance, publication.Event, _stopping.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Listener {ListenerId} failed to handle {EventType} (publication {PublicationId}); the publication stays incomplete.",
                publication.ListenerId,
                publication.EventType.Name,
                publication.Id);
            return ex;
        }
    }
}
