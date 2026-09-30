namespace Codefinity.EventEmitter.Sample.Orders;

// The Orders module's public events, in their own assembly so other modules can listen for them
// without referencing the Orders module itself.

/// <summary>Implemented by every event the Orders module publishes.</summary>
public interface IOrderEvent
{
    string OrderId { get; }
}

public sealed record OrderCompleted(string OrderId, string CustomerId) : IOrderEvent;

public sealed record OrderCancelled(string OrderId, string Reason) : IOrderEvent;
