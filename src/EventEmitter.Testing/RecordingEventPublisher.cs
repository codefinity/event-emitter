namespace Codefinity.EventEmitter.Testing;

/// <summary>
/// Wraps the application's <see cref="IEventPublisher"/> and records every event in <see cref="PublishedEvents"/>.
/// </summary>
internal sealed class RecordingEventPublisher(IEventPublisher inner, PublishedEvents events) : IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(evt);

        // Recorded first, so the event shows up even if publishing it fails.
        events.Record(evt);
        return inner.PublishAsync(evt, cancellationToken);
    }
}
