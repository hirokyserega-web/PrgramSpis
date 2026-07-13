using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenMind.Core.Settings;

namespace ScreenMind.Infrastructure.Settings;

public sealed partial class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly ILogger<JsonSettingsStore> logger;
    private readonly string filePath;

    public JsonSettingsStore(
        IOptions<SettingsStoreOptions> options,
        ILogger<JsonSettingsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.logger = logger;
        filePath = string.IsNullOrWhiteSpace(options.Value.FilePath)
            ? GetDefaultPath()
            : options.Value.FilePath;
    }

    public async Task<ScreenMindSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            ScreenMindSettings defaults = ScreenMindSettings.CreateDefault();
            await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        try
        {
            return await LoadFromFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            SettingsFileLoadFailed(logger, exception);
            return await RecoverFromBackupAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SaveAsync(ScreenMindSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        ScreenMindSettings migrated = SettingsSchemaMigrator.Migrate(settings);
        SettingsValidationResult validation = migrated.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Settings validation failed: " + string.Join("; ", validation.Errors));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        string tempPath = filePath + ".tmp";
        await using (FileStream stream = new(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, migrated, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        string backupPath = GetBackupPath(filePath);
        if (File.Exists(filePath))
        {
            File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, filePath);
        }
    }

    private async Task<ScreenMindSettings> RecoverFromBackupAsync(CancellationToken cancellationToken)
    {
        string backupPath = GetBackupPath(filePath);
        if (File.Exists(backupPath))
        {
            try
            {
                ScreenMindSettings settings = await LoadFromFileAsync(backupPath, cancellationToken)
                    .ConfigureAwait(false);

                string corruptPath = filePath + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                File.Move(filePath, corruptPath, overwrite: true);
                File.Copy(backupPath, filePath, overwrite: true);
                SettingsRestoredFromBackup(logger);
                return settings;
            }
            catch (Exception exception) when (exception is JsonException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
            {
                SettingsBackupLoadFailed(logger, exception);
            }
        }

        ScreenMindSettings defaults = ScreenMindSettings.CreateDefault();
        string brokenPath = filePath + ".broken-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (File.Exists(filePath))
        {
            File.Move(filePath, brokenPath, overwrite: true);
        }

        await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
        return defaults;
    }

    private static async Task<ScreenMindSettings> LoadFromFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);

        ScreenMindSettings? settings = await JsonSerializer.DeserializeAsync<ScreenMindSettings>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        ScreenMindSettings migrated = SettingsSchemaMigrator.Migrate(settings);
        SettingsValidationResult validation = migrated.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Settings validation failed: " + string.Join("; ", validation.Errors));
        }

        return migrated;
    }

    private static string GetDefaultPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "ScreenMind", "settings.json");
    }

    private static string GetBackupPath(string path) => path + ".bak";

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Settings file could not be loaded. Trying backup.")]
    private static partial void SettingsFileLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Settings restored from backup.")]
    private static partial void SettingsRestoredFromBackup(ILogger logger);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Settings backup could not be loaded. Recreating defaults.")]
    private static partial void SettingsBackupLoadFailed(ILogger logger, Exception exception);
}
