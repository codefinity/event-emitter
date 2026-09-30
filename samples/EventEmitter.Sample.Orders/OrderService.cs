using System.Transactions;
using Microsoft.Extensions.Logging;

namespace EventEmitter.Sample.Orders;

public sealed class OrderService(IEventPublisher events, ILogger<OrderService> logger)
{
    public async Task CompleteAsync(string orderId, string customerId, bool failBeforeCommit = false)
    {
        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        logger.LogInformation("Completing {OrderId} for {CustomerId}", orderId, customerId);

        // ...save the order here...
        await events.PublishAsync(new OrderCompleted(orderId, customerId));

        if (failBeforeCommit)
        {
            throw new InvalidOperationException($"Payment for {orderId} was declined.");
        }

        transaction.Complete();
        logger.LogInformation("Committed {OrderId}", orderId);
    }

    public async Task CancelAsync(string orderId, string reason)
    {
        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        logger.LogInformation("Cancelling {OrderId}: {Reason}", orderId, reason);
        await events.PublishAsync(new OrderCancelled(orderId, reason));

        transaction.Complete();
    }
}
