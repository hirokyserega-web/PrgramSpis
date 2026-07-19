using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Ai;
using ScreenMind.Providers.OpenAICompatible.Qwen;

namespace ScreenMind.Providers.OpenAICompatible.DependencyInjection;

public static class OpenAiCompatibleProviderServiceCollectionExtensions
{
    public const string HttpClientName = "ScreenMind.Providers.OpenAICompatible";

    public static IServiceCollection AddScreenMindOpenAiCompatibleProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<OpenAiCompatibleProvider>(HttpClientName);
        services.AddHttpClient<IQwenProxyClient, Qwen.QwenProxyClient>();
        services.AddTransient<IAiProvider, OpenAiCompatibleProvider>();

        return services;
    }
}
