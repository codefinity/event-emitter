namespace EventEmitter;

/// <summary>
/// A record of one event being delivered to one <see cref="ApplicationModuleListenerAttribute"/> listener.
/// It is created when the event is published and completed once the listener has handled it successfully.
/// </summary>
/// <param name="Id">Unique id of the publication.</param>
/// <param name="Event">The published event.</param>
/// <param name="EventType">The runtime type of <paramref name="Event"/>.</param>
/// <param name="ListenerId">Id of the listener method the event is delivered to.</param>
/// <param name="PublicationDate">When the event was published.</param>
public sealed record EventPublication(
    Guid Id,
    object Event,
    Type EventType,
    string ListenerId,
    DateTimeOffset PublicationDate)
{
    /// <summary>When the listener handled the event successfully; <c>null</c> while incomplete.</summary>
    public DateTimeOffset? CompletionDate { get; init; }

    /// <summary>The exception from the most recent failed delivery, if any.</summary>
    public string? LastFailure { get; init; }

    /// <summary>How many times delivery has been attempted, successful or not.</summary>
    public int Attempts { get; init; }

    public bool IsCompleted => CompletionDate.HasValue;
}
