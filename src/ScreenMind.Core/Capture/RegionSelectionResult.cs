namespace ScreenMind.Core.Capture;

public sealed record RegionSelectionResult(
    bool IsConfirmed,
    ScreenRectangle? Bounds)
{
    public static RegionSelectionResult Cancelled { get; } = new(false, null);
}

