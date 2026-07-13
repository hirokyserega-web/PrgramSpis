using FluentAssertions;
using NSubstitute;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Settings;
using ScreenMind.Platform.Windows.Capture;

namespace ScreenMind.Platform.Windows.Tests;

public sealed class WindowsScreenCaptureServiceTests
{
    [Fact]
    public async Task CaptureShouldRejectEmptyRegionBeforeReadingPixels()
    {
        ISettingsStore settingsStore = Substitute.For<ISettingsStore>();
        settingsStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(ScreenMindSettings.CreateDefault());
        WindowsScreenCaptureService service = new(settingsStore);

        Func<Task> action = async () => await service.CaptureAsync(
            new CaptureTarget.Region(new ScreenRectangle(0, 0, 0, 10)),
            CancellationToken.None);

        await action.Should().ThrowAsync<ScreenCaptureException>()
            .Where(exception => exception.Kind == ScreenCaptureErrorKind.InvalidRegion);
    }
}

