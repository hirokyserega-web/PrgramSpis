using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using ScreenMind.Core.Diagnostics;
using ScreenMind.Core.Privacy;
using ScreenMind.Core.Settings;

namespace ScreenMind.Platform.Windows.Diagnostics;

public sealed class WindowsDiagnosticsService : IDiagnosticsService
{
    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore secretStore;

    public WindowsDiagnosticsService(ISettingsStore settingsStore, ISecretStore secretStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public async Task<string> GetDiagnosticReportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("=== ScreenMind Diagnostics ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {DateTimeOffset.UtcNow:u}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS Version: {Environment.OSVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"64-Bit OS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Runtime: {Environment.Version}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Machine Name: {Environment.MachineName}");

        // Process info
        using Process currentProcess = Process.GetCurrentProcess();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Process ID: {currentProcess.Id}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Thread Count: {currentProcess.Threads.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Working Set: {currentProcess.WorkingSet64 / 1024 / 1024} MB");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Private Memory: {currentProcess.PrivateMemorySize64 / 1024 / 1024} MB");

        // Monitors
        sb.AppendLine("--- Monitors ---");
        Screen[] screens = Screen.AllScreens;
        sb.AppendLine(CultureInfo.InvariantCulture, $"Monitor Count: {screens.Length}");
        for (int i = 0; i < screens.Length; i++)
        {
            Screen screen = screens[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Monitor {i}: {screen.DeviceName} (Bounds: {screen.Bounds}, Primary: {screen.Primary})");
        }

        // Settings / Providers
        sb.AppendLine("--- Provider Configuration ---");
        try
        {
            ScreenMindSettings settings = await settingsStore.LoadAsync(cancellationToken);
            sb.AppendLine(CultureInfo.InvariantCulture, $"Active Profile: {settings.Profiles.SelectedProfileId}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Theme: {settings.Ui.Theme}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Check Exclusions Count: {settings.Privacy.BlockedProcessNames.Count}");

            foreach (KeyValuePair<string, ProviderEndpointSettings> kvp in settings.Providers.Providers)
            {
                string providerName = kvp.Key;
                ProviderEndpointSettings provider = kvp.Value;
                bool hasKey = false;
                if (!string.IsNullOrEmpty(provider?.SecretName))
                {
                    hasKey = await secretStore.ExistsAsync(provider.SecretName, cancellationToken);
                }
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Provider '{providerName}': Configured={provider is not null}, API Key Set={hasKey}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Error loading settings details: {ex.Message}");
        }

        return sb.ToString();
    }
}
