using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class TokenHealthService : ITokenHealthService
{
    public const string MetaExpiryWarning = "Meta long-lived token will expire soon; re-run /api/metaoauth/start.";
    private const string YouTubeTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string YouTubeChannelsEndpoint = "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true";
    private const string GraphEndpoint = "https://graph.facebook.com/v23.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] RequiredMetaScopes =
    [
        "pages_manage_posts",
        "pages_read_engagement",
        "pages_show_list",
        "instagram_basic",
        "instagram_content_publish",
        "business_management"
    ];

    private readonly HttpClient _httpClient;
    private readonly YouTubeOptions _youTubeOptions;
    private readonly MetaOptions _metaOptions;
    private readonly TokenHealthOptions _tokenHealthOptions;
    private readonly ILogger<TokenHealthService> _logger;

    public TokenHealthService(
        HttpClient httpClient,
        IOptions<YouTubeOptions> youTubeOptions,
        IOptions<MetaOptions> metaOptions,
        IOptions<TokenHealthOptions> tokenHealthOptions,
        ILogger<TokenHealthService> logger)
    {
        _httpClient = httpClient;
        _youTubeOptions = youTubeOptions.Value;
        _metaOptions = metaOptions.Value;
        _tokenHealthOptions = tokenHealthOptions.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TokenHealthResult>> CheckAllAsync(CancellationToken cancellationToken)
        => [await CheckYouTubeAsync(cancellationToken), await CheckMetaAsync(cancellationToken)];

    public async Task<TokenHealthResult> CheckYouTubeAsync(CancellationToken cancellationToken)
    {
        var result = new TokenHealthResult
        {
            Platform = "YouTube",
            CanRefresh = true
        };

        try
        {
            var refreshToken = await ResolveYouTubeRefreshTokenAsync(cancellationToken);
            result.IsConfigured = !string.IsNullOrWhiteSpace(refreshToken)
                && !string.IsNullOrWhiteSpace(_youTubeOptions.ClientId)
                && !string.IsNullOrWhiteSpace(_youTubeOptions.ClientSecret);

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                result.Error = YouTubeAuthService.MissingRefreshTokenMessage;
                return result;
            }

            if (string.IsNullOrWhiteSpace(_youTubeOptions.ClientId) || string.IsNullOrWhiteSpace(_youTubeOptions.ClientSecret))
            {
                result.Error = "YouTube OAuth client id and client secret are required.";
                return result;
            }

            using var tokenResponse = await _httpClient.PostAsync(YouTubeTokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _youTubeOptions.ClientId,
                ["client_secret"] = _youTubeOptions.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            }), cancellationToken);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                result.Error = $"YouTube OAuth token refresh failed with status {(int)tokenResponse.StatusCode}.";
                return result;
            }

            var token = await tokenResponse.Content.ReadFromJsonAsync<YouTubeTokenResponse>(JsonOptions, cancellationToken);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                result.Error = "YouTube OAuth token refresh did not return an access token.";
                return result;
            }

            using var channelRequest = new HttpRequestMessage(HttpMethod.Get, YouTubeChannelsEndpoint);
            channelRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var channelResponse = await _httpClient.SendAsync(channelRequest, cancellationToken);
            if (!channelResponse.IsSuccessStatusCode)
            {
                result.Error = $"YouTube channel validation failed with status {(int)channelResponse.StatusCode}.";
                return result;
            }

            var channels = await channelResponse.Content.ReadFromJsonAsync<YouTubeChannelsResponse>(JsonOptions, cancellationToken);
            var channel = channels?.Items.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(channel?.Id))
            {
                result.Error = "No YouTube channel found for authenticated account.";
                return result;
            }

            result.AccountId = channel.Id;
            result.AccountName = channel.Snippet?.Title ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_youTubeOptions.ExpectedChannelId)
                && !string.Equals(result.AccountId, _youTubeOptions.ExpectedChannelId.Trim(), StringComparison.Ordinal))
            {
                result.Error = "Authenticated YouTube channel id does not match configured expected channel id.";
                return result;
            }

            if (!string.IsNullOrWhiteSpace(_youTubeOptions.ExpectedChannelTitle)
                && !string.Equals(result.AccountName, _youTubeOptions.ExpectedChannelTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.Error = "Authenticated YouTube channel title does not match configured expected channel title.";
                return result;
            }

            result.IsValid = true;
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException)
        {
            _logger.LogWarning("YouTube token health check failed without exposing token values: {ErrorType}.", ex.GetType().Name);
            result.Error = ToSafeError(ex);
            return result;
        }
    }

    public async Task<TokenHealthResult> CheckMetaAsync(CancellationToken cancellationToken)
    {
        var result = new TokenHealthResult { Platform = "Meta", CanRefresh = false };

        try
        {
            var tokenPath = ResolveMetaTokenFilePath();
            result.IsConfigured = File.Exists(tokenPath) && !string.IsNullOrWhiteSpace(_metaOptions.AppId) && !string.IsNullOrWhiteSpace(_metaOptions.AppSecret);
            if (!File.Exists(tokenPath))
            {
                result.Error = "Meta OAuth token file is missing. Run /api/metaoauth/start first.";
                return result;
            }

            var token = JsonSerializer.Deserialize<MetaOAuthTokenFile>(await File.ReadAllTextAsync(tokenPath, cancellationToken), JsonOptions);
            if (token is null || string.IsNullOrWhiteSpace(token.LongLivedUserAccessToken))
            {
                result.Error = "Meta OAuth token file is missing long-lived user access token. Run /api/metaoauth/start first.";
                return result;
            }

            result.AccountId = token.FacebookPageId ?? string.Empty;
            result.AccountName = token.FacebookPageName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_metaOptions.AppId) || string.IsNullOrWhiteSpace(_metaOptions.AppSecret))
            {
                result.Error = "Meta app id and app secret are required for token debug validation.";
                return result;
            }

            var debug = await GetGraphAsync<MetaDebugTokenResponse>("/debug_token", new Dictionary<string, string>
            {
                ["input_token"] = token.LongLivedUserAccessToken,
                ["access_token"] = $"{_metaOptions.AppId}|{_metaOptions.AppSecret}"
            }, "Meta token debug", cancellationToken);

            if (debug.Data?.IsValid != true)
            {
                result.Error = "Meta token debug reported the token is invalid.";
                return result;
            }

            if (debug.Data.ExpiresAt.HasValue && debug.Data.ExpiresAt.Value > 0)
            {
                result.ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(debug.Data.ExpiresAt.Value).UtcDateTime;
                result.DaysUntilExpiry = (int)Math.Floor((result.ExpiresAtUtc.Value - DateTime.UtcNow).TotalDays);
                if (result.ExpiresAtUtc.Value <= DateTime.UtcNow)
                {
                    result.Error = "Meta long-lived token has expired; re-run /api/metaoauth/start.";
                    return result;
                }
            }

            var missingScopes = RequiredMetaScopes
                .Except(debug.Data.Scopes ?? [], StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingScopes.Length > 0)
            {
                result.Error = $"Meta token is missing required scopes: {string.Join(", ", missingScopes)}.";
                return result;
            }

            var pageId = string.IsNullOrWhiteSpace(_metaOptions.ExpectedFacebookPageId) ? token.FacebookPageId : _metaOptions.ExpectedFacebookPageId.Trim();
            if (string.IsNullOrWhiteSpace(pageId))
            {
                result.Error = "Meta Facebook page id is missing from token health configuration.";
                return result;
            }

            var page = await GetGraphAsync<MetaPageResponse>($"/{Uri.EscapeDataString(pageId)}", new Dictionary<string, string>
            {
                ["fields"] = "id,name",
                ["access_token"] = token.LongLivedUserAccessToken
            }, "Meta Facebook page validation", cancellationToken);

            if (string.IsNullOrWhiteSpace(page.Id))
            {
                result.Error = "Meta Facebook page validation did not return a page id.";
                return result;
            }

            result.AccountId = page.Id;
            result.AccountName = page.Name ?? token.FacebookPageName ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_metaOptions.ExpectedFacebookPageName)
                && !string.Equals(result.AccountName, _metaOptions.ExpectedFacebookPageName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.Error = "Validated Facebook page does not match configured expected page name.";
                return result;
            }

            if (!string.IsNullOrWhiteSpace(token.InstagramBusinessAccountId))
            {
                var instagram = await GetGraphAsync<MetaInstagramResponse>($"/{Uri.EscapeDataString(token.InstagramBusinessAccountId)}", new Dictionary<string, string>
                {
                    ["fields"] = "id,username",
                    ["access_token"] = token.LongLivedUserAccessToken
                }, "Meta Instagram account validation", cancellationToken);

                if (string.IsNullOrWhiteSpace(instagram.Id))
                {
                    result.Error = "Meta Instagram account validation did not return an account id.";
                    return result;
                }

                if (!string.IsNullOrWhiteSpace(_metaOptions.ExpectedInstagramUsername)
                    && !string.Equals(instagram.Username, _metaOptions.ExpectedInstagramUsername.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    result.Error = "Validated Instagram username does not match configured expected Instagram username.";
                    return result;
                }
            }
            else
            {
                result.Warning = MetaOAuthService.InstagramNotLinkedWarning;
            }

            if (result.ExpiresAtUtc.HasValue && result.ExpiresAtUtc.Value <= DateTime.UtcNow.AddDays(Math.Max(0, _tokenHealthOptions.RefreshBeforeExpiryDays)))
            {
                result.Warning = string.IsNullOrWhiteSpace(result.Warning)
                    ? MetaExpiryWarning
                    : $"{result.Warning} {MetaExpiryWarning}";
            }

            result.IsValid = true;
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException)
        {
            _logger.LogWarning("Meta token health check failed without exposing token values: {ErrorType}.", ex.GetType().Name);
            result.Error = ToSafeError(ex);
            return result;
        }
    }

    private static string ToSafeError(Exception ex)
        => ex is HttpRequestException
            ? "Token health HTTP request failed."
            : ex.Message;

    private async Task<string?> ResolveYouTubeRefreshTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_youTubeOptions.RefreshToken))
        {
            return _youTubeOptions.RefreshToken;
        }

        var path = string.IsNullOrWhiteSpace(_youTubeOptions.TokenFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "youtube-oauth-token.json")
            : Path.GetFullPath(_youTubeOptions.TokenFilePath);

        if (!File.Exists(path))
        {
            return null;
        }

        var file = JsonSerializer.Deserialize<YouTubeOAuthTokenFile>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        return file?.RefreshToken;
    }

    private string ResolveMetaTokenFilePath()
        => string.IsNullOrWhiteSpace(_metaOptions.TokenFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "meta-oauth-token.json")
            : Path.GetFullPath(_metaOptions.TokenFilePath);

    private async Task<T> GetGraphAsync<T>(string path, Dictionary<string, string> query, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(BuildUri(GraphEndpoint + path, query), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{operation} failed with status {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException($"{operation} returned an empty response.");
    }

    private static string BuildUri(string endpoint, Dictionary<string, string> query)
    {
        var builder = new UriBuilder(endpoint)
        {
            Query = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"))
        };
        return builder.Uri.ToString();
    }

    private sealed class YouTubeTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
    }

    private sealed class YouTubeChannelsResponse
    {
        [JsonPropertyName("items")]
        public List<YouTubeChannelResponse> Items { get; init; } = [];
    }

    private sealed class YouTubeChannelResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("snippet")]
        public YouTubeChannelSnippet? Snippet { get; init; }
    }

    private sealed class YouTubeChannelSnippet
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }
    }

    private sealed class MetaDebugTokenResponse
    {
        [JsonPropertyName("data")]
        public MetaDebugTokenData? Data { get; init; }
    }

    private sealed class MetaDebugTokenData
    {
        [JsonPropertyName("is_valid")]
        public bool IsValid { get; init; }

        [JsonPropertyName("expires_at")]
        public long? ExpiresAt { get; init; }

        [JsonPropertyName("scopes")]
        public List<string> Scopes { get; init; } = [];
    }

    private sealed class MetaPageResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class MetaInstagramResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("username")]
        public string? Username { get; init; }
    }
}
