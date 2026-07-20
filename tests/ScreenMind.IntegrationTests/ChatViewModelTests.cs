using FluentAssertions;
using NSubstitute;
using ScreenMind.AI;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Hotkeys;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Privacy;
using ScreenMind.Core.Settings;
using ScreenMind.Core.State;
using ScreenMind.UI.Chat;

namespace ScreenMind.IntegrationTests;

public sealed class ChatViewModelTests
{
    [Fact]
    public async Task SendMessageShouldHideQwenReasoningUntilFinalAnswer()
    {
        ChatSessionManager sessionManager = new();
        IAiOrchestrator orchestrator = Substitute.For<IAiOrchestrator>();
        ISettingsStore settingsStore = Substitute.For<ISettingsStore>();
        ISecretStore secretStore = Substitute.For<ISecretStore>();
        IHotkeyService hotkeyService = Substitute.For<IHotkeyService>();
        IExternalProxyManager proxyManager = Substitute.For<IExternalProxyManager>();
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Profiles.SelectedProfileId = "qwen3.8-max-preview";
        settingsStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(settings);

        IAsyncEnumerable<AiStreamEvent> StreamEvents()
        {
            return StreamEventsCore();

            static async IAsyncEnumerable<AiStreamEvent> StreamEventsCore()
            {
                await Task.Yield();
                yield return new AiStreamEvent.TextDelta("Пользователь просит... ", DateTimeOffset.UtcNow);
                yield return new AiStreamEvent.TextDelta("Окончательный ответ: ", DateTimeOffset.UtcNow);
                yield return new AiStreamEvent.TextDelta("Готово.", DateTimeOffset.UtcNow);
                yield return new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow);
            }
        }
        orchestrator.AnalyzeAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>()).Returns(StreamEvents());

        using ChatViewModel viewModel = new(sessionManager, orchestrator, settingsStore, secretStore, hotkeyService, proxyManager);
        viewModel.InputText = "Что видно?";

        await viewModel.SendMessageAsync();

        viewModel.ActiveMessages[^1].Content.Should().Be("Готово.");
        viewModel.ActiveMessages.Should().NotContain(message => message.Content.Contains("Пользователь просит", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeImageSilentlyAsyncShouldReportSettingsFailureWithoutThrowing()
    {
        ChatSessionManager sessionManager = new();
        IAiOrchestrator orchestrator = Substitute.For<IAiOrchestrator>();
        ISettingsStore settingsStore = Substitute.For<ISettingsStore>();
        ISecretStore secretStore = Substitute.For<ISecretStore>();
        IHotkeyService hotkeyService = Substitute.For<IHotkeyService>();
        IExternalProxyManager proxyManager = Substitute.For<IExternalProxyManager>();

        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ScreenMindSettings>(new InvalidOperationException("settings broken")));

        ChatViewModel viewModel = new(
            sessionManager,
            orchestrator,
            settingsStore,
            secretStore,
            hotkeyService,
            proxyManager);
        ScreenImage image = new([1, 2], "image/png", ScreenImageFormat.Png, 1, 1, DateTimeOffset.UtcNow);

        Func<Task> action = async () => await viewModel.AnalyzeImageSilentlyAsync(image);

        await action.Should().NotThrowAsync();
        viewModel.ErrorMessage.Should().Contain("settings broken");
        sessionManager.Sessions.Should().BeEmpty();
        Action readDisposedImage = () => _ = image.Bytes;
        readDisposedImage.Should().Throw<ObjectDisposedException>();
    }
}
