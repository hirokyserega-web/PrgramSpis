namespace ScreenMind.Core.Settings;

public sealed class ProviderSettings
{
    public string SelectedProviderId { get; set; } = "openai";

    public Dictionary<string, ProviderEndpointSettings> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderEndpointSettings
{
    public string? BaseUrl { get; set; }

    public string? ModelId { get; set; }

    public string? SecretName { get; set; }
}

