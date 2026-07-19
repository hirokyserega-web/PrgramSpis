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

    /// <summary>
    /// Toggle clean chat mode (messages + AI reply only). Default: Ctrl+Shift+H.
    /// </summary>
    public Hotkey ToggleCleanChat { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        0x48);

    /// <summary>
    /// Hard emergency exit (force process kill). Default: Ctrl+Shift+Alt+Q.
    /// </summary>
    public Hotkey EmergencyExit { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat,
        0x51);

    /// <summary>
    /// Toggle click-through/ghost mode. Default: Ctrl+Shift+G.
    /// </summary>
    public Hotkey ToggleClickThrough { get; set; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        0x47);

    public Hotkey PromptHotkey1 { get; set; } = new(HotkeyModifiers.None, 0);
    public string PromptText1 { get; set; } = string.Empty;

    public Hotkey PromptHotkey2 { get; set; } = new(HotkeyModifiers.None, 0);
    public string PromptText2 { get; set; } = string.Empty;

    public Hotkey PromptHotkey3 { get; set; } = new(HotkeyModifiers.None, 0);
    public string PromptText3 { get; set; } = string.Empty;
}

