namespace ScreenMind.AI;

public sealed record ProviderRuntimeConfiguration(
    string ProviderId,
    Uri BaseUri,
    string ModelId,
    string? ApiKey);

