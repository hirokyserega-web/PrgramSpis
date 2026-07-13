using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Settings;

public sealed class CaptureSettings
{
    public ScreenImageFormat DefaultFormat { get; set; } = ScreenImageFormat.Png;

    public int MaxPayloadBytes { get; set; } = 8 * 1024 * 1024;

    public bool IncludeCursor { get; set; } = true;

    public bool SilentMode { get; set; } = true;

    public string DefaultPrompt { get; set; } = "What is on my screen?";
}

