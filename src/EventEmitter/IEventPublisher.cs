namespace Codefinity.EventEmitter;

/// <summary>
/// Publishes application events to every listener method whose event parameter accepts the event.
/// The .NET counterpart of Spring's <c>ApplicationEventPublisher</c>.
/// </summary>
/// <remarks>
/// Each matching method marked with <see cref="ApplicationModuleListenerAttribute"/> is recorded as an event
/// publication and runs on a background worker once the ambient transaction (if any) commits. No listener runs
/// before this call returns.
/// </remarks>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default) where TEvent : notnull;
}
