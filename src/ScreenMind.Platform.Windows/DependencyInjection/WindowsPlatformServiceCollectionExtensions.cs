using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Diagnostics;
using ScreenMind.Core.Hotkeys;
using ScreenMind.Core.Privacy;
using ScreenMind.Core.Tray;
using ScreenMind.Platform.Windows.Capture;
using ScreenMind.Platform.Windows.Diagnostics;
using ScreenMind.Platform.Windows.Hotkeys;
using ScreenMind.Platform.Windows.Privacy;
using ScreenMind.Platform.Windows.Tray;

namespace ScreenMind.Platform.Windows.DependencyInjection;

public static class WindowsPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddScreenMindWindowsPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IScreenCaptureService, WindowsScreenCaptureService>();
        services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
        services.AddSingleton<ISecretStore, WindowsCredentialSecretStore>();
        services.AddSingleton<ITrayService, WindowsTrayService>();
        services.AddSingleton<IWindowCaptureExclusionService, WindowsWindowCaptureExclusionService>();
        services.AddSingleton<IDiagnosticsService, WindowsDiagnosticsService>();

        return services;
    }
}
