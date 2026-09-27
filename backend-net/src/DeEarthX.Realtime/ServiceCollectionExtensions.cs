using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Realtime;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeEarthXRealtime(this IServiceCollection services)
    {
        services.AddSingleton<ISocketIOServer, SocketIOServer>();
        services.AddSingleton<IMessageService, MessageService>();
        return services;
    }
}
