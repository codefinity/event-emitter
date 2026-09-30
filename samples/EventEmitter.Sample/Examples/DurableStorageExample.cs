using EventEmitter.Sample.Infrastructure;
using EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventEmitter.Sample.Examples;

/// <summary>
/// A custom, durable publication repository (a JSON file) together with ShutdownTimeout and
/// RepublishOutstandingEventsOnStartup: an event that couldn't be handled before the app stopped is
/// delivered when the app starts again.
/// </summary>
internal static class DurableStorageExample
{
    private sealed record BillingSettings(TimeSpan InvoiceDuration);

    private sealed class InvoiceListener(BillingSettings settings, ILogger<InvoiceListener> logger)
    {
        // An explicit id: publications stored under it must still match after a redeploy.
        [ApplicationModuleListener(Id = "billing.create-invoice")]
        public async Task On(OrderCompleted evt, CancellationToken cancellationToken)
        {
            logger.LogInformation("Creating the invoice for {OrderId}...", evt.OrderId);
            await Task.Delay(settings.InvoiceDuration, cancellationToken);
            logger.LogInformation("Invoice for {OrderId} created", evt.OrderId);
        }
    }

    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"event-publications-{Guid.NewGuid():N}.json");
        try
        {
            await FirstRunAsync(path);
            await SecondRunAsync(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task FirstRunAsync(string path)
    {
        var app = await ExampleApp.StartAsync(services =>
        {
            services.AddSingleton(new BillingSettings(InvoiceDuration: TimeSpan.FromSeconds(10)));
            services
                .AddEventEmitter(o => o.ShutdownTimeout = TimeSpan.FromMilliseconds(300))
                .UsePublicationRepository(new JsonFileEventPublicationRepository(path))
                .AddListener<InvoiceListener>();
        });

        app.Say($"Run 1: publications are stored in {Path.GetFileName(path)}. Invoices are slow today.");
        await app.PublishAsync(new OrderCompleted("order-1", "alice"));
        await Task.Delay(100);

        app.Say("Stopping the app mid-invoice (a deploy). ShutdownTimeout is 300 ms, then the listener is cancelled:");
        await app.StopAsync();
        await app.DisposeAsync();

        var stored = await new JsonFileEventPublicationRepository(path).FindIncompleteAsync();
        Console.WriteLine($"  Left in the file: {string.Join("; ", stored.Select(Describe.Publication))}");
    }

    private static async Task SecondRunAsync(string path)
    {
        Console.WriteLine("  Run 2: a new process starts with RepublishOutstandingEventsOnStartup = true, and invoices are fast again:");
        await using var app = await ExampleApp.StartAsync(services =>
        {
            services.AddSingleton(new BillingSettings(InvoiceDuration: TimeSpan.FromMilliseconds(100)));
            services
                .AddEventEmitter(o => o.RepublishOutstandingEventsOnStartup = true)
                .UsePublicationRepository(new JsonFileEventPublicationRepository(path))
                .AddListener<InvoiceListener>();
        });

        await app.WaitForCompletedAsync(1);

        app.Say("The publication from run 1 is now complete:");
        foreach (var p in await app.Get<ICompletedEventPublications>().FindAllAsync())
        {
            app.Say("  " + Describe.Publication(p));
        }
    }
}
