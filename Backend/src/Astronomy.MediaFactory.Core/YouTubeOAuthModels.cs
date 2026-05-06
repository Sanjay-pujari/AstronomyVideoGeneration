namespace Astronomy.MediaFactory.Core;

public sealed record YouTubeOAuthTokenExchangeResult(
    string AccessToken,
    string RefreshToken,
    int? ExpiresIn,
    string? Scope,
    string? TokenType);

public sealed record YouTubeOAuthSetupResult(
    bool Success,
    string ChannelTitle,
    string ChannelId,
    bool RefreshTokenGenerated,
    string Message,
    string? RefreshTokenPreview = null);

public sealed record YouTubeOAuthStartResponse(
    bool Success,
    string AuthorizationUrl,
    string Message);

public sealed record YouTubeOAuthTokenFile(
    string ChannelId,
    string ChannelTitle,
    string RefreshToken,
    DateTimeOffset CreatedUtc);

public sealed record YouTubeOAuthDiagnosticResult(
    string ChannelTitle,
    string ChannelId,
    DateTimeOffset GeneratedUtc,
    bool RefreshTokenGenerated);
