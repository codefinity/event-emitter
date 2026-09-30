using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Tests;

public class SynchronousListenerTests
{
    private sealed class AuditListener(CallLog calls, ScopeMarker scope)
    {
        [EventListener]
        public void On(Ping evt) => calls.Record("audit", evt, scope.Id);
    }

    private sealed class ThrowingListener
    {
        [EventListener]
        public async Task On(Ping evt)
        {
            await Task.Yield();
            throw new InvalidOperationException($"boom {evt.N}");
        }
    }

    private sealed class OrderedListener(CallLog calls)
    {
        [EventListener(Order = 2)]
        public void Second(Ping evt) => calls.Record("second", evt);

        [EventListener(Order = 1)]
        public void First(Ping evt) => calls.Record("first", evt);

        [EventListener(Order = 3)]
        public void Third(Ping evt) => calls.Record("third", evt);
    }

    private sealed class ModuleListener(CallLog calls)
    {
        [ApplicationModuleListener]
        public void On(Ping evt) => calls.Record("module", evt);
    }

    [Fact]
    public async Task Runs_inline_in_the_publishers_scope()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<AuditListener>());
        await using var scope = app.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(new Ping(1));

        var call = Assert.Single(app.Calls.All);
        Assert.Equal(new Ping(1), call.Event);
        Assert.Equal(scope.ServiceProvider.GetRequiredService<ScopeMarker>().Id, call.ScopeId);
    }

    [Fact]
    public async Task Exceptions_reach_the_publisher()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<ThrowingListener>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(new Ping(7)));

        Assert.Equal("boom 7", ex.Message);
    }

    [Fact]
    public async Task Run_in_order()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<OrderedListener>());

        await app.PublishAsync(new Ping(1));

        Assert.Equal(["first", "second", "third"], app.Calls.Listeners);
    }

    [Fact]
    public async Task A_failing_synchronous_listener_cancels_module_delivery()
    {
        await using var app = await TestApp.StartAsync(b => b
            .AddListener<ThrowingListener>()
            .AddListener<ModuleListener>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(new Ping(1)));
        await Task.Delay(100);

        Assert.Empty(app.Calls.All);
        Assert.Empty(await app.Incomplete.FindAllAsync());
    }

    [Fact]
    public async Task Events_without_listeners_are_ignored()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<AuditListener>());

        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));

        Assert.Empty(app.Calls.All);
    }
}
