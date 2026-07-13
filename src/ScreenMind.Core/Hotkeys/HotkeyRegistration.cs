namespace ScreenMind.Core.Hotkeys;

public sealed record HotkeyRegistration(
    string Id,
    Hotkey Hotkey,
    string Description);

