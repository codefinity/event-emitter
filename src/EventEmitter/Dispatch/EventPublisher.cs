using Codefinity.EventEmitter.Listeners;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Dispatch;

internal sealed class EventPublisher(
    ListenerRegistry registry,
    IEventPublicationRepository repository,
    AsyncEventDispatcher dispatcher,
    ITransactionSynchronization transactions,
    TimeProvider time,
    ILogger<EventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(evt);

        var eventType = evt.GetType();
        var listeners = registry.GetListeners(eventType);
        if (listeners.Length == 0)
        {
            return;
        }

        var publishedAt = time.GetUtcNow();
        var publications = listeners
            .Select(l => new EventPublication(Guid.NewGuid(), evt, eventType, l.Id, publishedAt))
            .ToArray();

        foreach (var publication in publications)
        {
            dispatcher.TryReserve(publication.Id);
        }

        try
        {
            await repository.CreateAsync(publications, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(publications);
            throw;
        }

        if (transactions.IsTransactionActive)
        {
            transactions.RegisterAfterCompletion(committed =>
            {
                if (committed)
                {
                    Enqueue(publications);
                }
                else
                {
                    Discard(publications);
                }
            });
        }
        else
        {
            Enqueue(publications);
        }
    }

    private void Enqueue(EventPublication[] publications)
    {
        foreach (var publication in publications)
        {
            if (!dispatcher.EnqueueReserved(publication))
            {
                logger.LogWarning(
                    "Event dispatch has stopped; publication {PublicationId} for listener {ListenerId} stays incomplete.",
                    publication.Id,
                    publication.ListenerId);
            }
        }
    }

    private void Discard(EventPublication[] publications)
    {
        Release(publications);
        _ = DeleteAsync();

        async Task DeleteAsync()
        {
            try
            {
                await repository.DeleteAsync(publications.Select(p => p.Id).ToArray()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not delete the publications of a rolled-back transaction.");
            }
        }
    }

    private void Release(EventPublication[] publications)
    {
        foreach (var publication in publications)
        {
            dispatcher.Release(publication.Id);
        }
    }
}
