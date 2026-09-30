using System.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codefinity.EventEmitter.Tests;

public class DispatcherRobustnessTests
{
    private sealed class MeteredListener(ConcurrencyMeter meter, CallLog calls)
    {
        [ApplicationModuleListener]
        public async Task On(Ping evt, CancellationToken cancellationToken)
        {
            using (meter.Enter())
            {
                await Task.Delay(50, cancellationToken);
            }

            calls.Record("metered", evt);
        }
    }

    private sealed class CountingListener(CallLog calls)
    {
        [ApplicationModuleListener]
        public void On(Ping evt) => calls.Record("background", evt);
    }

    private sealed class Gate
    {
        public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedListener(CallLog calls, Gate gate)
    {
        [ApplicationModuleListener(Id = "gated")]
        public async Task On(Ping evt, CancellationToken cancellationToken)
        {
            calls.Record("started", evt);
            await gate.Open.Task.WaitAsync(cancellationToken);
            calls.Record("finished", evt);
        }
    }

    private sealed class Switch
    {
        public volatile bool Fail = true;
    }

    private sealed class SwitchedListener(CallLog calls, Switch @switch)
    {
        [ApplicationModuleListener(Id = "switched")]
        public void On(Ping evt)
        {
            if (@switch.Fail)
            {
                throw new InvalidOperationException($"failing {evt.N}");
            }

            calls.Record("succeeded", evt);
        }
    }

    private sealed class InventoryListener(CallLog calls)
    {
        [ApplicationModuleListener(Id = "inventory")]
        public void Background(Ping evt) => calls.Record("background", evt);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Never_runs_more_module_listeners_at_once_than_MaxDegreeOfParallelism(int parallelism)
    {
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<MeteredListener>(),
            o => o.MaxDegreeOfParallelism = parallelism,
            s => s.AddSingleton<ConcurrencyMeter>());

        for (var i = 0; i < 12; i++)
        {
            await app.PublishAsync(new Ping(i));
        }

        await Eventually.AssertAsync(() => app.Calls.All.Count == 12, "all listeners ran");
        Assert.Equal(parallelism, app.Services.GetRequiredService<ConcurrencyMeter>().Max);
    }

    [Fact]
    public async Task Delivers_every_event_exactly_once_under_concurrent_publishing()
    {
        const int publishers = 10;
        const int eventsPerPublisher = 50;
        const int total = publishers * eventsPerPublisher;

        await using var app = await TestApp.StartAsync(
            b => b.AddListener<CountingListener>(),
            o => o.MaxDegreeOfParallelism = 8);

        await Task.WhenAll(Enumerable.Range(0, publishers).Select(p => Task.Run(async () =>
        {
            for (var i = 0; i < eventsPerPublisher; i++)
            {
                await app.PublishAsync(new Ping(p * eventsPerPublisher + i));
            }
        })));

        await Eventually.AssertAsync(async () => (await app.Completed.FindAllAsync()).Count == total, "every publication completed");

        var numbers = app.Calls.All.Select(c => ((Ping)c.Event).N).ToList();
        Assert.Equal(total, numbers.Count);
        Assert.Equal(total, numbers.Distinct().Count());

        Assert.Empty(await app.Incomplete.FindAllAsync());
        Assert.All(await app.Completed.FindAllAsync(), p => Assert.Equal(1, p.Attempts));
    }

    [Fact]
    public async Task Running_publications_are_not_resubmitted()
    {
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<GatedListener>(),
            services: s => s.AddSingleton<Gate>());

        await app.PublishAsync(new Ping(1));
        await Eventually.AssertAsync(() => app.Calls.Listeners.Contains("started"), "the listener started");

        Assert.Equal(0, await app.Incomplete.ResubmitAsync(_ => true));

        app.Services.GetRequiredService<Gate>().Open.SetResult();
        await Eventually.AssertAsync(async () => (await app.Completed.FindAllAsync()).Count == 1, "the publication completed");
        await Task.Delay(100);

        Assert.Equal(["started", "finished"], app.Calls.Listeners);
        Assert.Equal(1, Assert.Single(await app.Completed.FindAllAsync()).Attempts);
    }

    [Fact]
    public async Task A_resubmit_that_read_a_publication_while_it_was_running_does_not_deliver_it_again()
    {
        // Regression test: ResubmitAsync used to trust a query result that went stale while the query ran.
        var repository = new ControllableRepository();
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<GatedListener>().UsePublicationRepository(repository),
            services: s => s.AddSingleton<Gate>());

        await app.PublishAsync(new Ping(1));
        await Eventually.AssertAsync(() => app.Calls.Listeners.Contains("started"), "the listener started");

        // The resubmit's query sees the running publication as incomplete, and is slow to return.
        var slowQuery = repository.HoldNextFindIncomplete();
        var resubmit = app.Incomplete.ResubmitAsync(_ => true);

        // Meanwhile the delivery finishes and the publication completes.
        app.Services.GetRequiredService<Gate>().Open.SetResult();
        await Eventually.AssertAsync(async () => (await app.Completed.FindAllAsync()).Count == 1, "the publication completed");

        slowQuery.SetResult();
        Assert.Equal(0, await resubmit);

        await Task.Delay(100);
        Assert.Equal(["started", "finished"], app.Calls.Listeners);
    }

    [Fact]
    public async Task A_failed_publication_can_be_resubmitted_as_soon_as_it_shows_as_failed()
    {
        // Regression test: a failure used to be recorded before the publication was released, so a resubmit
        // that saw the failure could still find it reserved and skip it. The repository's slow acknowledgement
        // keeps that window open long enough to hit every time.
        var repository = new ControllableRepository { MarkFailedLatency = TimeSpan.FromMilliseconds(200) };
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<SwitchedListener>().UsePublicationRepository(repository),
            services: s => s.AddSingleton<Switch>());

        for (var i = 0; i < 5; i++)
        {
            await app.PublishAsync(new Ping(i));

            var failed = await PollAsync(async () => (await app.Incomplete.FindAllAsync())
                .SingleOrDefault(p => ((Ping)p.Event).N == i && p.Attempts == 1));

            Assert.Equal(1, await app.Incomplete.ResubmitAsync(p => p.Id == failed.Id));
            await PollAsync(async () => (await app.Incomplete.FindAllAsync()).SingleOrDefault(p => p.Id == failed.Id && p.Attempts == 2));
        }
    }

    [Fact]
    public async Task A_resubmit_that_fails_midway_releases_what_it_reserved()
    {
        var repository = new ControllableRepository();
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<SwitchedListener>().UsePublicationRepository(repository),
            services: s => s.AddSingleton<Switch>());

        await app.PublishAsync(new Ping(1));
        await Eventually.AssertAsync(
            async () => (await app.Incomplete.FindAllAsync()).Any(p => p.Attempts == 1),
            "the failure was recorded");

        // The resubmit's first query succeeds and reserves the publication; its second query fails.
        repository.FailFindIncompleteCall(2);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Incomplete.ResubmitAsync(_ => true));
        Assert.Contains("find incomplete", ex.Message);

        // Nothing is left reserved, so the next resubmit works.
        app.Services.GetRequiredService<Switch>().Fail = false;
        Assert.Equal(1, await app.Incomplete.ResubmitAsync(_ => true));
        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "the resubmission ran");
    }

    [Fact]
    public async Task Concurrent_resubmits_deliver_each_publication_once()
    {
        const int count = 20;
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<SwitchedListener>(),
            o => o.MaxDegreeOfParallelism = 4,
            s => s.AddSingleton<Switch>());

        for (var i = 0; i < count; i++)
        {
            await app.PublishAsync(new Ping(i));
        }

        await Eventually.AssertAsync(
            async () => (await app.Incomplete.FindAllAsync()).Count(p => p.Attempts == 1) == count,
            "every delivery failed once");

        app.Services.GetRequiredService<Switch>().Fail = false;
        var resubmitted = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => app.Incomplete.ResubmitAsync(_ => true))));

        Assert.Equal(count, resubmitted.Sum());
        await Eventually.AssertAsync(async () => (await app.Completed.FindAllAsync()).Count == count, "every resubmission completed");
        await Task.Delay(100);

        Assert.Equal(count, app.Calls.All.Count);
        Assert.Equal(count, app.Calls.All.Select(c => ((Ping)c.Event).N).Distinct().Count());
        Assert.All(await app.Completed.FindAllAsync(), p => Assert.Equal(2, p.Attempts));
    }

    [Fact]
    public async Task A_publication_for_an_unknown_listener_is_marked_failed()
    {
        var repository = new InMemoryEventPublicationRepository();
        await repository.CreateAsync([new EventPublication(Guid.NewGuid(), new Ping(1), typeof(Ping), "no-such-listener", DateTimeOffset.UtcNow)]);

        await using var app = await TestApp.StartAsync(
            b => b.AddListener<CountingListener>().UsePublicationRepository(repository),
            o => o.RepublishOutstandingEventsOnStartup = true);

        await Eventually.AssertAsync(
            async () => (await repository.FindIncompleteAsync()).Any(p => p.Attempts == 1),
            "the failure was recorded");

        Assert.Contains("No listener with id 'no-such-listener'", Assert.Single(await repository.FindIncompleteAsync()).LastFailure);
        Assert.Empty(app.Calls.All);
    }

    [Fact]
    public async Task A_repository_failure_while_creating_publications_fails_the_publish()
    {
        var repository = new ControllableRepository { FailCreate = true };
        await using var app = await TestApp.StartAsync(b => b.AddListener<InventoryListener>().UsePublicationRepository(repository));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(new Ping(1)));
        Assert.Contains("create", ex.Message);

        // No publication was stored, so the listener never runs.
        await Task.Delay(100);
        Assert.Empty(app.Calls.All);

        repository.FailCreate = false;
        await app.PublishAsync(new Ping(2));
        await Eventually.AssertAsync(() => app.Calls.Listeners.Contains("background"), "later publishes still work");
    }

    [Fact]
    public async Task A_repository_failure_after_delivery_is_logged_and_the_worker_keeps_going()
    {
        var repository = new ControllableRepository { FailMarkCompletedTimes = 1 };
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<InventoryListener>().UsePublicationRepository(repository),
            o => o.MaxDegreeOfParallelism = 1);

        await app.PublishAsync(new Ping(1));
        await app.PublishAsync(new Ping(2));

        await Eventually.AssertAsync(async () => (await repository.FindCompletedAsync()).Count == 1, "the second publication completed");
        Assert.Equal(2, app.Calls.Listeners.Count(l => l == "background"));
        Assert.Contains(app.Logs.AtLeast(LogLevel.Error), e => e.Message.Contains("Could not update publication"));
    }

    [Fact]
    public async Task A_repository_failure_while_discarding_a_rollback_is_logged()
    {
        var repository = new ControllableRepository { FailDelete = true };
        await using var app = await TestApp.StartAsync(b => b.AddListener<InventoryListener>().UsePublicationRepository(repository));

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await app.PublishAsync(new Ping(1));
        }

        await Eventually.AssertAsync(
            () => app.Logs.AtLeast(LogLevel.Error).Any(e => e.Message.Contains("rolled-back transaction")),
            "the failed cleanup was logged");
        await Task.Delay(100);
        Assert.DoesNotContain("background", app.Calls.Listeners);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    public async Task Invalid_options_stop_the_host_from_starting(int parallelism, int shutdownSeconds)
    {
        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => TestApp.StartAsync(
            b => b.AddListener<CountingListener>(),
            o =>
            {
                o.MaxDegreeOfParallelism = parallelism;
                o.ShutdownTimeout = TimeSpan.FromSeconds(shutdownSeconds);
            }));

        Assert.Contains(parallelism < 1 ? "MaxDegreeOfParallelism" : "ShutdownTimeout", ex.Message);
    }

    /// <summary>Polls as fast as possible, to give races the best chance to show.</summary>
    private static async Task<T> PollAsync<T>(Func<Task<T?>> probe) where T : class
    {
        var deadline = DateTime.UtcNow + TestTimeouts.SafetyNet;
        while (true)
        {
            if (await probe() is { } result)
            {
                return result;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out polling.");
            }

            await Task.Yield();
        }
    }
}
