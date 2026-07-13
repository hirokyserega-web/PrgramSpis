namespace ScreenMind.Core.Hotkeys;

public sealed record HotkeyRegistrationResult(
    bool IsSuccess,
    string? ConflictReason = null)
{
    public static HotkeyRegistrationResult Success { get; } = new(true);
}

