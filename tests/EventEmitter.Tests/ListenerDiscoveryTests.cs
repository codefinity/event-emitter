using Codefinity.EventEmitter.Listeners;
using Codefinity.EventEmitter.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Tests;

public class ListenerDiscoveryTests
{
    private sealed class ManyMethods(CallLog calls)
    {
        [ApplicationModuleListener]
        public void VoidMethod(Ping evt) => calls.Record(nameof(VoidMethod), evt);

        [ApplicationModuleListener]
        public Task TaskWithToken(Ping evt, CancellationToken cancellationToken)
        {
            calls.Record(nameof(TaskWithToken), evt);
            return Task.CompletedTask;
        }

        [ApplicationModuleListener]
        public Task<int> TaskOfT(Ping evt)
        {
            calls.Record(nameof(TaskOfT), evt);
            return Task.FromResult(evt.N);
        }

        [ApplicationModuleListener]
        private ValueTask PrivateValueTask(Ping evt)
        {
            calls.Record(nameof(PrivateValueTask), evt);
            return ValueTask.CompletedTask;
        }

        [ApplicationModuleListener]
        internal async Task InternalModuleMethod(Ping evt, CancellationToken cancellationToken)
        {
            await Task.Yield();
            calls.Record(nameof(InternalModuleMethod), evt);
        }

        [ApplicationModuleListener]
        private void PrivateModuleMethod(OrderCompleted evt) => calls.Record(nameof(PrivateModuleMethod), evt);
    }

    private sealed class DefaultId
    {
        [ApplicationModuleListener]
        public void On(OrderCompleted evt) => throw new InvalidOperationException("fail on purpose");
    }

    private sealed class NoParameters
    {
        [ApplicationModuleListener]
        public void On() { }
    }

    private sealed class WrongSecondParameter
    {
        [ApplicationModuleListener]
        public void On(Ping evt, string text) { }
    }

    private sealed class StaticMethod
    {
        [ApplicationModuleListener]
        public static void On(Ping evt) { }
    }

    private sealed class UnsupportedReturnType
    {
        [ApplicationModuleListener]
        public int On(Ping evt) => evt.N;
    }

    private sealed class RefParameter
    {
        [ApplicationModuleListener]
        public void On(ref Ping evt) { }
    }

    private sealed class NoListenerMethods
    {
        public void On(Ping evt) { }
    }

    private sealed class GenericMethod
    {
        [ApplicationModuleListener]
        public void On<T>(T evt) { }
    }

    private sealed class ThreeParameters
    {
        [ApplicationModuleListener]
        public void On(Ping evt, CancellationToken cancellationToken, int extra) { }
    }

    private sealed class InParameter
    {
        [ApplicationModuleListener]
        public void On(in Ping evt) { }
    }

    private sealed class OpenGeneric<T>
    {
        [ApplicationModuleListener]
        public void On(T evt) { }
    }

    private abstract class AbstractListener
    {
        [ApplicationModuleListener]
        public void On(Ping evt) { }
    }

    private sealed class FirstWithId
    {
        [ApplicationModuleListener(Id = "shared")]
        public void On(Ping evt) { }
    }

    private sealed class SecondWithId
    {
        [ApplicationModuleListener(Id = "shared")]
        public void On(Ping evt) { }
    }

    [Fact]
    public async Task Finds_every_supported_method_shape()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<ManyMethods>());

        await app.PublishAsync(new Ping(1));
        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));

        await Eventually.AssertAsync(() => app.Calls.All.Count == 6, "all six methods ran");
        Assert.Equal(
            ["InternalModuleMethod", "PrivateModuleMethod", "PrivateValueTask", "TaskOfT", "TaskWithToken", "VoidMethod"],
            app.Calls.Listeners.Order());
    }

    [Fact]
    public async Task Default_id_names_the_type_method_and_event()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<DefaultId>());

        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        await Eventually.AssertAsync(
            async () => (await app.Incomplete.FindAllAsync()).Any(p => p.Attempts == 1),
            "the failure was recorded");

        Assert.Equal(
            $"{typeof(DefaultId).FullName}.On({typeof(OrderCompleted).FullName})",
            Assert.Single(await app.Incomplete.FindAllAsync()).ListenerId);
    }

    [Theory]
    [InlineData(typeof(NoParameters), "must take the event as its first parameter")]
    [InlineData(typeof(WrongSecondParameter), "only a CancellationToken is allowed")]
    [InlineData(typeof(StaticMethod), "must be an instance method")]
    [InlineData(typeof(UnsupportedReturnType), "must return void, Task or ValueTask")]
    [InlineData(typeof(RefParameter), "by value")]
    [InlineData(typeof(NoListenerMethods), "has no methods marked")]
    [InlineData(typeof(AbstractListener), "must be a concrete, non-generic class")]
    [InlineData(typeof(GenericMethod), "must not be generic")]
    [InlineData(typeof(ThreeParameters), "must take the event as its first parameter")]
    [InlineData(typeof(InParameter), "by value")]
    [InlineData(typeof(OpenGeneric<>), "must be a concrete, non-generic class")]
    public void Invalid_listeners_are_rejected_at_registration(Type listenerType, string problem)
    {
        var builder = new ServiceCollection().AddEventEmitter();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddListener(listenerType));

        // Open generic types have no FullName; their name is still in the message.
        Assert.Contains(listenerType.FullName ?? listenerType.Name, ex.Message);
        Assert.Contains(problem, ex.Message);
    }

    [Fact]
    public void A_rejected_listener_leaves_nothing_registered()
    {
        var services = new ServiceCollection();
        var builder = services.AddEventEmitter();

        Assert.Throws<InvalidOperationException>(() => builder.AddListener<ThreeParameters>());

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ThreeParameters));
        var registry = (ListenerRegistry)services.Single(d => d.ServiceType == typeof(ListenerRegistry)).ImplementationInstance!;
        Assert.Empty(registry.ListenerTypes);
    }

    [Fact]
    public void Listener_ids_must_be_unique()
    {
        var builder = new ServiceCollection().AddEventEmitter().AddListener<FirstWithId>();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddListener<SecondWithId>());

        Assert.Contains("'shared'", ex.Message);
        Assert.Contains(typeof(FirstWithId).FullName!, ex.Message);
    }

    [Fact]
    public void Adding_the_same_listener_twice_registers_it_once()
    {
        var services = new ServiceCollection();
        services.AddEventEmitter().AddListener<FirstWithId>().AddListener<FirstWithId>();

        Assert.Single(services, d => d.ServiceType == typeof(FirstWithId));
    }

    [Fact]
    public void Assembly_scanning_skips_classes_without_listener_methods()
    {
        var services = new ServiceCollection();
        services.AddEventEmitter().AddListenersFromAssemblyContaining<PublishedEvents>();

        var registry = (ListenerRegistry)services.Single(d => d.ServiceType == typeof(ListenerRegistry)).ImplementationInstance!;
        Assert.Empty(registry.ListenerTypes);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(PublishedEvents));
    }

    [Fact]
    public void Assembly_scanning_validates_the_listeners_it_finds()
    {
        // This assembly holds the invalid listeners above, so scanning it must fail.
        var builder = new ServiceCollection().AddEventEmitter();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddListenersFromAssemblyContaining<ListenerDiscoveryTests>());

        Assert.StartsWith("Listener ", ex.Message);
    }
}
