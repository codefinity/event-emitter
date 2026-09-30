using Codefinity.EventEmitter.Sample.Infrastructure;
using Codefinity.EventEmitter.Sample.Inventory;
using Codefinity.EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Sample.Examples;

/// <summary>
/// An exception from an <see cref="EventListenerAttribute"/> listener comes out of PublishAsync. The publisher's
/// transaction rolls back, and module listeners never see the event.
/// </summary>
internal static class SynchronousFailureExample
{
    private sealed class FraudCheck(ILogger<FraudCheck> logger)
    {
        [EventListener(Order = -100)]
        public void On(OrderCompleted evt)
        {
            if (evt.CustomerId == "mallory")
            {
                throw new InvalidOperationException($"Fraud check rejected {evt.OrderId}.");
            }

            logger.LogInformation("{OrderId} passed the fraud check", evt.OrderId);
        }
    }

    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddOrdersModule()
            .AddInventoryModule()
            .AddEventEmitter()
            .AddListener<FraudCheck>());

        app.Say("alice's order passes the check:");
        await app.InScopeAsync(sp => sp.GetRequiredService<OrderService>().CompleteAsync("order-1", "alice"));
        await app.WaitForCompletedAsync(1);

        app.Say("mallory's order fails it:");
        try
        {
            await app.InScopeAsync(sp => sp.GetRequiredService<OrderService>().CompleteAsync("order-2", "mallory"));
        }
        catch (InvalidOperationException ex)
        {
            app.Say($"OrderService.CompleteAsync threw: {ex.Message}");
        }

        await Task.Delay(300);
        var completed = await app.Get<ICompletedEventPublications>().FindAllAsync();
        var incomplete = await app.Get<IIncompleteEventPublications>().FindAllAsync();
        app.Say($"Inventory handled {completed.Count} order(s); {incomplete.Count} publication(s) exist for order-2.");
    }
}
