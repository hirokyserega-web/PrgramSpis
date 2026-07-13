namespace ScreenMind.Core.Capture;

public sealed class ScreenCaptureException : Exception
{
    public ScreenCaptureException(ScreenCaptureErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public ScreenCaptureErrorKind Kind { get; }
}

