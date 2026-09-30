using System.Reflection;

namespace EventEmitter.Listeners;

internal enum ListenerMode
{
    Synchronous,
    ApplicationModule,
}

internal delegate ValueTask ListenerInvoker(object listener, object evt, CancellationToken cancellationToken);

internal sealed record ListenerDescriptor(
    Type ListenerType,
    MethodInfo Method,
    Type EventType,
    ListenerMode Mode,
    string Id,
    int Order,
    ListenerInvoker Invoker);
