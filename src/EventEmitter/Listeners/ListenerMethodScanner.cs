using System.Reflection;

namespace Codefinity.EventEmitter.Listeners;

/// <summary>
/// Finds and validates the methods marked with <see cref="ApplicationModuleListenerAttribute"/> on a listener type.
/// </summary>
internal static class ListenerMethodScanner
{
    private const BindingFlags AllMethods =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool HasListenerMethods(Type type) =>
        type.GetMethods(AllMethods).Any(m => m.IsDefined(typeof(ApplicationModuleListenerAttribute), inherit: true));

    public static IReadOnlyList<ListenerDescriptor> Scan(Type listenerType)
    {
        if (!listenerType.IsClass || listenerType.IsAbstract || listenerType.ContainsGenericParameters)
        {
            throw new InvalidOperationException(
                $"Listener type '{listenerType.FullName}' must be a concrete, non-generic class.");
        }

        var descriptors = new List<ListenerDescriptor>();

        foreach (var method in listenerType.GetMethods(AllMethods))
        {
            var attribute = method.GetCustomAttribute<ApplicationModuleListenerAttribute>(inherit: true);
            if (attribute is null)
            {
                continue;
            }

            var eventType = Validate(listenerType, method);
            var id = attribute.Id ?? $"{listenerType.FullName}.{method.Name}({eventType.FullName})";

            descriptors.Add(new ListenerDescriptor(
                listenerType,
                method,
                eventType,
                id,
                ListenerInvokerFactory.Create(method)));
        }

        if (descriptors.Count == 0)
        {
            throw new InvalidOperationException(
                $"Listener type '{listenerType.FullName}' has no methods marked with [ApplicationModuleListener].");
        }

        return descriptors;
    }

    private static Type Validate(Type listenerType, MethodInfo method)
    {
        var name = $"{listenerType.FullName}.{method.Name}";

        if (method.IsStatic)
        {
            throw Invalid(name, "must be an instance method.");
        }

        if (method.ContainsGenericParameters)
        {
            throw Invalid(name, "must not be generic.");
        }

        var parameters = method.GetParameters();
        if (parameters.Length is 0 or > 2)
        {
            throw Invalid(name, "must take the event as its first parameter and, optionally, a CancellationToken as its second.");
        }

        if (parameters[0].ParameterType.IsByRef)
        {
            throw Invalid(name, "must take the event by value, not as a ref, in or out parameter.");
        }

        if (parameters.Length == 2 && parameters[1].ParameterType != typeof(CancellationToken))
        {
            throw Invalid(name, $"has a second parameter of type '{parameters[1].ParameterType.Name}'; only a CancellationToken is allowed there.");
        }

        if (!ListenerInvokerFactory.IsSupportedReturnType(method.ReturnType))
        {
            throw Invalid(name, $"returns '{method.ReturnType.Name}'; listener methods must return void, Task or ValueTask.");
        }

        return parameters[0].ParameterType;
    }

    private static InvalidOperationException Invalid(string method, string problem) =>
        new($"Listener method '{method}' {problem}");
}
