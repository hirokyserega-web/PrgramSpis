using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScreenMind.Core.Settings;
using ScreenMind.Infrastructure.Settings;

namespace ScreenMind.IntegrationTests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task StoreShouldSaveAndRecoverFromBackupWhenMainFileIsCorrupted()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ScreenMind.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        JsonSettingsStore store = new(
            Options.Create(new SettingsStoreOptions { FilePath = path }),
            NullLogger<JsonSettingsStore>.Instance);

        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Ui.Theme = "dark";
        await store.SaveAsync(settings, CancellationToken.None);

        settings.Ui.Theme = "light";
        await store.SaveAsync(settings, CancellationToken.None);
        await File.WriteAllTextAsync(path, "{ broken json", CancellationToken.None);

        ScreenMindSettings recovered = await store.LoadAsync(CancellationToken.None);

        recovered.Ui.Theme.Should().Be("dark");
        File.Exists(path + ".bak").Should().BeTrue();
    }

    [Fact]
    public async Task StoreShouldNormalizeUiPreferences()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ScreenMind.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        JsonSettingsStore store = new(
            Options.Create(new SettingsStoreOptions { FilePath = path }),
            NullLogger<JsonSettingsStore>.Instance);

        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Ui.OverlayOpacity = -1d;
        settings.Ui.UiScale = 2d;

        await store.SaveAsync(settings, CancellationToken.None);
        ScreenMindSettings saved = await store.LoadAsync(CancellationToken.None);

        saved.Ui.OverlayOpacity.Should().Be(UiSettings.MinOverlayOpacity);
        saved.Ui.UiScale.Should().Be(UiSettings.MaxUiScale);
    }
}

