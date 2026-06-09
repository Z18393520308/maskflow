using Microsoft.Extensions.DependencyInjection;

namespace MaskFlow.Api.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMaskFlowInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IMaskFlowRepository, MySqlMaskFlowRepository>();
        services.AddSingleton<MaskFlowStore>();
        return services;
    }
}
