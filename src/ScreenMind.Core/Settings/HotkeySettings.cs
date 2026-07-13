using ScreenMind.Core.Hotkeys;

namespace ScreenMind.Core.Settings;

public sealed class HotkeySettings
{
    public Hotkey CaptureActiveWindow { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        0x53);

    public Hotkey CaptureMonitor { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        0x44);

    public Hotkey CaptureRegion { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        0x41);
}

