using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubeAuthService : IYouTubeAuthService
{
    public const string MissingRefreshTokenMessage = "YouTube refresh token is missing. Complete one-time OAuth setup first.";

    private readonly HttpClient _httpClient;
    private readonly YouTubeOptions _options;
    private readonly ILogger<YouTubeAuthService> _logger;
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static readonly TimeSpan AccessTokenSafetyWindow = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public YouTubeAuthService(HttpClient httpClient, IOptions<YouTubeOptions> options, ILogger<YouTubeAuthService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        => await GetAccessTokenAsync(false, cancellationToken);

    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
        var resolvedToken = await YouTubeTokenResolver.ResolveAsync(_options, _logger, cancellationToken);
        var refreshToken = resolvedToken.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException(MissingRefreshTokenMessage);
        }

        var path = YouTubeTokenResolver.ResolveTokenFilePath(_options);
        var stored = await ReadTokenFileAsync(path, cancellationToken);
        if (stored?.ReauthorizationRequired == true)
            throw new YouTubeReauthorizationRequiredException();
        if (!forceRefresh && !string.IsNullOrWhiteSpace(stored?.AccessToken)
            && stored.AccessTokenExpiresUtc > DateTimeOffset.UtcNow.Add(AccessTokenSafetyWindow))
            return stored.AccessToken;

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException("YouTube OAuth client id and client secret are required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await YouTubeTokenRefreshDiagnostics.ReadAsync(response, cancellationToken);
            YouTubeTokenRefreshDiagnostics.Log(_logger, error);
            if (string.Equals(error.GoogleError, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                if (stored is not null)
                    await AtomicTokenFile.WriteAsync(path, stored with { ReauthorizationRequired = true }, JsonOptions, cancellationToken);
                throw new YouTubeReauthorizationRequiredException();
            }
            throw new InvalidOperationException(error.FriendlyMessage);
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new InvalidOperationException("YouTube OAuth token refresh did not return an access token.");
        }

        var expiresUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.GetValueOrDefault(3600));
        // Google normally omits refresh_token on refresh. Preserve the existing value unless rotation is explicit.
        var persistedRefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken) ? refreshToken : token.RefreshToken;
        var updated = (stored ?? new YouTubeOAuthTokenFile(resolvedToken.ChannelId,
            resolvedToken.ChannelTitle, persistedRefreshToken, DateTimeOffset.UtcNow)) with
        {
            RefreshToken = persistedRefreshToken,
            AccessToken = token.AccessToken,
            AccessTokenExpiresUtc = expiresUtc,
            ReauthorizationRequired = false
        };
        await AtomicTokenFile.WriteAsync(path, updated, JsonOptions, cancellationToken);
        _logger.LogInformation("YouTube access token refreshed; expires at {ExpiryUtc}.", expiresUtc);
        return token.AccessToken;
        }
        finally { RefreshLock.Release(); }
    }

    private static async Task<YouTubeOAuthTokenFile?> ReadTokenFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<YouTubeOAuthTokenFile>(stream, JsonOptions, cancellationToken);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }
}

public sealed class YouTubeReauthorizationRequiredException : InvalidOperationException
{
    public YouTubeReauthorizationRequiredException()
        : base("YouTube authorization was revoked or expired; interactive reauthorization is required at /api/youtubeoauth/start.") { }
}
