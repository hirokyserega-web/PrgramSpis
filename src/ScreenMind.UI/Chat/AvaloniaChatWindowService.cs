using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Hotkeys;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Privacy;
using ScreenMind.Core.Settings;

namespace ScreenMind.UI.Chat;

public sealed class AvaloniaChatWindowService : IChatWindowService, IDisposable
{
    private readonly IChatSessionManager sessionManager;
    private readonly IAiOrchestrator orchestrator;
    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore secretStore;
    private readonly IWindowCaptureExclusionService exclusionService;
    private readonly IWindowClickThroughService clickThroughService;
    private readonly IHotkeyService hotkeyService;
    private readonly IServiceProvider serviceProvider;

    private ChatWindow? activeWindow;

    public void Dispose()
    {
        activeWindow?.Dispose();
        activeWindow = null;
        GC.SuppressFinalize(this);
    }

    public AvaloniaChatWindowService(
        IChatSessionManager sessionManager,
        IAiOrchestrator orchestrator,
        ISettingsStore settingsStore,
        ISecretStore secretStore,
        IWindowCaptureExclusionService exclusionService,
        IWindowClickThroughService clickThroughService,
        IHotkeyService hotkeyService,
        IServiceProvider serviceProvider)
    {
        this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        this.orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.exclusionService = exclusionService ?? throw new ArgumentNullException(nameof(exclusionService));
        this.clickThroughService = clickThroughService ?? throw new ArgumentNullException(nameof(clickThroughService));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void Show()
    {
        if (Application.Current is null)
        {
            throw new InvalidOperationException("Avalonia application is not initialized.");
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (activeWindow is not null)
                {
                    if (activeWindow.WindowState == WindowState.Minimized)
                    {
                        activeWindow.WindowState = WindowState.Normal;
                    }
                    activeWindow.Show();
                    ApplyCaptureExclusion(activeWindow);
                    activeWindow.Activate();
                    return;
                }

                await EnsureWindowCreatedAsync(activate: true);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[AvaloniaChatWindowService] Failed to show chat window: {exception}");
                Debug.WriteLine($"Failed to show chat window: {exception}");
                activeWindow = null;
            }
        });
    }

    public void PrepareForStealthCapture()
    {
        // Must run synchronously when already on the UI thread so exclusion is applied
        // before GDI capture starts. Posting would race with CaptureAsync.
        void Apply()
        {
            try
            {
                if (activeWindow is null)
                {
                    return;
                }

                // Re-apply exclusion immediately before capture. Do not Hide/Show/Activate —
                // those transfer focus and produce a visible active-window flash.
                ApplyCaptureExclusion(activeWindow);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to prepare stealth capture: {ex}");
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private async Task EnsureWindowCreatedAsync(bool activate)
    {
        IExternalProxyManager proxyManager = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IExternalProxyManager>(serviceProvider);
        ChatViewModel viewModel = new(sessionManager, orchestrator, settingsStore, secretStore, hotkeyService, proxyManager);
        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.ClickThroughMode))
            {
                ApplyClickThrough(activeWindow);
            }
        };
        try
        {
            await viewModel.LoadWindowPreferencesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Failed to load window preferences: {exception}");
        }

        viewModel.NewCaptureRequested += async (s, e) =>
        {
            if (activeWindow is not null)
            {
                try
                {
                    viewModel.ErrorMessage = string.Empty;
                    PrepareForStealthCapture();
                    IRegionSelectionService regionSelectionService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IRegionSelectionService>(serviceProvider);
                    ICompactOverlayService compactOverlayService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ICompactOverlayService>(serviceProvider);
                    RegionSelectionResult result = await regionSelectionService.SelectAsync(CancellationToken.None);
                    if (result.IsConfirmed && result.Bounds is not null)
                    {
                        await compactOverlayService.ShowAsync(new CaptureTarget.Region(result.Bounds.Value), CancellationToken.None);
                    }
                }
                catch (Exception exception)
                {
                    viewModel.ErrorMessage = $"Screenshot failed: {exception.Message}";
                }
            }
        };

        viewModel.ActiveWindowCaptureRequested += async (_, _) =>
        {
            if (activeWindow is null)
            {
                return;
            }

            try
            {
                viewModel.ErrorMessage = string.Empty;
                PrepareForStealthCapture();
                ICompactOverlayService compactOverlayService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ICompactOverlayService>(serviceProvider);
                await compactOverlayService.ShowAsync(new CaptureTarget.ActiveWindow(), CancellationToken.None);
            }
            catch (Exception exception)
            {
                viewModel.ErrorMessage = $"Screenshot failed: {exception.Message}";
            }
        };

        viewModel.MonitorCaptureRequested += async (_, _) =>
        {
            if (activeWindow is null)
            {
                return;
            }

            try
            {
                viewModel.ErrorMessage = string.Empty;
                PrepareForStealthCapture();
                ICompactOverlayService compactOverlayService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ICompactOverlayService>(serviceProvider);
                await compactOverlayService.ShowAsync(new CaptureTarget.MonitorWithCursor(), CancellationToken.None);
            }
            catch (Exception exception)
            {
                viewModel.ErrorMessage = $"Screenshot failed: {exception.Message}";
            }
        };

        activeWindow = new ChatWindow(viewModel);
        activeWindow.Closed += (sender, args) => activeWindow = null;
        activeWindow.Opened += (_, _) =>
        {
            ApplyCaptureExclusion(activeWindow);
            ApplyClickThrough(activeWindow);
        };
        activeWindow.ShowActivated = activate;
        activeWindow.Show();
        ApplyCaptureExclusion(activeWindow);
        ApplyClickThrough(activeWindow);
        if (activate)
        {
            activeWindow.Activate();
        }
    }

    private void ApplyCaptureExclusion(ChatWindow? window)
    {
        if (window is null)
        {
            return;
        }

        IntPtr hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            _ = exclusionService.ApplyAsync(hwnd, CancellationToken.None);
        }
    }

    private void ApplyClickThrough(ChatWindow? window)
    {
        if (window is null)
        {
            return;
        }

        IntPtr hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            clickThroughService.SetClickThrough(hwnd, window.ViewModel.ClickThroughMode);
        }
    }

    public void Hide()
    {
        Dispatcher.UIThread.Post(() =>
        {
            activeWindow?.Hide();
        });
    }

    public void SetDefaultPrompt(string promptText)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (activeWindow?.ViewModel is not null)
            {
                activeWindow.ViewModel.DefaultPrompt = promptText;
            }
        });
    }

    public void ToggleCleanChatMode()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (activeWindow is null)
                {
                    await EnsureWindowCreatedAsync(activate: false);
                    for (int i = 0; i < 20 && activeWindow is null; i++)
                    {
                        await Task.Delay(50);
                    }
                }

                if (activeWindow is not null)
                {
                    await activeWindow.ViewModel.ToggleCleanChatModeAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to toggle clean chat mode: {ex}");
            }
        });
    }

    public void ToggleClickThroughMode()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (activeWindow is null)
                {
                    await EnsureWindowCreatedAsync(activate: false);
                    for (int i = 0; i < 20 && activeWindow is null; i++)
                    {
                        await Task.Delay(50);
                    }
                }

                if (activeWindow is not null)
                {
                    await activeWindow.ViewModel.ToggleClickThroughModeAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to toggle click-through mode: {ex}");
            }
        });
    }

    public void AnalyzeImage(ScreenImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (activeWindow is null)
                {
                    // Create window for analysis UI updates, but do not steal foreground focus.
                    await EnsureWindowCreatedAsync(activate: false);
                    for (int i = 0; i < 20 && activeWindow is null; i++)
                    {
                        await Task.Delay(50);
                    }
                }

                if (activeWindow is null)
                {
                    throw new InvalidOperationException("Chat window is not available.");
                }

                // Keep the window available for streaming UI updates, but never Activate /
                // SetForegroundWindow — that is what causes the visible active-window flash.
                if (!activeWindow.IsVisible)
                {
                    activeWindow.ShowActivated = false;
                    activeWindow.Show();
                }

                if (activeWindow.WindowState == WindowState.Minimized)
                {
                    // Restore without activating so the user's focused app stays in front.
                    activeWindow.WindowState = WindowState.Normal;
                }

                ApplyCaptureExclusion(activeWindow);
                await activeWindow.ViewModel.AnalyzeImageSilentlyAsync(image);
            }
            catch (Exception exception)
            {
                image.Dispose();
                Dispatcher.UIThread.Post(() =>
                {
                    if (exception is ObjectDisposedException)
                    {
                        activeWindow = null;
                    }

                    if (activeWindow is not null)
                    {
                        try
                        {
                            activeWindow.ViewModel.ErrorMessage = $"Screenshot failed: {exception.Message}";
                        }
                        catch
                        {
                            activeWindow = null;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Screenshot analysis failed: {exception}");
                    }
                });
            }
        });
    }
}
