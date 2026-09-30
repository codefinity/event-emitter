using System.Transactions;

namespace EventEmitter.Tests;

public class TransactionTests
{
    private sealed class AuditListener(CallLog calls)
    {
        [EventListener]
        public void On(OrderCompleted evt) => calls.Record("audit", evt);
    }

    private sealed class InventoryListener(CallLog calls)
    {
        [ApplicationModuleListener]
        public void On(OrderCompleted evt) => calls.Record("inventory", evt);
    }

    [Fact]
    public async Task Module_listeners_run_only_after_commit()
    {
        await using var app = await TestApp.StartAsync(b => b
            .AddListener<AuditListener>()
            .AddListener<InventoryListener>());

        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
            await Task.Delay(100);

            Assert.Equal(["audit"], app.Calls.Listeners);
            transaction.Complete();
        }

        await Eventually.AssertAsync(() => app.Calls.Listeners.Contains("inventory"), "inventory ran after commit");
    }

    [Fact]
    public async Task Rollback_discards_module_deliveries()
    {
        await using var app = await TestApp.StartAsync(b => b
            .AddListener<AuditListener>()
            .AddListener<InventoryListener>());

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        }

        await Eventually.AssertAsync(async () => (await app.Incomplete.FindAllAsync()).Count == 0, "publications were deleted");
        await Task.Delay(100);
        Assert.Equal(["audit"], app.Calls.Listeners);
        Assert.Empty(await app.Completed.FindAllAsync());
    }

    /// <summary>Records the ambient transaction seen by each repository call.</summary>
    private sealed class TransactionRecordingRepository : IEventPublicationRepository
    {
        private readonly InMemoryEventPublicationRepository _inner = new();

        public Transaction? DuringCreate { get; private set; }
        public TaskCompletionSource<Transaction?> DuringDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CreateAsync(IReadOnlyCollection<EventPublication> publications, CancellationToken ct = default)
        {
            DuringCreate = Transaction.Current;
            return _inner.CreateAsync(publications, ct);
        }

        public Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        {
            DuringDelete.TrySetResult(Transaction.Current);
            return _inner.DeleteAsync(ids, ct);
        }

        public Task MarkCompletedAsync(Guid id, DateTimeOffset completionDate, CancellationToken ct = default) => _inner.MarkCompletedAsync(id, completionDate, ct);
        public Task MarkFailedAsync(Guid id, string failure, CancellationToken ct = default) => _inner.MarkFailedAsync(id, failure, ct);
        public Task<IReadOnlyList<EventPublication>> FindIncompleteAsync(CancellationToken ct = default) => _inner.FindIncompleteAsync(ct);
        public Task<IReadOnlyList<EventPublication>> FindCompletedAsync(CancellationToken ct = default) => _inner.FindCompletedAsync(ct);
        public Task DeleteCompletedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) => _inner.DeleteCompletedBeforeAsync(cutoff, ct);
    }

    [Fact]
    public async Task Repository_joins_the_publishers_transaction_but_not_the_rolled_back_one()
    {
        var repository = new TransactionRecordingRepository();
        await using var app = await TestApp.StartAsync(b => b
            .AddListener<InventoryListener>()
            .UsePublicationRepository(repository));

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));
        }

        Assert.NotNull(repository.DuringCreate);
        Assert.Null(await repository.DuringDelete.Task.WaitAsync(TestTimeouts.SafetyNet));
    }

    [Fact]
    public async Task Publications_awaiting_commit_are_not_resubmitted()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<InventoryListener>());

        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));

            Assert.Equal(0, await app.Incomplete.ResubmitAsync(_ => true));
            transaction.Complete();
        }

        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "inventory ran after commit");
        await Task.Delay(100);
        Assert.Single(app.Calls.All);
    }
}
