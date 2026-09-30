using Codefinity.EventEmitter.Sample.Inventory;
using Codefinity.EventEmitter.Sample.Orders;
using Codefinity.EventEmitter.Sample.Shipping;
using Codefinity.EventEmitter.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codefinity.EventEmitter.Sample.Tests;

/// <summary>
/// Integration tests for the Orders, Inventory and Shipping modules, using EventEmitter.Testing.
/// Each test gets its own host, so state doesn't leak between tests.
/// </summary>
public sealed class ShopModuleTests : IAsyncLifetime
{
    private IHost _host = null!;

    private Scenario Scenario => _host.Services.GetRequiredService<Scenario>();

    private PublishedEvents Published => _host.Services.GetRequiredService<PublishedEvents>();

    private CarrierGateway Carrier => _host.Services.GetRequiredService<CarrierGateway>();

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services
            .AddOrdersModule()
            .AddInventoryModule()
            .AddShippingModule()
            .AddEventEmitterTesting();

        _host = builder.Build();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Completing_an_order_reserves_stock()
    {
        // Wait for an event published by another assembly's background listener.
        var reserved = await Scenario
            .Stimulate(sp => sp.GetRequiredService<OrderService>().CompleteAsync("order-1", "alice"))
            .AndWaitForEventOfType<StockReserved>()
            .Matching(e => e.OrderId == "order-1")
            .ToArriveAsync();

        Assert.Equal(3, reserved.Items);
    }

    [Fact]
    public async Task Completing_an_order_books_a_shipment()
    {
        // Wait for state changed two modules away.
        var trackingNumber = await Scenario
            .Stimulate(sp => sp.GetRequiredService<OrderService>().CompleteAsync("order-1", "alice"))
            .AndWaitForStateChange(sp => sp.GetRequiredService<CarrierGateway>().Shipments.GetValueOrDefault("order-1"))
            .ToHappenAsync();

        Assert.Equal("TRK-order-1", trackingNumber);
    }

    [Fact]
    public async Task Inventory_reacts_to_an_OrderCompleted_published_directly()
    {
        // Publish the event itself as the stimulus, bypassing OrderService.
        await Scenario
            .Publish(new OrderCompleted("order-5", "dave"))
            .AndWaitForEventOfType<StockReserved>()
            .Matching(e => e.OrderId == "order-5")
            .ToArriveAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Waits_for_a_custom_condition()
    {
        var shipmentCount = await Scenario
            .Publish(new OrderCompleted("order-6", "erin"))
            .AndWaitForStateChange(
                sp => sp.GetRequiredService<CarrierGateway>().Shipments.Count,
                acceptWhen: count => count >= 1)
            .ToHappenAsync();

        Assert.Equal(1, shipmentCount);
    }

    [Fact]
    public async Task A_rolled_back_order_reaches_no_other_module()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<OrderService>()
                .CompleteAsync("order-2", "bob", failBeforeCommit: true));
        }

        // Waiting for StockReserved must time out: Inventory never saw the event.
        await Assert.ThrowsAsync<TimeoutException>(() => Scenario
            .Stimulate(_ => Task.CompletedTask)
            .AndWaitForEventOfType<StockReserved>()
            .ToArriveAsync(TimeSpan.FromMilliseconds(300)));

        // OrderCompleted was published (and recorded) before the rollback.
        Assert.Single(Published.OfType<OrderCompleted>().Matching(e => e.OrderId == "order-2"));
        Assert.Empty(Published.OfType<StockReserved>());
    }

    [Fact]
    public async Task Cancelling_an_order_publishes_an_order_event()
    {
        await Scenario
            .Stimulate(sp => sp.GetRequiredService<OrderService>().CancelAsync("order-3", "changed mind"))
            .AndWaitForEventOfType<IOrderEvent>()
            .Matching(e => e is OrderCancelled { Reason: "changed mind" })
            .ToArriveAsync();
    }

    [Fact]
    public async Task Failed_shipments_can_be_resubmitted()
    {
        var incomplete = _host.Services.GetRequiredService<IIncompleteEventPublications>();
        Carrier.IsAvailable = false;

        await Scenario
            .Stimulate(sp => sp.GetRequiredService<OrderService>().CompleteAsync("order-4", "carol"))
            .AndWaitForStateChange(_ => HasFailedShipment(incomplete))
            .ToHappenAsync();

        Carrier.IsAvailable = true;

        // The stimulus can be any async call, here the resubmit itself.
        var trackingNumber = await Scenario
            .Stimulate(sp => sp.GetRequiredService<IIncompleteEventPublications>()
                .ResubmitAsync(p => p.ListenerId == "shipping.book-shipment"))
            .AndWaitForStateChange(sp => sp.GetRequiredService<CarrierGateway>().Shipments.GetValueOrDefault("order-4"))
            .ToHappenAsync();

        Assert.Equal("TRK-order-4", trackingNumber);
    }

    // The in-memory repository completes synchronously, so this doesn't block.
    private static bool HasFailedShipment(IIncompleteEventPublications incomplete) =>
        incomplete.FindAllAsync().Result.Any(p => p.ListenerId == "shipping.book-shipment" && p.Attempts == 1);
}
