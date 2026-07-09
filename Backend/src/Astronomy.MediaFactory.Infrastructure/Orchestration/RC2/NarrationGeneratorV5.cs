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
        var briefsPath = Path.Combine(narrationRoot, "narration-briefs.json");
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
        var briefs = NarrativeDirector.BuildBriefs(plan, FindStringArray(contract, "missingFactWarnings"));
        var narrationScenes = briefs.Select(brief => BuildNarrationScene(brief, language)).ToArray();
        var fullText = string.Join("\n\n", narrationScenes.Select(s => s.NarrationText));
        var narration = new NarrationV5("AstroPulse-Narration-v5", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, narrationScenes, fullText, ChannelEnding);

        var coverage = requiredFacts.ToDictionary(f => f.Name, f => new RequiredFactCoverage(f.Value, fullText.Contains(f.Value, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        foreach (var missing in coverage.Where(kv => !kv.Value.Covered).Select(kv => kv.Key)) warnings.Add($"Required fact was not covered naturally in full narration: {missing}.");
        var prohibitedViolations = prohibited.Where(p => fullText.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();
        var missingWarnings = FindStringArray(contract, "missingFactWarnings");
        var missingFactViolations = missingWarnings.Where(w => MentionsMissingFact(fullText, w)).ToArray();
        var factsDistributedByScene = briefs.ToDictionary(b => b.SceneId, b => b.FactsToMention.Select(f => f.Name).ToArray(), StringComparer.OrdinalIgnoreCase);
        var repeatedFactWarnings = factsDistributedByScene.SelectMany(kv => kv.Value.Select(f => new { SceneId = kv.Key, Fact = f })).GroupBy(x => x.Fact, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => $"Fact {g.Key} assigned to multiple scenes: {string.Join(", ", g.Select(x => x.SceneId))}.").ToArray();
        var narrationNaturalnessWarnings = BuildNaturalnessWarnings(fullText, narrationScenes);

        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(briefsPath, JsonSerializer.Serialize(new NarrationBriefsV5("AstroPulse-NarrationBriefs-v1", Rc2PipelinePhaseRegistry.OrchestrationVersion, language, briefs), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(narrationPath, JsonSerializer.Serialize(narration, JsonOptions), cancellationToken);
        var errors = prohibitedViolations.Concat(missingFactViolations).ToArray();
        var diagnostics = new
        {
            phaseName = PhaseName,
            orchestrationVersion = Rc2PipelinePhaseRegistry.OrchestrationVersion,
            inputs = new[]
            {
                new { path = NormalizePath(editorialPath), exists = File.Exists(editorialPath) },
                new { path = NormalizePath(storyboardPath), exists = File.Exists(storyboardPath) },
                new { path = NormalizePath(planPath), exists = File.Exists(planPath) }
            },
            outputsCreated = new[] { planPath, briefsPath, narrationPath, diagnosticsPath }.Select(path => new { path = NormalizePath(path), exists = File.Exists(path) || path == diagnosticsPath }).ToArray(),
            sceneCount = narrationScenes.Length,
            requiredFactCoverage = coverage,
            narrativeDirectorExecuted = true,
            narrationBriefCount = briefs.Length,
            factsDistributedByScene,
            repeatedFactWarnings,
            prohibitedPhraseViolations = prohibitedViolations,
            missingFactUsageViolations = missingFactViolations,
            narrationNaturalnessWarnings,
            language,
            warnings,
            errors
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        logger.LogInformation("Narration Generator V5 wrote {SceneCount} scenes to {NarrationPath}.", narrationScenes.Length, narrationPath);
        return new NarrationGeneratorV5Result([planPath, briefsPath, narrationPath, diagnosticsPath]);
    }

    private static NarrationPlanV5Scene BuildPlanScene(JsonElement scene, int index, IReadOnlyList<NarrationFactV5> facts)
    {
        var purpose = GetString(scene, "scenePurpose") ?? FallbackPurpose(index);
        var must = purpose == "Observation" || index == 0 ? facts : facts.Take(2).ToArray();
        return new NarrationPlanV5Scene(GetString(scene, "sceneId") ?? $"scene-{index + 1:000}", purpose, GetInt(scene, "sceneOrder") ?? index + 1, GetString(scene, "keyMessage") ?? "Explain the event using verified facts.", GetString(scene, "viewerFocus") ?? "Stay oriented to the sky event.", GetString(scene, "emotionalRole") ?? "Calm curiosity.", $"Narrate the {purpose.ToLowerInvariant()} beat with factual restraint.", facts, must, ["Do not invent missing altitude, constellation, brightness, weather, or optical-aid facts."], GetString(scene, "transitionIntent") ?? "Move cleanly to the next scene.", "calm documentary", purpose == "Observation" ? "medium" : "short");
    }

    private static NarrationV5Scene BuildNarrationScene(NarrationBriefV5 brief, string language)
    {
        var facts = brief.FactsToMention.ToDictionary(f => f.Name, f => f.Value, StringComparer.OrdinalIgnoreCase);
        var factValues = brief.FactsToMention.Select(f => f.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var purpose = brief.ScenePurpose;
        string text;

        if (language.Equals("hi", StringComparison.OrdinalIgnoreCase))
        {
            var core = factValues.Length == 0 ? brief.SceneGoal : $"{brief.SceneGoal} {string.Join(" ", factValues)}.";
            text = purpose.Equals("Observation", StringComparison.OrdinalIgnoreCase)
                ? $"{brief.SceneGoal} {BuildObservationGuidance(facts)}".Trim()
                : core;
        }
        else if (purpose.Equals("Hook", StringComparison.OrdinalIgnoreCase))
        {
            var identity = factValues.Length == 0 ? string.Empty : $" The event identity is {factValues[0]}.";
            text = $"{brief.SceneGoal}{identity} This is worth watching because it turns a familiar sky into a timed moment you can actually plan for.";
        }
        else if (purpose.Equals("Science", StringComparison.OrdinalIgnoreCase))
        {
            text = $"{brief.SceneGoal} The scene should make the mechanics feel simple: nearby-looking worlds line up from our point of view as orbital motion changes their apparent spacing in the sky.";
        }
        else if (purpose.Equals("Observation", StringComparison.OrdinalIgnoreCase))
        {
            text = $"{brief.SceneGoal} {BuildObservationGuidance(facts)}".Trim();
        }
        else if (purpose.Equals("Takeaway", StringComparison.OrdinalIgnoreCase) || purpose.Equals("Closing", StringComparison.OrdinalIgnoreCase))
        {
            text = $"{brief.SceneGoal} {brief.AudienceTakeaway} {ChannelEnding}";
        }
        else
        {
            var support = factValues.Length == 0 ? brief.AudienceTakeaway : string.Join(" ", factValues.Select(v => $"Keep {v} in mind as the story moves forward."));
            text = $"{brief.SceneGoal} {support}";
        }

        if (brief.MustIncludeEnding && !text.Contains(ChannelEnding, StringComparison.OrdinalIgnoreCase)) text = $"{text} {ChannelEnding}";
        return new NarrationV5Scene(brief.SceneId, brief.ScenePurpose, CleanNarration(text), brief.FactsToMention.Select(f => f.Name).ToArray(), brief.FactsToAvoid);
    }

    private static string BuildObservationGuidance(IReadOnlyDictionary<string, string> facts)
    {
        var parts = new List<string>();
        if (TryGetFact(facts, "bestViewingWindowLocal", out var window)) parts.Add($"use the {window} viewing window");
        if (TryGetFact(facts, "skyDirectionHint", out var direction)) parts.Add($"look toward the {direction}");
        if (TryGetFact(facts, "visibilityRegion", out var region)) parts.Add($"from {region}");
        if (TryGetFact(facts, "eventDateLocal", out var date) || TryGetFact(facts, "date", out date)) parts.Add($"on {date}");
        if (TryGetFact(facts, "eventTimeLocal", out var time) || TryGetFact(facts, "time", out time)) parts.Add($"around {time}");
        var sentence = parts.Count == 0
            ? "Follow only the confirmed timing and direction from your local guide before heading outside."
            : $"For the practical view, {string.Join(", ", parts)}.";
        return sentence + " Choose an open horizon and arrive a few minutes early so your eyes can settle.";
    }

    private static bool TryGetFact(IReadOnlyDictionary<string, string> facts, string name, out string value) => facts.TryGetValue(name, out value!) && !string.IsNullOrWhiteSpace(value);
    private static string CleanNarration(string text) => string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace("Verified details", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    private static IReadOnlyList<string> BuildNaturalnessWarnings(string fullText, IReadOnlyList<NarrationV5Scene> scenes)
    {
        var warnings = new List<string>();
        if (fullText.Contains("Verified details", StringComparison.OrdinalIgnoreCase)) warnings.Add("Narration contains a source-label phrase.");
        warnings.AddRange(scenes.Where(s => s.RequiredFactsCovered.Count > 3).Select(s => $"Scene {s.SceneId} may be carrying too many facts."));
        return warnings;
    }

    private static IReadOnlyList<NarrationFactV5> ReadRequiredFacts(JsonElement? contract)
        => ReadArray(contract, "requiredNarrationFacts").Select(e => new NarrationFactV5(GetString(e, "name") ?? "Fact", GetString(e, "value") ?? string.Empty)).Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToArray();
    private static bool MentionsMissingFact(string text, string warning)
    {
        var guardedTerms = new[] { "altitude", "constellation", "brightness", "weather", "optical aid", "optical-aid", "binocular", "telescope" };
        return guardedTerms.Any(term => warning.Contains(term, StringComparison.OrdinalIgnoreCase) && text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
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

public static class NarrativeDirector
{
    public static NarrationBriefV5[] BuildBriefs(NarrationPlanV5 plan, IReadOnlyList<string> missingFactWarnings)
    {
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scenes = plan.Scenes.OrderBy(s => s.SceneOrder).ToArray();
        var finalSceneId = scenes.LastOrDefault()?.SceneId;
        var briefs = new List<NarrationBriefV5>();

        foreach (var scene in scenes)
        {
            var purpose = scene.ScenePurpose;
            var facts = SelectFactsForScene(purpose, plan.RequiredNarrationFacts, assigned);
            foreach (var fact in facts) assigned.Add(fact.Name);

            var avoid = scene.MustAvoidFacts.Concat(missingFactWarnings).Concat(["Do not expose phrases like Verified details in narration."]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var alreadyCovered = assigned.Except(facts.Select(f => f.Name), StringComparer.OrdinalIgnoreCase).ToArray();
            var isFinal = string.Equals(scene.SceneId, finalSceneId, StringComparison.OrdinalIgnoreCase);
            briefs.Add(new NarrationBriefV5(
                scene.SceneId,
                purpose,
                scene.SceneOrder,
                BuildSceneGoal(scene, facts),
                BuildTakeaway(purpose),
                facts,
                avoid,
                alreadyCovered,
                scene.EditorialConnectorToNext,
                scene.TargetTone,
                purpose.Equals("Observation", StringComparison.OrdinalIgnoreCase) ? "measured and practical" : "natural documentary",
                scene.TargetLength,
                isFinal,
                BuildInstructions(purpose, isFinal)));
        }

        return briefs.ToArray();
    }

    private static NarrationFactV5[] SelectFactsForScene(string purpose, IReadOnlyList<NarrationFactV5> facts, HashSet<string> assigned)
    {
        bool NameContains(NarrationFactV5 fact, params string[] terms) => terms.Any(term => fact.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        var pool = facts.Where(f => !assigned.Contains(f.Name)).ToArray();
        var selected = purpose.ToLowerInvariant() switch
        {
            "hook" => pool.Where(f => NameContains(f, "event", "title", "primary", "object")).Take(1).ToArray(),
            "science" => pool.Where(f => NameContains(f, "separation", "illumination", "interference", "object", "moon")).Take(2).ToArray(),
            "observation" => pool.Where(f => NameContains(f, "window", "direction", "date", "time", "region", "utc", "local")).Take(4).ToArray(),
            "takeaway" or "closing" => pool.Take(1).ToArray(),
            _ => pool.Take(1).ToArray()
        };

        return selected.Length > 0 ? selected : [];
    }

    private static string BuildSceneGoal(NarrationPlanV5Scene scene, IReadOnlyList<NarrationFactV5> facts)
    {
        if (!string.IsNullOrWhiteSpace(scene.KeyMessage)) return scene.KeyMessage;
        return scene.ScenePurpose switch
        {
            "Hook" => "Introduce the sky event and why it matters now.",
            "Science" => "Explain why the event appears the way it does.",
            "Observation" => "Give practical viewing guidance using only confirmed details.",
            "Takeaway" or "Closing" => "Reinforce the significance of noticing the sky on purpose.",
            _ => "Move the documentary story forward without overloading the viewer."
        };
    }

    private static string BuildTakeaway(string purpose) => purpose switch
    {
        "Hook" => "The viewer should know what event this is and why it deserves attention.",
        "Science" => "The viewer should understand the why and how behind the view.",
        "Observation" => "The viewer should know when, where, and how to try seeing it safely and realistically.",
        "Takeaway" or "Closing" => "The viewer should leave with a clear sense that ordinary nights can reveal extraordinary motion.",
        _ => "The viewer should stay oriented without hearing a checklist."
    };

    private static string BuildInstructions(string purpose, bool isFinal)
    {
        var ending = isFinal ? " Include the exact channel ending." : " Do not include the channel ending.";
        return purpose switch
        {
            "Hook" => "Mention only the event identity and why it matters; do not list every fact." + ending,
            "Science" => "Explain why or how the event works in plain documentary language." + ending,
            "Observation" => "Use available date, time, viewing window, direction, and practical viewing instructions; never invent altitude, constellation, brightness, weather, or optical aids." + ending,
            "Takeaway" or "Closing" => "Reinforce significance and close warmly." + ending,
            _ => "Narrate naturally, distribute facts lightly, and avoid source-label phrases like Verified details." + ending
        };
    }
}

public sealed record NarrationBriefsV5(string NarrationBriefsVersion, string OrchestrationVersion, string Language, IReadOnlyList<NarrationBriefV5> Briefs);
public sealed record NarrationBriefV5(string SceneId, string ScenePurpose, int SceneOrder, string SceneGoal, string AudienceTakeaway, IReadOnlyList<NarrationFactV5> FactsToMention, IReadOnlyList<string> FactsToAvoid, IReadOnlyList<string> AlreadyCoveredFacts, string ConnectorToNext, string Tone, string Pacing, string TargetLength, bool MustIncludeEnding, string GenerationInstructions);
