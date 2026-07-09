using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class NarrationPromptComposer : IPromptComposer<NarrationPromptComposerInput, NarrationPromptComposerOutput>
{
    public const string ComposerName = "NarrationPromptComposer";
    public static readonly string[] PromptSections =
    [
        "Your Role",
        "Astro Pulse Editorial Identity",
        "Story Overview",
        "Producer Notes",
        "Scientific Guardrails",
        "Writing Principles",
        "Output Contract"
    ];

    public static readonly string[] ProhibitedInternalPhrases =
    [
        "Verified details",
        "event identity",
        "the viewer should",
        "scene goal",
        "facts to mention",
        "metadata",
        "available facts",
        "prompt",
        "JSON",
        "planning",
        "checklist"
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
        if (scenes.Length > 0)
        {
            requiredFacts = scenes.SelectMany(s => s.FactsToMention)
                .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray();
        }
        var styleContract = input.DocumentaryStyleContract;

        var prompt = BuildPrompt(language, storyArc, requiredFacts, prohibitedPhrases, preferredPhrases, scenes, styleContract);
        prompt = new PromptLanguageCleaner().Clean(prompt);
        var quality = new PromptQualityEvaluator().Evaluate(prompt, scenes.Length, input.PromptQualityThreshold);
        var promptQualityPath = ResolvePromptQualityPath(input);
        var outputFiles = new[] { input.PromptPreviewPath, input.PromptDiagnosticsPath, promptQualityPath };
        var diagnostics = new NarrationPromptDiagnostics(
            ComposerName,
            Rc2PipelinePhaseRegistry.OrchestrationVersion,
            input.InputFiles.Select(NormalizePath).ToArray(),
            outputFiles.Select(NormalizePath).ToArray(),
            scenes.Length,
            PromptSections.Length,
            ProhibitedInternalPhrases,
            missing.Concat(quality.Warnings).ToArray(),
            missing.Count == 0);

        return new NarrationPromptComposerOutput(prompt, diagnostics, quality);
    }

    public async Task<NarrationPromptComposerOutput> ComposeAndWriteAsync(NarrationPromptComposerInput input, CancellationToken cancellationToken)
    {
        var output = Compose(input);
        Directory.CreateDirectory(Path.GetDirectoryName(input.PromptPreviewPath)!);
        var promptQualityPath = ResolvePromptQualityPath(input);
        await File.WriteAllTextAsync(input.PromptPreviewPath, output.PromptPreviewMarkdown, cancellationToken);
        await File.WriteAllTextAsync(promptQualityPath, JsonSerializer.Serialize(output.PromptQuality, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(input.PromptDiagnosticsPath, JsonSerializer.Serialize(output.Diagnostics, JsonOptions), cancellationToken);
        return output;
    }

    private static string BuildPrompt(string language, string storyArc, IReadOnlyList<NarrationFactV5> facts, IReadOnlyList<string> prohibited, IReadOnlyList<string> preferred, IReadOnlyList<NarrationBriefV5> scenes, DocumentaryStyleContract? styleContract)
    {
        var sb = new StringBuilder();
        AddSection(sb, 1, "Your Role", new RoleSectionBuilder().Build());
        AddSection(sb, 2, "Astro Pulse Editorial Identity", new EditorialIdentitySectionBuilder().Build());
        AddSection(sb, 3, "Story Overview", new StoryOverviewSectionBuilder().Build(language, storyArc));
        AddSection(sb, 4, "Producer Notes", new SceneEditorialBriefBuilder().Build(scenes, styleContract));
        AddSection(sb, 5, "Scientific Guardrails", new ScientificGuardrailSectionBuilder().Build(facts, prohibited));
        AddSection(sb, 6, "Writing Principles", new WritingPrinciplesSectionBuilder().Build(preferred, prohibited, styleContract));
        AddSection(sb, 7, "Output Contract", new OutputContractSectionBuilder().Build());
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AddSection(StringBuilder sb, int number, string title, string body) => sb.AppendLine($"## {number}. {title}").AppendLine(body.Trim()).AppendLine();

    private static string BuildGuardrails(IReadOnlyList<NarrationFactV5> facts, IReadOnlyList<string> prohibited)
    {
        var details = facts.Count == 0 ? "No confirmed sky details were supplied. Stay general and do not invent specifics." : "Natural details to weave in when they serve the scene:\n" + string.Join("\n", facts.Select(f => $"- {NormalizeFact(f)}"));
        var blocked = prohibited.Count == 0 ? "Do not state or imply unavailable altitude, constellation, brightness, weather, equipment, or optical-aid details." : "Do not state or imply:\n" + string.Join("\n", prohibited.Select(p => $"- {HumanizeProhibited(p)}"));
        return details + "\n\n" + blocked;
    }

    private static string BuildWritingPrinciples(IReadOnlyList<string> preferred, DocumentaryStyleContract? styleContract)
    {
        var phrases = styleContract?.VocabularyRules.Count > 0 ? styleContract.VocabularyRules : preferred;
        var rhythm = styleContract is null ? string.Empty : $"\nDocumentary rhythm for every scene: {styleContract.DocumentaryRhythm.Observe} → {styleContract.DocumentaryRhythm.Wonder} → {styleContract.DocumentaryRhythm.Understand} → {styleContract.DocumentaryRhythm.Continue}.";
        return "Write in natural documentary prose, not labels or checklists. Weave details into sentences only when they help the moment. Use gentle transitions, concrete sky language, and realistic observing advice." + rhythm + (phrases.Count == 0 ? string.Empty : $"\nApproved documentary phrasing palette: {string.Join(", ", phrases)}.");
    }

    private static string BuildSceneBriefs(IReadOnlyList<NarrationBriefV5> scenes, DocumentaryStyleContract? styleContract) => scenes.Count == 0 ? "No scene briefs were supplied." : string.Join("\n\n", scenes.Select(s =>
    {
        var style = styleContract?.SceneStyles.FirstOrDefault(scene => string.Equals(scene.SceneId, s.SceneId, StringComparison.OrdinalIgnoreCase));
        var styleLines = style is null ? string.Empty : $"\n- Documentary opening: {style.OpeningStyle}\n- Documentary development: {style.DevelopmentStyle}\n- Documentary closing: {style.ClosingStyle}\n- Semantic transition: {style.TransitionStyle}\n- Fact transformations: {FormatList(style.FactTransformations)}";
        return $"Scene {s.SceneOrder}: {s.SceneId} ({s.ScenePurpose})\n- Editorial objective: {RewriteInstructions(s.SceneGoal)}\n- Audience promise: {RewriteAudiencePromise(s.AudienceTakeaway)}\n- Natural details to weave in: {FormatFacts(s.FactsToMention)}\n- Do not state or imply: {FormatAvoidance(s.FactsToAvoid)}\n- Lead naturally into: {s.ConnectorToNext}\n- Writing rhythm: {s.Tone}; {s.Pacing}; {s.TargetLength}. {RewriteInstructions(s.GenerationInstructions)}{styleLines}";
    }));

    private static string FormatList(IReadOnlyList<string> values) => values.Count == 0 ? "none supplied" : string.Join("; ", values);
    private static string FormatFacts(IReadOnlyList<NarrationFactV5> facts) => facts.Count == 0 ? "none supplied" : string.Join("; ", facts.Select(NormalizeFact));

    public static string NormalizeFact(NarrationFactV5 fact)
    {
        var label = NormalizeFactName(fact.Name);
        var value = fact.Value.Trim();
        if (fact.Name.Contains("RelativePositions", StringComparison.OrdinalIgnoreCase) && decimal.TryParse(value, out var degrees)) value = $"about {degrees:0.##} degrees";
        return $"{label}: {value}";
    }

    public static string NormalizeFactName(string name) => name.ToLowerInvariant() switch
    {
        "eventdate" or "eventdatelocal" => "Peak date/time",
        "bestviewingtime" or "bestviewingwindowlocal" => "Best viewing window",
        "viewingwindow" => "Viewing window",
        "direction" or "skydirectionhint" => "Where to look",
        "visibility" or "visibilityregion" => "Viewing region",
        "relativepositions" => "Angular separation",
        "eventtimelocal" => "Peak time",
        _ when name.Contains("separation", StringComparison.OrdinalIgnoreCase) => "Angular separation",
        _ when name.Contains("direction", StringComparison.OrdinalIgnoreCase) => "Where to look",
        _ when name.Contains("window", StringComparison.OrdinalIgnoreCase) => "Best viewing window",
        _ when name.Contains("region", StringComparison.OrdinalIgnoreCase) => "Viewing region",
        _ => HumanizeName(name)
    };

    private static string HumanizeName(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : i == 0 ? char.ToUpperInvariant(c).ToString() : c.ToString())).Replace("Utc", "UTC", StringComparison.OrdinalIgnoreCase);
    private static string FormatAvoidance(IReadOnlyList<string> avoid) => avoid.Count == 0 ? "no unsupported details" : string.Join("; ", avoid.Select(HumanizeProhibited));
    private static string HumanizeProhibited(string value) => value.Replace("Verified details", "source-label language", StringComparison.OrdinalIgnoreCase).Replace("event identity", "internal identity wording", StringComparison.OrdinalIgnoreCase).Replace("the viewer should", "instructional audience wording", StringComparison.OrdinalIgnoreCase).Replace("scene goal", "planning labels", StringComparison.OrdinalIgnoreCase).Replace("facts to mention", "planning labels", StringComparison.OrdinalIgnoreCase).Replace("metadata", "production notes", StringComparison.OrdinalIgnoreCase).Replace("available facts", "planning labels", StringComparison.OrdinalIgnoreCase).Replace("prompt", "production notes", StringComparison.OrdinalIgnoreCase).Replace("JSON", "data-format language", StringComparison.OrdinalIgnoreCase);
    private static string RewriteAudiencePromise(string value) => value.Replace("The viewer should", "Leave the audience able to", StringComparison.OrdinalIgnoreCase);
    private static string RewriteInstructions(string value) => value.Replace("event identity", "what the sky event is", StringComparison.OrdinalIgnoreCase).Replace("Verified details", "source labels", StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyList<NarrationFactV5> ReadFacts(JsonElement? element, string name) => ReadArray(element, name).Select(e => new NarrationFactV5(GetString(e, "name") ?? "Fact", GetString(e, "value") ?? string.Empty)).Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToArray();
    private static IReadOnlyList<JsonElement> ReadArray(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return []; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().Select(i => i.Clone()).ToArray(); return []; }
    private static IReadOnlyList<string> FindStringArray(JsonElement? element, string name) => ReadArray(element, name).Select(ValueToString).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray();
    private static string? GetString(JsonElement? element, string name) { if (element is not { ValueKind: JsonValueKind.Object } e) return null; foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return ValueToString(p.Value); return null; }
    private static string? ValueToString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static string ResolvePromptQualityPath(NarrationPromptComposerInput input) => string.IsNullOrWhiteSpace(input.PromptQualityPath)
        ? Path.Combine(Path.GetDirectoryName(input.PromptPreviewPath) ?? string.Empty, "prompt-quality.json")
        : input.PromptQualityPath;
    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}

public sealed record NarrationPromptComposerInput(JsonElement? EditorialContract, JsonElement? CreativeStoryboard, NarrationBriefsV5? NarrationBriefs, IReadOnlyList<string> InputFiles, string PromptPreviewPath, string PromptDiagnosticsPath, DocumentaryStyleContract? DocumentaryStyleContract = null, string PromptQualityPath = "", int PromptQualityThreshold = 80);
public sealed record NarrationPromptComposerOutput(string PromptPreviewMarkdown, NarrationPromptDiagnostics Diagnostics, PromptQualityContract PromptQuality);
public sealed record NarrationPromptDiagnostics(string ComposerName, string OrchestrationVersion, IReadOnlyList<string> InputFiles, IReadOnlyList<string> OutputFiles, int SceneCount, int PromptSectionCount, IReadOnlyList<string> ProhibitedInternalPhraseList, IReadOnlyList<string> MissingInputWarnings, bool ReadyForGeneration);
