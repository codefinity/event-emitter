using Codefinity.EventEmitter.Sample.Infrastructure;
using Codefinity.EventEmitter.Sample.Inventory;
using Codefinity.EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Sample.Examples;

/// <summary>
/// The event parameter's type decides what a listener receives: an interface or base type matches
/// every event that implements or derives from it, and <see cref="object"/> matches everything.
/// </summary>
internal static class SupertypesExample
{
    private sealed class OrderTimeline(ILogger<OrderTimeline> logger)
    {
        [ApplicationModuleListener]
        public void On(IOrderEvent evt) =>
            logger.LogInformation("IOrderEvent listener got {Event} for {OrderId}", evt.GetType().Name, evt.OrderId);
    }

    private sealed class CancellationsOnly(ILogger<CancellationsOnly> logger)
    {
        [EventListener]
        public void On(OrderCancelled evt) =>
            logger.LogInformation("OrderCancelled listener got {Reason}", evt.Reason);
    }

    private sealed class EverythingListener(ILogger<EverythingListener> logger)
    {
        [EventListener]
        public void On(object evt) =>
            logger.LogInformation("object listener got {Event}", evt.GetType().Name);
    }

    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddEventEmitter()
            .AddListener<OrderTimeline>()
            .AddListener<CancellationsOnly>()
            .AddListener<EverythingListener>());

        app.Say("Publishing OrderCompleted (an IOrderEvent):");
        await app.PublishAsync(new OrderCompleted("order-1", "alice"));

        app.Say("Publishing OrderCancelled (an IOrderEvent):");
        await app.PublishAsync(new OrderCancelled("order-2", "out of stock"));

        app.Say("Publishing StockReserved (not an IOrderEvent):");
        await app.PublishAsync(new StockReserved("order-1", Items: 3));

        await app.WaitForCompletedAsync(2);
    }
}
