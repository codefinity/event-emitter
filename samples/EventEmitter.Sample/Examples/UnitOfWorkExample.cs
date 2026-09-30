using Codefinity.EventEmitter.Sample.Infrastructure;
using Codefinity.EventEmitter.Sample.Inventory;
using Codefinity.EventEmitter.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Sample.Examples;

/// <summary>
/// Plugging in your own transaction mechanism with ITransactionSynchronization, instead of System.Transactions.
/// </summary>
internal static class UnitOfWorkExample
{
    /// <summary>
    /// A minimal unit of work tracked in an AsyncLocal. In a real app, Commit would save the DbContext and
    /// commit its database transaction before notifying the callbacks.
    /// </summary>
    private sealed class UnitOfWork : IDisposable
    {
        private static readonly AsyncLocal<UnitOfWork?> CurrentSlot = new();
        private readonly List<Action<bool>> _onCompleted = [];

        public static UnitOfWork? Current => CurrentSlot.Value;

        public bool IsCompleted { get; private set; }

        public static UnitOfWork Begin() => CurrentSlot.Value = new UnitOfWork();

        public void OnCompleted(Action<bool> callback) => _onCompleted.Add(callback);

        public void Commit() => Complete(committed: true);

        public void Dispose()
        {
            if (!IsCompleted)
            {
                Complete(committed: false);
            }
        }

        private void Complete(bool committed)
        {
            IsCompleted = true;
            CurrentSlot.Value = null;
            foreach (var callback in _onCompleted)
            {
                callback(committed);
            }
        }
    }

    /// <summary>Tells EventEmitter about the current unit of work.</summary>
    private sealed class UnitOfWorkSynchronization : ITransactionSynchronization
    {
        public bool IsTransactionActive => UnitOfWork.Current is { IsCompleted: false };

        public void RegisterAfterCompletion(Action<bool> onCompleted) => UnitOfWork.Current!.OnCompleted(onCompleted);
    }

    public static async Task RunAsync()
    {
        await using var app = await ExampleApp.StartAsync(services => services
            .AddInventoryModule()
            .AddEventEmitter()
            .UseTransactionSynchronization<UnitOfWorkSynchronization>());

        app.Say("Publishing inside a unit of work that commits:");
        using (var unitOfWork = UnitOfWork.Begin())
        {
            await app.PublishAsync(new OrderCompleted("order-1", "alice"));
            await Task.Delay(300);
            app.Say("300 ms later Inventory still hasn't run, because the unit of work is open. Committing now.");
            unitOfWork.Commit();
        }

        await app.WaitForCompletedAsync(1);

        app.Say("Publishing inside a unit of work that is disposed without committing:");
        using (UnitOfWork.Begin())
        {
            await app.PublishAsync(new OrderCompleted("order-2", "bob"));
        }

        await Task.Delay(300);
        var completed = await app.Get<ICompletedEventPublications>().FindAllAsync();
        var incomplete = await app.Get<IIncompleteEventPublications>().FindAllAsync();
        app.Say($"Inventory handled {completed.Count} order(s); order-2 left {incomplete.Count} publication(s) behind.");
    }
}
