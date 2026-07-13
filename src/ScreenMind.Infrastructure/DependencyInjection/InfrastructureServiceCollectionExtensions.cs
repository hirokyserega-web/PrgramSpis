using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Settings;
using ScreenMind.Infrastructure.Imaging;
using ScreenMind.Infrastructure.Settings;

namespace ScreenMind.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddScreenMindInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SettingsStoreOptions>()
            .Bind(configuration.GetSection("SettingsStore"));
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IImagePreprocessor, SkiaSharpImagePreprocessor>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ScreenMind.Core.Ai.IExternalProxyManager, ScreenMind.Infrastructure.Ai.ExternalProxyManager>();

        return services;
    }
}
