using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Hotkeys;
using ScreenMind.Core.Settings;
using ScreenMind.Core.Tray;

namespace ScreenMind.App;

public sealed class AvaloniaApp : Application
{
    private const string ActiveWindowHotkeyId = "active_window";
    private const string RegionHotkeyId = "region";
    private const string MonitorHotkeyId = "monitor";

    private readonly IServiceProvider serviceProvider;
    private IHotkeyService? hotkeyService;
    private ITrayService? trayService;

    public AvaloniaApp(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            // Start hosting background services
            _ = StartBackgroundServicesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartBackgroundServicesAsync()
    {
        try
        {
            ISettingsStore settingsStore = serviceProvider.GetRequiredService<ISettingsStore>();
            hotkeyService = serviceProvider.GetRequiredService<IHotkeyService>();
            trayService = serviceProvider.GetRequiredService<ITrayService>();

            IRegionSelectionService regionSelectionService = serviceProvider.GetRequiredService<IRegionSelectionService>();
            ICompactOverlayService compactOverlayService = serviceProvider.GetRequiredService<ICompactOverlayService>();
            IChatWindowService chatWindowService = serviceProvider.GetRequiredService<IChatWindowService>();

            // Load settings
            ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);

            // Start enabled managed background proxies on startup
            try
            {
                Core.Ai.IExternalProxyManager proxyManager = serviceProvider.GetRequiredService<ScreenMind.Core.Ai.IExternalProxyManager>();
                Core.Privacy.ISecretStore secretStore = serviceProvider.GetRequiredService<ScreenMind.Core.Privacy.ISecretStore>();

                if (settings.ManagedProxies.Qwen.Enabled)
                {
                    string cookie = await secretStore.GetAsync("managed-qwen-cookie", CancellationToken.None) ?? string.Empty;
                    await proxyManager.StartAsync("FreeQwenApi", settings.ManagedProxies.Qwen.Port, cookie, CancellationToken.None);
                }
                if (settings.ManagedProxies.Deepseek.Enabled)
                {
                    string cookie = await secretStore.GetAsync("managed-deepseek-cookie", CancellationToken.None) ?? string.Empty;
                    await proxyManager.StartAsync("FreeDeepseekAPI", settings.ManagedProxies.Deepseek.Port, cookie, CancellationToken.None);
                }
                if (settings.ManagedProxies.GlmKimi.Enabled)
                {
                    string cookie = await secretStore.GetAsync("managed-kimi-cookie", CancellationToken.None) ?? string.Empty;
                    await proxyManager.StartAsync("FreeGLMKimiAPI", settings.ManagedProxies.GlmKimi.Port, cookie, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start managed background proxies on startup: {ex}");
            }

            // Register global hotkeys from settings
            hotkeyService.HotkeyPressed += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        if (e.RegistrationId == ActiveWindowHotkeyId)
                        {
                            await compactOverlayService.ShowAsync(new CaptureTarget.ActiveWindow(), CancellationToken.None);
                        }
                        else if (e.RegistrationId == RegionHotkeyId)
                        {
                            RegionSelectionResult regionResult = await regionSelectionService.SelectAsync(CancellationToken.None);
                            if (regionResult.IsConfirmed && regionResult.Bounds is not null)
                            {
                                await compactOverlayService.ShowAsync(new CaptureTarget.Region(regionResult.Bounds.Value), CancellationToken.None);
                            }
                        }
                        else if (e.RegistrationId == MonitorHotkeyId)
                        {
                            await compactOverlayService.ShowAsync(new CaptureTarget.MonitorWithCursor(), CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Hotkey handler failed: {ex}");
                    }
                });
            };

            await hotkeyService.RegisterAsync(new HotkeyRegistration(ActiveWindowHotkeyId, settings.Hotkeys.CaptureActiveWindow, "Capture Active Window"), CancellationToken.None);
            await hotkeyService.RegisterAsync(new HotkeyRegistration(RegionHotkeyId, settings.Hotkeys.CaptureRegion, "Capture Region"), CancellationToken.None);
            await hotkeyService.RegisterAsync(new HotkeyRegistration(MonitorHotkeyId, settings.Hotkeys.CaptureMonitor, "Capture Monitor"), CancellationToken.None);

            // Set tray commands
            List<TrayCommand> commands = new List<TrayCommand>
            {
                new TrayCommand("capture_region", "Capture Region", true, async ct =>
                {
                    RegionSelectionResult regionResult = await regionSelectionService.SelectAsync(ct);
                    if (regionResult.IsConfirmed && regionResult.Bounds is not null)
                    {
                        await compactOverlayService.ShowAsync(new CaptureTarget.Region(regionResult.Bounds.Value), ct);
                    }
                }),
                new TrayCommand("capture_window", "Capture Active Window", true, async ct =>
                {
                    await compactOverlayService.ShowAsync(new CaptureTarget.ActiveWindow(), ct);
                }),
                new TrayCommand("open_chat", "Open Chat & Settings", true, ct =>
                {
                    chatWindowService.Show();
                    return Task.CompletedTask;
                }),
                new TrayCommand("exit", "Exit", true, ct =>
                {
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                    return Task.CompletedTask;
                })
            };

            await trayService.SetCommandsAsync(commands, CancellationToken.None);

            // Open Chat & Settings window automatically on startup
            chatWindowService.Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start background services: {ex}");
        }
    }
}
