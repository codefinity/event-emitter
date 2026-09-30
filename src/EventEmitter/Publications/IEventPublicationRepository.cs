namespace Codefinity.EventEmitter;

/// <summary>
/// Storage for <see cref="EventPublication"/>s. The default is <see cref="InMemoryEventPublicationRepository"/>;
/// register a durable implementation with <c>UsePublicationRepository</c> to keep publications across restarts.
/// </summary>
/// <remarks>
/// Methods that take an id must ignore ids that no longer exist: a publication can be deleted
/// (after a rollback, or with <see cref="CompletionMode.Delete"/>) while another operation is in flight.
/// </remarks>
public interface IEventPublicationRepository
{
    Task CreateAsync(IReadOnlyCollection<EventPublication> publications, CancellationToken cancellationToken = default);

    /// <summary>Sets the completion date and counts the attempt.</summary>
    Task MarkCompletedAsync(Guid id, DateTimeOffset completionDate, CancellationToken cancellationToken = default);

    /// <summary>Records the failure and counts the attempt. The publication stays incomplete.</summary>
    Task MarkFailedAsync(Guid id, string failure, CancellationToken cancellationToken = default);

    /// <summary>Incomplete publications, oldest first.</summary>
    Task<IReadOnlyList<EventPublication>> FindIncompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>Completed publications, oldest first.</summary>
    Task<IReadOnlyList<EventPublication>> FindCompletedAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Deletes completed publications whose completion date is before <paramref name="cutoff"/>.</summary>
    Task DeleteCompletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
