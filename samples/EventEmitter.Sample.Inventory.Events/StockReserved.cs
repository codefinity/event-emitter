namespace Codefinity.EventEmitter.Sample.Inventory;

// Inventory's public event: published after stock for an order has been reserved.
// Kept in its own assembly so other modules can listen for it without referencing the Inventory module.
public sealed record StockReserved(string OrderId, int Items);
