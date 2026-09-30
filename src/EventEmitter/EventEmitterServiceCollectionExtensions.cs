using Codefinity.EventEmitter;
using Codefinity.EventEmitter.Dispatch;
using Codefinity.EventEmitter.Listeners;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class EventEmitterServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IEventPublisher"/>, the publication registry and the background dispatcher.
    /// Calling it again returns a builder for the same registration, and applies <paramref name="configure"/> too.
    /// </summary>
    /// <remarks>
    /// <see cref="ApplicationModuleListenerAttribute"/> listeners run on a hosted service, so the app must run on
    /// the .NET Generic Host (or WebApplication).
    /// </remarks>
    public static EventEmitterBuilder AddEventEmitter(
        this IServiceCollection services,
        Action<EventEmitterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        var registry = services
            .Where(d => d.ServiceType == typeof(ListenerRegistry) && !d.IsKeyedService)
            .Select(d => d.ImplementationInstance)
            .OfType<ListenerRegistry>()
            .FirstOrDefault();

        if (registry is null)
        {
            registry = new ListenerRegistry();
            services.AddSingleton(registry);

            services.AddOptions<EventEmitterOptions>()
                .Validate(o => o.MaxDegreeOfParallelism >= 1, "MaxDegreeOfParallelism must be at least 1.")
                .Validate(o => o.ShutdownTimeout >= TimeSpan.Zero, "ShutdownTimeout must not be negative.");

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IEventPublicationRepository, InMemoryEventPublicationRepository>();
            services.TryAddSingleton<ITransactionSynchronization, SystemTransactionsSynchronization>();
            services.TryAddSingleton<AsyncEventDispatcher>();
            services.TryAddSingleton<EventPublicationRegistry>();
            services.TryAddSingleton<IIncompleteEventPublications>(sp => sp.GetRequiredService<EventPublicationRegistry>());
            services.TryAddSingleton<ICompletedEventPublications>(sp => sp.GetRequiredService<EventPublicationRegistry>());
            services.TryAddScoped<IEventPublisher, EventPublisher>();
            services.AddHostedService<EventDispatcherHostedService>();
        }

        return new EventEmitterBuilder(services, registry);
    }
}
