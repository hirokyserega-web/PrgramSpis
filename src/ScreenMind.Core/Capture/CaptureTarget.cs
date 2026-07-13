namespace ScreenMind.Core.Capture;

public abstract record CaptureTarget
{
    private CaptureTarget()
    {
    }

    public sealed record ActiveWindow : CaptureTarget;

    public sealed record MonitorWithCursor : CaptureTarget;

    public sealed record Monitor(IntPtr Handle) : CaptureTarget;

    public sealed record Region(ScreenRectangle Bounds) : CaptureTarget;
}

