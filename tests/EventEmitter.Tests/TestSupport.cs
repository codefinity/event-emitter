using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventEmitter.Tests;

public interface IOrderEvent
{
    Guid OrderId { get; }
}

public record OrderEvent(Guid OrderId) : IOrderEvent;

public sealed record OrderCompleted(Guid OrderId) : OrderEvent(OrderId);

public sealed record OrderCancelled(Guid OrderId) : OrderEvent(OrderId);

public sealed record Ping(int N);

internal sealed record Call(string Listener, object Event, Guid ScopeId);

/// <summary>Singleton that test listeners write to.</summary>
internal sealed class CallLog
{
    private readonly ConcurrentQueue<Call> _calls = new();

    public IReadOnlyList<Call> All => _calls.ToArray();

    public IReadOnlyList<string> Listeners => _calls.Select(c => c.Listener).ToArray();

    public void Record(string listener, object evt, Guid scopeId = default) => _calls.Enqueue(new Call(listener, evt, scopeId));
}

/// <summary>Scoped service that identifies the DI scope a listener ran in.</summary>
internal sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

internal sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>Captures every log entry, so tests can assert on the library's warnings and errors.</summary>
internal sealed class LogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> All => _entries.ToArray();

    public IReadOnlyList<LogEntry> AtLeast(LogLevel level) => All.Where(e => e.Level >= level).ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink._entries.Enqueue(new LogEntry(logLevel, category, formatter(state, exception), exception));
    }
}

/// <summary>Tracks how many callers are inside a region at once, and the highest that ever was.</summary>
internal sealed class ConcurrencyMeter
{
    private int _current;
    private int _max;

    public int Max => Volatile.Read(ref _max);

    public IDisposable Enter()
    {
        var now = Interlocked.Increment(ref _current);
        int max;
        while (now > (max = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, max) != max)
        {
        }

        return new Exit(this);
    }

    private sealed class Exit(ConcurrencyMeter meter) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref meter._current);
    }
}

/// <summary>A host with EventEmitter, a <see cref="CallLog"/> and a <see cref="ScopeMarker"/>.</summary>
internal sealed class TestApp : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _stopped;

    private TestApp(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public CallLog Calls => Services.GetRequiredService<CallLog>();

    public LogSink Logs => Services.GetRequiredService<LogSink>();

    public IEventPublicationRepository Repository => Services.GetRequiredService<IEventPublicationRepository>();

    public IIncompleteEventPublications Incomplete => Services.GetRequiredService<IIncompleteEventPublications>();

    public ICompletedEventPublications Completed => Services.GetRequiredService<ICompletedEventPublications>();

    public static async Task<TestApp> StartAsync(
        Action<EventEmitterBuilder> listeners,
        Action<EventEmitterOptions>? options = null,
        Action<IServiceCollection>? services = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        }));

        var logs = new LogSink();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton(logs);
        builder.Services.AddSingleton<CallLog>();
        builder.Services.AddScoped<ScopeMarker>();
        services?.Invoke(builder.Services);
        listeners(builder.Services.AddEventEmitter(options));

        var host = builder.Build();
        try
        {
            await host.StartAsync();
        }
        catch
        {
            host.Dispose();
            throw;
        }

        return new TestApp(host);
    }

    /// <summary>Stops the host: queued module listeners get up to the shutdown timeout to finish.</summary>
    public async Task StopAsync()
    {
        if (!_stopped)
        {
            _stopped = true;
            await _host.StopAsync();
        }
    }

    /// <summary>Publishes from a new DI scope, like a request would.</summary>
    public async Task PublishAsync(object evt)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(evt);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _host.Dispose();
    }
}

/// <summary>
/// How long a test waits for something that should happen. Passing tests return as soon as it happens, so this
/// only matters on a starved machine, where a short limit would report slowness as failure.
/// </summary>
internal static class TestTimeouts
{
    public static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(30);
}

internal static class Eventually
{
    private static readonly TimeSpan Timeout = TestTimeouts.SafetyNet;

    public static async Task AssertAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out waiting until {because}.");
            }

            await Task.Delay(10);
        }
    }

    public static Task AssertAsync(Func<bool> condition, string because) =>
        AssertAsync(() => Task.FromResult(condition()), because);
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
