using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class NarrationPromptComposer : IPromptComposer<NarrationPromptComposerInput, NarrationPromptComposerOutput>
{
    public const string ComposerName = "NarrationPromptComposer";
    public static readonly string[] PromptSections =
    [
        "System Role",
        "Astro Pulse Identity",
        "Voice Profile",
        "Editorial Rules",
        "Creative Context",
        "Story Arc",
        "Scene Briefs",
        "Fact Usage Rules",
        "Prohibited Phrases",
        "Output Format"
    ];

    public static readonly string[] ProhibitedInternalPhrases =
    [
        "Verified details",
        "event identity",
        "the viewer should",
        "scene goal",
        "facts to mention",
        "metadata"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public NarrationPromptComposerOutput Compose(NarrationPromptComposerInput input)
    {
        var missing = new List<string>();
        if (!input.EditorialContract.HasValue) missing.Add("Missing input file editorial/editorial-contract.json.");
        if (!input.CreativeStoryboard.HasValue) missing.Add("Missing input file creative/creative-storyboard.json.");
        if (input.NarrationBriefs is null || input.NarrationBriefs.Briefs.Count == 0) missing.Add("Missing or empty input file narration-v5/narration-briefs.json.");

        var language = FirstNonEmpty(GetString(input.EditorialContract, "language"), GetString(input.CreativeStoryboard, "language"), input.NarrationBriefs?.Language, "en")!;
        var storyArc = FirstNonEmpty(GetString(input.CreativeStoryboard, "storyArc"), "Hook → Discovery → Science → Observation → Takeaway")!;
        var requiredFacts = ReadFacts(input.EditorialContract, "requiredNarrationFacts");
        var prohibitedPhrases = FindStringArray(input.EditorialContract, "prohibitedPhrases").Concat(ProhibitedInternalPhrases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var preferredPhrases = FindStringArray(input.EditorialContract, "preferredPhrases");
        var scenes = input.NarrationBriefs?.Briefs.OrderBy(b => b.SceneOrder).ToArray() ?? [];

        var prompt = BuildPrompt(language, storyArc, requiredFacts, prohibitedPhrases, preferredPhrases, scenes);
        var outputFiles = new[] { input.PromptPreviewPath, input.PromptDiagnosticsPath };
        var diagnostics = new NarrationPromptDiagnostics(
            ComposerName,
            Rc2PipelinePhaseRegistry.OrchestrationVersion,
            input.InputFiles.Select(NormalizePath).ToArray(),
            outputFiles.Select(NormalizePath).ToArray(),
            scenes.Length,
            PromptSections.Length,
            ProhibitedInternalPhrases,
            missing,
            missing.Count == 0);

        return new NarrationPromptComposerOutput(prompt, diagnostics);
    }

    public async Task<NarrationPromptComposerOutput> ComposeAndWriteAsync(NarrationPromptComposerInput input, CancellationToken cancellationToken)
    {
        var output = Compose(input);
        Directory.CreateDirectory(Path.GetDirectoryName(input.PromptPreviewPath)!);
        await File.WriteAllTextAsync(input.PromptPreviewPath, output.PromptPreviewMarkdown, cancellationToken);
        await File.WriteAllTextAsync(input.PromptDiagnosticsPath, JsonSerializer.Serialize(output.Diagnostics, JsonOptions), cancellationToken);
        return output;
    }

    private static string BuildPrompt(string language, string storyArc, IReadOnlyList<NarrationFactV5> facts, IReadOnlyList<string> prohibited, IReadOnlyList<string> preferred, IReadOnlyList<NarrationBriefV5> scenes)
    {
        var sb = new StringBuilder();
        AddSection(sb, 1, "System Role", "You are a senior documentary narration writer for an astronomy video. Write natural documentary narration only. Do not expose planning notes, source labels, or production metadata.");
        AddSection(sb, 2, "Astro Pulse Identity", "The channel voice is Astro Pulse: calm, precise, cinematic, practical, and grounded in verified astronomy. The narration should feel warm and human, not like a checklist.");
        AddSection(sb, 3, "Voice Profile", $"Language: {language}. Tone: calm documentary. Use clear spoken sentences that are compatible with scene-based TTS and SRT segmentation.");
        AddSection(sb, 4, "Editorial Rules", BuildEditorialRules(preferred));
        AddSection(sb, 5, "Creative Context", BuildCreativeContext(storyArc));
        AddSection(sb, 6, "Story Arc", storyArc);
        AddSection(sb, 7, "Scene Briefs", BuildSceneBriefs(scenes));
        AddSection(sb, 8, "Fact Usage Rules", BuildFactRules(facts));
        AddSection(sb, 9, "Prohibited Phrases", string.Join("\n", prohibited.Select(p => $"- Do not use: {p}")));
        AddSection(sb, 10, "Output Format", "Return scene-based output only, preserving each sceneId and scenePurpose. Each scene must contain narrationText suitable for direct TTS/SRT use. Do not add markdown, bullet lists, diagnostics, metadata, or commentary inside the generated narration. The observation scene must include practical viewing guidance. The final scene must include the channel ending exactly once: \"Until next time, keep looking up.\"");
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AddSection(StringBuilder sb, int number, string title, string body) => sb.AppendLine($"## {number}. {title}").AppendLine(body.Trim()).AppendLine();

    private static string BuildEditorialRules(IReadOnlyList<string> preferred) => "Write natural documentary narration. Do not list facts mechanically. Use facts naturally in sentences. Do not invent missing facts. Do not say or imply unavailable altitude, constellation, brightness, weather, equipment, or optical-aid details. Avoid internal phrases such as Verified details, event identity, the viewer should, scene goal, facts to mention, and metadata." + (preferred.Count == 0 ? string.Empty : $"\nPreferred wording cues: {string.Join(", ", preferred)}.");
    private static string BuildCreativeContext(string storyArc) => $"Shape the narration around the creative storyboard and its arc: {storyArc}. Keep transitions cinematic but concise.";
    private static string BuildFactRules(IReadOnlyList<NarrationFactV5> facts) => facts.Count == 0 ? "No required facts were supplied. Use only facts present in the scene briefs and do not invent missing details." : "Use these verified facts only when they fit naturally; do not dump them as a list:\n" + string.Join("\n", facts.Select(f => $"- {f.Name}: {f.Value}"));
    private static string BuildSceneBriefs(IReadOnlyList<NarrationBriefV5> scenes) => scenes.Count == 0 ? "No scene briefs were supplied." : string.Join("\n\n", scenes.Select(s => $"Scene {s.SceneOrder}: {s.SceneId} ({s.ScenePurpose})\n- Intent: {s.GenerationInstructions}\n- Takeaway cue: {s.AudienceTakeaway}\n- Connector: {s.ConnectorToNext}\n- Tone/Pacing: {s.Tone}; {s.Pacing}\n- Target length: {s.TargetLength}\n- Available facts: {FormatFacts(s.FactsToMention)}\n- Avoid: {string.Join("; ", s.FactsToAvoid)}"));
    private static string FormatFacts(IReadOnlyList<NarrationFactV5> facts) => facts.Count == 0 ? "none" : string.Join("; ", facts.Select(f => $"{f.Name}={f.Value}"));
    private static IReadOnlyList<NarrationFactV5> ReadFacts(JsonElement? element, string name) => ReadArray(element, name).Select(e => new NarrationFactV5(GetString(e, "name") ?? "Fact", GetString(e, "value") ?? string.Empty)).Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToArray();
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(i => i.Clone()).ToArray(); return []; }
    private static IReadOnlyList<string> FindStringArray(JsonElement? element, string name) => ReadArray(element, name).Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray();
    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(p.Value); return null; }
    private static string? ValueToString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record NarrationPromptComposerInput(JsonElement? EditorialContract, JsonElement? CreativeStoryboard, NarrationBriefsV5? NarrationBriefs, IReadOnlyList<string> InputFiles, string PromptPreviewPath, string PromptDiagnosticsPath);
public sealed record NarrationPromptComposerOutput(string PromptPreviewMarkdown, NarrationPromptDiagnostics Diagnostics);
public sealed record NarrationPromptDiagnostics(string ComposerName, string OrchestrationVersion, IReadOnlyList<string> InputFiles, IReadOnlyList<string> OutputFiles, int SceneCount, int PromptSectionCount, IReadOnlyList<string> ProhibitedInternalPhraseList, IReadOnlyList<string> MissingInputWarnings, bool ReadyForGeneration);
