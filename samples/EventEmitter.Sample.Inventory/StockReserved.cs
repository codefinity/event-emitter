namespace EventEmitter.Sample.Inventory;

// Inventory's public event: published after stock for an order has been reserved.
public sealed record StockReserved(string OrderId, int Items);
