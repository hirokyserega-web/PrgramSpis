using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Ai;

namespace ScreenMind.Providers.OpenAI.DependencyInjection;

public static class OpenAiProviderServiceCollectionExtensions
{
    public const string HttpClientName = "ScreenMind.Providers.OpenAI";

    public static IServiceCollection AddScreenMindOpenAiProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<OpenAiProvider>(HttpClientName);
        services.AddTransient<IAiProvider, OpenAiProvider>();

        return services;
    }
}
