namespace Codefinity.EventEmitter;

/// <summary>
/// Marks a method as a synchronous event listener, like Spring's <c>@EventListener</c>.
/// </summary>
/// <remarks>
/// The method runs inline during <see cref="IEventPublisher.PublishAsync{TEvent}"/>, in the publisher's DI scope and
/// ambient transaction, and any exception it throws propagates to the publisher.
/// <para>
/// The first parameter is the event; its type decides which events the method receives (a base type or interface
/// receives all subtypes). An optional second parameter receives the publisher's <see cref="CancellationToken"/>.
/// The method may return <c>void</c>, <see cref="Task"/> or <see cref="ValueTask"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class EventListenerAttribute : Attribute
{
    /// <summary>Stable listener identifier. Defaults to <c>{ListenerType}.{Method}({EventType})</c>.</summary>
    public string? Id { get; set; }

    /// <summary>Invocation order among the synchronous listeners of an event; lower values run first.</summary>
    public int Order { get; set; }
}
