using System.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Tests;

public interface ITick
{
    int N { get; }
}

public readonly record struct Tick(int N) : ITick;

public class PublisherTests
{
    private sealed class TickListener(CallLog calls)
    {
        [ApplicationModuleListener]
        public void Background(Tick evt) => calls.Record("background", evt);

        [ApplicationModuleListener]
        public void ByInterface(ITick evt) => calls.Record("interface", evt);
    }

    private sealed class CompletedOnly(CallLog calls)
    {
        [ApplicationModuleListener]
        public void Background(OrderCompleted evt) => calls.Record("background", evt);
    }

    [Fact]
    public async Task Rejects_a_null_event()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<CompletedOnly>());
        await using var scope = app.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.PublishAsync<object>(null!));
    }

    [Fact]
    public async Task Value_type_events_reach_listeners_for_the_type_and_its_interfaces()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<TickListener>());

        await app.PublishAsync(new Tick(7));

        await Eventually.AssertAsync(() => app.Calls.All.Count == 2, "both listeners ran");
        Assert.All(app.Calls.All, c => Assert.Equal(new Tick(7), c.Event));
        Assert.Equal(["background", "interface"], app.Calls.Listeners.Order());
    }

    [Fact]
    public async Task Events_without_listeners_create_no_publications()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<CompletedOnly>());

        await app.PublishAsync(new Ping(1));
        await Task.Delay(100);

        Assert.Empty(app.Calls.All);
        Assert.Empty(await app.Incomplete.FindAllAsync());
        Assert.Empty(await app.Completed.FindAllAsync());
    }

    [Fact]
    public async Task Listeners_are_matched_by_the_runtime_type_not_the_static_type()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<CompletedOnly>());
        await using var scope = app.Services.CreateAsyncScope();
        OrderEvent evt = new OrderCompleted(Guid.NewGuid());

        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(evt);

        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "the listener ran");
    }

    [Fact]
    public async Task Every_listener_receives_the_same_event_instance()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<CompletedOnly>());
        var evt = new OrderCompleted(Guid.NewGuid());

        await app.PublishAsync(evt);

        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "the listener ran");
        Assert.Same(evt, Assert.Single(app.Calls.All).Event);
        Assert.Same(evt, Assert.Single(await app.Completed.FindAllAsync()).Event);
    }

    [Fact]
    public async Task All_events_published_in_one_transaction_are_delivered_after_commit()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<CompletedOnly>());

        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            for (var i = 0; i < 5; i++)
            {
                await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
            }

            await Task.Delay(100);
            Assert.DoesNotContain("background", app.Calls.Listeners);
            transaction.Complete();
        }

        await Eventually.AssertAsync(() => app.Calls.Listeners.Count(l => l == "background") == 5, "all five were delivered");
    }

    [Fact]
    public async Task A_suppressed_scope_inside_a_transaction_delivers_immediately()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<CompletedOnly>());

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            {
                await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
            }

            // Delivered while the outer transaction is still open, and its rollback doesn't undo it.
            await Eventually.AssertAsync(() => app.Calls.Listeners.Contains("background"), "delivered before the outer commit");
        }

        await Task.Delay(100);
        Assert.Single(await app.Completed.FindAllAsync());
    }
}
