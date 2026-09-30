using Codefinity.EventEmitter;
using Codefinity.EventEmitter.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class EventEmitterTestingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PublishedEvents"/> (which records every published event) and <see cref="Scenario"/>.
    /// Also calls <c>AddEventEmitter()</c>, so it can be used before or after it.
    /// </summary>
    public static IServiceCollection AddEventEmitterTesting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEventEmitter();
        services.TryAddSingleton<PublishedEvents>();
        services.TryAddSingleton<Scenario>();

        var publisher = services.Last(d => d.ServiceType == typeof(IEventPublisher) && !d.IsKeyedService);
        if (publisher.ImplementationFactory?.Target is not RecordingDecorator)
        {
            services.Remove(publisher);
            services.Add(ServiceDescriptor.Describe(
                typeof(IEventPublisher),
                new RecordingDecorator(publisher).Create,
                publisher.Lifetime));
        }

        return services;
    }

    /// <summary>Builds the publisher that <paramref name="inner"/> describes and wraps it in a <see cref="RecordingEventPublisher"/>.</summary>
    private sealed class RecordingDecorator(ServiceDescriptor inner)
    {
        public object Create(IServiceProvider services)
        {
            var publisher = inner switch
            {
                { ImplementationInstance: IEventPublisher instance } => instance,
                { ImplementationFactory: { } factory } => (IEventPublisher)factory(services),
                _ => (IEventPublisher)ActivatorUtilities.CreateInstance(services, inner.ImplementationType!),
            };

            return new RecordingEventPublisher(publisher, services.GetRequiredService<PublishedEvents>());
        }
    }
}
