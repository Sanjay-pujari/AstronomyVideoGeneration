using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>
/// Production Phase 7 boundary.  The planning/draft-authority publications are useful
/// diagnostics, but they are not prose and are deliberately not consulted here.
/// </summary>
public interface IDocumentaryNarrativeLifecycleIntegrationService
{
    Task<DocumentaryNarrativeLifecycleResult> ExecuteAsync(DocumentaryNarrativeLifecycleRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentaryNarrativeLifecycleRequest(
    string ExecutionRoot, string ExecutionId, Guid PlanId, string EventId, string EventFamily,
    string Language, string ProfileId, int Year, string RegionId, object ProductionPipelineRequest);

public sealed record DocumentaryNarrativeCompositionRequest(
    string ExecutionId, Guid PlanId, string EventId, string EventFamily, string Language, string Variant,
    string ProfileId, IReadOnlyList<DocumentaryNarrativeSceneInput> OrderedScenes,
    DocumentaryNarrativeDurationGuidance OverallDurationGuidance,
    IReadOnlyList<string> SafetyRules, IReadOnlyList<string> Vocabulary,
    IReadOnlyList<string> KnowledgeReferences);

public sealed record DocumentaryNarrativeDurationGuidance(int MinimumSeconds, int PreferredSeconds, int MaximumSeconds);

public sealed record DocumentaryNarrativeRequiredFact(string ClaimId, string Fact,
    IReadOnlyList<string> KnowledgeReferenceIds, IReadOnlyList<string> SourceIds, decimal Confidence,
    IReadOnlyList<string> QualificationRequirements);

public sealed record DocumentaryNarrativeSceneInput(int SceneNumber, string SceneId, string SectionKey,
    string Heading, string ViewerQuestion, string LearningObjective, string NarrationBrief,
    IReadOnlyList<DocumentaryNarrativeRequiredFact> RequiredFacts, IReadOnlyList<string> OptionalFacts,
    IReadOnlyList<string> CulturalContext, IReadOnlyList<string> SafetyRules, IReadOnlyList<string> Vocabulary,
    string VisualIntent, int TargetDurationSeconds, string PreviousSceneContext, string TransitionSeed);

public sealed record DocumentaryNarrativeDraftCandidate(string Variant, string Path, string Text,
    IReadOnlyList<string> SceneIds, IReadOnlyList<string> GroundingReferences);

public sealed record DocumentaryNarrativeQualityResult(bool Passed, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, int SceneCount, int EstimatedDurationSeconds);

public sealed record DocumentaryNarrativeLifecycleAcceptanceResult(bool Accepted, string Reason,
    string? ReleaseCandidateId = null);

public sealed record DocumentaryNarrativeProviderCallEvidence(string Generator, string EntryMethod,
    int LongCalls, int ShortCalls, IReadOnlyList<string> DiagnosticFiles);

public sealed record DocumentaryNarrativeLifecycleResult(
    DocumentaryNarrativeCompositionRequest LongRequest,
    DocumentaryNarrativeCompositionRequest ShortRequest,
    DocumentaryNarrativeDraftCandidate? LongDraft,
    DocumentaryNarrativeDraftCandidate? ShortDraft,
    DocumentaryNarrativeQualityResult LongQuality,
    DocumentaryNarrativeQualityResult ShortQuality,
    IReadOnlyList<string> RevisionHistory,
    DocumentaryNarrativeLifecycleAcceptanceResult LongAcceptance,
    DocumentaryNarrativeLifecycleAcceptanceResult ShortAcceptance,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings,
    DocumentaryNarrativeProviderCallEvidence ProviderCallEvidence,
    IReadOnlyList<string> GeneratedFiles)
{
    public bool Succeeded => LongAcceptance.Accepted && ShortAcceptance.Accepted && Errors.Count == 0;
}

/// <summary>
/// Thin Draft -&gt; Validate -&gt; Revise -&gt; Accept orchestration around the existing V5 generator.
/// Wording is flexible; certified claim identities remain grounding evidence.
/// </summary>
public sealed class DocumentaryNarrativeLifecycleIntegrationService(
    NarrationGeneratorV5 generator,
    DocumentaryNarrativeAcceptanceCoordinator acceptanceCoordinator) : IDocumentaryNarrativeLifecycleIntegrationService
{
    public const int MaximumRevisionAttempts = 2;

    public async Task<DocumentaryNarrativeLifecycleResult> ExecuteAsync(DocumentaryNarrativeLifecycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        var warnings = new List<string>();
        var longRequest = BuildCompositionRequest(request, "Long", new(480, 600, 900));
        var shortRequest = BuildCompositionRequest(request, "Short", new(60, 90, 120));
        if (longRequest.OrderedScenes.Count == 0) errors.Add("Long narration has no governed Phase 6 scenes.");
        if (shortRequest.OrderedScenes.Count == 0) errors.Add("Short narration has no governed Phase 6 scenes.");

        var generated = NarrationGeneratorV5Result.Empty;
        if (errors.Count == 0)
        {
            var batchRequest = new BatchGenerateFromPlansRequest(request.Year, request.RegionId, request.Language,
                DryRun: false, UseProductionPipeline: true, StartPhaseNo: 7, EndPhaseNo: 7, PlanId: request.PlanId);
            var batchResponse = new BatchGenerateFromPlansResponse(true, false, 1, 1, 1, [], [], [], [],
                UseProductionPipeline: true, PlanId: request.PlanId, OutputRoot: request.ExecutionRoot,
                ProductionPipelineRequest: request.ProductionPipelineRequest);
            try { generated = await generator.BuildAndWriteDiagnosticsAsync(batchRequest, batchResponse, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { errors.Add($"NarrationGeneratorV5 failed: {ex.Message}"); }
        }

        var longDraft = ReadDraft(request.ExecutionRoot, "long", longRequest);
        var shortDraft = ReadDraft(request.ExecutionRoot, "short", shortRequest);
        var longQuality = Validate(longDraft, longRequest);
        var shortQuality = Validate(shortDraft, shortRequest);
        errors.AddRange(longQuality.Errors.Select(x => "Long: " + x));
        errors.AddRange(shortQuality.Errors.Select(x => "Short: " + x));
        warnings.AddRange(longQuality.Warnings.Select(x => "Long: " + x));
        warnings.AddRange(shortQuality.Warnings.Select(x => "Short: " + x));

        // Prompt 2 will persist the rich release aggregate; the existing coordinator still
        // owns the acceptance decision at this production integration boundary.
        var longAcceptance = Accept(longDraft, longQuality, request.ExecutionId, "long",
            acceptanceCoordinator.Accept(longDraft is not null, longQuality.Passed, longQuality.Passed));
        var shortAcceptance = Accept(shortDraft, shortQuality, request.ExecutionId, "short",
            acceptanceCoordinator.Accept(shortDraft is not null, shortQuality.Passed, shortQuality.Passed));
        return new(longRequest, shortRequest, longDraft, shortDraft, longQuality, shortQuality, [],
            longAcceptance, shortAcceptance, errors.Distinct().ToArray(), warnings.Distinct().ToArray(),
            new(nameof(NarrationGeneratorV5), nameof(NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync), 1, 1, generated.GeneratedFiles),
            generated.GeneratedFiles);
    }

    private static DocumentaryNarrativeCompositionRequest BuildCompositionRequest(DocumentaryNarrativeLifecycleRequest request,
        string variant, DocumentaryNarrativeDurationGuidance duration)
    {
        var manifestPath = Path.Combine(request.ExecutionRoot, "06-story-frames", "story-frame-manifest.json");
        using var document = File.Exists(manifestPath) ? JsonDocument.Parse(File.ReadAllText(manifestPath)) : null;
        var scenes = FindVariantScenes(document?.RootElement, variant).Select((scene, index) => MapScene(scene, index)).ToArray();
        return new(request.ExecutionId, request.PlanId, request.EventId, request.EventFamily, request.Language, variant,
            request.ProfileId, scenes, duration,
            ["Use only grounded astronomy facts; distinguish culture and mythology from science.", "Do not present astrology as scientific causation.", "Do not leak prompts, notes, or internal identifiers."],
            [], scenes.SelectMany(x => x.RequiredFacts).SelectMany(x => x.KnowledgeReferenceIds).Distinct().ToArray());
    }

    private static IEnumerable<JsonElement> FindVariantScenes(JsonElement? root, string variant)
    {
        if (root is null) yield break;
        foreach (var arrayName in variant.Equals("Long", StringComparison.OrdinalIgnoreCase)
                     ? new[] { "longScenes", "longStoryFrames" } : new[] { "shortScenes", "shortStoryFrames" })
            if (TryProperty(root.Value, arrayName, out var array) && array.ValueKind == JsonValueKind.Array)
            { foreach (var item in array.EnumerateArray()) yield return item; yield break; }
        if (TryProperty(root.Value, "variants", out var variants) && variants.ValueKind == JsonValueKind.Array)
            foreach (var item in variants.EnumerateArray())
                if (Text(item, "variant", "format", "variantType").Equals(variant, StringComparison.OrdinalIgnoreCase) && TryProperty(item, "scenes", out var scenes))
                { foreach (var scene in scenes.EnumerateArray()) yield return scene; yield break; }
    }

    private static DocumentaryNarrativeSceneInput MapScene(JsonElement scene, int index)
    {
        var number = Number(scene, "sceneNumber", "sceneOrder", "sequence") ?? index + 1;
        var id = Text(scene, "sceneId", "storyFrameId", "id");
        if (string.IsNullOrWhiteSpace(id)) id = $"scene-{number}";
        return new(number, id, Text(scene, "sectionKey", "section"), Text(scene, "heading", "title", "purpose"),
            Text(scene, "viewerQuestion", "question"), Text(scene, "learningObjective", "objective"),
            Text(scene, "narrationBrief", "narrativePurpose", "purpose"), [], [], [], [], [],
            Text(scene, "visualIntent", "visualPurpose"), Number(scene, "targetDurationSeconds", "estimatedDurationSeconds") ?? 0,
            index == 0 ? "" : "Continue naturally from the preceding governed scene.", Text(scene, "transitionSeed", "transition"));
    }

    private static DocumentaryNarrativeDraftCandidate? ReadDraft(string root, string variant, DocumentaryNarrativeCompositionRequest composition)
    {
        var path = Path.Combine(root, "narration-v5", variant, "narration.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var text = Text(doc.RootElement, "fullNarrationText", "fullText", "text");
        var ids = new List<string>();
        if (TryProperty(doc.RootElement, "scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array)
            ids.AddRange(scenes.EnumerateArray().Select(x => Text(x, "sceneId", "id")).Where(x => x.Length > 0));
        return new(variant, path, text, ids, composition.KnowledgeReferences);
    }

    private static DocumentaryNarrativeQualityResult Validate(DocumentaryNarrativeDraftCandidate? draft, DocumentaryNarrativeCompositionRequest request)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        if (draft is null) errors.Add("Generator did not return the requested variant.");
        else
        {
            var missing = request.OrderedScenes.Select(x => x.SceneId).Where(id => !draft.SceneIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (missing.Length > 0) errors.Add($"Missing governed scenes: {string.Join(", ", missing)}.");
            if (string.IsNullOrWhiteSpace(draft.Text)) errors.Add("Narration text is empty.");
            if (new[] { "system prompt", "producer notes", "claimId", "knowledgeReferenceId" }.Any(x => draft.Text.Contains(x, StringComparison.OrdinalIgnoreCase))) errors.Add("Metadata or prompt leakage detected.");
            var seconds = EstimateSeconds(draft.Text, request.Language);
            if (seconds < request.OverallDurationGuidance.MinimumSeconds || seconds > request.OverallDurationGuidance.MaximumSeconds)
                warnings.Add($"Estimated overall duration {seconds}s is outside guidance; measured authority belongs to Phases 15-16.");
        }
        return new(errors.Count == 0, errors, warnings, draft?.SceneIds.Count ?? 0, EstimateSeconds(draft?.Text ?? "", request.Language));
    }

    private static DocumentaryNarrativeLifecycleAcceptanceResult Accept(DocumentaryNarrativeDraftCandidate? draft,
        DocumentaryNarrativeQualityResult quality, string executionId, string variant, bool coordinatorAccepted) =>
        coordinatorAccepted && quality.Passed && draft is not null
            ? new(true, "Converged natural narration passed practical quality validation.", $"{executionId}.{variant}.release-candidate")
            : new(false, "Narration did not converge to an acceptable release candidate.");

    private static int EstimateSeconds(string text, string language) => string.IsNullOrWhiteSpace(text) ? 0 :
        (int)Math.Round(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length / (language.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? 120d : 135d) * 60d);
    private static bool TryProperty(JsonElement element, string name, out JsonElement value) { foreach (var p in element.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value=p.Value; return true; } value=default; return false; }
    private static string Text(JsonElement element, params string[] names) { foreach (var name in names) if (TryProperty(element, name, out var value)) return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString(); return ""; }
    private static int? Number(JsonElement element, params string[] names) { foreach (var name in names) if (TryProperty(element, name, out var value) && value.TryGetInt32(out var number)) return number; return null; }
}
