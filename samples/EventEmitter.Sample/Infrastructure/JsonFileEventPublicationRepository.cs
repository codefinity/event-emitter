using System.Text.Json;

namespace EventEmitter.Sample.Infrastructure;

/// <summary>
/// A durable <see cref="IEventPublicationRepository"/> that keeps publications in a JSON file, so they
/// survive a restart. A real application would use its database; the shape of the code is the same.
/// </summary>
internal sealed class JsonFileEventPublicationRepository(string path) : IEventPublicationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>One stored publication. The event is kept as JSON plus its type name.</summary>
    private sealed record Row(
        Guid Id,
        string EventType,
        JsonElement Event,
        string ListenerId,
        DateTimeOffset PublicationDate,
        DateTimeOffset? CompletionDate,
        string? LastFailure,
        int Attempts);

    public Task CreateAsync(IReadOnlyCollection<EventPublication> publications, CancellationToken cancellationToken = default) =>
        ChangeAsync(rows => rows.AddRange(publications.Select(ToRow)), cancellationToken);

    public Task MarkCompletedAsync(Guid id, DateTimeOffset completionDate, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, r => r with { CompletionDate = completionDate, Attempts = r.Attempts + 1 }, cancellationToken);

    public Task MarkFailedAsync(Guid id, string failure, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, r => r with { LastFailure = failure, Attempts = r.Attempts + 1 }, cancellationToken);

    public Task<IReadOnlyList<EventPublication>> FindIncompleteAsync(CancellationToken cancellationToken = default) =>
        QueryAsync(r => r.CompletionDate is null, cancellationToken);

    public Task<IReadOnlyList<EventPublication>> FindCompletedAsync(CancellationToken cancellationToken = default) =>
        QueryAsync(r => r.CompletionDate is not null, cancellationToken);

    public Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        ChangeAsync(rows => rows.RemoveAll(r => ids.Contains(r.Id)), cancellationToken);

    public Task DeleteCompletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        ChangeAsync(rows => rows.RemoveAll(r => r.CompletionDate < cutoff), cancellationToken);

    private Task UpdateAsync(Guid id, Func<Row, Row> change, CancellationToken cancellationToken) =>
        ChangeAsync(rows =>
        {
            // Unknown ids are ignored: the publication may have been deleted in the meantime.
            var index = rows.FindIndex(r => r.Id == id);
            if (index >= 0)
            {
                rows[index] = change(rows[index]);
            }
        }, cancellationToken);

    private async Task ChangeAsync(Action<List<Row>> change, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var rows = await LoadAsync(cancellationToken);
            change(rows);

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, rows, JsonOptions, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<EventPublication>> QueryAsync(Func<Row, bool> filter, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var rows = await LoadAsync(cancellationToken);
            return rows.Where(filter).OrderBy(r => r.PublicationDate).Select(ToPublication).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<Row>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<Row>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private static Row ToRow(EventPublication p) => new(
        p.Id,
        p.EventType.AssemblyQualifiedName!,
        JsonSerializer.SerializeToElement(p.Event, p.EventType),
        p.ListenerId,
        p.PublicationDate,
        p.CompletionDate,
        p.LastFailure,
        p.Attempts);

    private static EventPublication ToPublication(Row r)
    {
        var eventType = Type.GetType(r.EventType, throwOnError: true)!;
        var evt = r.Event.Deserialize(eventType, JsonOptions)!;

        return new EventPublication(r.Id, evt, eventType, r.ListenerId, r.PublicationDate)
        {
            CompletionDate = r.CompletionDate,
            LastFailure = r.LastFailure,
            Attempts = r.Attempts,
        };
    }
}
