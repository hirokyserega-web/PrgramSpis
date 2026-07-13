using FluentAssertions;
using ScreenMind.Core.Settings;

namespace ScreenMind.Core.Tests;

public sealed class SettingsValidationTests
{
    [Fact]
    public void DefaultsShouldProvideWindowAndPromptPreferences()
    {
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();

        settings.Ui.AlwaysOnTop.Should().BeTrue();
        settings.Profiles.Items.Should().OnlyContain(profile => !string.IsNullOrWhiteSpace(profile.SystemPrompt));
    }

    [Fact]
    public void DefaultSettingsShouldBeValid()
    {
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();

        settings.Validate().IsValid.Should().BeTrue();
    }

    [Fact]
    public void SettingsShouldRejectMissingSelectedProfile()
    {
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Profiles.SelectedProfileId = "missing";

        settings.Validate().IsValid.Should().BeFalse();
    }
}

