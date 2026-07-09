using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class CreativeStoryboardBuilder(ILogger<CreativeStoryboardBuilder> logger)
{
    private const string PhaseName = "Creative Intelligence Foundation";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] AstronomyVisualAccuracyRules =
    [
        "Planets must remain circular.",
        "Do not exaggerate angular separation beyond editorially acceptable framing.",
        "Do not show false surface detail.",
        "Do not imply the planets physically touch.",
        "Do not show daylight if the event is described after sunset.",
        "Observation visuals must respect direction and timing metadata when available.",
        "If altitude, constellation, moon interference, or brightness are missing, do not visualize them as confirmed facts."
    ];

    private static readonly string[] ProhibitedVisualChoices =
    [
        "fantasy sky",
        "sci-fi spaceship",
        "alien elements",
        "distorted planets",
        "unrealistic planet scale unless explicitly marked as editorial thumbnail treatment",
        "misleading constellation labels",
        "fake telescope detail",
        "overdramatic disaster-like lighting"
    ];

    public async Task<CreativeStoryboardBuilderResult> BuildAndWriteDiagnosticsAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creative Intelligence Foundation executed for RC2 batch generation. OutputRoot={OutputRoot}; Success={Success}", response.OutputRoot, response.Success);
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return CreativeStoryboardBuilderResult.Empty;

        var outputRoot = response.OutputRoot!;
        var editorialContractPath = Path.Combine(outputRoot, "editorial", "editorial-contract.json");
        var storyGraphPath = Path.Combine(outputRoot, "editorial", "story-graph.json");
        var sceneIntentsPath = Path.Combine(outputRoot, "editorial", "scene-intents.json");
        var creativeRoot = Path.Combine(outputRoot, "creative");
        Directory.CreateDirectory(creativeRoot);
        var storyboardPath = Path.Combine(creativeRoot, "creative-storyboard.json");
        var diagnosticsPath = Path.Combine(creativeRoot, "creative-diagnostics.json");

        var contract = ReadFirstJson(editorialContractPath);
        var storyGraph = ReadFirstJson(storyGraphPath);
        var sceneIntents = ReadFirstJson(sceneIntentsPath);
        var storyboard = BuildStoryboard(request, response, contract, storyGraph, sceneIntents);

        await File.WriteAllTextAsync(storyboardPath, JsonSerializer.Serialize(storyboard, JsonOptions), cancellationToken);
        var inputs = new[] { editorialContractPath, storyGraphPath, sceneIntentsPath };
        var diagnostics = new
        {
            phaseNo = 7,
            phaseName = PhaseName,
            orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            subPhases = new[] { "7.1 Creative Storyboard Builder", "7.6 Creative Diagnostics" },
            inputs = inputs.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) }).ToArray(),
            outputs = new[] { NormalizePath(storyboardPath), NormalizePath(diagnosticsPath) },
            creativeSceneCount = storyboard.Scenes.Count,
            missingCreativeWarningCount = storyboard.MissingCreativeWarnings.Count,
            missingCreativeWarnings = storyboard.MissingCreativeWarnings
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);

        logger.LogInformation("Creative Intelligence Foundation wrote {CreativeSceneCount} creative storyboard scenes and diagnostics to {DiagnosticsPath}.", storyboard.Scenes.Count, diagnosticsPath);
        return new CreativeStoryboardBuilderResult(storyboard, [storyboardPath, diagnosticsPath]);
    }

    private static CreativeStoryboard BuildStoryboard(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, JsonElement? contract, JsonElement? storyGraph, JsonElement? sceneIntents)
    {
        var eventType = FirstNonEmpty(GetString(contract, "eventType"), GetString(storyGraph, "eventType"), response.SelectedPlans.FirstOrDefault()?.ContentCategoryCode, "Unknown")!;
        var eventName = FirstNonEmpty(GetString(contract, "eventName"), GetString(storyGraph, "eventName"), response.Title, response.SelectedPlans.FirstOrDefault()?.Title, "Unknown")!;
        var language = FirstNonEmpty(GetString(contract, "language"), GetString(storyGraph, "language"), request.Language)!;
        var regionId = FirstNonEmpty(GetString(contract, "regionId"), GetString(storyGraph, "regionId"), request.RegionId)!;
        var storyArc = FirstNonEmpty(GetString(storyGraph, "storyArc"), FindString(contract, "storyArc"), "Hook → Discovery → Science → Observation → Takeaway")!;
        var warnings = new List<string>();
        if (!contract.HasValue) warnings.Add("Missing input file editorial/editorial-contract.json.");
        if (!storyGraph.HasValue) warnings.Add("Missing input file editorial/story-graph.json.");
        if (!sceneIntents.HasValue) warnings.Add("Missing input file editorial/scene-intents.json.");

        var graphScenes = ReadArray(storyGraph, "scenes");
        var intentScenes = sceneIntents.HasValue && sceneIntents.Value.ValueKind == JsonValueKind.Array ? sceneIntents.Value.EnumerateArray().Select(e => e.Clone()).ToArray() : [];
        var sourceScenes = graphScenes.Count > 0 ? graphScenes : intentScenes;
        if (sourceScenes.Count == 0) warnings.Add("No editorial scenes were available for creative storyboard generation.");

        var scenes = sourceScenes.Select((scene, index) => BuildScene(scene, intentScenes, index, eventName, warnings)).ToArray();
        warnings.AddRange(FindStringArray(contract, "missingFactWarnings").Select(w => $"Editorial warning carried into creative layer: {w}"));
        return new CreativeStoryboard(
            "AstroPulse-CreativeStoryboard-v1",
            Rc2PipelinePhaseRegistry.OrchestrationVersion,
            eventType,
            eventName,
            language,
            regionId,
            "Make the astronomy understandable, observable, calm, and visually accurate before any generator writes prompts.",
            storyArc,
            "Natural documentary sky visuals with restrained motion, factual object relationships, and no invented observational metadata.",
            scenes,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static CreativeStoryboardScene BuildScene(JsonElement scene, IReadOnlyList<JsonElement> intents, int index, string eventName, List<string> warnings)
    {
        var sceneId = FirstNonEmpty(GetString(scene, "sceneId"), $"scene-{index + 1:000}")!;
        var purpose = NormalizePurpose(FirstNonEmpty(GetString(scene, "scenePurpose"), GetString(scene, "purpose"))) ?? FallbackPurpose(index);
        var intent = intents.FirstOrDefault(i => string.Equals(GetString(i, "sceneId"), sceneId, StringComparison.OrdinalIgnoreCase));
        var keyMessage = FirstNonEmpty(GetString(scene, "keyMessage"), GetString(intent, "keyMessage"), $"Help viewers understand {eventName} without inventing unsupported facts.")!;
        var defaults = DefaultsFor(purpose);
        if (!string.Equals(purpose, FirstNonEmpty(GetString(scene, "scenePurpose"), GetString(scene, "purpose")), StringComparison.OrdinalIgnoreCase)) warnings.Add($"Scene {sceneId} used creative fallback purpose {purpose}.");

        return new CreativeStoryboardScene(
            sceneId,
            purpose,
            GetInt(scene, "sceneOrder") ?? index + 1,
            keyMessage,
            defaults.ViewerFocus,
            defaults.EmotionalRole,
            FirstNonEmpty(GetString(scene, "visualRole"), GetString(intent, "visualIntent"), $"Translate the {purpose.ToLowerInvariant()} beat into a clear visual decision.")!,
            FirstNonEmpty(GetString(scene, "motionRole"), "Use motion only to support comprehension and pacing.")!,
            ResolvePrimarySubject(intent, eventName),
            ResolveSecondarySubjects(intent),
            defaults.CompositionIntent,
            purpose == "Science" ? "Stable explanatory camera with legible spatial relationships." : "Calm documentary camera that keeps the main subjects easy to understand.",
            purpose == "Hook" ? "Natural high-contrast night-sky lighting without disaster-like drama." : "Natural sky lighting consistent with the supported observation context.",
            FirstNonEmpty(GetString(scene, "motionRole"), GetString(intent, "motionIntent"), "Slow, restrained motion that clarifies the scene relationship.")!,
            FirstNonEmpty(GetString(scene, "transitionToNext"), "Use a simple editorial transition that preserves story continuity.")!,
            AstronomyVisualAccuracyRules,
            ProhibitedVisualChoices);
    }

    private static string ResolvePrimarySubject(JsonElement intent, string eventName)
        => ReadObjectStrings(intent, "observationFacts").TryGetValue("RelativePositions", out var relative) ? relative : eventName;
    private static IReadOnlyList<string> ResolveSecondarySubjects(JsonElement intent)
        => ReadObjectStrings(intent, "observationFacts").Where(kv => !string.Equals(kv.Key, "RelativePositions", StringComparison.OrdinalIgnoreCase)).Select(kv => $"{kv.Key}: {kv.Value}").ToArray();
    private static (string ViewerFocus, string EmotionalRole, string CompositionIntent) DefaultsFor(string purpose) => purpose switch
    {
        "Hook" => ("Understand why this event is worth watching.", "Create curiosity and immediate visual interest.", "Strong opening composition with the main astronomical subjects clearly visible."),
        "Discovery" => ("Understand where this event appears in the sky.", "Make the sky feel approachable and easy to navigate.", "Orientation-style composition that helps viewers locate the event."),
        "Science" => ("Understand why the event happens.", "Turn curiosity into understanding.", "Clean explanatory visual, orbit geometry, or perspective-based diagram."),
        "Observation" => ("Know exactly what to look for.", "Build confidence for real sky observation.", "Practical sky-view composition with direction, horizon, and object relationship."),
        "Takeaway" => ("Remember why the event matters.", "Leave the viewer with a calm sense of wonder.", "Beautiful closing composition that reinforces the event’s significance."),
        _ => ("Know what action to take next.", "Encourage the viewer to observe or save the event.", "Simple action-oriented composition.")
    };
    private static string FallbackPurpose(int index) => index switch { 0 => "Hook", 1 => "Discovery", 2 => "Science", 3 => "Observation", 4 => "Takeaway", _ => "SupportingDetail" };
    private static string? NormalizePurpose(string? purpose) { if (string.IsNullOrWhiteSpace(purpose)) return null; var c = new string(purpose.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(); return c switch { "hook" => "Hook", "discovery" => "Discovery", "science" => "Science", "observation" or "viewing" or "observing" => "Observation", "takeaway" or "summary" or "closing" => "Takeaway", "supportingdetail" or "detail" => "SupportingDetail", _ => null }; }
    private static JsonElement? ReadFirstJson(string path) { if (!File.Exists(path)) return null; using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.Clone(); }
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(i => i.Clone()).ToArray(); return []; }
    private static IReadOnlyDictionary<string, string> ReadObjectStrings(JsonElement element, string name) { if (element.ValueKind != JsonValueKind.Object) return new Dictionary<string, string>(); foreach (var p in element.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object) return p.Value.EnumerateObject().Where(kv => ValueToString(kv.Value) is not null).ToDictionary(kv => kv.Name, kv => ValueToString(kv.Value)!); return new Dictionary<string, string>(); }
    private static int? GetInt(JsonElement? element, string name) => int.TryParse(GetString(element, name), out var value) ? value : null;
    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(p.Value); return null; }
    private static string? FindString(JsonElement? element, string name) { if (!element.HasValue) return null; if (element.Value.ValueKind == JsonValueKind.Object) foreach (var p in element.Value.EnumerateObject()) { if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(p.Value); var nested = FindString(p.Value, name); if (!string.IsNullOrWhiteSpace(nested)) return nested; } return null; }
    private static IReadOnlyList<string> FindStringArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) { if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray(); var nested = FindStringArray(p.Value, name); if (nested.Count > 0) return nested; } return []; }
    private static string? ValueToString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record CreativeStoryboard(string CreativeStoryboardVersion, string OrchestrationVersion, string EventType, string EventName, string Language, string RegionId, string CreativePrinciple, string StoryArc, string GlobalVisualDirection, IReadOnlyList<CreativeStoryboardScene> Scenes, IReadOnlyList<string> MissingCreativeWarnings);
public sealed record CreativeStoryboardScene(string SceneId, string ScenePurpose, int SceneOrder, string KeyMessage, string ViewerFocus, string EmotionalRole, string VisualRole, string MotionRole, string PrimarySubject, IReadOnlyList<string> SecondarySubjects, string CompositionIntent, string CameraIntent, string LightingIntent, string MotionIntent, string TransitionIntent, IReadOnlyList<string> VisualAccuracyRules, IReadOnlyList<string> ProhibitedVisualChoices);
public sealed record CreativeStoryboardBuilderResult(CreativeStoryboard? Storyboard, IReadOnlyList<string> GeneratedFiles)
{
    public static CreativeStoryboardBuilderResult Empty { get; } = new(null, []);
}
