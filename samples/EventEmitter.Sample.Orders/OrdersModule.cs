using Microsoft.Extensions.DependencyInjection;

namespace Codefinity.EventEmitter.Sample.Orders;

public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        services.AddScoped<OrderService>();

        // Orders only publishes, so it needs the publisher but registers no listeners.
        services.AddEventEmitter();
        return services;
    }
}
