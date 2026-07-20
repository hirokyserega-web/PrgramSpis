namespace ScreenMind.Core.Ai;

public sealed record ExternalProxyCredentials(
    string Cookie = "",
    string? ApiMasterKey = null,
    string? SpaceId = null,
    string? UserId = null,
    string? UserName = null,
    string? UserEmail = null,
    string? BlockId = null);
