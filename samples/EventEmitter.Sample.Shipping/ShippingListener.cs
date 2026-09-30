using EventEmitter.Sample.Inventory;
using Microsoft.Extensions.Logging;

namespace EventEmitter.Sample.Shipping;

internal sealed class ShippingListener(CarrierGateway carrier, ILogger<ShippingListener> logger)
{
    [ApplicationModuleListener(Id = "shipping.book-shipment")]
    public void On(StockReserved evt)
    {
        var trackingNumber = carrier.Book(evt.OrderId);
        logger.LogInformation("Booked shipment {TrackingNumber} for {OrderId} (event from {Assembly})",
            trackingNumber, evt.OrderId, evt.GetType().Assembly.GetName().Name);
    }
}
