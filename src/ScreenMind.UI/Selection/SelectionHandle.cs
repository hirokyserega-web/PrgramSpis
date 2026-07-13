namespace ScreenMind.UI.Selection;

[Flags]
public enum SelectionHandle
{
    None = 0,
    Body = 1,
    Left = 2,
    Top = 4,
    Right = 8,
    Bottom = 16,
    TopLeft = Top | Left,
    TopRight = Top | Right,
    BottomLeft = Bottom | Left,
    BottomRight = Bottom | Right,
}

