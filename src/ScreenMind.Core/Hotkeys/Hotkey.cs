namespace ScreenMind.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
    NoRepeat = 16,
}

public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public override string ToString() => $"{Modifiers}+0x{VirtualKey:X2}";
}

