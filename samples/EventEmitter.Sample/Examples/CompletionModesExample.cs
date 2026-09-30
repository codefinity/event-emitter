using EventEmitter.Sample.Infrastructure;
using EventEmitter.Sample.Inventory;
using EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace EventEmitter.Sample.Examples;

/// <summary>
/// What happens to publications after their listener succeeds: kept with a completion date
/// (<see cref="CompletionMode.Update"/>, cleaned up by age) or deleted (<see cref="CompletionMode.Delete"/>).
/// </summary>
internal static class CompletionModesExample
{
    public static async Task RunAsync()
    {
        await UpdateModeAsync();
        await DeleteModeAsync();
    }

    private static async Task UpdateModeAsync()
    {
        var clock = new ManualClock();
        await using var app = await ExampleApp.StartAsync(services => services
            .AddSingleton<TimeProvider>(clock)
            .AddInventoryModule()
            .AddEventEmitter(o => o.CompletionMode = CompletionMode.Update));   // the default

        var completed = app.Get<ICompletedEventPublications>();

        app.Say("CompletionMode.Update: completed publications are kept.");
        await app.PublishAsync(new OrderCompleted("order-1", "alice"));
        await app.PublishAsync(new OrderCompleted("order-2", "bob"));
        await app.WaitForCompletedAsync(2);

        clock.Advance(TimeSpan.FromDays(8));
        await app.PublishAsync(new OrderCompleted("order-3", "carol"));
        await app.WaitForCompletedAsync(3);

        foreach (var p in await completed.FindAllAsync())
        {
            app.Say($"  {((OrderCompleted)p.Event).OrderId} completed {p.CompletionDate:yyyy-MM-dd}");
        }

        app.Say("DeletePublicationsOlderThanAsync(7 days) removes the two from 8 days ago:");
        await completed.DeletePublicationsOlderThanAsync(TimeSpan.FromDays(7));
        foreach (var p in await completed.FindAllAsync())
        {
            app.Say($"  {((OrderCompleted)p.Event).OrderId} completed {p.CompletionDate:yyyy-MM-dd}");
        }
    }

    private static async Task DeleteModeAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddInventoryModule()
            .AddEventEmitter(o => o.CompletionMode = CompletionMode.Delete));

        var incomplete = app.Get<IIncompleteEventPublications>();

        app.Say("CompletionMode.Delete: a publication exists only until its listener succeeds.");
        await app.PublishAsync(new OrderCompleted("order-4", "dave"));
        app.Say($"  right after publishing: {(await incomplete.FindAllAsync()).Count} incomplete");

        await app.WaitUntilAsync(async () => (await incomplete.FindAllAsync()).Count == 0);
        var completed = await app.Get<ICompletedEventPublications>().FindAllAsync();
        app.Say($"  after Inventory handled it: {completed.Count} completed, 0 incomplete");
    }
}
