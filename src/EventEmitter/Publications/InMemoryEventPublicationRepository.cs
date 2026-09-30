using System.Collections.Concurrent;

namespace Codefinity.EventEmitter;

/// <summary>
/// Keeps publications in process memory. Incomplete publications are lost when the process exits.
/// </summary>
public sealed class InMemoryEventPublicationRepository : IEventPublicationRepository
{
    private readonly ConcurrentDictionary<Guid, EventPublication> _publications = new();

    public Task CreateAsync(IReadOnlyCollection<EventPublication> publications, CancellationToken cancellationToken = default)
    {
        foreach (var publication in publications)
        {
            if (!_publications.TryAdd(publication.Id, publication))
            {
                throw new InvalidOperationException($"A publication with id '{publication.Id}' already exists.");
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkCompletedAsync(Guid id, DateTimeOffset completionDate, CancellationToken cancellationToken = default)
    {
        Update(id, p => p with { CompletionDate = completionDate, Attempts = p.Attempts + 1 });
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid id, string failure, CancellationToken cancellationToken = default)
    {
        Update(id, p => p with { LastFailure = failure, Attempts = p.Attempts + 1 });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EventPublication>> FindIncompleteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Find(p => !p.IsCompleted));

    public Task<IReadOnlyList<EventPublication>> FindCompletedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Find(p => p.IsCompleted));

    public Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        foreach (var id in ids)
        {
            _publications.TryRemove(id, out _);
        }

        return Task.CompletedTask;
    }

    public Task DeleteCompletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        foreach (var publication in _publications.Values)
        {
            if (publication.CompletionDate < cutoff)
            {
                _publications.TryRemove(publication.Id, out _);
            }
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<EventPublication> Find(Func<EventPublication, bool> predicate) =>
        _publications.Values.Where(predicate).OrderBy(p => p.PublicationDate).ToArray();

    private void Update(Guid id, Func<EventPublication, EventPublication> change)
    {
        while (_publications.TryGetValue(id, out var current))
        {
            if (_publications.TryUpdate(id, change(current), current))
            {
                return;
            }
        }
    }
}
