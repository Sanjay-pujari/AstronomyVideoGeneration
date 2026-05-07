namespace Astronomy.MediaFactory.Core;

public sealed record MetaOAuthSetupResult(
    bool Success,
    string FacebookPageName,
    string FacebookPageId,
    string? InstagramUsername,
    string? InstagramBusinessAccountId,
    bool LongLivedTokenGenerated,
    string? Warning = null);

public sealed record MetaOAuthStartResponse(
    bool Success,
    string AuthorizationUrl,
    string Message);

public sealed record MetaOAuthPage(
    string Id,
    string Name,
    string AccessToken);

public sealed record MetaOAuthInstagramAccount(
    string? BusinessAccountId,
    string? Username,
    string? Warning = null);

public sealed record MetaOAuthTokenFile(
    string FacebookPageId,
    string FacebookPageName,
    string FacebookPageAccessToken,
    string? InstagramBusinessAccountId,
    string? InstagramUsername,
    string LongLivedUserAccessToken,
    DateTimeOffset GeneratedUtc);

public sealed record MetaOAuthDiagnosticResult(
    string SelectedPage,
    string PageId,
    string? InstagramUsername,
    string? InstagramBusinessId,
    DateTimeOffset? TokenExpirationEstimate,
    DateTimeOffset GeneratedUtc,
    string? Warning = null);
