using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Publishing;

internal static class YouTubeTokenRefreshDiagnostics
{
    private const string InvalidGrantMessage = "YouTube refresh token is invalid/revoked. Re-run /api/youtubeoauth/start.";
    private const string InvalidClientMessage = "YouTube client id/secret does not match the refresh token.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<YouTubeTokenRefreshError> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        GoogleOAuthError? payload = null;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                payload = JsonSerializer.Deserialize<GoogleOAuthError>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // Keep diagnostics safe: return status and a generic friendly message when Google does not return JSON.
            }
        }

        var error = payload?.Error;
        var description = payload?.ErrorDescription;
        var message = ToFriendlyMessage(response.StatusCode, error)
            ?? $"YouTube OAuth token refresh failed with status {(int)response.StatusCode}.";

        return new YouTubeTokenRefreshError((int)response.StatusCode, error, description, message);
    }

    public static void Log(ILogger logger, YouTubeTokenRefreshError error)
    {
        logger.LogWarning(
            "YouTube OAuth token refresh failed with status {StatusCode}. GoogleError={GoogleError}; GoogleErrorDescription={GoogleErrorDescription}",
            error.StatusCode,
            string.IsNullOrWhiteSpace(error.GoogleError) ? "<missing>" : error.GoogleError,
            string.IsNullOrWhiteSpace(error.GoogleErrorDescription) ? "<missing>" : error.GoogleErrorDescription);
    }

    public static string? ToFriendlyMessage(HttpStatusCode statusCode, string? googleError)
        => statusCode == HttpStatusCode.BadRequest && string.Equals(googleError, "invalid_grant", StringComparison.OrdinalIgnoreCase)
            ? InvalidGrantMessage
            : statusCode == HttpStatusCode.BadRequest && string.Equals(googleError, "invalid_client", StringComparison.OrdinalIgnoreCase)
                ? InvalidClientMessage
                : null;

    private sealed class GoogleOAuthError
    {
        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }
    }
}

internal sealed record YouTubeTokenRefreshError(int StatusCode, string? GoogleError, string? GoogleErrorDescription, string FriendlyMessage);
