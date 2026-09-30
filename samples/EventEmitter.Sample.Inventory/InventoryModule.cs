using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Sample.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        // Picks up InventoryListener, which is internal to this assembly.
        services.AddEventEmitter().AddListenersFromAssembly(typeof(InventoryModule).Assembly);
        return services;
    }
}
