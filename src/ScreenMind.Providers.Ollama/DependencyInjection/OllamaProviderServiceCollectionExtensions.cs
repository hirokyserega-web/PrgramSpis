using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Ai;

namespace ScreenMind.Providers.Ollama.DependencyInjection;

public static class OllamaProviderServiceCollectionExtensions
{
    public const string HttpClientName = "ScreenMind.Providers.Ollama";

    public static IServiceCollection AddScreenMindOllamaProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<OllamaProvider>(HttpClientName);
        services.AddTransient<IAiProvider, OllamaProvider>();

        return services;
    }
}
