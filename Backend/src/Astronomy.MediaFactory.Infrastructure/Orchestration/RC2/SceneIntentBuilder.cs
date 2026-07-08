using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class SceneIntentBuilder(ILogger<SceneIntentBuilder> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IReadOnlyList<SceneIntent>> BuildAndWriteDiagnosticsAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        logger.LogInformation("SceneIntentBuilder executed for RC2 batch generation. OutputRoot={OutputRoot}; Success={Success}", response.OutputRoot, response.Success);

        if (string.IsNullOrWhiteSpace(response.OutputRoot))
        {
            logger.LogWarning("SceneIntentBuilder skipped diagnostics because RC2 response did not include an OutputRoot.");
            return [];
        }

        var outputRoot = response.OutputRoot!;
        var planInput = ReadFirstJson(Path.Combine(outputRoot, "plan-input", "content-plan-production-request.json"));
        var intelligence = ReadFirstJson(Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json"));
        var sceneElements = ReadSceneElements(outputRoot);

        if (sceneElements.Count == 0)
        {
            sceneElements.Add(default);
        }

        var intents = sceneElements.Select((scene, index) => BuildIntent(request, response, planInput, intelligence, scene, index)).ToArray();
        var diagnosticsRoot = Path.Combine(outputRoot, "editorial");
        Directory.CreateDirectory(diagnosticsRoot);
        var diagnosticsPath = Path.Combine(diagnosticsRoot, "scene-intents.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(intents, JsonOptions), cancellationToken);

        logger.LogInformation("SceneIntentBuilder wrote {SceneIntentCount} scene intents to {SceneIntentDiagnosticsPath}.", intents.Length, diagnosticsPath);
        return intents;
    }

    private static SceneIntent BuildIntent(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, JsonElement? planInput, JsonElement? intelligence, JsonElement scene, int index)
    {
        var sceneId = FirstNonEmpty(GetString(scene, "sceneId"), GetString(scene, "id"), $"scene-{index + 1:000}")!;
        var purpose = FirstNonEmpty(GetString(scene, "scenePurpose"), GetString(scene, "purpose"), GetString(scene, "sceneType"), GetString(scene, "segment"), InferPurpose(sceneId))!;
        var eventType = FirstNonEmpty(GetString(scene, "eventType"), GetString(planInput, "eventType"), GetString(intelligence, "eventType"), response.SelectedPlans.FirstOrDefault()?.ContentCategoryCode, "Unknown")!;
        var eventName = FirstNonEmpty(GetString(planInput, "title"), GetString(intelligence, "title"), response.Title, response.SelectedPlans.FirstOrDefault()?.Title, "Unknown")!;
        var observation = string.Equals(purpose, "Observation", StringComparison.OrdinalIgnoreCase) || sceneId.Contains("observ", StringComparison.OrdinalIgnoreCase);

        var required = new SceneIntentRequiredFacts(
            Fact("EventDate", FirstNonEmpty(GetString(planInput, "peakUtc"), GetString(planInput, "startUtc"), GetString(planInput, "scheduledUtc"), response.SelectedPlans.FirstOrDefault()?.ScheduledUtc?.ToString("O")), observation),
            Fact("BestViewingTime", FirstNonEmpty(GetString(planInput, "bestViewingWindowLocal"), GetString(planInput, "localPeakTime"), GetString(intelligence, "bestViewingTime")), observation),
            Fact("ViewingWindow", FirstNonEmpty(GetString(planInput, "bestViewingWindowLocal"), GetString(intelligence, "viewingWindow")), observation),
            Fact("Direction", FirstNonEmpty(GetString(planInput, "skyDirectionHint"), GetString(intelligence, "direction"), GetString(scene, "direction")), observation),
            Fact("Altitude", FirstNonEmpty(GetString(scene, "altitude"), GetString(scene, "altitudeDegrees"), GetString(intelligence, "altitude")), false),
            Fact("Constellation", FirstNonEmpty(GetString(scene, "constellation"), GetString(intelligence, "constellation")), false),
            Fact("Brightness", FirstNonEmpty(GetString(scene, "brightness"), GetString(scene, "magnitude"), GetString(intelligence, "brightness")), false),
            Fact("MoonInterference", FirstNonEmpty(GetString(planInput, "moonInterference"), GetString(intelligence, "moonInterference")), false),
            Fact("Visibility", FirstNonEmpty(GetString(planInput, "visibilityRegion"), GetString(scene, "visibility"), GetString(intelligence, "visibility")), false),
            Fact("RelativePositions", FirstNonEmpty(GetString(planInput, "angularSeparationDegrees"), GetString(scene, "relativePositions"), GetString(intelligence, "relativePositions")), false));

        var warnings = MissingWarnings(required).ToArray();
        var observationFacts = ToObservationFacts(required);
        return new SceneIntent(sceneId, purpose, request.Language, eventType, eventName, required, observationFacts,
            FirstNonEmpty(GetString(scene, "narrationIntent"), GetString(scene, "narrationText"), GetString(scene, "caption"), $"Explain the {purpose.ToLowerInvariant()} role without adding unsupported facts.")!,
            FirstNonEmpty(GetString(scene, "visualIntent"), GetString(scene, "visualPrompt"), $"Show a scientifically respectful {purpose.ToLowerInvariant()} visual for {eventName}.")!,
            ["Do not invent missing facts.", "Use only supplied event metadata and scene metadata.", "Surface missing metadata as warnings."],
            FirstNonEmpty(GetString(scene, "editorialTone"), "Clear, accurate, practical, and wonder-driven")!, warnings);
    }

    private static SceneIntentFact Fact(string name, string? value, bool highPriority) => new(name, string.IsNullOrWhiteSpace(value) ? null : value, highPriority ? "High" : "Normal", string.IsNullOrWhiteSpace(value));
    private static IEnumerable<string> MissingWarnings(SceneIntentRequiredFacts facts) => new[] { facts.EventDate, facts.BestViewingTime, facts.ViewingWindow, facts.Direction, facts.Altitude, facts.Constellation, facts.Brightness, facts.MoonInterference, facts.Visibility, facts.RelativePositions }.Where(f => f.IsMissing).Select(f => $"Missing metadata for {f.Name}.");
    private static IReadOnlyDictionary<string, string> ToObservationFacts(SceneIntentRequiredFacts facts) => new[] { facts.EventDate, facts.BestViewingTime, facts.ViewingWindow, facts.Direction, facts.Altitude, facts.Constellation, facts.Brightness, facts.MoonInterference, facts.Visibility, facts.RelativePositions }.Where(f => !f.IsMissing && f.Value is not null).ToDictionary(f => f.Name, f => f.Value!);
    private static string InferPurpose(string sceneId) => sceneId.Contains("observ", StringComparison.OrdinalIgnoreCase) || sceneId.Contains("view", StringComparison.OrdinalIgnoreCase) ? "Observation" : "Editorial";
    private static JsonElement? ReadFirstJson(string path) { if (!File.Exists(path)) return null; using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.Clone(); }
    private static List<JsonElement> ReadSceneElements(string outputRoot)
    {
        var scenes = new List<JsonElement>();
        if (!Directory.Exists(outputRoot)) return scenes;
        foreach (var path in Directory.EnumerateFiles(outputRoot, "*.json", SearchOption.AllDirectories).Where(p => Path.GetFileName(p).Contains("scene", StringComparison.OrdinalIgnoreCase)).Take(50))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                AddScenes(doc.RootElement, scenes);
            }
            catch
            {
                // Diagnostics must never block the mapped RC2 phases. Ignore malformed or transient scene files.
            }
        }
        return scenes.GroupBy(s => FirstNonEmpty(GetString(s, "sceneId"), GetString(s, "id"), s.GetRawText())).Select(g => g.First()).ToList();
    }
    private static void AddScenes(JsonElement element, List<JsonElement> scenes)
    {
        if (element.ValueKind == JsonValueKind.Array) { foreach (var item in element.EnumerateArray()) AddScenes(item, scenes); return; }
        if (element.ValueKind != JsonValueKind.Object) return;
        if (GetString(element, "sceneId") is not null || GetString(element, "sceneType") is not null) scenes.Add(element.Clone());
        foreach (var property in element.EnumerateObject().Where(p => p.Name.Contains("scene", StringComparison.OrdinalIgnoreCase))) AddScenes(property.Value, scenes);
    }
    private static string? GetString(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e) return null;
        foreach (var property in e.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(property.Value);
        }
        return null;
    }
    private static string? ValueToString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
