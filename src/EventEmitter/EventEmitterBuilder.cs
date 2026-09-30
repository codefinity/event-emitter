using System.Reflection;
using EventEmitter.Listeners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventEmitter;

/// <summary>Registers listeners and replaces EventEmitter's default services.</summary>
public sealed class EventEmitterBuilder
{
    private readonly ListenerRegistry _registry;

    internal EventEmitterBuilder(IServiceCollection services, ListenerRegistry registry)
    {
        Services = services;
        _registry = registry;
    }

    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers the listener methods of <typeparamref name="TListener"/>. The class is added as a scoped service
    /// unless it is already registered. Invalid listener methods throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public EventEmitterBuilder AddListener<TListener>() where TListener : class => AddListener(typeof(TListener));

    /// <inheritdoc cref="AddListener{TListener}"/>
    public EventEmitterBuilder AddListener(Type listenerType)
    {
        ArgumentNullException.ThrowIfNull(listenerType);

        if (_registry.Add(listenerType))
        {
            Services.TryAddScoped(listenerType);
        }

        return this;
    }

    /// <summary>Registers every concrete class in <paramref name="assembly"/> that has listener methods.</summary>
    public EventEmitterBuilder AddListenersFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var listenerTypes = LoadableTypes(assembly)
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters)
            .Where(ListenerMethodScanner.HasListenerMethods);

        foreach (var listenerType in listenerTypes)
        {
            AddListener(listenerType);
        }

        return this;
    }

    public EventEmitterBuilder AddListenersFromAssemblyContaining<T>() => AddListenersFromAssembly(typeof(T).Assembly);

    public EventEmitterBuilder UsePublicationRepository<TRepository>()
        where TRepository : class, IEventPublicationRepository
    {
        Services.RemoveAll<IEventPublicationRepository>();
        Services.AddSingleton<IEventPublicationRepository, TRepository>();
        return this;
    }

    public EventEmitterBuilder UsePublicationRepository(IEventPublicationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        Services.RemoveAll<IEventPublicationRepository>();
        Services.AddSingleton(repository);
        return this;
    }

    public EventEmitterBuilder UseTransactionSynchronization<TSynchronization>()
        where TSynchronization : class, ITransactionSynchronization
    {
        Services.RemoveAll<ITransactionSynchronization>();
        Services.AddSingleton<ITransactionSynchronization, TSynchronization>();
        return this;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
