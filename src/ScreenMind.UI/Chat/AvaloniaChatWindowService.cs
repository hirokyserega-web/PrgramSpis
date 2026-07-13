using Avalonia;
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
        IHotkeyService hotkeyService,
        IServiceProvider serviceProvider)
    {
        this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        this.orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.exclusionService = exclusionService ?? throw new ArgumentNullException(nameof(exclusionService));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void Show()
    {
        if (Application.Current is null)
        {
            throw new InvalidOperationException("Avalonia application is not initialized.");
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (activeWindow is not null)
            {
                if (!activeWindow.IsVisible)
                {
                    activeWindow.Show();
                }
                ApplyCaptureExclusion(activeWindow);
                activeWindow.Activate();
                return;
            }

            IExternalProxyManager proxyManager = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IExternalProxyManager>(serviceProvider);
            ChatViewModel viewModel = new(sessionManager, orchestrator, settingsStore, secretStore, hotkeyService, proxyManager);

            viewModel.NewCaptureRequested += async (s, e) =>
            {
                if (activeWindow is not null)
                {
                    try
                    {
                        viewModel.ErrorMessage = string.Empty;
                        activeWindow.Hide();
                        IRegionSelectionService regionSelectionService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IRegionSelectionService>(serviceProvider);
                        ICompactOverlayService compactOverlayService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ICompactOverlayService>(serviceProvider);
                        RegionSelectionResult result = await regionSelectionService.SelectAsync(CancellationToken.None);
                        if (result.IsConfirmed && result.Bounds is not null)
                            await compactOverlayService.ShowAsync(new CaptureTarget.Region(result.Bounds.Value), CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        viewModel.ErrorMessage = $"Screenshot failed: {exception.Message}";
                    }
                    finally
                    {
                        RestoreChatWindow();
                    }
                }
            };

            viewModel.ActiveWindowCaptureRequested += async (_, _) =>
            {
                if (activeWindow is null) return;
                try
                {
                    viewModel.ErrorMessage = string.Empty;
                    activeWindow.Hide();
                    await Task.Delay(80);
                    ICompactOverlayService compactOverlayService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ICompactOverlayService>(serviceProvider);
                    await compactOverlayService.ShowAsync(new CaptureTarget.ActiveWindow(), CancellationToken.None);
                }
                catch (Exception exception) { viewModel.ErrorMessage = $"Screenshot failed: {exception.Message}"; }
                finally { RestoreChatWindow(); }
            };

            viewModel.MonitorCaptureRequested += async (_, _) =>
            {
                if (activeWindow is null) return;
                try
                {
                    viewModel.ErrorMessage = string.Empty;
                    activeWindow.Hide();
                    await Task.Delay(80);
                    ICompactOverlayService compactOverlayService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ICompactOverlayService>(serviceProvider);
                    await compactOverlayService.ShowAsync(new CaptureTarget.MonitorWithCursor(), CancellationToken.None);
                }
                catch (Exception exception) { viewModel.ErrorMessage = $"Screenshot failed: {exception.Message}"; }
                finally { RestoreChatWindow(); }
            };

            activeWindow = new ChatWindow(viewModel);
            activeWindow.Closed += (sender, args) => activeWindow = null;
            activeWindow.Opened += (_, _) => ApplyCaptureExclusion(activeWindow);
            activeWindow.Show();
            ApplyCaptureExclusion(activeWindow);
            activeWindow.Activate();
        });
    }

    private void RestoreChatWindow()
    {
        if (activeWindow is null) return;
        activeWindow.Show();
        ApplyCaptureExclusion(activeWindow);
        activeWindow.Activate();
    }

    private void ApplyCaptureExclusion(ChatWindow window)
    {
        IntPtr hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            _ = exclusionService.ApplyAsync(hwnd, CancellationToken.None);
        }
    }

    public void Hide()
    {
        Dispatcher.UIThread.Post(() =>
        {
            activeWindow?.Hide();
        });
    }

    public void AnalyzeImage(ScreenImage image)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (activeWindow is null)
            {
                Show();
                for (int i = 0; i < 20 && activeWindow is null; i++)
                {
                    await Task.Delay(50);
                }
            }

            if (activeWindow is not null)
            {
                activeWindow.Show();
                ApplyCaptureExclusion(activeWindow);
                activeWindow.Activate();
                await activeWindow.ViewModel.AnalyzeImageSilentlyAsync(image);
            }
        });
    }
}
