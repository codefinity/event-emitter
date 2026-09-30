namespace EventEmitter;

/// <summary>
/// Marks a method as an asynchronous, transactional event listener, like Spring Modulith's
/// <c>@ApplicationModuleListener</c>.
/// </summary>
/// <remarks>
/// Publishing the event records an <see cref="EventPublication"/> for the method. The method runs on a background
/// worker, in a new DI scope, after the publisher's ambient transaction commits (or immediately when there is none).
/// If the transaction rolls back, the method never runs. If the method throws, the publication stays incomplete and
/// can be resubmitted through <see cref="IIncompleteEventPublications"/>.
/// <para>
/// The first parameter is the event; its type decides which events the method receives (a base type or interface
/// receives all subtypes). An optional second parameter receives a <see cref="CancellationToken"/> that is cancelled
/// when the host's shutdown timeout runs out. The method may return <c>void</c>, <see cref="Task"/> or <see cref="ValueTask"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApplicationModuleListenerAttribute : Attribute
{
    /// <summary>
    /// Stable listener identifier stored with each publication. Defaults to <c>{ListenerType}.{Method}({EventType})</c>.
    /// Set it explicitly when publications are persisted, so renaming the method doesn't orphan them.
    /// </summary>
    public string? Id { get; set; }
}
