namespace ScreenMind.Core.Capture;

public readonly record struct ScreenRectangle(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

