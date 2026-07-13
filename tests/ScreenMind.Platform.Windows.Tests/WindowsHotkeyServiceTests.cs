using FluentAssertions;
using ScreenMind.Core.Hotkeys;
using ScreenMind.Platform.Windows.Hotkeys;

namespace ScreenMind.Platform.Windows.Tests;

public sealed class WindowsHotkeyServiceTests
{
    [Fact]
    public async Task RegisterShouldReturnConflictForDuplicateIdWithoutCrashing()
    {
        await using WindowsHotkeyService service = new();
        await service.PauseAsync(CancellationToken.None);

        HotkeyRegistration registration = new(
            "capture",
            new Hotkey(HotkeyModifiers.Control, 0x87),
            "Capture");

        HotkeyRegistrationResult first = await service.RegisterAsync(registration, CancellationToken.None);
        HotkeyRegistrationResult second = await service.RegisterAsync(registration, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeFalse();
        second.ConflictReason.Should().NotBeNullOrWhiteSpace();
    }
}

