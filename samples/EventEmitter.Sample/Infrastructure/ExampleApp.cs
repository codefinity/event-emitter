using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Codefinity.EventEmitter.Sample.Infrastructure;

/// <summary>
/// A running Generic Host for one example, with helpers to publish, narrate and wait.
/// </summary>
internal sealed class ExampleApp : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IHost _host;
    private bool _stopped;

    private ExampleApp(IHost host)
    {
        _host = host;
        Log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Example");
    }

    public IServiceProvider Services => _host.Services;

    public ILogger Log { get; }

    public static async Task<ExampleApp> StartAsync(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging
            .AddConsole(o => o.FormatterName = ShortConsoleFormatter.FormatterName)
            .AddConsoleFormatter<ShortConsoleFormatter, ConsoleFormatterOptions>()
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Microsoft", LogLevel.Warning);

        configure(builder.Services);

        var host = builder.Build();
        await host.StartAsync();
        return new ExampleApp(host);
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Writes a line of narration, in order with the listeners' log output.</summary>
    public void Say(string message) => Log.LogInformation("{Message}", message);

    /// <summary>Runs <paramref name="action"/> in a new DI scope, like one web request.</summary>
    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public Task PublishAsync(object evt) =>
        InScopeAsync(sp => sp.GetRequiredService<IEventPublisher>().PublishAsync(evt));

    public async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The example did not reach the expected state in time.");
            }

            await Task.Delay(20);
        }
    }

    public Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null) =>
        WaitUntilAsync(() => Task.FromResult(condition()), timeout);

    /// <summary>Waits until at least <paramref name="count"/> publications have completed.</summary>
    public Task WaitForCompletedAsync(int count) =>
        WaitUntilAsync(async () => (await Get<ICompletedEventPublications>().FindAllAsync()).Count >= count);

    /// <summary>Stops the host. Queued module listeners get up to the shutdown timeout to finish.</summary>
    public async Task StopAsync()
    {
        if (!_stopped)
        {
            _stopped = true;
            await _host.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _host.Dispose();
    }
}
