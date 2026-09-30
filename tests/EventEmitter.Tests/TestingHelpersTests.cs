using System.Collections;
using System.Diagnostics;
using Codefinity.EventEmitter.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Tests;

public class TestingHelpersTests
{
    private sealed class PingListener
    {
        [ApplicationModuleListener]
        public void On(Ping evt) { }
    }

    private sealed class EchoListener(IEventPublisher events)
    {
        // Publishes a Ping later, from a background worker.
        [ApplicationModuleListener]
        public async Task On(OrderCompleted evt, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            await events.PublishAsync(new Ping(99), cancellationToken);
        }
    }

    private static async Task<TestApp> StartAsync(Action<EventEmitterBuilder>? listeners = null)
    {
        var app = await TestApp.StartAsync(b => listeners?.Invoke(b), services: s => s.AddEventEmitterTesting());
        app.Services.GetRequiredService<Scenario>().DefaultTimeout = TestTimeouts.SafetyNet;
        return app;
    }

    private static async Task PublishAllAsync(IServiceProvider services, params object[] events)
    {
        var publisher = services.GetRequiredService<IEventPublisher>();
        foreach (var evt in events)
        {
            await publisher.PublishAsync(evt);
        }
    }

    [Fact]
    public async Task PublishedEvents_can_be_cleared()
    {
        await using var app = await StartAsync();
        var published = app.Services.GetRequiredService<PublishedEvents>();

        await app.PublishAsync(new Ping(1));
        Assert.Single(published.All);

        published.Clear();
        Assert.Empty(published.All);
    }

    [Fact]
    public async Task PublishedEvents_records_events_even_when_publishing_fails()
    {
        var repository = new ControllableRepository { FailCreate = true };
        await using var app = await StartAsync(b => b.AddListener<PingListener>().UsePublicationRepository(repository));

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(new Ping(1)));

        Assert.Equal(new Ping(1), Assert.Single(app.Services.GetRequiredService<PublishedEvents>().All));
    }

    [Fact]
    public async Task TypedPublishedEvents_is_a_read_only_list()
    {
        await using var app = await StartAsync();
        await app.PublishAsync(new Ping(1));
        await app.PublishAsync(new Ping(2));

        var pings = app.Services.GetRequiredService<PublishedEvents>().OfType<Ping>();

        Assert.Equal(2, pings.Count);
        Assert.Equal(new Ping(2), pings[1]);
        Assert.Equal([new Ping(1), new Ping(2)], pings);

        var nonGeneric = new List<object>();
        foreach (var item in (IEnumerable)pings)
        {
            nonGeneric.Add(item);
        }

        Assert.Equal([new Ping(1), new Ping(2)], nonGeneric);
    }

    [Fact]
    public async Task Registering_the_testing_helpers_twice_records_each_event_once()
    {
        await using var app = await TestApp.StartAsync(
            b => { },
            services: s => s.AddEventEmitterTesting().AddEventEmitterTesting());

        await app.PublishAsync(new Ping(1));

        Assert.Single(app.Services.GetRequiredService<PublishedEvents>().All);
    }

    [Fact]
    public async Task The_testing_helpers_can_be_registered_after_AddEventEmitter()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddEventEmitter();
        services.AddEventEmitterTesting();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(new Ping(1));

        Assert.Equal(new Ping(1), Assert.Single(provider.GetRequiredService<PublishedEvents>().All));
    }

    [Fact]
    public async Task ToArriveAsync_finds_an_event_published_during_the_stimulus()
    {
        await using var app = await StartAsync();

        var ping = await app.Services.GetRequiredService<Scenario>()
            .Publish(new Ping(1))
            .AndWaitForEventOfType<Ping>()
            .ToArriveAsync();

        Assert.Equal(new Ping(1), ping);
    }

    [Fact]
    public async Task ToArriveAsync_waits_for_an_event_published_later()
    {
        await using var app = await StartAsync(b => b.AddListener<EchoListener>());

        var ping = await app.Services.GetRequiredService<Scenario>()
            .Publish(new OrderCompleted(Guid.NewGuid()))
            .AndWaitForEventOfType<Ping>()
            .ToArriveAsync();

        Assert.Equal(new Ping(99), ping);
    }

    [Fact]
    public async Task ToArriveAsync_ignores_events_published_before_the_stimulus()
    {
        await using var app = await StartAsync();
        await app.PublishAsync(new Ping(1));

        await Assert.ThrowsAsync<TimeoutException>(() => app.Services.GetRequiredService<Scenario>()
            .Stimulate(_ => Task.CompletedTask)
            .AndWaitForEventOfType<Ping>()
            .ToArriveAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Chained_Matching_calls_must_all_pass()
    {
        await using var app = await StartAsync();

        var ping = await app.Services.GetRequiredService<Scenario>()
            .Stimulate(sp => PublishAllAsync(sp, new Ping(1), new Ping(2), new Ping(3)))
            .AndWaitForEventOfType<Ping>()
            .Matching(p => p.N > 1)
            .Matching(p => p.N < 3)
            .ToArriveAsync();

        Assert.Equal(new Ping(2), ping);
    }

    [Fact]
    public async Task An_exception_in_a_Matching_predicate_is_rethrown()
    {
        await using var app = await StartAsync(b => b.AddListener<EchoListener>());
        var scenario = app.Services.GetRequiredService<Scenario>();

        // Once for an event already published, once for one that arrives while waiting.
        var fromHistory = await Assert.ThrowsAsync<InvalidOperationException>(() => scenario
            .Publish(new Ping(1))
            .AndWaitForEventOfType<Ping>()
            .Matching(_ => throw new InvalidOperationException("bad predicate"))
            .ToArriveAsync());

        var whileWaiting = await Assert.ThrowsAsync<InvalidOperationException>(() => scenario
            .Publish(new OrderCompleted(Guid.NewGuid()))
            .AndWaitForEventOfType<Ping>()
            .Matching(_ => throw new InvalidOperationException("bad predicate"))
            .ToArriveAsync());

        Assert.Equal("bad predicate", fromHistory.Message);
        Assert.Equal("bad predicate", whileWaiting.Message);
    }

    [Fact]
    public async Task DefaultTimeout_applies_when_no_timeout_is_given()
    {
        await using var app = await StartAsync();
        var scenario = app.Services.GetRequiredService<Scenario>();
        scenario.DefaultTimeout = TimeSpan.FromMilliseconds(150);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() => scenario
            .Stimulate(_ => Task.CompletedTask)
            .AndWaitForEventOfType<Ping>()
            .ToArriveAsync());

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Waiting_can_be_cancelled()
    {
        await using var app = await StartAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.Services.GetRequiredService<Scenario>()
            .Stimulate(_ => Task.CompletedTask)
            .AndWaitForEventOfType<Ping>()
            .ToArriveAsync(TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact]
    public async Task A_state_change_that_never_happens_times_out_with_the_last_value()
    {
        await using var app = await StartAsync();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => app.Services.GetRequiredService<Scenario>()
            .Stimulate(_ => Task.CompletedTask)
            .AndWaitForStateChange(_ => 41, acceptWhen: n => n == 42)
            .ToHappenAsync(TimeSpan.FromMilliseconds(100)));

        Assert.Contains("last value was '41'", ex.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task By_default_a_state_is_accepted_when_it_is_not_null_or_false(bool? state, bool accepted)
    {
        await using var app = await StartAsync();

        var wait = app.Services.GetRequiredService<Scenario>()
            .Stimulate(_ => Task.CompletedTask)
            .AndWaitForStateChange(_ => state)
            .ToHappenAsync(TimeSpan.FromMilliseconds(100));

        if (accepted)
        {
            Assert.Equal(state, await wait);
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() => wait);
        }
    }
}
