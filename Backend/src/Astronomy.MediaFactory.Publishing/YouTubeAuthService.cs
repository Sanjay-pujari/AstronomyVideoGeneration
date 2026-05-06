using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubeAuthService : IYouTubeAuthService
{
    public const string MissingRefreshTokenMessage = "YouTube refresh token is missing. Complete one-time OAuth setup first.";

    private readonly HttpClient _httpClient;
    private readonly YouTubeOptions _options;

    public YouTubeAuthService(HttpClient httpClient, IOptions<YouTubeOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var refreshToken = ResolveRefreshToken();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException(MissingRefreshTokenMessage);
        }

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
            throw new InvalidOperationException($"YouTube OAuth token refresh failed with status {(int)response.StatusCode}.");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new InvalidOperationException("YouTube OAuth token refresh did not return an access token.");
        }

        return token.AccessToken;
    }

    private string? ResolveRefreshToken()
    {
        if (!string.IsNullOrWhiteSpace(_options.RefreshToken))
        {
            return _options.RefreshToken;
        }

        var path = string.IsNullOrWhiteSpace(_options.TokenFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "youtube-oauth-token.json")
            : Path.GetFullPath(_options.TokenFilePath);

        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        var tokenFile = JsonSerializer.Deserialize<StoredRefreshToken>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return tokenFile?.RefreshToken;
    }

    private sealed class StoredRefreshToken
    {
        public string? RefreshToken { get; init; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
    }
}
