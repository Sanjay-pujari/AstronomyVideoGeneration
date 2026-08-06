using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class NarrationPromptComposer : IPromptComposer<NarrationPromptComposerInput, NarrationPromptComposerOutput>
{
    public const string ComposerName = "NarrationPromptComposer";
    public static readonly string[] PromptSections =
    [
        "Output Language",
        "Narrative Role",
        "Documentary Purpose",
        "Speakable Facts",
        "Scientific Boundaries",
        "Observation Details",
        "Transition Intent",
        "Voice and Rhythm",
        "Word Budget",
        "Prohibited Content",
        "Scientific Guardrails",
        "Astro Pulse Voice Profile",
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
        if (input.NarrationContext.Formats.Count == 0 || input.NarrationContext.Formats.All(f => f.Beats.Count == 0)) missing.Add("Missing or empty input file narration-v5/narration-context.json.");

        var contextFacts = input.NarrationContext.Formats
            .SelectMany(f => f.Beats)
            .SelectMany(b => b.VerifiedFacts)
            .GroupBy(f => f.FactKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NarrationFactV5(g.First().FactKey, g.First().Value))
            .ToArray();
        var prompt = (input.Realizations?.Count ?? 0) > 0 ? BuildRealizedPrompt(input.Realizations!, input.LanguageProfile) : BuildPrompt(input.NarrationContext, contextFacts, input.LanguageProfile);
        prompt = new PromptLanguageCleaner().Clean(prompt);
        var sceneCount = input.NarrationContext.Formats.Sum(f => f.Beats.Count);
        var quality = new PromptQualityEvaluator().Evaluate(prompt, sceneCount, input.PromptQualityThreshold);
        var promptQualityPath = ResolvePromptQualityPath(input);
        var outputFiles = new[] { input.PromptPreviewPath, input.PromptDiagnosticsPath, promptQualityPath };
        var diagnostics = new NarrationPromptDiagnostics(
            ComposerName,
            Rc2PipelinePhaseRegistry.OrchestrationVersion,
            input.InputFiles.Select(NormalizePath).ToArray(),
            outputFiles.Select(NormalizePath).ToArray(),
            sceneCount,
            PromptSections.Length,
            ProhibitedInternalPhrases,
            missing.Concat(quality.Warnings).ToArray(),
            missing.Count == 0,
            input.LanguageProfile is not null,
            input.LanguageProfile is null ? "missing" : "sections 1 and 4",
            input.LanguageProfile is not null && prompt.Contains(input.LanguageProfile.DisplayName, StringComparison.OrdinalIgnoreCase),
            input.LanguageProfile is not null && prompt.Contains(input.LanguageProfile.Culture, StringComparison.OrdinalIgnoreCase),
            input.LanguageProfile is not null && !input.LanguageProfile.LanguageCode.Equals("en", StringComparison.OrdinalIgnoreCase),
            false,
            input.LanguageProfile?.Source ?? "none",
            input.LanguageProfile?.TerminologySource ?? "none");

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

    private static string BuildPrompt(NarrationContextDocument context, IReadOnlyList<NarrationFactV5> facts, LanguageProfile? languageProfile)
    {
        var sb = new StringBuilder();
        AddSection(sb, 1, "Your Role", "Perform the supplied narration context only. Write narration, not production instructions. Do not expose labels, data formats, internal identifiers, or visual-production language.");
        if (languageProfile is not null) AddSection(sb, 2, "Output Language", BuildLanguageHeader(languageProfile));
        AddSection(sb, 3, "Narration Context", FormatNarrationContext(context));
        AddSection(sb, 4, "Scientific Guardrails", new ScientificGuardrailSectionBuilder().Build(facts, ProhibitedInternalPhrases));
        AddSection(sb, 7, "Output Contract", new OutputContractSectionBuilder().Build() + (languageProfile is null ? string.Empty : "\n\nFinal language constraint: " + languageProfile.OutputInstruction));
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }


    private static string BuildRealizedPrompt(IReadOnlyList<NarrationRealizationResult> realizations, LanguageProfile? languageProfile)
    {
        var sb = new StringBuilder();
        AddSection(sb, 1, "OUTPUT LANGUAGE", languageProfile is null ? "Use the requested language profile." : BuildLanguageHeader(languageProfile));
        AddSection(sb, 2, "PERFORMANCE CONTRACT", "You are writing polished spoken narration for a professional astronomy documentary. Use the supplied facts and scene purpose to write natural viewer-facing prose. Explain astronomy facts naturally rather than listing them, give every scene a distinct opening, and write Short independently from Long.");
        foreach (var pair in realizations.Select((item, index) => (item, index)))
        {
            var item = pair.item;
            var projection = ProviderSemanticProjection.Project(item);
            AddSection(sb, 3, $"SCENE {pair.index + 1}", $"Purpose:\n{projection.Purpose}\n\nGrounded astronomy:\n{FormatProjectionList(projection.FactualStatements)}\n\nApproved names and terminology:\n{FormatProjectionList(projection.ObjectVocabulary)}\n\nPronunciation guidance:\n{FormatProjectionList(projection.Pronunciations)}\n\nScientific boundaries:\n{FormatList(item.ScientificBoundaries.Select(CleanSemanticText).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray())}\n\nObservation guidance:\n{FormatProjectionList(projection.ObservationStatements)}\n\nTransition:\n{projection.Transition}\n\nTone and pacing:\n{CleanSemanticText(item.Tone)}; {CleanSemanticText(item.Rhythm)}\n\nDuration guidance:\nApproximately {item.WordBudget.ToString(CultureInfo.InvariantCulture)} spoken words.");
        }
        AddSection(sb, 11, "NEGATIVE OUTPUT RULES", "Do not mention or repeat IDs, field names, labels, contracts, phases, validation, producer notes, internal instructions, data structures, scene role names, transition codes, or placeholder names. Scene numbers are mapping fields only and must never be spoken.");
        AddSection(sb, 12, "OUTPUT CONTRACT", new OutputContractSectionBuilder().Build());
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string FormatSemanticFacts(IReadOnlyList<RealizedSemanticFact> facts)
        => facts.Count == 0 ? "none supplied" : string.Join("\n", facts.Select(f => $"- {CleanSemanticText(f.Label)}: {CleanSemanticText(f.Value)}" + (string.IsNullOrWhiteSpace(f.Unit) ? string.Empty : $" {CleanSemanticText(f.Unit)}")));

    private static string FormatTransition(TransitionIntent? transition)
        => transition is null ? "Continue naturally to the next idea." : $"Move naturally from {CleanSemanticText(transition.FromConcept)} toward {CleanSemanticText(transition.ToConcept)}.";

    private static string CleanSemanticText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = Regex.Replace(value.Trim(), @"\b(?:Scene|ViewerQuestion|LearningObjective|Claim|KnowledgeReference|StoryFrame|BlueprintScene|Authority)Id\s*[:=]?\s*[A-Za-z0-9_.:-]*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        clean = Regex.Replace(clean, @"\bAdvance\d{1,3}\b", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(clean, @"\s{2,}", " ").Trim(' ', '-', ':');
    }

    private static string BuildLanguageHeader(LanguageProfile profile)
        => $"Requested output language: {profile.DisplayName}\nLanguage code: {profile.Culture}\nScript: {profile.Script}\n\n{profile.OutputInstruction}\n\nThe brief below is semantic guidance. Realize its meaning as natural {profile.DisplayName} narration, without copying labels or guidance text.\n\nTerminology policy: {string.Join("; ", profile.Terminology.Select(kv => $"{kv.Key} → {kv.Value}"))}";

    private static string FormatNarrationContext(NarrationContextDocument context)
        => string.Join("\n\n", context.Formats.Select(format => $"Format: {format.Format}\n" + string.Join("\n", format.Beats.Select((beat, index) =>
            $"Beat {index + 1}: narrative role: {beat.KnowledgeGoal} documentary purpose: {beat.AudienceOutcome} editorial intent: {beat.EditorialIntent} localized speakable facts: {FormatVerifiedFacts(beat.VerifiedFacts)} scientific boundaries: {FormatList(beat.ScientificConstraints)} observation details: {beat.ObservationObjective ?? "none"} transition relationship: {beat.TransitionGoal} tone: {beat.Tone} rhythm: {beat.NarrativeRhythm}"))));

    private static string FormatVerifiedFacts(IReadOnlyList<NarrationVerifiedFact> facts) => facts.Count == 0 ? "none" : string.Join("; ", facts.Select(f => $"{NormalizeFactName(f.FactKey)} — {f.Value}"));

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
    private static string FormatProjectionList(IReadOnlyList<string> values) => values.Count == 0 ? "none supplied" : string.Join("\n", values.Select(value => "- " + value));
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

public sealed record NarrationPromptComposerInput(NarrationContextDocument NarrationContext, IReadOnlyList<string> InputFiles, string PromptPreviewPath, string PromptDiagnosticsPath, string PromptQualityPath = "", int PromptQualityThreshold = 80, LanguageProfile? LanguageProfile = null, IReadOnlyList<NarrationRealizationResult>? Realizations = null);
public sealed record NarrationPromptComposerOutput(string PromptPreviewMarkdown, NarrationPromptDiagnostics Diagnostics, PromptQualityContract PromptQuality);

public sealed record ProviderSemanticProjectionResult(
    string Purpose, string Transition, IReadOnlyList<string> FactualStatements,
    IReadOnlyList<string> ObjectVocabulary, IReadOnlyList<string> Pronunciations,
    IReadOnlyList<string> UnsupportedFragments, IReadOnlyList<string> ObservationStatements);

/// <summary>Last-mile projection only: committed inputs remain untouched while provider text is performer-safe.</summary>
public static class ProviderSemanticProjection
{
    private static readonly Regex Internal = new(@"\b(?:Advance|Outcome)\d+\b|\bOrion Gold scene\b|\b(?:ScientificExplanation|Hook|Discovery|Observation|Takeaway|Closing)\b|certified evidence", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SentenceVerb = new(@"\b(?:is|are|was|were|has|have|contains?|forms?|appears?|lies|marks?|shines?|moves?|rises?|sets?|can|will)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ProviderSemanticProjectionResult Project(NarrationRealizationResult realization)
    {
        var facts = new List<string>(); var names = new List<string>(); var pronunciations = new List<string>(); var unsupported = new List<string>();
        foreach (var fact in realization.SpeakableFacts)
        {
            var value = Clean(fact.Value + (string.IsNullOrWhiteSpace(fact.Unit) ? string.Empty : " " + fact.Unit));
            if (string.IsNullOrWhiteSpace(value)) { unsupported.Add(fact.Value); continue; }
            if (fact.Label.Contains("pronunciation", StringComparison.OrdinalIgnoreCase) || fact.Label.Contains("alias", StringComparison.OrdinalIgnoreCase)) pronunciations.Add(value);
            else if (IsStatement(value)) facts.Add(value.TrimEnd('.') + ".");
            else if (Regex.IsMatch(value, @"^[\p{L}][\p{L}'’ -]{0,40}$", RegexOptions.CultureInvariant) && value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3) names.Add(value);
            else unsupported.Add(value);
        }
        var purpose = Clean(realization.NarrativePurpose);
        if (string.IsNullOrWhiteSpace(purpose) || Internal.IsMatch(purpose))
            purpose = facts.Count > 0 ? "Explain the astronomical meaning of the grounded details in natural viewer-facing language." : "Give the audience a clear, natural understanding of this part of Orion.";
        var transition = realization.TransitionIntent is null ? "Continue naturally to the next astronomical idea." :
            $"Move naturally from {Clean(realization.TransitionIntent.FromConcept)} toward {Clean(realization.TransitionIntent.ToConcept)}.";
        if (Internal.IsMatch(transition) || transition.Count(char.IsLetterOrDigit) < 20) transition = "Connect this understanding naturally to the next astronomical idea.";
        var observations = realization.ObservationDetails.Select(Clean).Where(IsStatement).Select(v => v.TrimEnd('.') + ".").ToArray();
        return new(purpose, transition, facts, names, pronunciations, unsupported, observations);
    }

    public static bool HasMeaningfulContext(ProviderSemanticProjectionResult projection)
        => projection.FactualStatements.Count > 0 && projection.Purpose.Count(char.IsLetterOrDigit) >= 30;

    private static bool IsStatement(string value) => value.Count(char.IsLetterOrDigit) >= 12 && (SentenceVerb.IsMatch(value) || value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 5);
    private static string Clean(string? value) => Regex.Replace(Internal.Replace(value ?? string.Empty, string.Empty), @"\s{2,}", " ").Trim(' ', '-', ':', '.');
}

public sealed record NarrationPromptDiagnostics(string ComposerName, string OrchestrationVersion, IReadOnlyList<string> InputFiles, IReadOnlyList<string> OutputFiles, int SceneCount, int PromptSectionCount, IReadOnlyList<string> ProhibitedInternalPhraseList, IReadOnlyList<string> MissingInputWarnings, bool ReadyForGeneration, bool LanguageInstructionPresent, string LanguageInstructionLocation, bool RequestedLanguageIncludedInSystemPrompt, bool RequestedLanguageIncludedInUserPrompt, bool LanguageExamplesUsed, bool EnglishDominatingExamplesDetected, string LanguageProfileSource, string TerminologyProfileSource);
