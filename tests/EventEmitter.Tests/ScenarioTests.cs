using System.Collections.Concurrent;
using Codefinity.EventEmitter.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Tests;

public class ScenarioTests
{
    public sealed record ShipmentPrepared(Guid OrderId, string TrackingNumber);

    private sealed class ShipmentStore
    {
        public ConcurrentDictionary<Guid, string> TrackingNumbers { get; } = new();
    }

    private sealed class ShippingListener(IEventPublisher events, ShipmentStore store)
    {
        [ApplicationModuleListener]
        public async Task On(OrderCompleted evt, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            var trackingNumber = $"TRK-{evt.OrderId.ToString()[..8]}";
            store.TrackingNumbers[evt.OrderId] = trackingNumber;
            await events.PublishAsync(new ShipmentPrepared(evt.OrderId, trackingNumber), cancellationToken);
        }
    }

    private static async Task<TestApp> StartAsync()
    {
        var app = await TestApp.StartAsync(
            b => b.AddListener<ShippingListener>(),
            services: s => s.AddEventEmitterTesting().AddSingleton<ShipmentStore>());
        app.Services.GetRequiredService<Scenario>().DefaultTimeout = TestTimeouts.SafetyNet;
        return app;
    }

    [Fact]
    public async Task Waits_for_an_event_published_by_an_async_listener()
    {
        await using var app = await StartAsync();
        var scenario = app.Services.GetRequiredService<Scenario>();
        var orderId = Guid.NewGuid();

        var shipment = await scenario
            .Stimulate(sp => sp.GetRequiredService<IEventPublisher>().PublishAsync(new OrderCompleted(orderId)))
            .AndWaitForEventOfType<ShipmentPrepared>()
            .Matching(e => e.OrderId == orderId)
            .ToArriveAsync();

        Assert.StartsWith("TRK-", shipment.TrackingNumber);
    }

    [Fact]
    public async Task Waits_for_a_state_change()
    {
        await using var app = await StartAsync();
        var scenario = app.Services.GetRequiredService<Scenario>();
        var orderId = Guid.NewGuid();

        var trackingNumber = await scenario
            .Publish(new OrderCompleted(orderId))
            .AndWaitForStateChange(sp => sp.GetRequiredService<ShipmentStore>().TrackingNumbers.GetValueOrDefault(orderId))
            .ToHappenAsync();

        Assert.StartsWith("TRK-", trackingNumber);
    }

    [Fact]
    public async Task Times_out_when_no_matching_event_arrives()
    {
        await using var app = await StartAsync();
        var scenario = app.Services.GetRequiredService<Scenario>();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => scenario
            .Publish(new OrderCompleted(Guid.NewGuid()))
            .AndWaitForEventOfType<ShipmentPrepared>()
            .Matching(_ => false)
            .ToArriveAsync(TimeSpan.FromMilliseconds(200)));

        Assert.Contains(nameof(ShipmentPrepared), ex.Message);
    }

    [Fact]
    public async Task PublishedEvents_records_every_event()
    {
        await using var app = await StartAsync();
        var published = app.Services.GetRequiredService<PublishedEvents>();
        var first = Guid.NewGuid();

        await app.PublishAsync(new OrderCompleted(first));
        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        await Eventually.AssertAsync(() => published.OfType<ShipmentPrepared>().Count == 2, "both shipments were prepared");

        Assert.Equal(2, published.OfType<OrderCompleted>().Count);
        Assert.Single(published.OfType<IOrderEvent>().Matching(e => e.OrderId == first));
        Assert.Single(published.OfType<ShipmentPrepared>().Matching(e => e.OrderId == first));
    }
}
