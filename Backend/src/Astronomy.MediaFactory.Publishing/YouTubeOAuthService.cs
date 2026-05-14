using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Google;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubeOAuthService : IYouTubeOAuthService
{
    public const string YouTubeUploadScope = "https://www.googleapis.com/auth/youtube.upload";
    public const string YouTubeReadonlyScope = "https://www.googleapis.com/auth/youtube.readonly";
    public const string InsufficientOAuthScopesGuidance = "Google OAuth did not grant the required YouTube scopes. Restart setup at /api/youtubeoauth/start so consent includes both youtube.upload and youtube.readonly access.";
    public const string MissingRefreshTokenGuidance = "Google did not return refresh_token. Remove previous app consent and retry with prompt=consent.";
    public const string ChannelMismatchMessage = "Authenticated channel does not match configured expected channel.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] RequiredAuthorizationScopes = [YouTubeUploadScope, YouTubeReadonlyScope];

    private readonly HttpClient _httpClient;
    private readonly IYouTubeApiClient _youTubeApiClient;
    private readonly YouTubeOptions _options;

    public YouTubeOAuthService(HttpClient httpClient, IYouTubeApiClient youTubeApiClient, IOptions<YouTubeOptions> options)
    {
        _httpClient = httpClient;
        _youTubeApiClient = youTubeApiClient;
        _options = options.Value;
    }

    public string BuildAuthorizationUrl()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException("YouTube OAuth client id is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException("YouTube OAuth redirect uri is required.");
        }

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(" ", RequiredAuthorizationScopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };

        var builder = new UriBuilder("https://accounts.google.com/o/oauth2/v2/auth")
        {
            Query = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"))
        };

        return builder.Uri.ToString();
    }

    public async Task<YouTubeOAuthSetupResult> CompleteSetupAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("OAuth authorization code is required.", nameof(code));
        }

        ValidateTokenExchangeConfiguration();

        var token = await ExchangeCodeAsync(code, cancellationToken);
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidOperationException(MissingRefreshTokenGuidance);
        }

        ValidateGrantedScopes(token.Scope);

        YouTubeChannelInfo channel;
        try
        {
            channel = await _youTubeApiClient.GetAuthenticatedChannelAsync(token.AccessToken, cancellationToken);
        }
        catch (GoogleApiException ex) when (IsInsufficientScopeException(ex))
        {
            throw new InvalidOperationException(InsufficientOAuthScopesGuidance, ex);
        }

        ValidateExpectedChannel(channel);

        var createdUtc = DateTimeOffset.UtcNow;
        var tokenFilePath = await PersistRefreshTokenAsync(channel, token.RefreshToken, createdUtc, cancellationToken);
        await WriteDiagnosticsAsync(channel, createdUtc, refreshTokenGenerated: true, cancellationToken);

        return new YouTubeOAuthSetupResult(
            Success: true,
            ChannelTitle: channel.ChannelTitle,
            ChannelId: channel.ChannelId,
            RefreshTokenGenerated: true,
            Message: "YouTube OAuth completed successfully. Full refresh token was saved to tokenFilePath.",
            RefreshTokenPreview: MaskRefreshToken(token.RefreshToken),
            TokenFilePath: tokenFilePath);
    }

    private void ValidateTokenExchangeConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException("YouTube OAuth client id and client secret are required.");
        }

        if (string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException("YouTube OAuth redirect uri is required.");
        }
    }

    private async Task<TokenResponse> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = _options.RedirectUri
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"YouTube OAuth token exchange failed with status {(int)response.StatusCode}.");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new InvalidOperationException("YouTube OAuth token exchange did not return an access token.");
        }

        return token;
    }


    private static void ValidateGrantedScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return;
        }

        var grantedScopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (RequiredAuthorizationScopes.Any(requiredScope => !grantedScopes.Contains(requiredScope)))
        {
            throw new InvalidOperationException(InsufficientOAuthScopesGuidance);
        }
    }

    private static bool IsInsufficientScopeException(GoogleApiException exception)
        => exception.HttpStatusCode == System.Net.HttpStatusCode.Forbidden
            && exception.Message.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("scope", StringComparison.OrdinalIgnoreCase);

    private void ValidateExpectedChannel(YouTubeChannelInfo channel)
    {
        var expectedId = _options.ExpectedChannelId?.Trim();
        if (!string.IsNullOrWhiteSpace(expectedId) && !string.Equals(expectedId, channel.ChannelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(ChannelMismatchMessage);
        }

        var expectedTitle = _options.ExpectedChannelTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(expectedTitle) && !string.Equals(expectedTitle, channel.ChannelTitle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(ChannelMismatchMessage);
        }
    }

    private async Task<string> PersistRefreshTokenAsync(YouTubeChannelInfo channel, string refreshToken, DateTimeOffset createdUtc, CancellationToken cancellationToken)
    {
        var payload = new YouTubeOAuthTokenFile(channel.ChannelId, channel.ChannelTitle, refreshToken, createdUtc);
        var path = ResolveTokenFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
        return path;
    }

    private async Task WriteDiagnosticsAsync(YouTubeChannelInfo channel, DateTimeOffset generatedUtc, bool refreshTokenGenerated, CancellationToken cancellationToken)
    {
        var payload = new YouTubeOAuthDiagnosticResult(channel.ChannelTitle, channel.ChannelId, generatedUtc, refreshTokenGenerated);
        var path = Path.Combine(Path.GetDirectoryName(ResolveTokenFilePath())!, "youtube-oauth-result.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
    }

    private string ResolveTokenFilePath()
        => YouTubeTokenResolver.ResolveTokenFilePath(_options);

    private static string MaskRefreshToken(string refreshToken)
        => refreshToken.Length <= 10 ? "***" : $"{refreshToken[..Math.Min(10, refreshToken.Length)]}...";

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
    }
}
