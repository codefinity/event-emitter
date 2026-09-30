using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Tests;

public class PublicationRegistryTests
{
    private sealed class FlakySwitch
    {
        public volatile bool Fail = true;
    }

    private sealed class FlakyListener(CallLog calls, FlakySwitch flaky)
    {
        [ApplicationModuleListener(Id = "flaky")]
        public void On(OrderCompleted evt)
        {
            if (flaky.Fail)
            {
                throw new InvalidOperationException("temporarily unavailable");
            }

            calls.Record("flaky", evt);
        }
    }

    private sealed class InventoryListener(CallLog calls)
    {
        [ApplicationModuleListener(Id = "inventory")]
        public void On(OrderCompleted evt) => calls.Record("inventory", evt);
    }

    [Fact]
    public async Task Failed_publications_can_be_resubmitted()
    {
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<FlakyListener>(),
            services: s => s.AddSingleton<FlakySwitch>());

        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        await Eventually.AssertAsync(
            async () => (await app.Incomplete.FindAllAsync()).Any(p => p.Attempts == 1),
            "the failure was recorded");

        var failed = Assert.Single(await app.Incomplete.FindAllAsync());
        Assert.Contains("temporarily unavailable", failed.LastFailure);

        app.Services.GetRequiredService<FlakySwitch>().Fail = false;
        Assert.Equal(1, await app.Incomplete.ResubmitAsync(p => p.ListenerId == "flaky"));

        await Eventually.AssertAsync(async () => (await app.Incomplete.FindAllAsync()).Count == 0, "the resubmission completed");
        var completed = Assert.Single(await app.Completed.FindAllAsync());
        Assert.Equal(failed.Id, completed.Id);
        Assert.Equal(2, completed.Attempts);
        Assert.NotNull(completed.CompletionDate);
        Assert.Equal(["flaky"], app.Calls.Listeners);
        Assert.Equal(0, await app.Incomplete.ResubmitAsync(_ => true));
    }

    [Fact]
    public async Task Resubmits_only_publications_older_than_the_given_age()
    {
        var clock = new ManualTimeProvider();
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<FlakyListener>(),
            services: s => s.AddSingleton<FlakySwitch>().AddSingleton<TimeProvider>(clock));

        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        await Eventually.AssertAsync(
            async () => (await app.Incomplete.FindAllAsync()).Any(p => p.Attempts == 1),
            "the failure was recorded");
        app.Services.GetRequiredService<FlakySwitch>().Fail = false;

        Assert.Equal(0, await app.Incomplete.ResubmitOlderThanAsync(TimeSpan.FromMinutes(5)));

        clock.Now += TimeSpan.FromMinutes(10);
        Assert.Equal(1, await app.Incomplete.ResubmitOlderThanAsync(TimeSpan.FromMinutes(5)));
        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "the resubmission ran");
    }

    [Fact]
    public async Task Update_mode_keeps_completed_publications_until_deleted()
    {
        var clock = new ManualTimeProvider();
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<InventoryListener>(),
            o => o.CompletionMode = CompletionMode.Update,
            s => s.AddSingleton<TimeProvider>(clock));

        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        await Eventually.AssertAsync(async () => (await app.Completed.FindAllAsync()).Count == 1, "the publication completed");

        var completed = Assert.Single(await app.Completed.FindAllAsync());
        Assert.Equal(clock.Now, completed.CompletionDate);

        await app.Completed.DeletePublicationsOlderThanAsync(TimeSpan.FromDays(1));
        Assert.Single(await app.Completed.FindAllAsync());

        clock.Now += TimeSpan.FromDays(2);
        await app.Completed.DeletePublicationsOlderThanAsync(TimeSpan.FromDays(1));
        Assert.Empty(await app.Completed.FindAllAsync());
    }

    [Fact]
    public async Task Delete_mode_removes_completed_publications()
    {
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<InventoryListener>(),
            o => o.CompletionMode = CompletionMode.Delete);

        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "the listener ran");

        await Eventually.AssertAsync(async () => (await app.Incomplete.FindAllAsync()).Count == 0, "the publication was deleted");
        Assert.Empty(await app.Completed.FindAllAsync());
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Outstanding_publications_are_republished_on_startup_when_enabled(bool republish, int expectedCalls)
    {
        var repository = new InMemoryEventPublicationRepository();
        var evt = new OrderCompleted(Guid.NewGuid());
        await repository.CreateAsync([new EventPublication(Guid.NewGuid(), evt, evt.GetType(), "inventory", DateTimeOffset.UtcNow)]);

        await using var app = await TestApp.StartAsync(
            b => b.AddListener<InventoryListener>().UsePublicationRepository(repository),
            o => o.RepublishOutstandingEventsOnStartup = republish);

        await Task.Delay(100);
        if (republish)
        {
            await Eventually.AssertAsync(async () => (await repository.FindCompletedAsync()).Count == 1, "the publication completed");
        }

        Assert.Equal(expectedCalls, app.Calls.All.Count);
        Assert.Equal(1 - expectedCalls, (await repository.FindIncompleteAsync()).Count);
    }
}
