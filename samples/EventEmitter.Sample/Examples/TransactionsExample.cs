using EventEmitter.Sample.Audit;
using EventEmitter.Sample.Infrastructure;
using EventEmitter.Sample.Inventory;
using EventEmitter.Sample.Orders;
using EventEmitter.Sample.Shipping;
using Microsoft.Extensions.DependencyInjection;

namespace EventEmitter.Sample.Examples;

/// <summary>
/// OrderService publishes inside a TransactionScope. Module listeners run only if it commits;
/// synchronous listeners run inside it either way.
/// </summary>
internal static class TransactionsExample
{
    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddOrdersModule()
            .AddInventoryModule()
            .AddShippingModule()
            .AddEventEmitter()
            .AddListener<AuditListener>());

        var carrier = app.Get<CarrierGateway>();

        app.Say("order-1 commits:");
        await app.InScopeAsync(sp => sp.GetRequiredService<OrderService>().CompleteAsync("order-1", "alice"));
        await app.WaitUntilAsync(() => carrier.Shipments.ContainsKey("order-1"));

        app.Say("order-2 publishes OrderCompleted, then payment fails and the transaction rolls back:");
        try
        {
            await app.InScopeAsync(sp => sp.GetRequiredService<OrderService>()
                .CompleteAsync("order-2", "bob", failBeforeCommit: true));
        }
        catch (InvalidOperationException ex)
        {
            app.Say($"Caught: {ex.Message}");
        }

        await Task.Delay(300);
        var incomplete = await app.Get<IIncompleteEventPublications>().FindAllAsync();

        app.Say("The audit ran inside the transaction, but Inventory and Shipping never heard about order-2:");
        app.Say($"  shipment for order-2: {(carrier.Shipments.ContainsKey("order-2") ? "yes" : "none")}");
        app.Say($"  publications left waiting: {incomplete.Count} (the rollback deleted them)");
    }
}
