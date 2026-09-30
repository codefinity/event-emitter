using Codefinity.EventEmitter.Sample.Infrastructure;
using Codefinity.EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Sample.Examples;

/// <summary>
/// Synchronous listeners run in ascending <see cref="EventListenerAttribute.Order"/>, whatever order they
/// are declared or registered in.
/// </summary>
internal static class OrderingExample
{
    private sealed class CheckoutSteps(ILogger<CheckoutSteps> logger)
    {
        [EventListener(Order = 30)]
        public void Audit(OrderCompleted evt) => logger.LogInformation("3. Audit     (Order = 30)");

        [EventListener]
        public void Invoice(OrderCompleted evt) => logger.LogInformation("2. Invoice   (Order = 0, the default)");

        [EventListener(Order = -10)]
        public void Validate(OrderCompleted evt) => logger.LogInformation("1. Validate  (Order = -10)");
    }

    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddEventEmitter()
            .AddListener<CheckoutSteps>());

        app.Say("The methods are declared Audit, Invoice, Validate, but run by Order:");
        await app.PublishAsync(new OrderCompleted("order-1", "alice"));
    }
}
