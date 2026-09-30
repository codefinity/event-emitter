namespace EventEmitter.Sample.Orders;

// The Orders module's public events. Other modules may listen for them; nothing else in Orders is their business.

/// <summary>Implemented by every event the Orders module publishes.</summary>
public interface IOrderEvent
{
    string OrderId { get; }
}

public sealed record OrderCompleted(string OrderId, string CustomerId) : IOrderEvent;

public sealed record OrderCancelled(string OrderId, string Reason) : IOrderEvent;
