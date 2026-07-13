using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Ai;

namespace ScreenMind.Providers.Gemini.DependencyInjection;

public static class GeminiProviderServiceCollectionExtensions
{
    public const string HttpClientName = "ScreenMind.Providers.Gemini";

    public static IServiceCollection AddScreenMindGeminiProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<GeminiProvider>(HttpClientName);
        services.AddTransient<IAiProvider, GeminiProvider>();

        return services;
    }
}
