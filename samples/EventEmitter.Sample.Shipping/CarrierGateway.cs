using System.Collections.Concurrent;

namespace EventEmitter.Sample.Shipping;

/// <summary>
/// Stands in for an external carrier API. Set <see cref="IsAvailable"/> to false to simulate an outage.
/// </summary>
public sealed class CarrierGateway
{
    private readonly ConcurrentDictionary<string, string> _shipments = new();

    public bool IsAvailable { get; set; } = true;

    /// <summary>Tracking numbers by order id.</summary>
    public IReadOnlyDictionary<string, string> Shipments => _shipments;

    internal string Book(string orderId)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Carrier API is unavailable.");
        }

        return _shipments[orderId] = $"TRK-{orderId}";
    }
}
