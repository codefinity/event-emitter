using EventEmitter.Sample.Audit;
using EventEmitter.Sample.Infrastructure;
using EventEmitter.Sample.Inventory;
using EventEmitter.Sample.Orders;
using EventEmitter.Sample.Shipping;
using Microsoft.Extensions.DependencyInjection;

namespace EventEmitter.Sample.Examples;

/// <summary>
/// An event travels Orders → Inventory → Shipping across three assemblies. No module references a
/// service of another module; each one only knows the event types it publishes or listens for.
/// </summary>
internal static class ModulesExample
{
    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddOrdersModule()       // EventEmitter.Sample.Orders.dll
            .AddInventoryModule()    // EventEmitter.Sample.Inventory.dll
            .AddShippingModule()     // EventEmitter.Sample.Shipping.dll
            .AddEventEmitter()
            .AddListener<AuditListener>());

        app.Say("Completing order-1. OrderService only has an IEventPublisher; it can't see Inventory or Shipping.");
        await app.InScopeAsync(sp => sp.GetRequiredService<OrderService>().CompleteAsync("order-1", "alice"));
        app.Say("CompleteAsync has returned. The audit ran inline on the same thread; the rest happens in the background:");

        var carrier = app.Get<CarrierGateway>();
        await app.WaitUntilAsync(() => carrier.Shipments.ContainsKey("order-1"));

        app.Say("OrderCompleted reached Inventory, whose StockReserved event reached Shipping.");
    }
}
