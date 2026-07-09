using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class NarrationGeneratorV5(ILogger<NarrationGeneratorV5> logger)
{
    private const string PhaseName = "Narration Generator V5";
    private const string ChannelEnding = "Until next time, keep looking up.";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<NarrationGeneratorV5Result> BuildAndWriteDiagnosticsAsync(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return NarrationGeneratorV5Result.Empty;

        var outputRoot = response.OutputRoot!;
        var editorialPath = Path.Combine(outputRoot, "editorial", "editorial-contract.json");
        var storyboardPath = Path.Combine(outputRoot, "creative", "creative-storyboard.json");
        var narrationRoot = Path.Combine(outputRoot, "narration-v5");
        Directory.CreateDirectory(narrationRoot);
        var planPath = Path.Combine(narrationRoot, "narration-plan.json");
        var narrationPath = Path.Combine(narrationRoot, "narration.json");
        var diagnosticsPath = Path.Combine(narrationRoot, "narration-diagnostics.json");

        var contract = ReadFirstJson(editorialPath);
        var storyboard = ReadFirstJson(storyboardPath);
        var warnings = new List<string>();
        if (!contract.HasValue) warnings.Add("Missing input file editorial/editorial-contract.json.");
        if (!storyboard.HasValue) warnings.Add("Missing input file creative/creative-storyboard.json.");

        var language = FirstNonEmpty(GetString(contract, "language"), GetString(storyboard, "language"), request.Language, "en")!;
        var requiredFacts = ReadRequiredFacts(contract);
        var prohibited = FindStringArray(contract, "prohibitedPhrases");
        var preferred = FindStringArray(contract, "preferredPhrases");
        var scenes = ReadArray(storyboard, "scenes").OrderBy(s => GetInt(s, "sceneOrder") ?? 0).ToArray();
        if (scenes.Length == 0) warnings.Add("No creative storyboard scenes were available for narration generation.");

        var planScenes = scenes.Select((scene, index) => BuildPlanScene(scene, index, requiredFacts)).ToArray();
        var plan = new NarrationPlanV5("AstroPulse-NarrationPlan-v1", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, "CalmDocumentary", GetString(storyboard, "storyArc") ?? "Hook → Discovery → Science → Observation → Takeaway", requiredFacts, prohibited, preferred, ChannelEnding, planScenes);
        var narrationScenes = planScenes.Select(scene => BuildNarrationScene(scene, requiredFacts, language)).ToArray();
        var fullText = string.Join("\n\n", narrationScenes.Select(s => s.NarrationText));
        var narration = new NarrationV5("AstroPulse-Narration-v5", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, narrationScenes, fullText, ChannelEnding);

        var coverage = requiredFacts.ToDictionary(f => f.Name, f => new RequiredFactCoverage(f.Value, fullText.Contains(f.Value, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        foreach (var missing in coverage.Where(kv => !kv.Value.Covered).Select(kv => kv.Key)) warnings.Add($"Required fact was not covered naturally in full narration: {missing}.");
        var prohibitedViolations = prohibited.Where(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var missingFactViolations = FindStringArray(contract, "missingFactWarnings").Where(w => MentionsMissingFact(fullText, w)).ToArray();

        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(narrationPath, JsonSerializer.Serialize(narration, JsonOptions), cancellationToken);
        var errors = prohibitedViolations.Concat(missingFactViolations).ToArray();
        var diagnostics = new
        {
            phaseName = PhaseName,
            orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            inputs = new[]
            {
                new { path = NormalizePath(editorialPath), exists = File.Exists(editorialPath) },
                new { path = NormalizePath(storyboardPath), exists = File.Exists(storyboardPath) }
            },
            outputsCreated = new[] { planPath, narrationPath, diagnosticsPath }.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) || path == diagnosticsPath }).ToArray(),
            sceneCount = narrationScenes.Length,
            requiredFactCoverage = coverage,
            prohibitedPhraseViolations,
            missingFactUsageViolations,
            language,
            warnings,
            errors
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        logger.LogInformation("Narration Generator V5 wrote {SceneCount} scenes to {NarrationPath}.", narrationScenes.Length, narrationPath);
        return new NarrationGeneratorV5Result([planPath, narrationPath, diagnosticsPath]);
    }

    private static NarrationPlanV5Scene BuildPlanScene(JsonElement scene, int index, IReadOnlyList<NarrationFactV5> facts)
    {
        var purpose = GetString(scene, "scenePurpose") ?? FallbackPurpose(index);
        var must = purpose == "Observation" || index == 0 ? facts : facts.Take(2).ToArray();
        return new NarrationPlanV5Scene(GetString(scene, "sceneId") ?? $"scene-{index + 1:000}", purpose, GetInt(scene, "sceneOrder") ?? index + 1, GetString(scene, "keyMessage") ?? "Explain the event using verified facts.", GetString(scene, "viewerFocus") ?? "Stay oriented to the sky event.", GetString(scene, "emotionalRole") ?? "Calm curiosity.", $"Narrate the {purpose.ToLowerInvariant()} beat with factual restraint.", facts, must, ["Do not invent missing altitude, constellation, brightness, weather, or optical-aid facts."], GetString(scene, "transitionIntent") ?? "Move cleanly to the next scene.", "calm documentary", purpose == "Observation" ? "medium" : "short");
    }

    private static NarrationV5Scene BuildNarrationScene(NarrationPlanV5Scene scene, IReadOnlyList<NarrationFactV5> facts, string language)
    {
        var factText = string.Join(", ", scene.MustMentionFacts.Select(f => $"{Humanize(f.Name)}: {f.Value}"));
        var guidance = scene.ScenePurpose == "Observation" ? " For observing, use the supported viewing window, look in the stated sky direction, and allow a clear horizon and a few quiet minutes for your eyes to settle." : string.Empty;
        var ending = scene.ScenePurpose is "Takeaway" or "Closing" ? $" {ChannelEnding}" : string.Empty;
        var text = language.Equals("hi", StringComparison.OrdinalIgnoreCase)
            ? $"{scene.KeyMessage} सत्यापित जानकारी के साथ: {factText}.{guidance}{ending}"
            : $"{scene.KeyMessage} Verified details: {factText}.{guidance}{ending}";
        return new NarrationV5Scene(scene.SceneId, scene.ScenePurpose, text, scene.MustMentionFacts.Select(f => f.Name).ToArray(), []);
    }

    private static IReadOnlyList<NarrationFactV5> ReadRequiredFacts(JsonElement? contract)
        => ReadArray(contract, "requiredNarrationFacts").Select(e => new NarrationFactV5(GetString(e, "name") ?? "Fact", GetString(e, "value") ?? string.Empty)).Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToArray();
    private static bool MentionsMissingFact(string text, string warning) => warning.Contains("altitude", StringComparison.OrdinalIgnoreCase) && text.Contains("altitude", StringComparison.OrdinalIgnoreCase);
    private static string Humanize(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    private static string FallbackPurpose(int index) => index switch { 0 => "Hook", 1 => "Discovery", 2 => "Science", 3 => "Observation", _ => "Takeaway" };
    private static JsonElement? ReadFirstJson(string path) { if (!File.Exists(path)) return null; using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.Clone(); }
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(i => i.Clone()).ToArray(); return []; }
    private static IReadOnlyList<string> FindStringArray(JsonElement? element, string name) => ReadArray(element, name).Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray();
    private static int? GetInt(JsonElement? element, string name) => int.TryParse(GetString(element, name), out var value) ? value : null;
    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(p.Value); return null; }
    private static string? ValueToString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record NarrationFactV5(string Name, string Value);
public sealed record NarrationPlanV5(string NarrationPlanVersion, string OrchestrationVersion, string Language, string VoiceProfile, string StoryArc, IReadOnlyList<NarrationFactV5> RequiredNarrationFacts, IReadOnlyList<string> ProhibitedPhrases, IReadOnlyList<string> PreferredPhrases, string ChannelEnding, IReadOnlyList<NarrationPlanV5Scene> Scenes);
public sealed record NarrationPlanV5Scene(string SceneId, string ScenePurpose, int SceneOrder, string KeyMessage, string ViewerFocus, string EmotionalRole, string NarrationIntent, IReadOnlyList<NarrationFactV5> RequiredFacts, IReadOnlyList<NarrationFactV5> MustMentionFacts, IReadOnlyList<string> MustAvoidFacts, string EditorialConnectorToNext, string TargetTone, string TargetLength);
public sealed record NarrationV5(string NarrationVersion, string OrchestrationVersion, string Language, IReadOnlyList<NarrationV5Scene> Scenes, string FullNarrationText, string ChannelEnding);
public sealed record NarrationV5Scene(string SceneId, string ScenePurpose, string NarrationText, IReadOnlyList<string> RequiredFactsCovered, IReadOnlyList<string> Warnings);
public sealed record RequiredFactCoverage(string Value, bool Covered);
public sealed record NarrationGeneratorV5Result(IReadOnlyList<string> GeneratedFiles) { public static NarrationGeneratorV5Result Empty { get; } = new([]); }
