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
        settings.Ui.OverlayOpacity.Should().BeInRange(UiSettings.MinOverlayOpacity, UiSettings.MaxOverlayOpacity);
        settings.Ui.UiScale.Should().Be(1d);
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

    [Theory]
    [InlineData(-0.01, 1.0)]
    [InlineData(1.01, 1.0)]
    [InlineData(0.96, 0.74)]
    [InlineData(0.96, 1.51)]
    public void SettingsShouldRejectInvalidUiPreferences(double opacity, double scale)
    {
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Ui.OverlayOpacity = opacity;
        settings.Ui.UiScale = scale;

        settings.Validate().IsValid.Should().BeFalse();
    }
}

