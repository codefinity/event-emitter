using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Testing;

/// <summary>
/// Drives a module test the way Spring Modulith's <c>Scenario</c> does: run a stimulus, then wait for an event or a
/// state change caused by asynchronous listeners. Registered by <c>AddEventEmitterTesting()</c>.
/// </summary>
/// <example>
/// <code>
/// var shipment = await scenario
///     .Stimulate(sp => sp.GetRequiredService&lt;OrderService&gt;().CompleteAsync(orderId))
///     .AndWaitForEventOfType&lt;ShipmentPrepared&gt;()
///     .Matching(e => e.OrderId == orderId)
///     .ToArriveAsync();
/// </code>
/// </example>
public sealed class Scenario(IServiceScopeFactory scopeFactory, PublishedEvents events)
{
    /// <summary>How long to wait when no timeout is passed.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Sets the stimulus. It runs in a new DI scope when the expectation is awaited.</summary>
    public ScenarioStimulus Stimulate(Func<IServiceProvider, Task> stimulus)
    {
        ArgumentNullException.ThrowIfNull(stimulus);
        return new ScenarioStimulus(this, stimulus);
    }

    /// <summary>Uses publishing <paramref name="evt"/> as the stimulus.</summary>
    public ScenarioStimulus Publish(object evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return Stimulate(sp => sp.GetRequiredService<IEventPublisher>().PublishAsync(evt));
    }

    internal IServiceScopeFactory ScopeFactory => scopeFactory;

    internal PublishedEvents Events => events;

    internal async Task RunAsync(Func<IServiceProvider, Task> stimulus)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await stimulus(scope.ServiceProvider).ConfigureAwait(false);
    }
}

public sealed class ScenarioStimulus
{
    private readonly Scenario _scenario;
    private readonly Func<IServiceProvider, Task> _stimulus;

    internal ScenarioStimulus(Scenario scenario, Func<IServiceProvider, Task> stimulus)
    {
        _scenario = scenario;
        _stimulus = stimulus;
    }

    /// <summary>Expects an event of type <typeparamref name="TEvent"/> to be published after the stimulus runs.</summary>
    public ScenarioEventExpectation<TEvent> AndWaitForEventOfType<TEvent>() => new(_scenario, _stimulus, _ => true);

    /// <summary>
    /// Expects <paramref name="supplier"/> to return an accepted value after the stimulus runs. It is polled in a new
    /// DI scope each time. By default a value is accepted when it is non-null and not <c>false</c>.
    /// </summary>
    public ScenarioStateChange<TState> AndWaitForStateChange<TState>(
        Func<IServiceProvider, TState> supplier,
        Func<TState, bool>? acceptWhen = null)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        return new ScenarioStateChange<TState>(
            _scenario,
            _stimulus,
            supplier,
            acceptWhen ?? (value => value is not null && value is not false));
    }
}

public sealed class ScenarioEventExpectation<TEvent>
{
    private readonly Scenario _scenario;
    private readonly Func<IServiceProvider, Task> _stimulus;
    private readonly Func<TEvent, bool> _predicate;

    internal ScenarioEventExpectation(Scenario scenario, Func<IServiceProvider, Task> stimulus, Func<TEvent, bool> predicate)
    {
        _scenario = scenario;
        _stimulus = stimulus;
        _predicate = predicate;
    }

    /// <summary>Narrows the expectation to events that also match <paramref name="predicate"/>.</summary>
    public ScenarioEventExpectation<TEvent> Matching(Func<TEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var previous = _predicate;
        return new ScenarioEventExpectation<TEvent>(_scenario, _stimulus, e => previous(e) && predicate(e));
    }

    /// <summary>Runs the stimulus and waits for a matching event.</summary>
    /// <returns>The first matching event.</returns>
    /// <exception cref="TimeoutException">No matching event was published in time.</exception>
    public async Task<TEvent> ToArriveAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? _scenario.DefaultTimeout;
        var start = _scenario.Events.Count;

        await _scenario.RunAsync(_stimulus).ConfigureAwait(false);

        try
        {
            var evt = await _scenario.Events
                .WaitForAsync(start, e => e is TEvent typed && _predicate(typed), limit, cancellationToken)
                .ConfigureAwait(false);
            return (TEvent)evt;
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"No matching {typeof(TEvent).Name} event was published within {limit.TotalMilliseconds:0} ms.");
        }
    }
}

public sealed class ScenarioStateChange<TState>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly Scenario _scenario;
    private readonly Func<IServiceProvider, Task> _stimulus;
    private readonly Func<IServiceProvider, TState> _supplier;
    private readonly Func<TState, bool> _acceptWhen;

    internal ScenarioStateChange(
        Scenario scenario,
        Func<IServiceProvider, Task> stimulus,
        Func<IServiceProvider, TState> supplier,
        Func<TState, bool> acceptWhen)
    {
        _scenario = scenario;
        _stimulus = stimulus;
        _supplier = supplier;
        _acceptWhen = acceptWhen;
    }

    /// <summary>Runs the stimulus and polls until the state is accepted.</summary>
    /// <returns>The accepted state.</returns>
    /// <exception cref="TimeoutException">The state wasn't accepted in time.</exception>
    public async Task<TState> ToHappenAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? _scenario.DefaultTimeout;
        var deadline = DateTime.UtcNow + limit;

        await _scenario.RunAsync(_stimulus).ConfigureAwait(false);

        while (true)
        {
            TState state;
            using (var scope = _scenario.ScopeFactory.CreateScope())
            {
                state = _supplier(scope.ServiceProvider);
            }

            if (_acceptWhen(state))
            {
                return state;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"The expected state change did not happen within {limit.TotalMilliseconds:0} ms; last value was '{state}'.");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
