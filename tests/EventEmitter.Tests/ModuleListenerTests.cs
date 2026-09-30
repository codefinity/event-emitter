using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Tests;

public class ModuleListenerTests
{
    private sealed class Gate
    {
        public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedListener(CallLog calls, ScopeMarker scope, Gate gate)
    {
        [ApplicationModuleListener]
        public async Task On(OrderCompleted evt, CancellationToken cancellationToken)
        {
            await gate.Open.Task.WaitAsync(cancellationToken);
            calls.Record("gated", evt, scope.Id);
        }
    }

    private sealed class InventoryListener(CallLog calls)
    {
        [ApplicationModuleListener(Id = "inventory")]
        public Task On(OrderCompleted evt)
        {
            calls.Record("inventory", evt);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingShippingListener
    {
        [ApplicationModuleListener(Id = "shipping")]
        public void On(OrderCompleted evt) => throw new InvalidOperationException("carrier unavailable");
    }

    private sealed class SupertypeListener(CallLog calls)
    {
        [ApplicationModuleListener]
        public void OnAnyOrderEvent(IOrderEvent evt) => calls.Record("interface", evt);

        [ApplicationModuleListener]
        public void OnBaseRecord(OrderEvent evt) => calls.Record("base", evt);

        [ApplicationModuleListener]
        public void OnCancelledOnly(OrderCancelled evt) => calls.Record("cancelled-only", evt);
    }

    [Fact]
    public async Task Runs_in_the_background_in_a_new_scope()
    {
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<GatedListener>(),
            services: s => s.AddSingleton<Gate>());
        await using var scope = app.Services.CreateAsyncScope();
        var evt = new OrderCompleted(Guid.NewGuid());

        // Returns while the listener is still blocked on the gate.
        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(evt);
        Assert.Empty(app.Calls.All);

        app.Services.GetRequiredService<Gate>().Open.SetResult();
        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "the listener ran");

        var call = app.Calls.All[0];
        Assert.Equal(evt, call.Event);
        Assert.NotEqual(scope.ServiceProvider.GetRequiredService<ScopeMarker>().Id, call.ScopeId);
    }

    [Fact]
    public async Task Each_listener_gets_its_own_publication()
    {
        await using var app = await TestApp.StartAsync(b => b
            .AddListener<InventoryListener>()
            .AddListener<FailingShippingListener>());

        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));

        await Eventually.AssertAsync(
            async () => (await app.Incomplete.FindAllAsync()).Any(p => p.LastFailure is not null),
            "the shipping failure was recorded");

        var failed = Assert.Single(await app.Incomplete.FindAllAsync());
        Assert.Equal("shipping", failed.ListenerId);
        Assert.Contains("carrier unavailable", failed.LastFailure);
        Assert.Equal(1, failed.Attempts);

        await Eventually.AssertAsync(async () => (await app.Completed.FindAllAsync()).Count == 1, "inventory completed");
        Assert.Equal("inventory", Assert.Single(await app.Completed.FindAllAsync()).ListenerId);
        Assert.Equal(["inventory"], app.Calls.Listeners);
    }

    [Fact]
    public async Task Listeners_for_a_supertype_receive_subtypes()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<SupertypeListener>());

        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        await app.PublishAsync(new OrderCancelled(Guid.NewGuid()));

        await Eventually.AssertAsync(() => app.Calls.All.Count == 5, "all listeners ran");
        Assert.Equal(2, app.Calls.Listeners.Count(l => l == "interface"));
        Assert.Equal(2, app.Calls.Listeners.Count(l => l == "base"));
        Assert.IsType<OrderCancelled>(Assert.Single(app.Calls.All, c => c.Listener == "cancelled-only").Event);
    }
}
