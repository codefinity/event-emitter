using EventEmitter.Sample.Infrastructure;
using EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventEmitter.Sample.Examples;

/// <summary>
/// Every supported listener method shape, in one class with no interface or base class.
/// </summary>
internal static class ListenerMethodsExample
{
    private sealed class ListenerShapes(ILogger<ListenerShapes> logger)
    {
        [EventListener]
        public void ReturnsVoid(OrderCompleted evt) =>
            logger.LogInformation("sync    void ReturnsVoid(OrderCompleted)");

        [EventListener]
        public Task ReturnsTask(OrderCompleted evt, CancellationToken cancellationToken)
        {
            logger.LogInformation("sync    Task ReturnsTask(OrderCompleted, CancellationToken)");
            return Task.CompletedTask;
        }

        [EventListener]
        private ValueTask PrivateValueTask(OrderCompleted evt)
        {
            logger.LogInformation("sync    private ValueTask PrivateValueTask(OrderCompleted)");
            return ValueTask.CompletedTask;
        }

        [EventListener]
        internal Task<int> ReturnsTaskOfT(OrderCompleted evt)
        {
            logger.LogInformation("sync    internal Task<int> ReturnsTaskOfT(OrderCompleted); the result is ignored");
            return Task.FromResult(42);
        }

        [ApplicationModuleListener]
        public async Task AsyncWithToken(OrderCompleted evt, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            logger.LogInformation("module  async Task AsyncWithToken(OrderCompleted, CancellationToken)");
        }

        [ApplicationModuleListener]
        private void AnotherEventType(OrderCancelled evt) =>
            logger.LogInformation("module  private void AnotherEventType(OrderCancelled)");
    }

    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddEventEmitter()
            .AddListener<ListenerShapes>());

        app.Say("Publishing OrderCompleted:");
        await app.PublishAsync(new OrderCompleted("order-1", "alice"));
        app.Say("Publishing OrderCancelled:");
        await app.PublishAsync(new OrderCancelled("order-2", "customer changed their mind"));

        await app.WaitForCompletedAsync(2);
        app.Say("Six methods, six call styles, one class.");
    }
}
