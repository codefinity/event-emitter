namespace EventEmitter.Tests;

/// <summary>
/// An in-memory repository whose operations a test can make fail, or hold until released.
/// </summary>
internal sealed class ControllableRepository : IEventPublicationRepository
{
    private readonly InMemoryEventPublicationRepository _inner = new();
    private TaskCompletionSource? _holdNextFindIncomplete;

    public volatile bool FailCreate;
    public volatile bool FailDelete;
    public int FailMarkCompletedTimes;

    /// <summary>
    /// The next FindIncompleteAsync takes its snapshot immediately but doesn't return it until the returned
    /// source is completed, like a slow database query.
    /// </summary>
    public TaskCompletionSource HoldNextFindIncomplete()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _holdNextFindIncomplete = hold;
        return hold;
    }

    public Task CreateAsync(IReadOnlyCollection<EventPublication> publications, CancellationToken cancellationToken = default) =>
        FailCreate
            ? throw new InvalidOperationException("Database unavailable (create).")
            : _inner.CreateAsync(publications, cancellationToken);

    public Task MarkCompletedAsync(Guid id, DateTimeOffset completionDate, CancellationToken cancellationToken = default) =>
        Interlocked.Decrement(ref FailMarkCompletedTimes) >= 0
            ? throw new InvalidOperationException("Database unavailable (mark completed).")
            : _inner.MarkCompletedAsync(id, completionDate, cancellationToken);

    /// <summary>
    /// How long MarkFailedAsync takes to return after the failure is already visible, like a database write
    /// whose acknowledgement is slow.
    /// </summary>
    public TimeSpan MarkFailedLatency { get; set; }

    public async Task MarkFailedAsync(Guid id, string failure, CancellationToken cancellationToken = default)
    {
        await _inner.MarkFailedAsync(id, failure, cancellationToken);
        await Task.Delay(MarkFailedLatency, cancellationToken);
    }

    /// <summary>Makes the n-th FindIncompleteAsync from now fail (1 = the next one).</summary>
    public void FailFindIncompleteCall(int n) => Volatile.Write(ref _findIncompleteCallsUntilFailure, n);

    private int _findIncompleteCallsUntilFailure;

    public async Task<IReadOnlyList<EventPublication>> FindIncompleteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Decrement(ref _findIncompleteCallsUntilFailure) == 0)
        {
            throw new InvalidOperationException("Database unavailable (find incomplete).");
        }

        var snapshot = await _inner.FindIncompleteAsync(cancellationToken);
        if (Interlocked.Exchange(ref _holdNextFindIncomplete, null) is { } hold)
        {
            await hold.Task;
        }

        return snapshot;
    }

    public Task<IReadOnlyList<EventPublication>> FindCompletedAsync(CancellationToken cancellationToken = default) =>
        _inner.FindCompletedAsync(cancellationToken);

    public Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        FailDelete
            ? throw new InvalidOperationException("Database unavailable (delete).")
            : _inner.DeleteAsync(ids, cancellationToken);

    public Task DeleteCompletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        _inner.DeleteCompletedBeforeAsync(cutoff, cancellationToken);
}
