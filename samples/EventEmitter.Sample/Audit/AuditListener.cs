using Codefinity.EventEmitter.Sample.Orders;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Sample.Audit;

public sealed class AuditListener(ILogger<AuditListener> logger)
{
    // Synchronous: runs inside OrderService's transaction, before PublishAsync returns.
    // IOrderEvent is an interface, so this receives OrderCompleted and OrderCancelled.
    [EventListener]
    public void On(IOrderEvent evt) =>
        logger.LogInformation("Audited {Event} for {OrderId}", evt.GetType().Name, evt.OrderId);
}
