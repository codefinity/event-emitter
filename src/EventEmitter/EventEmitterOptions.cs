namespace EventEmitter;

/// <summary>What happens to a publication once its listener has handled the event.</summary>
public enum CompletionMode
{
    /// <summary>Keep it with a completion date; see <see cref="ICompletedEventPublications"/>.</summary>
    Update,

    /// <summary>Delete it.</summary>
    Delete,
}

public sealed class EventEmitterOptions
{
    public CompletionMode CompletionMode { get; set; } = CompletionMode.Update;

    /// <summary>
    /// Deliver the repository's incomplete publications again when the host starts. Only useful with a durable
    /// <see cref="IEventPublicationRepository"/>, like Spring Modulith's <c>republish-outstanding-events-on-restart</c>.
    /// </summary>
    public bool RepublishOutstandingEventsOnStartup { get; set; }

    /// <summary>How many <see cref="ApplicationModuleListenerAttribute"/> listeners may run at the same time.</summary>
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// How long shutdown waits for queued and running listeners to finish before cancelling them.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
