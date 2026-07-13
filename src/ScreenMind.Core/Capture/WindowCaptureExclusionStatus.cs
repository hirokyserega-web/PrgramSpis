namespace ScreenMind.Core.Capture;

public sealed record WindowCaptureExclusionStatus(
    bool IsSupported,
    bool IsApplied,
    string? Reason = null);

