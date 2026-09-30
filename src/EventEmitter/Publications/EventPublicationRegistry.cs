using Codefinity.EventEmitter.Dispatch;

namespace Codefinity.EventEmitter;

/// <summary>
/// Publications whose listener hasn't handled the event successfully yet, like Spring Modulith's
/// <c>IncompleteEventPublications</c>.
/// </summary>
public interface IIncompleteEventPublications
{
    Task<IReadOnlyList<EventPublication>> FindAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers the matching incomplete publications again. Publications that are queued, running, or waiting for
    /// their transaction to commit are skipped.
    /// </summary>
    /// <returns>How many publications were resubmitted.</returns>
    Task<int> ResubmitAsync(Func<EventPublication, bool> filter, CancellationToken cancellationToken = default);

    /// <summary>Resubmits incomplete publications published more than <paramref name="age"/> ago.</summary>
    /// <returns>How many publications were resubmitted.</returns>
    Task<int> ResubmitOlderThanAsync(TimeSpan age, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publications whose listener handled the event successfully, like Spring Modulith's <c>CompletedEventPublications</c>.
/// Only kept when <see cref="EventEmitterOptions.CompletionMode"/> is <see cref="CompletionMode.Update"/>.
/// </summary>
public interface ICompletedEventPublications
{
    Task<IReadOnlyList<EventPublication>> FindAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes completed publications that were completed more than <paramref name="age"/> ago.</summary>
    Task DeletePublicationsOlderThanAsync(TimeSpan age, CancellationToken cancellationToken = default);
}

internal sealed class EventPublicationRegistry(
    IEventPublicationRepository repository,
    AsyncEventDispatcher dispatcher,
    TimeProvider time) : IIncompleteEventPublications, ICompletedEventPublications
{
    Task<IReadOnlyList<EventPublication>> IIncompleteEventPublications.FindAllAsync(CancellationToken cancellationToken) =>
        repository.FindIncompleteAsync(cancellationToken);

    Task<IReadOnlyList<EventPublication>> ICompletedEventPublications.FindAllAsync(CancellationToken cancellationToken) =>
        repository.FindCompletedAsync(cancellationToken);

    public async Task<int> ResubmitAsync(Func<EventPublication, bool> filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var incomplete = await repository.FindIncompleteAsync(cancellationToken).ConfigureAwait(false);
        var reserved = incomplete.Where(filter).Where(p => dispatcher.TryReserve(p.Id)).ToList();
        if (reserved.Count == 0)
        {
            return 0;
        }

        // The query above can be stale: a publication that was being delivered may have completed before we
        // reserved it. Reserved publications can't be delivered by anyone else, so a second query is reliable.
        HashSet<Guid> stillIncomplete;
        try
        {
            stillIncomplete = (await repository.FindIncompleteAsync(cancellationToken).ConfigureAwait(false))
                .Select(p => p.Id)
                .ToHashSet();
        }
        catch
        {
            reserved.ForEach(p => dispatcher.Release(p.Id));
            throw;
        }

        var resubmitted = 0;
        foreach (var publication in reserved)
        {
            if (!stillIncomplete.Contains(publication.Id))
            {
                dispatcher.Release(publication.Id);
            }
            else if (dispatcher.EnqueueReserved(publication))
            {
                resubmitted++;
            }
        }

        return resubmitted;
    }

    public Task<int> ResubmitOlderThanAsync(TimeSpan age, CancellationToken cancellationToken = default)
    {
        var cutoff = time.GetUtcNow() - age;
        return ResubmitAsync(p => p.PublicationDate < cutoff, cancellationToken);
    }

    public Task DeletePublicationsOlderThanAsync(TimeSpan age, CancellationToken cancellationToken = default) =>
        repository.DeleteCompletedBeforeAsync(time.GetUtcNow() - age, cancellationToken);
}
