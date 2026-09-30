using System.Linq.Expressions;
using System.Reflection;

namespace EventEmitter.Listeners;

/// <summary>
/// Compiles a listener method into a <see cref="ListenerInvoker"/> once, so events are dispatched without reflection.
/// </summary>
internal static class ListenerInvokerFactory
{
    private static readonly ConstructorInfo ValueTaskFromTask = typeof(ValueTask).GetConstructor([typeof(Task)])!;

    public static ListenerInvoker Create(MethodInfo method)
    {
        var listener = Expression.Parameter(typeof(object), "listener");
        var evt = Expression.Parameter(typeof(object), "evt");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var parameters = method.GetParameters();
        var arguments = new List<Expression> { Expression.Convert(evt, parameters[0].ParameterType) };
        if (parameters.Length == 2)
        {
            arguments.Add(cancellationToken);
        }

        var call = Expression.Call(Expression.Convert(listener, method.DeclaringType!), method, arguments);

        Expression body = method.ReturnType switch
        {
            var t when t == typeof(void) => Expression.Block(call, Expression.Default(typeof(ValueTask))),
            var t when t == typeof(ValueTask) => call,
            _ => Expression.New(ValueTaskFromTask, Expression.Convert(call, typeof(Task))),
        };

        return Expression.Lambda<ListenerInvoker>(body, listener, evt, cancellationToken).Compile();
    }

    public static bool IsSupportedReturnType(Type returnType) =>
        returnType == typeof(void) || returnType == typeof(ValueTask) || typeof(Task).IsAssignableFrom(returnType);
}
