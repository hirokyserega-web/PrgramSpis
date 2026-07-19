using Avalonia;
using Avalonia.Threading;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Settings;
using ScreenMind.Core.State;

namespace ScreenMind.UI.Overlay;

public sealed class AvaloniaCompactOverlayService : ICompactOverlayService
{
    private readonly MainAnalysisStateMachine stateMachine;
    private readonly IAiOrchestrator orchestrator;
    private readonly IScreenCaptureService captureService;
    private readonly IImagePreprocessor preprocessor;
    private readonly ISettingsStore settingsStore;
    private readonly IChatSessionManager sessionManager;
    private readonly IChatWindowService chatWindowService;
    private readonly IWindowCaptureExclusionService exclusionService;

    public AvaloniaCompactOverlayService(
        MainAnalysisStateMachine stateMachine,
        IAiOrchestrator orchestrator,
        IScreenCaptureService captureService,
        IImagePreprocessor preprocessor,
        ISettingsStore settingsStore,
        IChatSessionManager sessionManager,
        IChatWindowService chatWindowService,
        IWindowCaptureExclusionService exclusionService)
    {
        this.stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        this.orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        this.captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        this.preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        this.chatWindowService = chatWindowService ?? throw new ArgumentNullException(nameof(chatWindowService));
        this.exclusionService = exclusionService ?? throw new ArgumentNullException(nameof(exclusionService));
    }

    public Task ShowAsync(CaptureTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        if (Application.Current is null)
        {
            return Task.FromException(new InvalidOperationException("Avalonia application is not initialized."));
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CompactOverlayWindow? window = null;
        CompactOverlayViewModel? viewModel = null;

        CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (viewModel is not null)
                {
                    viewModel.Cancel();
                }
                if (window is not null)
                {
                    window.Close();
                }
                completion.TrySetResult();
            });
        });

        _ = completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        Dispatcher.UIThread.Post(async () =>
        {
            ScreenImage? preCaptured = null;
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetResult();
                    return;
                }

                // Stealth path: do not Hide/Show the chat window (that flashes the active app).
                // SetWindowDisplayAffinity (WDA_EXCLUDEFROMCAPTURE) keeps ScreenMind off the bitmap
                // while the window stays exactly as the user sees it.
                chatWindowService.PrepareForStealthCapture();

                preCaptured = await captureService.CaptureAsync(target, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    preCaptured.Dispose();
                    completion.TrySetResult();
                    return;
                }

                // Analyze in chat without Activate / SetForegroundWindow.
                chatWindowService.AnalyzeImage(preCaptured);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                preCaptured?.Dispose();
                completion.TrySetException(exception);
            }
        });

        return completion.Task;
    }
}
