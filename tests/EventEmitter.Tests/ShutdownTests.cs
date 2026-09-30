using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Tests;

public class ShutdownTests
{
    private sealed class SlowListener(CallLog calls)
    {
        [ApplicationModuleListener(Id = "slow")]
        public async Task On(Ping evt, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            calls.Record("slow", evt);
        }
    }

    private sealed class HangingListener(CallLog calls)
    {
        [ApplicationModuleListener(Id = "hanging")]
        public async Task On(Ping evt, CancellationToken cancellationToken)
        {
            calls.Record("started", evt);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Cleanup that takes a while: shutdown must still wait for the failure to be recorded.
                await Task.Delay(200, CancellationToken.None);
                calls.Record("cancelled", evt);
                throw;
            }
        }
    }

    [Fact]
    public async Task Stopping_waits_for_queued_listeners_to_finish()
    {
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<SlowListener>(),
            o => o.MaxDegreeOfParallelism = 1);

        for (var i = 0; i < 5; i++)
        {
            await app.PublishAsync(new Ping(i));
        }

        await app.StopAsync();

        Assert.Equal(5, app.Calls.All.Count);
        Assert.Equal(5, (await app.Completed.FindAllAsync()).Count);
        Assert.Empty(await app.Incomplete.FindAllAsync());
    }

    [Fact]
    public async Task Shutdown_timeout_cancels_running_listeners_and_leaves_the_rest_incomplete()
    {
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<HangingListener>(),
            o =>
            {
                o.MaxDegreeOfParallelism = 1;
                o.ShutdownTimeout = TimeSpan.FromMilliseconds(200);
            });

        await app.PublishAsync(new Ping(1));
        await app.PublishAsync(new Ping(2));
        await app.PublishAsync(new Ping(3));
        await Eventually.AssertAsync(() => app.Calls.Listeners.Contains("started"), "the first listener started");

        var stopwatch = Stopwatch.StartNew();
        await app.StopAsync();

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(150), TestTimeouts.SafetyNet);
        Assert.Equal(["started", "cancelled"], app.Calls.Listeners);

        // The cancelled delivery's failure is recorded before StopAsync returns; the queued ones never ran.
        var incomplete = (await app.Incomplete.FindAllAsync()).ToDictionary(p => ((Ping)p.Event).N);
        Assert.Equal(3, incomplete.Count);
        Assert.Equal(1, incomplete[1].Attempts);
        Assert.Contains("canceled", incomplete[1].LastFailure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, incomplete[2].Attempts);
        Assert.Equal(0, incomplete[3].Attempts);

        Assert.Contains(app.Logs.AtLeast(LogLevel.Warning), e => e.Message.Contains("did not finish within the shutdown timeout"));
    }

    [Fact]
    public async Task Publishing_after_shutdown_leaves_the_publication_incomplete()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<SlowListener>());
        await app.StopAsync();

        await app.PublishAsync(new Ping(9));

        var publication = Assert.Single(await app.Incomplete.FindAllAsync());
        Assert.Equal(0, publication.Attempts);
        Assert.Empty(app.Calls.All);
        Assert.Contains(app.Logs.AtLeast(LogLevel.Warning), e => e.Message.Contains("Event dispatch has stopped"));
    }
}
