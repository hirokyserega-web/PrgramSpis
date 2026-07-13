using System.Text;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Settings;
using ScreenMind.Core.State;

namespace ScreenMind.UI.Overlay;

public sealed partial class CompactOverlayViewModel : ObservableObject
{
    private static readonly TimeSpan StreamUiUpdateInterval = TimeSpan.FromMilliseconds(50);

    private readonly MainAnalysisStateMachine stateMachine;
    private readonly IAiOrchestrator orchestrator;
    private readonly IScreenCaptureService captureService;
    private readonly IImagePreprocessor preprocessor;
    private readonly ISettingsStore settingsStore;
    private readonly IChatSessionManager sessionManager;
    private readonly IChatWindowService chatWindowService;

    private CancellationTokenSource? activeCts;
    private CaptureTarget? currentTarget;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private bool isPrivacyWarningActive;

    public bool WasPrivacyWarningApproved { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private string statusText = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private string responseText = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private double overlayOpacity = 0.96d;

    public CompactOverlayViewModel(
        MainAnalysisStateMachine stateMachine,
        IAiOrchestrator orchestrator,
        IScreenCaptureService captureService,
        IImagePreprocessor preprocessor,
        ISettingsStore settingsStore,
        IChatSessionManager sessionManager,
        IChatWindowService chatWindowService)
    {
        this.stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        this.orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        this.captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        this.preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        this.chatWindowService = chatWindowService ?? throw new ArgumentNullException(nameof(chatWindowService));
    }

    public AnalysisStateKind CurrentStateKind => stateMachine.CurrentKind;

    public bool CanCancel =>
        CurrentStateKind is AnalysisStateKind.Capturing
            or AnalysisStateKind.Preprocessing
            or AnalysisStateKind.Sending
            or AnalysisStateKind.Streaming;

    public bool CanRetry =>
        !CanCancel && currentTarget is not null;

    public bool CanCopy =>
        !string.IsNullOrEmpty(ResponseText);

    public event EventHandler? CloseRequested;
    public event EventHandler? ExpandRequested;

    public void ReportCaptureFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ErrorMessage = exception.Message;
        StatusText = "Capture failed";
        UpdateProperties();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        if (CanCancel)
        {
            activeCts?.Cancel();
            try
            {
                stateMachine.Cancel();
            }
            catch (InvalidOperationException)
            {
                // Ignore invalid transitions during race conditions
            }
            StatusText = "Cancelled";
            try
            {
                stateMachine.ResetToIdle();
            }
            catch (InvalidOperationException)
            {
            }
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    public async Task RetryAsync()
    {
        if (currentTarget is not null && !CanCancel)
        {
            await StartAnalysisAsync(currentTarget, null, CancellationToken.None);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    public async Task CopyToClipboardAsync()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            if (topLevel?.Clipboard is not null)
            {
                await topLevel.Clipboard.SetTextAsync(ResponseText);
            }
        }
    }

    [RelayCommand]
    public void Expand()
    {
        chatWindowService.Show();
        ExpandRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    [RelayCommand]
    public void ApprovePrivacyWarning()
    {
        IsPrivacyWarningActive = false;
    }

    [RelayCommand]
    public void RejectPrivacyWarning()
    {
        IsPrivacyWarningActive = false;
    }

    [RelayCommand]
    public void Close()
    {
        Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public async Task StartAnalysisAsync(CaptureTarget target, ScreenImage? preCapturedImage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (CanCancel)
        {
            throw new InvalidOperationException("An analysis is already running.");
        }

        currentTarget = target;
        ResponseText = string.Empty;
        ErrorMessage = string.Empty;
        activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        ScreenImage? localCaptured = null;
        try
        {
            // 1. Capturing (if not pre-captured)
            if (preCapturedImage is not null)
            {
                localCaptured = preCapturedImage;
            }
            else
            {
                try
                {
                    stateMachine.ResetToIdle();
                }
                catch (InvalidOperationException)
                {
                }
                stateMachine.StartCapturing();
                StatusText = "Capturing screen...";
                UpdateProperties();

                localCaptured = await captureService.CaptureAsync(target, activeCts.Token);
            }

            try
            {
                ScreenMindSettings settings = await settingsStore.LoadAsync(activeCts.Token);
                OverlayOpacity = settings.Ui.OverlayOpacity;

                AiProfile profile = settings.Profiles.Items.FirstOrDefault(p => p.Id == settings.Profiles.SelectedProfileId)
                    ?? settings.Profiles.Items.FirstOrDefault()
                    ?? new AiProfile("universal", "Universal", "openai", "gpt-4o-mini", "Analyze the screenshot and answer clearly.");

                // 2. Preprocessing
                stateMachine.StartPreprocessing();
                StatusText = "Processing image...";
                UpdateProperties();

                ImagePreprocessingOptions options = new(
                    OutputFormat: settings.Capture.DefaultFormat,
                    MaxPayloadBytes: settings.Capture.MaxPayloadBytes,
                    JpegQuality: 88
                );
                ScreenImage preprocessed = await preprocessor.ProcessAsync(localCaptured, options, activeCts.Token);
                bool sessionCreated = false;

                try
                {
                    // 3. Sending / Connecting
                    stateMachine.StartSending();
                    StatusText = "Connecting to AI...";
                    UpdateProperties();

                    string question = "Analyze this screen.";
                    AiRequest request = new(profile, preprocessed, question, Array.Empty<AiMessage>());

                    // 4. Streaming
                    stateMachine.StartStreaming();
                    StatusText = "Streaming response...";
                    UpdateProperties();

                    StringBuilder responseBuilder = new();
                    DateTimeOffset lastResponseUpdate = DateTimeOffset.MinValue;

                    void PublishResponse(bool force)
                    {
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        if (!force && now - lastResponseUpdate < StreamUiUpdateInterval)
                        {
                            return;
                        }

                        lastResponseUpdate = now;
                        ResponseText = responseBuilder.ToString();
                    }

                    await foreach (AiStreamEvent ev in orchestrator.AnalyzeAsync(request, activeCts.Token).ConfigureAwait(false))
                    {
                        if (ev is AiStreamEvent.TextDelta delta)
                        {
                            responseBuilder.Append(delta.Text);
                            PublishResponse(force: false);
                        }
                        else if (ev is AiStreamEvent.Failed failed)
                        {
                            throw new ScreenMindAiException("AI streaming failed.", failed.Error);
                        }
                    }

                    PublishResponse(force: true);
                    stateMachine.Complete(new AiResult(ResponseText, new AiUsage()));
                    StatusText = "Analysis complete";

                    ChatSession session = sessionManager.CreateSession(profile, preprocessed);
                    session.Messages.Add(new AiMessage(AiMessageRole.User, question, DateTimeOffset.UtcNow));
                    session.Messages.Add(new AiMessage(AiMessageRole.Assistant, ResponseText, DateTimeOffset.UtcNow));
                    sessionCreated = true;
                }
                finally
                {
                    if (!sessionCreated)
                    {
                        preprocessed.Dispose();
                    }
                }
            }
            finally
            {
                if (preCapturedImage is null && localCaptured is not null)
                {
                    localCaptured.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                stateMachine.Cancel();
            }
            catch (InvalidOperationException)
            {
            }
            StatusText = "Cancelled";
            try
            {
                stateMachine.ResetToIdle();
            }
            catch (InvalidOperationException)
            {
            }
        }
        catch (Exception ex)
        {
            AiError error = ex is ScreenMindAiException aiEx
                ? aiEx.Error
                : new AiError(AiErrorKind.Unknown, ex.Message);

            try
            {
                stateMachine.Fail(error);
            }
            catch (InvalidOperationException)
            {
            }
            ErrorMessage = error.Message;
            StatusText = "Failed";
        }
        finally
        {
            activeCts?.Dispose();
            activeCts = null;
            UpdateProperties();
        }
    }

    private void UpdateProperties()
    {
        OnPropertyChanged(nameof(CurrentStateKind));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanCopy));
    }
}

public class ScreenMindAiException : Exception
{
    public AiError Error { get; }
    public ScreenMindAiException(string message, AiError error) : base(message)
    {
        Error = error;
    }
}
