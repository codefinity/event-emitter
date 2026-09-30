using EventEmitter.Sample.Infrastructure;
using EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace EventEmitter.Sample.Examples;

/// <summary>
/// Listener ids (default and explicit), and the errors registration raises for invalid listener methods.
/// </summary>
internal static class ValidationExample
{
    private sealed class IdsListener
    {
        [ApplicationModuleListener]
        public void DefaultId(OrderCompleted evt) { }

        [ApplicationModuleListener(Id = "loyalty.award-points")]
        public void ExplicitId(OrderCompleted evt) { }
    }

    private sealed class NoParameters
    {
        [EventListener]
        public void On() { }
    }

    private sealed class ExtraParameter
    {
        [EventListener]
        public void On(OrderCompleted evt, string note) { }
    }

    private sealed class BothAttributes
    {
        [EventListener]
        [ApplicationModuleListener]
        public void On(OrderCompleted evt) { }
    }

    private sealed class StaticMethod
    {
        [EventListener]
        public static void On(OrderCompleted evt) { }
    }

    private sealed class ReturnsInt
    {
        [EventListener]
        public int On(OrderCompleted evt) => 0;
    }

    private sealed class NoListenerMethods
    {
        public void On(OrderCompleted evt) { }
    }

    private sealed class SameIdAgain
    {
        [EventListener(Id = "loyalty.award-points")]
        public void On(OrderCompleted evt) { }
    }

    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddEventEmitter()
            .AddListener<IdsListener>());

        app.Say("Listener ids stored with each publication:");
        await app.PublishAsync(new OrderCompleted("order-1", "alice"));
        await app.WaitForCompletedAsync(2);
        foreach (var p in await app.Get<ICompletedEventPublications>().FindAllAsync())
        {
            app.Say($"  {p.ListenerId}");
        }

        app.Say("Registering invalid listeners fails immediately, naming the method:");
        foreach (var invalid in new[]
                 {
                     typeof(NoParameters), typeof(ExtraParameter), typeof(BothAttributes), typeof(StaticMethod),
                     typeof(ReturnsInt), typeof(NoListenerMethods),
                 })
        {
            try
            {
                new ServiceCollection().AddEventEmitter().AddListener(invalid);
            }
            catch (InvalidOperationException ex)
            {
                app.Say($"  {invalid.Name}: {ex.Message}");
            }
        }

        try
        {
            new ServiceCollection().AddEventEmitter().AddListener<IdsListener>().AddListener<SameIdAgain>();
        }
        catch (InvalidOperationException ex)
        {
            app.Say($"  {nameof(SameIdAgain)}: {ex.Message}");
        }
    }
}
