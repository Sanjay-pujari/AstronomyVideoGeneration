using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Publishing;

/// <summary>Sanitized Meta Graph failure details. This type deliberately has no token-shaped fields.</summary>
internal sealed record MetaGraphError(
    int HttpStatus, string? Type, int? Code, int? ErrorSubcode, string? Message,
    string? ErrorUserTitle, string? ErrorUserMessage, string? FbTraceId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<MetaGraphError> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        GraphEnvelope? envelope = null;
        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            envelope = await JsonSerializer.DeserializeAsync<GraphEnvelope>(content, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // Provider/proxy returned a non-Graph body; never include that untrusted body in diagnostics.
        }

        var error = envelope?.Error;
        return new MetaGraphError((int)response.StatusCode, error?.Type, error?.Code, error?.ErrorSubcode,
            error?.Message, error?.ErrorUserTitle, error?.ErrorUserMessage, error?.FbTraceId);
    }

    internal string ToSafeMessage(string operation)
        => $"{operation} failed: HTTP {HttpStatus}; type={Type ?? "unknown"}; code={Code?.ToString() ?? "unknown"}; " +
           $"subcode={ErrorSubcode?.ToString() ?? "none"}; message={Message ?? "Meta Graph returned no safe error message"}; " +
           $"error_user_title={ErrorUserTitle ?? "none"}; error_user_msg={ErrorUserMessage ?? "none"}; fbtrace_id={FbTraceId ?? "none"}.";

    private sealed class GraphEnvelope { [JsonPropertyName("error")] public GraphError? Error { get; init; } }
    private sealed class GraphError
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("code")] public int? Code { get; init; }
        [JsonPropertyName("error_subcode")] public int? ErrorSubcode { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
        [JsonPropertyName("error_user_title")] public string? ErrorUserTitle { get; init; }
        [JsonPropertyName("error_user_msg")] public string? ErrorUserMessage { get; init; }
        [JsonPropertyName("fbtrace_id")] public string? FbTraceId { get; init; }
    }
}
