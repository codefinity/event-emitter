namespace Codefinity.EventEmitter;

/// <summary>
/// Publishes application events to every listener method whose event parameter accepts the event.
/// The .NET counterpart of Spring's <c>ApplicationEventPublisher</c>.
/// </summary>
/// <remarks>
/// Methods marked with <see cref="EventListenerAttribute"/> run inline, in the caller's DI scope, before this call returns.
/// Methods marked with <see cref="ApplicationModuleListenerAttribute"/> are recorded as event publications and run
/// on a background worker once the ambient transaction (if any) commits.
/// </remarks>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default) where TEvent : notnull;
}
