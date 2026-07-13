namespace ScreenMind.Core.Capture;

public enum ScreenCaptureErrorKind
{
    ActiveWindowUnavailable,
    OwnWindow,
    ClosedWindow,
    MinimizedWindow,
    ProtectedWindow,
    InaccessibleWindow,
    InvalidRegion,
    UnsupportedTarget,
    Cancelled,
    Unknown,
}

