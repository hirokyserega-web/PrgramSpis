using FluentAssertions;
using NSubstitute;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Settings;
using ScreenMind.Core.State;
using ScreenMind.UI.Overlay;

namespace ScreenMind.IntegrationTests;

public sealed class CompactOverlayViewModelTests
{
    private readonly MainAnalysisStateMachine stateMachine;
    private readonly IAiOrchestrator orchestrator;
    private readonly IScreenCaptureService captureService;
    private readonly IImagePreprocessor preprocessor;
    private readonly ISettingsStore settingsStore;
    private readonly IChatSessionManager sessionManager;
    private readonly IChatWindowService chatWindowService;
    private readonly IClock clock;

    public CompactOverlayViewModelTests()
    {
        clock = Substitute.For<IClock>();
        clock.GetUtcNow().Returns(DateTimeOffset.UtcNow);
        stateMachine = new MainAnalysisStateMachine(clock);
        orchestrator = Substitute.For<IAiOrchestrator>();
        captureService = Substitute.For<IScreenCaptureService>();
        preprocessor = Substitute.For<IImagePreprocessor>();
        settingsStore = Substitute.For<ISettingsStore>();
        sessionManager = Substitute.For<IChatSessionManager>();
        sessionManager.CreateSession(Arg.Any<AiProfile>(), Arg.Any<ScreenImage>())
            .Returns(call => new ChatSession(call.Arg<AiProfile>(), call.Arg<ScreenImage>()));
        chatWindowService = Substitute.For<IChatWindowService>();
    }

    [Fact]
    public async Task StartAnalysisAsyncShouldStreamResponseAndComplete()
    {
        // Arrange
        CaptureTarget.ActiveWindow target = new CaptureTarget.ActiveWindow();
        ScreenImage capturedImage = new ScreenImage([1, 2], "image/png", ScreenImageFormat.Png, 10, 10, DateTimeOffset.UtcNow);
        ScreenImage preprocessedImage = new ScreenImage([3, 4], "image/png", ScreenImageFormat.Png, 5, 5, DateTimeOffset.UtcNow);
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Privacy.WarnBeforeCloudUpload = false;

        captureService.CaptureAsync(target, Arg.Any<CancellationToken>()).Returns(capturedImage);
        preprocessor.ProcessAsync(capturedImage, Arg.Any<ImagePreprocessingOptions>(), Arg.Any<CancellationToken>())
            .Returns(preprocessedImage);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(settings);

        async IAsyncEnumerable<AiStreamEvent> StreamEvents()
        {
            await Task.Yield();
            yield return new AiStreamEvent.TextDelta("Hello ", DateTimeOffset.UtcNow);
            yield return new AiStreamEvent.TextDelta("world!", DateTimeOffset.UtcNow);
            yield return new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow);
        }
        orchestrator.AnalyzeAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>()).Returns(StreamEvents());

        CompactOverlayViewModel viewModel = new CompactOverlayViewModel(stateMachine, orchestrator, captureService, preprocessor, settingsStore, sessionManager, chatWindowService);

        // Act
        await viewModel.StartAnalysisAsync(target, null, CancellationToken.None);

        // Assert
        viewModel.StatusText.Should().Be("Analysis complete");
        viewModel.ResponseText.Should().Be("Hello world!");
        viewModel.ErrorMessage.Should().BeEmpty();
        stateMachine.CurrentKind.Should().Be(AnalysisStateKind.Completed);
    }

    [Fact]
    public async Task StartAnalysisAsyncShouldHandleFailure()
    {
        // Arrange
        CaptureTarget.ActiveWindow target = new CaptureTarget.ActiveWindow();
        ScreenImage capturedImage = new ScreenImage([1, 2], "image/png", ScreenImageFormat.Png, 10, 10, DateTimeOffset.UtcNow);
        ScreenImage preprocessedImage = new ScreenImage([3, 4], "image/png", ScreenImageFormat.Png, 5, 5, DateTimeOffset.UtcNow);
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Privacy.WarnBeforeCloudUpload = false;

        captureService.CaptureAsync(target, Arg.Any<CancellationToken>()).Returns(capturedImage);
        preprocessor.ProcessAsync(capturedImage, Arg.Any<ImagePreprocessingOptions>(), Arg.Any<CancellationToken>())
            .Returns(preprocessedImage);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(settings);

        async IAsyncEnumerable<AiStreamEvent> StreamEvents()
        {
            await Task.Yield();
            yield return new AiStreamEvent.Failed(new AiError(AiErrorKind.Network, "No internet access"), DateTimeOffset.UtcNow);
        }
        orchestrator.AnalyzeAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>()).Returns(StreamEvents());

        CompactOverlayViewModel viewModel = new CompactOverlayViewModel(stateMachine, orchestrator, captureService, preprocessor, settingsStore, sessionManager, chatWindowService);

        // Act
        await viewModel.StartAnalysisAsync(target, null, CancellationToken.None);

        // Assert
        viewModel.StatusText.Should().Be("Failed");
        viewModel.ResponseText.Should().BeEmpty();
        viewModel.ErrorMessage.Should().Be("No internet access");
        stateMachine.CurrentKind.Should().Be(AnalysisStateKind.Failed);
    }

    [Fact]
    public async Task CancelShouldStopAnalysisAndReset()
    {
        // Arrange
        CaptureTarget.ActiveWindow target = new CaptureTarget.ActiveWindow();
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Privacy.WarnBeforeCloudUpload = false;
        TaskCompletionSource<ScreenImage> tcs = new TaskCompletionSource<ScreenImage>();

        captureService.CaptureAsync(target, Arg.Any<CancellationToken>())
            .Returns(async call => await tcs.Task);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(settings);

        CompactOverlayViewModel viewModel = new CompactOverlayViewModel(stateMachine, orchestrator, captureService, preprocessor, settingsStore, sessionManager, chatWindowService);

        // Act
        Task runTask = viewModel.StartAnalysisAsync(target, null, CancellationToken.None);

        // Wait to ensure we are in the Capturing state
        await Task.Delay(50);
        viewModel.CanCancel.Should().BeTrue();
        stateMachine.CurrentKind.Should().Be(AnalysisStateKind.Capturing);

        viewModel.Cancel();

        // Let the task finish
        tcs.SetCanceled();
        await runTask;

        // Assert
        viewModel.StatusText.Should().Be("Cancelled");
        stateMachine.CurrentKind.Should().Be(AnalysisStateKind.Idle);
    }
}
