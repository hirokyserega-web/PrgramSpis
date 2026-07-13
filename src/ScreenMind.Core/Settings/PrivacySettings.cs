namespace ScreenMind.Core.Settings;

public sealed class PrivacySettings
{
    public bool WarnBeforeCloudUpload { get; set; }

    public List<string> BlockedProcessNames { get; set; } = [];

    public List<string> BlockedWindowTitleFragments { get; set; } = [];
}

