using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Hotkeys;
using ScreenMind.Core.Settings;
using ScreenMind.Core.Tray;
using ScreenMind.Core.Ai;

namespace ScreenMind.App;

public sealed class AvaloniaApp : Application
{
    private const string ActiveWindowHotkeyId = "active_window";
    private const string RegionHotkeyId = "region";
    private const string MonitorHotkeyId = "monitor";
    private const string CleanChatHotkeyId = "clean_chat";
    private const string ClickThroughHotkeyId = "click_through";
    private const string EmergencyExitHotkeyId = "emergency_exit";
    private const string Prompt1HotkeyId = "prompt_1";
    private const string Prompt2HotkeyId = "prompt_2";
    private const string Prompt3HotkeyId = "prompt_3";

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
            Console.WriteLine("[AvaloniaApp] Starting background services...");
            ISettingsStore settingsStore = serviceProvider.GetRequiredService<ISettingsStore>();
            hotkeyService = serviceProvider.GetRequiredService<IHotkeyService>();
            trayService = serviceProvider.GetRequiredService<ITrayService>();

            IRegionSelectionService regionSelectionService = serviceProvider.GetRequiredService<IRegionSelectionService>();
            ICompactOverlayService compactOverlayService = serviceProvider.GetRequiredService<ICompactOverlayService>();
            IChatWindowService chatWindowService = serviceProvider.GetRequiredService<IChatWindowService>();

            // Load settings
            Console.WriteLine("[AvaloniaApp] Loading settings...");
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
                if (settings.ManagedProxies.Notion.Enabled)
                {
                    ExternalProxyCredentials credentials = new(
                        await secretStore.GetAsync("managed-notion-cookie", CancellationToken.None) ?? string.Empty,
                        await secretStore.GetAsync("managed-notion-api-master-key", CancellationToken.None),
                        await secretStore.GetAsync("managed-notion-space-id", CancellationToken.None),
                        await secretStore.GetAsync("managed-notion-user-id", CancellationToken.None),
                        await secretStore.GetAsync("managed-notion-user-name", CancellationToken.None),
                        await secretStore.GetAsync("managed-notion-user-email", CancellationToken.None),
                        await secretStore.GetAsync("managed-notion-block-id", CancellationToken.None));
                    await proxyManager.StartAsync("notion-2api", settings.ManagedProxies.Notion.Port, credentials, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AvaloniaApp] Failed to start managed background proxies on startup: {ex}");
                Debug.WriteLine($"Failed to start managed background proxies on startup: {ex}");
            }

            // Register global hotkeys from settings
            Console.WriteLine("[AvaloniaApp] Registering hotkeys...");
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
                        else if (e.RegistrationId == CleanChatHotkeyId)
                        {
                            chatWindowService.ToggleCleanChatMode();
                        }
                        else if (e.RegistrationId == ClickThroughHotkeyId)
                        {
                            chatWindowService.ToggleClickThroughMode();
                        }
                        else if (e.RegistrationId == Prompt1HotkeyId)
                        {
                            var freshSettings = await settingsStore.LoadAsync(CancellationToken.None);
                            string promptText = freshSettings.Hotkeys.PromptText1;
                            if (!string.IsNullOrWhiteSpace(promptText))
                            {
                                await compactOverlayService.ShowAsync(new CaptureTarget.MonitorWithCursor(), promptText, CancellationToken.None);
                            }
                        }
                        else if (e.RegistrationId == Prompt2HotkeyId)
                        {
                            var freshSettings = await settingsStore.LoadAsync(CancellationToken.None);
                            string promptText = freshSettings.Hotkeys.PromptText2;
                            if (!string.IsNullOrWhiteSpace(promptText))
                            {
                                await compactOverlayService.ShowAsync(new CaptureTarget.MonitorWithCursor(), promptText, CancellationToken.None);
                            }
                        }
                        else if (e.RegistrationId == Prompt3HotkeyId)
                        {
                            var freshSettings = await settingsStore.LoadAsync(CancellationToken.None);
                            string promptText = freshSettings.Hotkeys.PromptText3;
                            if (!string.IsNullOrWhiteSpace(promptText))
                            {
                                await compactOverlayService.ShowAsync(new CaptureTarget.MonitorWithCursor(), promptText, CancellationToken.None);
                            }
                        }
                        else if (e.RegistrationId == EmergencyExitHotkeyId)
                        {
                            // Hard emergency exit — works even if UI is stuck.
                            try
                            {
                                if (serviceProvider.GetService(typeof(ScreenMind.Core.Ai.IExternalProxyManager)) is IDisposable proxyManager)
                                {
                                    proxyManager.Dispose();
                                }
                            }
                            catch (Exception killEx)
                            {
                                Debug.WriteLine($"Emergency exit proxy stop failed: {killEx}");
                            }

                            try
                            {
                                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
                                {
                                    desktopLifetime.Shutdown();
                                }
                            }
                            catch
                            {
                                // ignore and force-kill process
                            }

                            Environment.Exit(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AvaloniaApp] Hotkey handler failed: {ex}");
                        Debug.WriteLine($"Hotkey handler failed: {ex}");
                    }
                });
            };

            await hotkeyService.RegisterAsync(new HotkeyRegistration(ActiveWindowHotkeyId, settings.Hotkeys.CaptureActiveWindow, "Capture Active Window"), CancellationToken.None);
            await hotkeyService.RegisterAsync(new HotkeyRegistration(RegionHotkeyId, settings.Hotkeys.CaptureRegion, "Capture Region"), CancellationToken.None);
            await hotkeyService.RegisterAsync(new HotkeyRegistration(MonitorHotkeyId, settings.Hotkeys.CaptureMonitor, "Capture Monitor"), CancellationToken.None);
            await hotkeyService.RegisterAsync(new HotkeyRegistration(CleanChatHotkeyId, settings.Hotkeys.ToggleCleanChat, "Toggle Clean Chat"), CancellationToken.None);
            await hotkeyService.RegisterAsync(new HotkeyRegistration(ClickThroughHotkeyId, settings.Hotkeys.ToggleClickThrough, "Toggle Click Through"), CancellationToken.None);
            await hotkeyService.RegisterAsync(new HotkeyRegistration(EmergencyExitHotkeyId, settings.Hotkeys.EmergencyExit, "Emergency Exit"), CancellationToken.None);

            if (settings.Hotkeys.PromptHotkey1.VirtualKey > 0)
                await hotkeyService.RegisterAsync(new HotkeyRegistration(Prompt1HotkeyId, settings.Hotkeys.PromptHotkey1, "Switch to Prompt 1"), CancellationToken.None);
            if (settings.Hotkeys.PromptHotkey2.VirtualKey > 0)
                await hotkeyService.RegisterAsync(new HotkeyRegistration(Prompt2HotkeyId, settings.Hotkeys.PromptHotkey2, "Switch to Prompt 2"), CancellationToken.None);
            if (settings.Hotkeys.PromptHotkey3.VirtualKey > 0)
                await hotkeyService.RegisterAsync(new HotkeyRegistration(Prompt3HotkeyId, settings.Hotkeys.PromptHotkey3, "Switch to Prompt 3"), CancellationToken.None);

            // Set tray commands
            Console.WriteLine("[AvaloniaApp] Setting tray commands...");
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
            Console.WriteLine("[AvaloniaApp] Showing chat window...");
            chatWindowService.Show();

            // Ensure tray/hotkeys/proxies stop when the desktop lifetime ends (X or tray Exit).
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                lifetime.Exit += OnDesktopExit;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AvaloniaApp] Failed to start background services: {ex}");
            Debug.WriteLine($"Failed to start background services: {ex}");
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            if (hotkeyService is IAsyncDisposable hotkeyDisposable)
            {
                hotkeyDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to dispose hotkeys on exit: {ex}");
        }

        try
        {
            if (trayService is IAsyncDisposable trayDisposable)
            {
                trayDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to dispose tray on exit: {ex}");
        }

        try
        {
            if (serviceProvider.GetService(typeof(ScreenMind.Core.Ai.IExternalProxyManager))
                is IDisposable proxyManager)
            {
                proxyManager.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to stop proxies on exit: {ex}");
        }
    }
}
