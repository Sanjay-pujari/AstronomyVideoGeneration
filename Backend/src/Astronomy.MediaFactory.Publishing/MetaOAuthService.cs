using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class MetaOAuthService : IMetaOAuthService
{
    public const string AuthorizationEndpoint = "https://www.facebook.com/v23.0/dialog/oauth";
    public const string GraphEndpoint = "https://graph.facebook.com/v23.0";
    public const string NoPagesMessage = "No manageable Facebook pages found.";
    public const string PageMismatchMessage = "Authenticated Facebook page does not match configured expected page.";
    public const string InstagramMismatchMessage = "Authenticated Instagram username does not match configured expected Instagram username.";
    public const string InstagramNotLinkedWarning = "Facebook page is not linked to Instagram Business account.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly MetaOptions _options;
    private readonly ILogger<MetaOAuthService> _logger;

    public MetaOAuthService(HttpClient httpClient, IOptions<MetaOptions> options, ILogger<MetaOAuthService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string BuildAuthorizationUrl()
    {
        if (string.IsNullOrWhiteSpace(_options.AppId))
        {
            throw new InvalidOperationException("Meta OAuth app id is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException("Meta OAuth redirect uri is required.");
        }

        var scopes = GetScopes();
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.AppId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(",", scopes)
        };

        return BuildUri(AuthorizationEndpoint, query);
    }

    public async Task<MetaOAuthSetupResult> CompleteSetupAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("OAuth authorization code is required.", nameof(code));
        }

        ValidateTokenExchangeConfiguration();

        var shortToken = await ExchangeCodeAsync(code, cancellationToken);
        var longToken = await ExchangeLongLivedTokenAsync(shortToken.AccessToken, cancellationToken);
        var grantedPermissions = await ReadAndValidateGrantedPermissionsAsync(longToken.AccessToken, cancellationToken);
        var generatedUtc = DateTimeOffset.UtcNow;
        var pages = await DiscoverPagesAsync(longToken.AccessToken, cancellationToken);
        var selectedPage = SelectExpectedPage(pages);
        var instagram = await DiscoverInstagramAccountAsync(selectedPage, longToken.AccessToken, cancellationToken);
        ValidateExpectedInstagram(instagram);

        await PersistTokenFileAsync(selectedPage, instagram, longToken.AccessToken, generatedUtc,
            longToken.ExpiresIn.HasValue ? generatedUtc.AddSeconds(longToken.ExpiresIn.Value) : generatedUtc.AddDays(60),
            grantedPermissions, cancellationToken);
        await WriteDiagnosticsAsync(selectedPage, instagram, generatedUtc, longToken.ExpiresIn, grantedPermissions, cancellationToken);

        _logger.LogInformation("Meta OAuth completed for page {PageName} ({PageId}); authorization persisted.",
            selectedPage.Name, selectedPage.Id);

        return new MetaOAuthSetupResult(
            Success: true,
            FacebookPageName: selectedPage.Name,
            FacebookPageId: selectedPage.Id,
            InstagramUsername: instagram.Username,
            InstagramBusinessAccountId: instagram.BusinessAccountId,
            LongLivedTokenGenerated: true,
            Warning: instagram.Warning,
            InstagramName: instagram.Name,
            InstagramAccountType: instagram.AccountType,
            GrantedPermissions: grantedPermissions);
    }

    private void ValidateTokenExchangeConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppSecret))
        {
            throw new InvalidOperationException("Meta OAuth app id and app secret are required.");
        }

        if (string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException("Meta OAuth redirect uri is required.");
        }
    }

    private async Task<TokenResponse> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var token = await GetJsonAsync<TokenResponse>("/oauth/access_token", new Dictionary<string, string>
        {
            ["client_id"] = _options.AppId,
            ["client_secret"] = _options.AppSecret,
            ["redirect_uri"] = _options.RedirectUri,
            ["code"] = code
        }, "Meta OAuth token exchange", cancellationToken);

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Meta OAuth token exchange did not return an access token.");
        }

        return token;
    }

    private async Task<TokenResponse> ExchangeLongLivedTokenAsync(string shortToken, CancellationToken cancellationToken)
    {
        var token = await GetJsonAsync<TokenResponse>("/oauth/access_token", new Dictionary<string, string>
        {
            ["grant_type"] = "fb_exchange_token",
            ["client_id"] = _options.AppId,
            ["client_secret"] = _options.AppSecret,
            ["fb_exchange_token"] = shortToken
        }, "Meta long-lived token exchange", cancellationToken);

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Meta long-lived token exchange did not return an access token.");
        }

        return token;
    }

    private async Task<IReadOnlyList<MetaOAuthPage>> DiscoverPagesAsync(string longLivedUserToken, CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<PageListResponse>("/me/accounts", new Dictionary<string, string>
        {
            ["fields"] = "id,name,access_token",
            ["access_token"] = longLivedUserToken
        }, "Meta Facebook page discovery", cancellationToken);

        var pages = response.Data
            .Where(page => !string.IsNullOrWhiteSpace(page.Id) && !string.IsNullOrWhiteSpace(page.Name) && !string.IsNullOrWhiteSpace(page.AccessToken))
            .Select(page => new MetaOAuthPage(page.Id!, page.Name!, page.AccessToken!))
            .ToArray();

        if (pages.Length == 0)
        {
            throw new InvalidOperationException(NoPagesMessage);
        }

        return pages;
    }

    private MetaOAuthPage SelectExpectedPage(IReadOnlyList<MetaOAuthPage> pages)
    {
        var expectedId = _options.ExpectedFacebookPageId?.Trim();
        var expectedName = _options.ExpectedFacebookPageName?.Trim();
        MetaOAuthPage? selected = null;

        if (!string.IsNullOrWhiteSpace(expectedId))
        {
            selected = pages.FirstOrDefault(page => string.Equals(page.Id, expectedId, StringComparison.Ordinal));
            if (selected is null)
            {
                throw new InvalidOperationException(PageMismatchMessage);
            }
        }
        else if (!string.IsNullOrWhiteSpace(expectedName))
        {
            selected = pages.FirstOrDefault(page => string.Equals(page.Name, expectedName, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                throw new InvalidOperationException(PageMismatchMessage);
            }
        }
        else
        {
            selected = pages[0];
        }

        if (!string.IsNullOrWhiteSpace(expectedName) && !string.Equals(selected.Name, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(PageMismatchMessage);
        }

        return selected;
    }

    private async Task<MetaOAuthInstagramAccount> DiscoverInstagramAccountAsync(MetaOAuthPage selectedPage,
        string longLivedUserToken, CancellationToken cancellationToken)
    {
        var page = await GetJsonAsync<PageInstagramResponse>($"/{Uri.EscapeDataString(selectedPage.Id)}", new Dictionary<string, string>
        {
            ["fields"] = "instagram_business_account",
            // A Page token proves the Page-to-Instagram relationship.
            ["access_token"] = selectedPage.AccessToken
        }, "Meta Instagram business account discovery", cancellationToken);

        var businessId = page.InstagramBusinessAccount?.Id;
        if (string.IsNullOrWhiteSpace(businessId))
        {
            _logger.LogWarning(InstagramNotLinkedWarning);
            return new MetaOAuthInstagramAccount(null, null, InstagramNotLinkedWarning);
        }

        var instagram = await GetJsonAsync<InstagramUsernameResponse>($"/{Uri.EscapeDataString(businessId)}", new Dictionary<string, string>
        {
            ["fields"] = "username,name,account_type",
            // Instagram object reads use the same long-lived user-token authority as TokenHealth.
            ["access_token"] = longLivedUserToken
        }, "Meta Instagram username discovery", cancellationToken);

        return new MetaOAuthInstagramAccount(businessId, instagram.Username, Name: instagram.Name,
            AccountType: instagram.AccountType);
    }

    private async Task<IReadOnlyList<string>> ReadAndValidateGrantedPermissionsAsync(string userToken, CancellationToken cancellationToken)
    {
        var debug = await GetJsonAsync<DebugTokenResponse>("/debug_token", new Dictionary<string, string>
        {
            ["input_token"] = userToken,
            ["access_token"] = $"{_options.AppId}|{_options.AppSecret}"
        }, "Meta granted-permission validation", cancellationToken);
        if (debug.Data?.IsValid != true)
            throw new InvalidOperationException("Meta token validation reported an invalid authorization.");
        var granted = (debug.Data.Scopes ?? []).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var missing = GetScopes().Except(granted, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Meta authorization is missing required permissions: {string.Join(", ", missing)}.");
        return granted;
    }

    private void ValidateExpectedInstagram(MetaOAuthInstagramAccount instagram)
    {
        var expectedId = _options.ExpectedInstagramAccountId?.Trim();
        if (!string.IsNullOrWhiteSpace(expectedId)
            && !string.Equals(instagram.BusinessAccountId, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Authenticated linked Instagram account does not match configured expected Instagram account id.");
        }

        var expectedUsername = _options.ExpectedInstagramUsername?.Trim();
        if (string.IsNullOrWhiteSpace(expectedUsername))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(instagram.Username) || !string.Equals(instagram.Username, expectedUsername, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(InstagramMismatchMessage);
        }
    }

    private async Task<T> GetJsonAsync<T>(string path, Dictionary<string, string> query, string operation, CancellationToken cancellationToken)
    {
        var url = BuildUri(GraphEndpoint + path, query);
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await MetaGraphError.ReadAsync(response, cancellationToken);
            _logger.LogWarning(
                "{Operation} failed: HTTP {Status}; type={ErrorType}; code={ErrorCode}; subcode={ErrorSubcode}; message={ErrorMessage}; userTitle={UserTitle}; userMessage={UserMessage}; fbtrace_id={TraceId}.",
                operation, error.HttpStatus, error.Type, error.Code, error.ErrorSubcode, error.Message,
                error.ErrorUserTitle, error.ErrorUserMessage, error.FbTraceId);
            throw new InvalidOperationException(error.ToSafeMessage(operation));
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException($"{operation} returned an empty response.");
    }

    private async Task PersistTokenFileAsync(MetaOAuthPage page, MetaOAuthInstagramAccount instagram, string longLivedUserToken,
        DateTimeOffset generatedUtc, DateTimeOffset expiresUtc, IReadOnlyList<string> grantedPermissions, CancellationToken cancellationToken)
    {
        var payload = new MetaOAuthTokenFile(page.Id, page.Name, page.AccessToken, instagram.BusinessAccountId, instagram.Username,
            longLivedUserToken, generatedUtc, expiresUtc, GrantedPermissions: grantedPermissions,
            InstagramName: instagram.Name, InstagramAccountType: instagram.AccountType);
        var path = ResolveTokenFilePath();
        await AtomicTokenFile.WriteAsync(path, payload, JsonOptions, cancellationToken);
    }

    private async Task WriteDiagnosticsAsync(MetaOAuthPage page, MetaOAuthInstagramAccount instagram, DateTimeOffset generatedUtc,
        int? expiresIn, IReadOnlyList<string> grantedPermissions, CancellationToken cancellationToken)
    {
        var expirationEstimate = expiresIn.HasValue ? generatedUtc.AddSeconds(expiresIn.Value) : generatedUtc.AddDays(60);
        var payload = new MetaOAuthDiagnosticResult(page.Name, page.Id, instagram.Username, instagram.BusinessAccountId,
            expirationEstimate, generatedUtc, instagram.Warning, instagram.Name, instagram.AccountType, grantedPermissions);
        var path = Path.Combine(Path.GetDirectoryName(ResolveTokenFilePath())!, "meta-oauth-result.json");
        await AtomicTokenFile.WriteAsync(path, payload, JsonOptions, cancellationToken);
    }

    private string ResolveTokenFilePath()
        => string.IsNullOrWhiteSpace(_options.TokenFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "meta-oauth-token.json")
            : Path.GetFullPath(_options.TokenFilePath);

    private IReadOnlyList<string> GetScopes()
        => (_options.Scopes is { Count: > 0 } ? _options.Scopes : new MetaOptions().Scopes)
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildUri(string endpoint, Dictionary<string, string> query)
    {
        var builder = new UriBuilder(endpoint)
        {
            Query = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"))
        };

        return builder.Uri.ToString();
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
    }

    private sealed class PageListResponse
    {
        [JsonPropertyName("data")]
        public List<PageResponse> Data { get; init; } = [];
    }

    private sealed class PageResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
    }

    private sealed class PageInstagramResponse
    {
        [JsonPropertyName("instagram_business_account")]
        public InstagramBusinessAccountResponse? InstagramBusinessAccount { get; init; }
    }

    private sealed class InstagramBusinessAccountResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed class InstagramUsernameResponse
    {
        [JsonPropertyName("username")]
        public string? Username { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("account_type")]
        public string? AccountType { get; init; }
    }

    private sealed class DebugTokenResponse { [JsonPropertyName("data")] public DebugTokenData? Data { get; init; } }
    private sealed class DebugTokenData
    {
        [JsonPropertyName("is_valid")] public bool IsValid { get; init; }
        [JsonPropertyName("scopes")] public List<string>? Scopes { get; init; }
    }
}
