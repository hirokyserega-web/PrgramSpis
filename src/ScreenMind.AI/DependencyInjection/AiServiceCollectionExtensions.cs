using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Ai;
using ScreenMind.Core.State;

namespace ScreenMind.AI.DependencyInjection;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddScreenMindAi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<MainAnalysisStateMachine>();
        services.AddSingleton<ProviderConfigurationResolver>();
        services.AddSingleton<AiProviderRegistry>();
        services.AddSingleton<IAiOrchestrator, AiOrchestrator>();
        services.AddSingleton<IChatSessionManager, ChatSessionManager>();

        return services;
    }
}
