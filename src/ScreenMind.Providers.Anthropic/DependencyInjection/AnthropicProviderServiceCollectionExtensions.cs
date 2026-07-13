using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Ai;

namespace ScreenMind.Providers.Anthropic.DependencyInjection;

public static class AnthropicProviderServiceCollectionExtensions
{
    public const string HttpClientName = "ScreenMind.Providers.Anthropic";

    public static IServiceCollection AddScreenMindAnthropicProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<AnthropicProvider>(HttpClientName);
        services.AddTransient<IAiProvider, AnthropicProvider>();

        return services;
    }
}
