using Codefinity.EventEmitter.Sample.Orders;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Sample.Inventory;

// Internal: no other assembly can call it. It is reached only through Orders' events.
internal sealed class InventoryListener(IEventPublisher events, ILogger<InventoryListener> logger)
{
    // Async, after commit, in its own DI scope.
    [ApplicationModuleListener(Id = "inventory.reserve-stock")]
    public async Task On(OrderCompleted evt, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        logger.LogInformation("Reserved stock for {OrderId} (event from {Assembly})",
            evt.OrderId, evt.GetType().Assembly.GetName().Name);

        // Listeners can publish too: this one hands over to the Shipping module.
        await events.PublishAsync(new StockReserved(evt.OrderId, Items: 3), cancellationToken);
    }

    [ApplicationModuleListener(Id = "inventory.release-stock")]
    public void On(OrderCancelled evt) =>
        logger.LogInformation("Released stock for {OrderId}", evt.OrderId);
}
