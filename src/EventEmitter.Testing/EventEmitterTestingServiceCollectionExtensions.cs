using EventEmitter.Testing;
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

        // Registered as a singleton first, so AddListener's scoped registration is skipped.
        services.TryAddSingleton<PublishedEvents>();
        services.TryAddSingleton<Scenario>();
        services.AddEventEmitter().AddListener<PublishedEvents>();

        return services;
    }
}
