using System.Diagnostics;
using Codefinity.EventEmitter.Sample.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codefinity.EventEmitter.Sample.Examples;

/// <summary>
/// MaxDegreeOfParallelism limits how many module listeners run at the same time.
/// </summary>
internal static class ParallelismExample
{
    private sealed record ReportRequested(int Number);

    private sealed class ReportGenerator(ILogger<ReportGenerator> logger)
    {
        [ApplicationModuleListener]
        public async Task On(ReportRequested evt, CancellationToken cancellationToken)
        {
            logger.LogInformation("Report {Number} started", evt.Number);
            await Task.Delay(400, cancellationToken);
        }
    }

    public static async Task RunAsync()
    {
        foreach (var parallelism in new[] { 1, 4 })
        {
            await using var app = await ExampleApp.StartAsync(services => services
                .AddEventEmitter(o => o.MaxDegreeOfParallelism = parallelism)
                .AddListener<ReportGenerator>());

            app.Say($"MaxDegreeOfParallelism = {parallelism}: four 400 ms reports");
            var stopwatch = Stopwatch.StartNew();

            for (var i = 1; i <= 4; i++)
            {
                await app.PublishAsync(new ReportRequested(i));
            }

            await app.WaitForCompletedAsync(4);
            app.Say($"All four done after {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
