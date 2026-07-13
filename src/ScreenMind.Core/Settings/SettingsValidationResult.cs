namespace ScreenMind.Core.Settings;

public sealed record SettingsValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static SettingsValidationResult Valid { get; } = new(true, Array.Empty<string>());

    public static SettingsValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}

