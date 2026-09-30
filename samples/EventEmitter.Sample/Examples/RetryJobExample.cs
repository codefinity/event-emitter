using System.Collections.Concurrent;
using Codefinity.EventEmitter.Sample.Infrastructure;
using Codefinity.EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Sample.Examples;

/// <summary>
/// Automatic retries built from IIncompleteEventPublications: a background job resubmits failed
/// publications on a timer and gives up after a maximum number of attempts.
/// </summary>
internal static class RetryJobExample
{
    private sealed record RetryPolicy(TimeSpan Interval, int MaxAttempts);

    private sealed class RetryFailedEvents(
        IIncompleteEventPublications incomplete,
        RetryPolicy policy,
        ILogger<RetryFailedEvents> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(policy.Interval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    // Attempts > 0: it has failed at least once. Below the maximum: we haven't given up on it.
                    var count = await incomplete.ResubmitAsync(
                        p => p.Attempts > 0 && p.Attempts < policy.MaxAttempts,
                        stoppingToken);

                    if (count > 0)
                    {
                        logger.LogInformation("Resubmitted {Count} failed publication(s)", count);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host is shutting down; that is the normal way for this job to end.
            }
        }
    }

    /// <summary>Fails the first two times for each order, and always for the "bounces" customer.</summary>
    private sealed class ReceiptEmailListener(ILogger<ReceiptEmailListener> logger)
    {
        private static readonly ConcurrentDictionary<string, int> Calls = new();

        [ApplicationModuleListener(Id = "email.send-receipt")]
        public void On(OrderCompleted evt)
        {
            var call = Calls.AddOrUpdate(evt.OrderId, 1, (_, n) => n + 1);
            if (call <= 2 || evt.CustomerId == "bounces")
            {
                throw new InvalidOperationException($"Mail server rejected the receipt for {evt.OrderId} (try {call}).");
            }

            logger.LogInformation("Sent the receipt for {OrderId} on try {Call}", evt.OrderId, call);
        }
    }

    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services =>
        {
            services.AddEventEmitter().AddListener<ReceiptEmailListener>();
            services.AddSingleton(new RetryPolicy(Interval: TimeSpan.FromMilliseconds(300), MaxAttempts: 3));
            services.AddHostedService<RetryFailedEvents>();
        });

        var incomplete = app.Get<IIncompleteEventPublications>();
        var completed = app.Get<ICompletedEventPublications>();

        app.Say("order-1 will succeed on its third try; order-2 will never succeed. The retry job allows 3 attempts:");
        await app.PublishAsync(new OrderCompleted("order-1", "alice"));
        await app.PublishAsync(new OrderCompleted("order-2", "bounces"));

        await app.WaitUntilAsync(async () =>
            (await completed.FindAllAsync()).Count == 1 &&
            (await incomplete.FindAllAsync()).Any(p => p.Attempts == 3));
        await Task.Delay(700);   // a few more ticks: nothing else is resubmitted

        app.Say("Final state:");
        foreach (var p in (await completed.FindAllAsync()).Concat(await incomplete.FindAllAsync()))
        {
            app.Say($"  {((OrderCompleted)p.Event).OrderId}: {Describe.Publication(p)}");
        }

        app.Say("order-2 has used up its attempts and stays incomplete for someone to look at.");
    }
}
