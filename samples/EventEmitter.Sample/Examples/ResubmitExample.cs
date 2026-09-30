using Codefinity.EventEmitter.Sample.Infrastructure;
using Codefinity.EventEmitter.Sample.Inventory;
using Codefinity.EventEmitter.Sample.Orders;
using Codefinity.EventEmitter.Sample.Shipping;
using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Sample.Examples;

/// <summary>
/// A module listener fails: the publication stays incomplete with its failure recorded, other listeners are
/// unaffected, and the publication can be inspected and resubmitted with IIncompleteEventPublications.
/// </summary>
internal static class ResubmitExample
{
    public static async Task RunAsync()
    {
        var clock = new ManualClock();
        await using var app = await ExampleApp.StartAsync(services => services
            .AddSingleton<TimeProvider>(clock)    // before AddEventEmitter(), so the library uses it
            .AddOrdersModule()
            .AddInventoryModule()
            .AddShippingModule());

        var carrier = app.Get<CarrierGateway>();
        var incomplete = app.Get<IIncompleteEventPublications>();
        var completed = app.Get<ICompletedEventPublications>();

        app.Say("The carrier API is down while order-1 is completed:");
        carrier.IsAvailable = false;
        await app.InScopeAsync(sp => sp.GetRequiredService<OrderService>().CompleteAsync("order-1", "alice"));
        await WaitForShippingAttemptsAsync(app, 1);

        await ShowAsync(app, "Inventory's publication completed; Shipping's is incomplete:");

        app.Say("ResubmitOlderThanAsync(5 minutes) skips it, because it was published just now:");
        app.Say($"  resubmitted: {await incomplete.ResubmitOlderThanAsync(TimeSpan.FromMinutes(5))}");

        clock.Advance(TimeSpan.FromMinutes(10));
        app.Say("Ten minutes later it qualifies, but the carrier is still down, so it fails again:");
        app.Say($"  resubmitted: {await incomplete.ResubmitOlderThanAsync(TimeSpan.FromMinutes(5))}");
        await WaitForShippingAttemptsAsync(app, 2);

        app.Say("The carrier is back. Resubmitting only Shipping's publications:");
        carrier.IsAvailable = true;
        var count = await incomplete.ResubmitAsync(p => p.ListenerId == "shipping.book-shipment");
        app.Say($"  resubmitted: {count}");
        await app.WaitUntilAsync(async () => (await incomplete.FindAllAsync()).Count == 0);

        await ShowAsync(app, "Everything has completed; attempts records each delivery:");
        app.Say($"Nothing left to resubmit: {await incomplete.ResubmitAsync(_ => true)}");

        async Task ShowAsync(ExampleApp app, string heading)
        {
            app.Say(heading);
            foreach (var p in await completed.FindAllAsync())
            {
                app.Say("  " + Describe.Publication(p));
            }

            foreach (var p in await incomplete.FindAllAsync())
            {
                app.Say("  " + Describe.Publication(p));
            }
        }
    }

    private static Task WaitForShippingAttemptsAsync(ExampleApp app, int attempts) =>
        app.WaitUntilAsync(async () => (await app.Get<IIncompleteEventPublications>().FindAllAsync())
            .Any(p => p.EventType == typeof(StockReserved) && p.Attempts == attempts));
}
