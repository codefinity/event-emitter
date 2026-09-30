using System.Reflection;

namespace Codefinity.EventEmitter.Listeners;

internal delegate ValueTask ListenerInvoker(object listener, object evt, CancellationToken cancellationToken);

internal sealed record ListenerDescriptor(
    Type ListenerType,
    MethodInfo Method,
    Type EventType,
    string Id,
    ListenerInvoker Invoker);
