using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventEmitter.Sample.Shipping;

public static class ShippingModule
{
    public static IServiceCollection AddShippingModule(this IServiceCollection services)
    {
        services.TryAddSingleton<CarrierGateway>();
        services.AddEventEmitter().AddListenersFromAssembly(typeof(ShippingModule).Assembly);
        return services;
    }
}
