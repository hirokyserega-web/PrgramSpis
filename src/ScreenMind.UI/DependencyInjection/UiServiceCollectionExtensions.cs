using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Capture;
using ScreenMind.UI.Chat;
using ScreenMind.UI.Overlay;
using ScreenMind.UI.Selection;

namespace ScreenMind.UI.DependencyInjection;

public static class UiServiceCollectionExtensions
{
    public static IServiceCollection AddScreenMindUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRegionSelectionService, AvaloniaRegionSelectionService>();
        services.AddSingleton<ICompactOverlayService, AvaloniaCompactOverlayService>();
        services.AddSingleton<IChatWindowService, AvaloniaChatWindowService>();

        return services;
    }
}
