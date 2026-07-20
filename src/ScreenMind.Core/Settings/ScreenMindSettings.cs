namespace ScreenMind.Core.Settings;

public sealed class ScreenMindSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public GeneralSettings General { get; set; } = new();

    public UiSettings Ui { get; set; } = new();

    public CaptureSettings Capture { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();

    public ProviderSettings Providers { get; set; } = CreateDefaultProviderSettings();

    public ProfileSettings Profiles { get; set; } = ProfileSettings.CreateDefault();

    public PrivacySettings Privacy { get; set; } = new();

    public UpdateSettings Updates { get; set; } = new();

    public ManagedProxiesSettings ManagedProxies { get; set; } = new();

    public static ScreenMindSettings CreateDefault() => new();

    public SettingsValidationResult Validate()
    {
        List<string> errors = [];

        if (SchemaVersion <= 0)
        {
            errors.Add("SchemaVersion must be positive.");
        }

        if (Capture.MaxPayloadBytes < 256 * 1024)
        {
            errors.Add("Capture.MaxPayloadBytes must be at least 262144 bytes.");
        }

        if (Profiles.Items.Count == 0)
        {
            errors.Add("At least one profile is required.");
        }

        if (string.IsNullOrWhiteSpace(Profiles.SelectedProfileId))
        {
            errors.Add("Profiles.SelectedProfileId is required.");
        }

        if (!Profiles.Items.Any(profile => string.Equals(
            profile.Id,
            Profiles.SelectedProfileId,
            StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Profiles.SelectedProfileId must reference an existing profile.");
        }

        if (Updates.CheckIntervalHours < 1)
        {
            errors.Add("Updates.CheckIntervalHours must be at least 1.");
        }

        if (!double.IsFinite(Ui.OverlayOpacity)
            || Ui.OverlayOpacity < UiSettings.MinOverlayOpacity
            || Ui.OverlayOpacity > UiSettings.MaxOverlayOpacity)
        {
            errors.Add("Ui.OverlayOpacity must be between 0 and 1.");
        }

        if (!double.IsFinite(Ui.UiScale)
            || Ui.UiScale < UiSettings.MinUiScale
            || Ui.UiScale > UiSettings.MaxUiScale)
        {
            errors.Add("Ui.UiScale must be between 0.75 and 1.5.");
        }

        return errors.Count == 0
            ? SettingsValidationResult.Valid
            : SettingsValidationResult.Invalid(errors);
    }

    private static ProviderSettings CreateDefaultProviderSettings()
    {
        ProviderSettings settings = new();
        settings.Providers["openai"] = new ProviderEndpointSettings
        {
            SecretName = "openai-api-key",
        };
        settings.Providers["openai-compatible"] = new ProviderEndpointSettings
        {
            BaseUrl = "http://localhost:3264/api/",
            SecretName = "openai-compatible-api-key",
        };
        settings.Providers["anthropic"] = new ProviderEndpointSettings
        {
            SecretName = "anthropic-api-key",
        };
        settings.Providers["gemini"] = new ProviderEndpointSettings
        {
            SecretName = "gemini-api-key",
        };
        settings.Providers["ollama"] = new ProviderEndpointSettings
        {
            BaseUrl = "http://localhost:11434",
        };
        return settings;
    }
}

public sealed class GeneralSettings
{
    public bool StartMinimized { get; set; }
}

public sealed class UiSettings
{
    public const double MinOverlayOpacity = 0d;
    public const double MaxOverlayOpacity = 1d;
    public const double MinUiScale = 0.75d;
    public const double MaxUiScale = 1.5d;

    public string Theme { get; set; } = "system";

    public double OverlayOpacity { get; set; } = 0.96d;

    public double UiScale { get; set; } = 1d;

    public bool ShowConsole { get; set; }

    public bool HideSidebar { get; set; }

    /// <summary>
    /// Minimal chat: hide compose/input bar; only user messages and AI replies remain visible.
    /// </summary>
    public bool CleanChatMode { get; set; }

    public bool AlwaysOnTop { get; set; } = true;

    public bool ClickThroughMode { get; set; }
}

public sealed class UpdateSettings
{
    public bool Enabled { get; set; } = true;

    public string Channel { get; set; } = "stable";

    public int CheckIntervalHours { get; set; } = 24;
}

public sealed class ManagedProxyItem
{
    public bool Enabled { get; set; }
    public int Port { get; set; }
}

public sealed class ManagedProxiesSettings
{
    public ManagedProxyItem Qwen { get; set; } = new() { Enabled = false, Port = 3264 };
    public ManagedProxyItem Deepseek { get; set; } = new() { Enabled = false, Port = 9655 };
    public ManagedProxyItem GlmKimi { get; set; } = new() { Enabled = false, Port = 3265 };
    public ManagedProxyItem Notion { get; set; } = new() { Enabled = false, Port = 8088 };
}

