using System.Net.Http.Json;
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
        if (string.IsNullOrWhiteSpace(_options.RefreshToken))
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
                ["refresh_token"] = _options.RefreshToken,
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

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
    }
}
