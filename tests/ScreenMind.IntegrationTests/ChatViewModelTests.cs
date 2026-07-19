using FluentAssertions;
using NSubstitute;
using ScreenMind.AI;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Hotkeys;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Privacy;
using ScreenMind.Core.Settings;
using ScreenMind.UI.Chat;

namespace ScreenMind.IntegrationTests;

public sealed class ChatViewModelTests
{
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
