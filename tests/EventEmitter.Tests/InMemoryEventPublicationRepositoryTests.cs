namespace EventEmitter.Tests;

public class InMemoryEventPublicationRepositoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EventPublication Publication(DateTimeOffset publishedAt, int n = 0) =>
        new(Guid.NewGuid(), new Ping(n), typeof(Ping), "listener", publishedAt);

    [Fact]
    public async Task Rejects_a_duplicate_id()
    {
        var repository = new InMemoryEventPublicationRepository();
        var publication = Publication(T0);
        await repository.CreateAsync([publication]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateAsync([publication]));

        Assert.Contains(publication.Id.ToString(), ex.Message);
    }

    [Fact]
    public async Task Ignores_unknown_ids()
    {
        var repository = new InMemoryEventPublicationRepository();

        await repository.MarkCompletedAsync(Guid.NewGuid(), T0);
        await repository.MarkFailedAsync(Guid.NewGuid(), "failure");
        await repository.DeleteAsync([Guid.NewGuid()]);

        Assert.Empty(await repository.FindIncompleteAsync());
        Assert.Empty(await repository.FindCompletedAsync());
    }

    [Fact]
    public async Task Returns_publications_oldest_first()
    {
        var repository = new InMemoryEventPublicationRepository();
        await repository.CreateAsync([Publication(T0.AddMinutes(2), 2), Publication(T0, 0), Publication(T0.AddMinutes(1), 1)]);

        var numbers = (await repository.FindIncompleteAsync()).Select(p => ((Ping)p.Event).N);

        Assert.Equal([0, 1, 2], numbers);
    }

    [Fact]
    public async Task Tracks_attempts_failures_and_completion()
    {
        var repository = new InMemoryEventPublicationRepository();
        var publication = Publication(T0);
        await repository.CreateAsync([publication]);

        await repository.MarkFailedAsync(publication.Id, "first failure");
        var failed = Assert.Single(await repository.FindIncompleteAsync());
        Assert.Equal(1, failed.Attempts);
        Assert.Equal("first failure", failed.LastFailure);
        Assert.False(failed.IsCompleted);

        await repository.MarkCompletedAsync(publication.Id, T0.AddMinutes(5));
        var completed = Assert.Single(await repository.FindCompletedAsync());
        Assert.Equal(2, completed.Attempts);
        Assert.Equal(T0.AddMinutes(5), completed.CompletionDate);
        Assert.Equal("first failure", completed.LastFailure);
        Assert.True(completed.IsCompleted);
        Assert.Empty(await repository.FindIncompleteAsync());
    }

    [Fact]
    public async Task DeleteCompletedBefore_only_removes_old_completed_publications()
    {
        var repository = new InMemoryEventPublicationRepository();
        var old = Publication(T0);
        var recent = Publication(T0);
        var incomplete = Publication(T0);
        await repository.CreateAsync([old, recent, incomplete]);
        await repository.MarkCompletedAsync(old.Id, T0);
        await repository.MarkCompletedAsync(recent.Id, T0.AddDays(2));

        await repository.DeleteCompletedBeforeAsync(T0.AddDays(1));

        Assert.Equal(recent.Id, Assert.Single(await repository.FindCompletedAsync()).Id);
        Assert.Equal(incomplete.Id, Assert.Single(await repository.FindIncompleteAsync()).Id);
    }

    [Fact]
    public async Task Concurrent_updates_to_one_publication_are_not_lost()
    {
        var repository = new InMemoryEventPublicationRepository();
        var publication = Publication(T0);
        await repository.CreateAsync([publication]);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 2000),
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (i, _) => await repository.MarkFailedAsync(publication.Id, $"failure {i}"));

        Assert.Equal(2000, Assert.Single(await repository.FindIncompleteAsync()).Attempts);
    }
}
