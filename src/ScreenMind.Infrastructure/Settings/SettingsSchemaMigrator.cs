using System;
using System.Collections.Generic;
using System.Linq;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Settings;

namespace ScreenMind.Infrastructure.Settings;

public static class SettingsSchemaMigrator
{
    public static ScreenMindSettings Migrate(ScreenMindSettings? settings)
    {
        ScreenMindSettings normalized = settings ?? ScreenMindSettings.CreateDefault();

        if (normalized.SchemaVersion <= 0)
        {
            normalized.SchemaVersion = 1;
        }

        normalized.General ??= new GeneralSettings();
        normalized.Ui ??= new UiSettings();
        normalized.Capture ??= new CaptureSettings();
        normalized.Hotkeys ??= new HotkeySettings();
        normalized.Providers ??= new ProviderSettings();
        normalized.Profiles ??= ProfileSettings.CreateDefault();
        normalized.Privacy ??= new PrivacySettings();
        normalized.Updates ??= new UpdateSettings();
        normalized.ManagedProxies ??= new ManagedProxiesSettings();

        normalized.ManagedProxies.Qwen ??= new ManagedProxyItem { Port = 3264 };
        normalized.ManagedProxies.Deepseek ??= new ManagedProxyItem { Port = 9655 };
        normalized.ManagedProxies.GlmKimi ??= new ManagedProxyItem { Port = 3265 };
        normalized.Profiles.Items ??= [];

        normalized.Ui.OverlayOpacity = ClampFinite(
            normalized.Ui.OverlayOpacity,
            UiSettings.MinOverlayOpacity,
            UiSettings.MaxOverlayOpacity,
            0.96d);
        normalized.Ui.UiScale = ClampFinite(
            normalized.Ui.UiScale,
            UiSettings.MinUiScale,
            UiSettings.MaxUiScale,
            1d);

        if (normalized.ManagedProxies.Qwen.Port <= 0)
        {
            normalized.ManagedProxies.Qwen.Port = 3264;
        }

        if (normalized.ManagedProxies.Deepseek.Port <= 0)
        {
            normalized.ManagedProxies.Deepseek.Port = 9655;
        }

        if (normalized.ManagedProxies.GlmKimi.Port <= 0)
        {
            normalized.ManagedProxies.GlmKimi.Port = 3265;
        }

        MergeDefaultProviderSettings(normalized.Providers);

        if (normalized.Profiles.Items.Count == 0)
        {
            normalized.Profiles = ProfileSettings.CreateDefault();
        }
        else
        {
            MergeDefaultProfiles(normalized.Profiles);
        }

        normalized.SchemaVersion = ScreenMindSettings.CurrentSchemaVersion;

        return normalized;
    }

    private static void MergeDefaultProviderSettings(ProviderSettings providers)
    {
        providers.Providers ??= new Dictionary<string, ProviderEndpointSettings>(StringComparer.OrdinalIgnoreCase);
        if (providers.Providers.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            providers.Providers = new Dictionary<string, ProviderEndpointSettings>(
                providers.Providers,
                StringComparer.OrdinalIgnoreCase);
        }

        ScreenMindSettings defaults = ScreenMindSettings.CreateDefault();
        foreach (KeyValuePair<string, ProviderEndpointSettings> pair in defaults.Providers.Providers)
        {
            if (!providers.Providers.ContainsKey(pair.Key))
            {
                providers.Providers[pair.Key] = pair.Value;
            }
        }
    }

    private static void MergeDefaultProfiles(ProfileSettings profiles)
    {
        profiles.Items ??= [];
        foreach (AiProfile defaultProfile in ProfileSettings.CreateDefault().Items)
        {
            if (!profiles.Items.Any(profile => string.Equals(profile.Id, defaultProfile.Id, StringComparison.OrdinalIgnoreCase)))
            {
                profiles.Items.Add(defaultProfile);
            }
        }
    }

    private static double ClampFinite(double value, double min, double max, double fallback)
    {
        if (!double.IsFinite(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
