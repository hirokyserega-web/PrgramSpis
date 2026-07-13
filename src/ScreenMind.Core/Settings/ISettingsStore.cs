namespace ScreenMind.Core.Settings;

public interface ISettingsStore
{
    Task<ScreenMindSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ScreenMindSettings settings, CancellationToken cancellationToken);
}

