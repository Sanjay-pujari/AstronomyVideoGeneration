using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed record NarrationPreviewRequest(
    string? PlanId,
    string EventType,
    string EventName,
    string? ShortTitle,
    string Language,
    string RegionId,
    string? Format,
    JsonElement? EventMetadata,
    [property: JsonConverter(typeof(FlexibleBooleanJsonConverter))]
    bool ReturnScenes = true);

public sealed class FlexibleBooleanJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
            JsonTokenType.Number when reader.TryGetInt32(out var value) => value != 0,
            JsonTokenType.StartArray => ReadArrayAsBoolean(ref reader),
            JsonTokenType.Null => true,
            _ => throw new JsonException($"Expected a boolean-compatible value, but received {reader.TokenType}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);

    private static bool ReadArrayAsBoolean(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetArrayLength() > 0;
    }
}

public sealed record NarrationPreviewResponse(
    string? PlanId,
    string EventType,
    string EventName,
    string Language,
    string RegionId,
    string? Format,
    IReadOnlyList<NarrationPreviewScene> Scenes,
    NarrationValidationResult OverallValidation,
    NarrationFormattingDiagnostics FormattingDiagnostics);

public sealed record NarrationPreviewScene(
    string SceneId,
    string ScenePurpose,
    string Narration,
    NarrationValidationResult Validation);

public sealed record NarrationValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record NarrationFormattingDiagnostics(
    string EventDate,
    string PeakTime,
    string ViewingWindow,
    string Direction,
    IReadOnlyList<string> AppliedRules,
    IReadOnlyList<string> Warnings);

public interface INarrationGenerationService
{
    Task<NarrationPreviewResponse> GeneratePreviewAsync(NarrationPreviewRequest request, CancellationToken cancellationToken);
    Task<NarrationPreviewResponse> GenerateProductionNarrationAsync(NarrationPreviewRequest request, CancellationToken cancellationToken);
}
