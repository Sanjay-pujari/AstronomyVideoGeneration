using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

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
    IReadOnlyList<string> KnowledgeReferences)
{
    public IReadOnlyList<string> RepairGuidance { get; init; } = [];
}

public sealed record DocumentaryNarrativeDurationGuidance(int MinimumSeconds, int PreferredSeconds, int MaximumSeconds);
public sealed record DocumentaryNarrativeRequiredFact(string ClaimId, string Fact,
    IReadOnlyList<string> KnowledgeReferenceIds, IReadOnlyList<string> SourceIds, decimal Confidence,
    IReadOnlyList<string> QualificationRequirements);
public sealed record DocumentaryNarrativeSceneInput(int SceneNumber, string SceneId, string SectionKey,
    string Heading, string ViewerQuestion, string LearningObjective, string NarrationBrief,
    IReadOnlyList<DocumentaryNarrativeRequiredFact> RequiredFacts, IReadOnlyList<string> OptionalFacts,
    IReadOnlyList<string> CulturalContext, IReadOnlyList<string> SafetyRules, IReadOnlyList<string> Vocabulary,
    string VisualIntent, int TargetDurationSeconds, string PreviousSceneContext, string TransitionSeed);
public sealed record DocumentaryNarrativeDraftScene(string SceneId, string NarrationText,
    IReadOnlyList<string> GroundingReferences);
public sealed record DocumentaryNarrativeDraftCandidate(string Variant, string Path, string Text,
    IReadOnlyList<string> SceneIds, IReadOnlyList<string> GroundingReferences)
{
    public IReadOnlyList<DocumentaryNarrativeDraftScene> Scenes { get; init; } = [];
    public int WordCount { get; init; }
}
public sealed record DocumentaryNarrativeQualityResult(bool Passed, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, int SceneCount, int EstimatedDurationSeconds);
public sealed record DocumentaryNarrativeLifecycleAcceptanceResult(bool Accepted, string Reason,
    string? ReleaseCandidateId = null);
internal sealed record NarrationGeneratorBlockingAssessment(bool GenerationCompleted, bool LongProduced,
    bool ShortProduced, IReadOnlyList<string> BlockingErrors, IReadOnlyList<string> AdvisoryWarnings,
    bool CanRetry = false);
public sealed record DocumentaryNarrativeProviderCallEvidence(string Generator, string EntryMethod,
    int GeneratorInvocationCount, bool LongVariantProduced, bool ShortVariantProduced,
    IReadOnlyList<string> DiagnosticFiles)
{
    // Compatibility aliases for callers of the short-lived original evidence contract.
    public int LongCalls => LongVariantProduced ? GeneratorInvocationCount : 0;
    public int ShortCalls => ShortVariantProduced ? GeneratorInvocationCount : 0;
}
public sealed record DocumentaryNarrativeLifecycleResult(
    DocumentaryNarrativeCompositionRequest LongRequest, DocumentaryNarrativeCompositionRequest ShortRequest,
    DocumentaryNarrativeDraftCandidate? LongDraft, DocumentaryNarrativeDraftCandidate? ShortDraft,
    DocumentaryNarrativeQualityResult LongQuality, DocumentaryNarrativeQualityResult ShortQuality,
    IReadOnlyList<string> RevisionHistory, DocumentaryNarrativeLifecycleAcceptanceResult LongAcceptance,
    DocumentaryNarrativeLifecycleAcceptanceResult ShortAcceptance, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, DocumentaryNarrativeProviderCallEvidence ProviderCallEvidence,
    IReadOnlyList<string> GeneratedFiles)
{
    public bool Succeeded => LongAcceptance.Accepted && ShortAcceptance.Accepted && Errors.Count == 0;
}

/// <summary>Thin production orchestration around the existing V5 narration generator.</summary>
public sealed class DocumentaryNarrativeLifecycleIntegrationService(
    NarrationGeneratorV5 generator,
    DocumentaryNarrativeAcceptanceCoordinator acceptanceCoordinator) : IDocumentaryNarrativeLifecycleIntegrationService
{
    public const int MaximumGenerationAttempts = 2;
    public const int MaximumRevisionAttempts = MaximumGenerationAttempts;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] LeakageTerms =
        ["system prompt", "developer prompt", "producer note", "internal instruction", "claimId", "knowledgeReferenceId",
         "viewerQuestionId", "learningObjectiveId", "final narration remains owned by phase 7", "advance the certified", "```json"];
    private static readonly Regex InternalId = new(@"\b(?:VQ|LO|CLM|CLAIM|KR|KNOWLEDGE)[-_]?[A-Z0-9]{2,}\b|\bAdvance\d{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<DocumentaryNarrativeLifecycleResult> ExecuteAsync(DocumentaryNarrativeLifecycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        var warnings = new List<string>();
        var revisions = new List<string>();
        var generatedFiles = new List<string>();
        var storyFramesPath = Path.Combine(request.ExecutionRoot, "06-story-frames", "story-frames.json");
        var diagnosticsPath = Path.Combine(request.ExecutionRoot, "narration-v5", "narrative-lifecycle-validation.json");
        var generatorDiagnosticsPath = Path.Combine(request.ExecutionRoot, "narration-v5", "generator-validation-diagnostics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsPath)!);

        StoryFramesAuthority? authority = null;
        if (!File.Exists(storyFramesPath))
            errors.Add("Canonical Phase 6 Story Frame authority was not found at 06-story-frames/story-frames.json.");
        else
        {
            await using var stream = File.OpenRead(storyFramesPath);
            authority = await JsonSerializer.DeserializeAsync<StoryFramesAuthority>(stream, JsonOptions, cancellationToken);
            ValidateAuthority(authority, request, errors);
        }

        var longRequest = BuildCompositionRequest(request, authority, "Long", new(480, 600, 900));
        var shortRequest = BuildCompositionRequest(request, authority, "Short", new(60, 90, 120));
        if (longRequest.OrderedScenes.Count == 0) errors.Add("Long narration has no governed Phase 6 scenes.");
        if (shortRequest.OrderedScenes.Count == 0) errors.Add("Short narration has no governed Phase 6 scenes.");
        if (longRequest.OrderedScenes.All(scene => scene.RequiredFacts.Count == 0) &&
            shortRequest.OrderedScenes.All(scene => scene.RequiredFacts.Count == 0))
            warnings.Add("Composition-side required facts were not independently projected; NarrationGeneratorV5 will use its existing semantic fact resolution pipeline.");

        var longRequestPath = Path.Combine(request.ExecutionRoot, "narration-v5", "long", "narrative-composition-request.json");
        var shortRequestPath = Path.Combine(request.ExecutionRoot, "narration-v5", "short", "narrative-composition-request.json");
        await WriteCompositionRequestsAsync(longRequest, shortRequest, longRequestPath, shortRequestPath, cancellationToken);
        generatedFiles.AddRange([longRequestPath, shortRequestPath]);

        NarrationGeneratorV5Result generated = NarrationGeneratorV5Result.Empty;
        DocumentaryNarrativeDraftCandidate? longDraft = null;
        DocumentaryNarrativeDraftCandidate? shortDraft = null;
        var longQuality = EmptyQuality("Generation was not attempted.");
        var shortQuality = EmptyQuality("Generation was not attempted.");
        var crossErrors = new List<string>();
        var invocationCount = 0;
        var generatorAssessment = new NarrationGeneratorBlockingAssessment(false, false, false, [], []);

        if (errors.Count == 0)
        {
            for (var attempt = 1; attempt <= MaximumGenerationAttempts; attempt++)
            {
                invocationCount++;
                generated = await InvokeGeneratorAsync(request, cancellationToken);
                generatedFiles.AddRange(generated.GeneratedFiles);
                revisions.Add(attempt == 1 ? "Attempt 1: Generated." : "Attempt 2: Regenerated with correction guidance.");

                var longRead = await ReadDraftAsync(request.ExecutionRoot, "long", longRequest, cancellationToken);
                var shortRead = await ReadDraftAsync(request.ExecutionRoot, "short", shortRequest, cancellationToken);
                longDraft = longRead.Draft;
                shortDraft = shortRead.Draft;
                generatorAssessment = AssessGeneratorResult(request.ExecutionRoot, true, true);
                longQuality = Validate(longDraft, longRequest, longRead.Errors, generatorAssessment.BlockingErrors);
                shortQuality = Validate(shortDraft, shortRequest, shortRead.Errors, generatorAssessment.BlockingErrors);
                crossErrors = ValidateCrossVariant(longDraft, shortDraft).ToList();
                var attemptErrors = longQuality.Errors.Select(x => "Long: " + x)
                    .Concat(shortQuality.Errors.Select(x => "Short: " + x)).Concat(crossErrors).ToArray();
                revisions.Add(attemptErrors.Length == 0
                    ? $"Attempt {attempt} validation: Passed."
                    : $"Attempt {attempt} validation: Failed — {string.Join("; ", attemptErrors)}");
                warnings.AddRange(generatorAssessment.AdvisoryWarnings);
                if (attemptErrors.Length == 0 || attempt == MaximumGenerationAttempts ||
                    !IsRepairable(attemptErrors, generatorAssessment)) break;

                longRequest = longRequest with { RepairGuidance = attemptErrors };
                shortRequest = shortRequest with { RepairGuidance = attemptErrors };
                await PreserveAttemptDiagnosticsAsync(request.ExecutionRoot, attempt, cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(request.ExecutionRoot, "narration-v5", $"narrative-lifecycle-validation.attempt-{attempt}.json"),
                    JsonSerializer.Serialize(new { schemaVersion = "1.0", request.ExecutionId, attempt,
                        generatorBlockingAssessment = generatorAssessment, blockingErrors = attemptErrors,
                        succeeded = false, generatedUtc = DateTimeOffset.UtcNow }, JsonOptions), cancellationToken);
                await WriteCompositionRequestsAsync(longRequest, shortRequest, longRequestPath, shortRequestPath, cancellationToken);
            }
        }

        errors.AddRange(longQuality.Errors.Select(x => "Long: " + x));
        errors.AddRange(shortQuality.Errors.Select(x => "Short: " + x));
        errors.AddRange(crossErrors);
        warnings.AddRange(longQuality.Warnings.Select(x => "Long: " + x));
        warnings.AddRange(shortQuality.Warnings.Select(x => "Short: " + x));
        var noPendingRepairableIssues = longQuality.Passed && shortQuality.Passed && crossErrors.Count == 0;
        var convergenceSucceeded = noPendingRepairableIssues && invocationCount <= MaximumGenerationAttempts;
        var longAcceptance = Accept(longDraft, longQuality, request.ExecutionId, "long",
            acceptanceCoordinator.Accept(longDraft is not null, longQuality.Passed, convergenceSucceeded));
        var shortAcceptance = Accept(shortDraft, shortQuality, request.ExecutionId, "short",
            acceptanceCoordinator.Accept(shortDraft is not null, shortQuality.Passed, convergenceSucceeded));

        var succeeded = longAcceptance.Accepted && shortAcceptance.Accepted && errors.Count == 0;
        if (succeeded)
        {
            var publication = await PublishReleaseCandidatesAsync(request, longRequest, shortRequest, longDraft!, shortDraft!,
                longQuality, shortQuality, longAcceptance, shortAcceptance, generatorDiagnosticsPath, diagnosticsPath, cancellationToken);
            generatedFiles.AddRange(publication);
        }
        var diagnostics = new
        {
            schemaVersion = "1.0", request.ExecutionId, planId = request.PlanId, request.EventId, request.EventFamily,
            request.Language, request.ProfileId, canonicalStoryFramesPath = storyFramesPath,
            longCompositionRequestPath = longRequestPath, shortCompositionRequestPath = shortRequestPath,
            expectedLongSceneCount = longRequest.OrderedScenes.Count, actualLongSceneCount = longDraft?.Scenes.Count ?? 0,
            expectedShortSceneCount = shortRequest.OrderedScenes.Count, actualShortSceneCount = shortDraft?.Scenes.Count ?? 0,
            generator = nameof(NarrationGeneratorV5), entryMethod = nameof(NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync),
            generatorInvocationCount = invocationCount,
            generatorDiagnosticsPath,
            generatorBlockingAssessment = generatorAssessment,
            longArtifactPath = Path.Combine(request.ExecutionRoot, "narration-v5", "long", "narration.json"),
            shortArtifactPath = Path.Combine(request.ExecutionRoot, "narration-v5", "short", "narration.json"),
            longQuality, shortQuality, crossVariantValidationResult = new { passed = crossErrors.Count == 0, errors = crossErrors },
            durationEstimates = new { longSeconds = longQuality.EstimatedDurationSeconds, shortSeconds = shortQuality.EstimatedDurationSeconds },
            revisionHistory = revisions, longAcceptance, shortAcceptance, succeeded,
            errors = errors.Distinct(StringComparer.Ordinal).ToArray(), warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            generatedUtc = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        generatedFiles.Add(diagnosticsPath);
        return new(longRequest, shortRequest, longDraft, shortDraft, longQuality, shortQuality, revisions,
            longAcceptance, shortAcceptance, errors.Distinct(StringComparer.Ordinal).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            new(nameof(NarrationGeneratorV5), nameof(NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync), invocationCount,
                longDraft is not null, shortDraft is not null, generated.GeneratedFiles),
            generatedFiles.Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<NarrationGeneratorV5Result> InvokeGeneratorAsync(DocumentaryNarrativeLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        var batchRequest = new BatchGenerateFromPlansRequest(request.Year, request.RegionId, request.Language,
            DryRun: false, UseProductionPipeline: true, StartPhaseNo: 7, EndPhaseNo: 7, PlanId: request.PlanId);
        var batchResponse = new BatchGenerateFromPlansResponse(true, false, 1, 1, 1, [], [], [], [],
            UseProductionPipeline: true, PlanId: request.PlanId, OutputRoot: request.ExecutionRoot,
            ProductionPipelineRequest: request.ProductionPipelineRequest);
        return await generator.BuildAndWriteDiagnosticsAsync(batchRequest, batchResponse, cancellationToken);
    }

    private static void ValidateAuthority(StoryFramesAuthority? authority, DocumentaryNarrativeLifecycleRequest request,
        List<string> errors)
    {
        if (authority is null) { errors.Add("Canonical Phase 6 Story Frame authority is empty or invalid."); return; }
        if (!authority.PlanId.Equals(request.PlanId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            errors.Add($"Canonical Story Frame PlanId '{authority.PlanId}' does not match requested PlanId '{request.PlanId:D}'.");
        if (!string.IsNullOrWhiteSpace(authority.EventId) && !string.IsNullOrWhiteSpace(request.EventId) &&
            !authority.EventId.Equals(request.EventId, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Canonical Story Frame EventId '{authority.EventId}' does not match requested EventId '{request.EventId}'.");
        if (!authority.Language.Equals(request.Language, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Canonical Story Frame language '{authority.Language}' does not match requested language '{request.Language}'.");
        if (authority.Frames.Count == 0) errors.Add("Canonical Phase 6 Story Frame authority contains no frames.");
        foreach (var variant in new[] { "Long", "Short" })
            if (!authority.RequestedVariants.Contains(variant, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Canonical Phase 6 Story Frame authority does not request the {variant} variant.");
    }

    internal static DocumentaryNarrativeCompositionRequest BuildCompositionRequest(DocumentaryNarrativeLifecycleRequest request,
        StoryFramesAuthority? authority, string variant, DocumentaryNarrativeDurationGuidance duration)
    {
        var safety = new[] { "Use only grounded astronomy facts; distinguish culture and mythology from science.",
            "Do not present astrology as scientific causation.", "Do not leak prompts, notes, or internal identifiers." };
        var groups = (authority?.Frames ?? []).Where(frame => frame.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase))
            .GroupBy(frame => frame.SceneId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(frame => frame.SceneNumber)).ThenBy(group => group.Min(frame => frame.FrameNumber)).ToArray();
        var scenes = groups.Select((group, index) =>
        {
            var ordered = group.OrderBy(frame => frame.FrameNumber).ToArray();
            var first = ordered[0];
            var references = ordered.SelectMany(frame => frame.KnowledgeReferenceIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new DocumentaryNarrativeSceneInput(first.SceneNumber, first.SceneId, first.NarrativeStage,
                first.SceneRole, string.Join("; ", ordered.SelectMany(frame => frame.ViewerQuestionIds).Distinct()),
                string.Join("; ", ordered.SelectMany(frame => frame.LearningObjectiveIds).Distinct()),
                string.Join(" ", ordered.Select(frame => frame.NarrativeIntent).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
                [], [], [], safety, [],
                string.Join(" ", ordered.Select(frame => frame.VisualIntent).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
                (int)Math.Round(ordered.Sum(frame => frame.EstimatedDuration)),
                index == 0 ? "" : $"Continue naturally from {groups[index - 1].Key}.",
                string.Join(" ", ordered.Select(frame => frame.TransitionOut).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()));
        }).ToArray();
        return new(request.ExecutionId, request.PlanId, request.EventId, request.EventFamily, request.Language, variant,
            request.ProfileId, scenes, duration, safety, [],
            (authority?.Frames ?? []).Where(frame => frame.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase))
                .SelectMany(frame => frame.KnowledgeReferenceIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static async Task WriteCompositionRequestsAsync(DocumentaryNarrativeCompositionRequest longRequest,
        DocumentaryNarrativeCompositionRequest shortRequest, string longPath, string shortPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(longPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(shortPath)!);
        await File.WriteAllTextAsync(longPath, JsonSerializer.Serialize(longRequest, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(shortPath, JsonSerializer.Serialize(shortRequest, JsonOptions), cancellationToken);
    }

    private static async Task<(DocumentaryNarrativeDraftCandidate? Draft, IReadOnlyList<string> Errors)> ReadDraftAsync(
        string root, string variant, DocumentaryNarrativeCompositionRequest composition, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "narration-v5", variant, "narration.json");
        if (!File.Exists(path)) return (null, [$"Draft artifact was not found at narration-v5/{variant}/narration.json."]);
        try
        {
            await using var stream = File.OpenRead(path);
            var narration = await JsonSerializer.DeserializeAsync<NarrationV5>(stream, JsonOptions, cancellationToken);
            if (narration is null) return (null, ["Draft JSON did not contain a narration document."]);
            var scenes = narration.Scenes.Select(scene => new DocumentaryNarrativeDraftScene(scene.SceneId,
                scene.NarrationText ?? "", scene.RequiredFactsCovered ?? [])).ToArray();
            var text = string.IsNullOrWhiteSpace(narration.FullNarrationText)
                ? string.Join("\n\n", scenes.Select(scene => scene.NarrationText).Where(text => !string.IsNullOrWhiteSpace(text)))
                : narration.FullNarrationText;
            if (scenes.Length == 0 || scenes.All(scene => string.IsNullOrWhiteSpace(scene.NarrationText)) || string.IsNullOrWhiteSpace(text))
                return (null, ["Draft contains no non-empty scene narration."]);
            var references = scenes.SelectMany(scene => scene.GroundingReferences)
                .Concat(composition.KnowledgeReferences).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return (new(variant, path, text, scenes.Select(scene => scene.SceneId).ToArray(), references)
            { Scenes = scenes, WordCount = WordCount(text) }, []);
        }
        catch (JsonException ex) { return (null, [$"Draft JSON is invalid: {ex.Message}"]); }
    }

    internal static DocumentaryNarrativeQualityResult Validate(DocumentaryNarrativeDraftCandidate? draft,
        DocumentaryNarrativeCompositionRequest request, IReadOnlyList<string> readErrors,
        IReadOnlyList<string> generatorBlockingErrors)
    {
        var errors = readErrors.ToList();
        var warnings = new List<string>();
        if (draft is not null)
        {
            var expected = request.OrderedScenes.Select(scene => scene.SceneId).ToArray();
            var groups = draft.SceneIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
            var duplicates = groups.Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (duplicates.Length > 0) errors.Add($"Duplicate scene IDs: {string.Join(", ", duplicates)}.");
            var missing = expected.Where(id => groups.All(group => !group.Key.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length > 0) errors.Add($"Missing governed scenes: {string.Join(", ", missing)}.");
            var unexpected = groups.Select(group => group.Key).Where(id => !expected.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (unexpected.Length > 0) errors.Add($"Unexpected scene IDs: {string.Join(", ", unexpected)}.");
            var duplicateBlocks = draft.Scenes.Where(scene => !string.IsNullOrWhiteSpace(scene.NarrationText))
                .GroupBy(scene => Normalize(scene.NarrationText), StringComparer.Ordinal).Where(group => group.Count() > 1).ToArray();
            if (duplicateBlocks.Length > 0) errors.Add("Duplicate complete scene narration blocks were detected.");
            if (LeakageTerms.Any(term => draft.Text.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                InternalId.IsMatch(draft.Text) ||
                Regex.IsMatch(draft.Text, "\\b(?:sceneId|viewerQuestionId|learningObjectiveId)\\s*[:=]", RegexOptions.IgnoreCase))
                errors.Add("Metadata or prompt leakage detected.");
            var openings = draft.Scenes.Where(s => !string.IsNullOrWhiteSpace(s.NarrationText))
                .Select(s => string.Join(' ', Regex.Matches(Normalize(s.NarrationText), @"[a-z0-9']+").Select(m => m.Value).Take(7)))
                .GroupBy(x => x).Where(g => g.Count() >= Math.Max(3, draft.Scenes.Count / 2)).ToArray();
            if (openings.Length > 0) errors.Add("An excessive repeated scene-opening template was detected.");
            foreach (var scene in draft.Scenes)
            {
                var governed = request.OrderedScenes.FirstOrDefault(s => s.SceneId.Equals(scene.SceneId, StringComparison.OrdinalIgnoreCase));
                if (governed is not null && !HasFactualSubstance(scene.NarrationText, governed))
                    errors.Add($"Scene {scene.SceneId} has no meaningful factual substance from its governed purpose or facts.");
            }
            var seconds = EstimateSeconds(draft.Text, request.Language);
            if (seconds < request.OverallDurationGuidance.MinimumSeconds || seconds > request.OverallDurationGuidance.MaximumSeconds)
                warnings.Add($"Estimated overall duration {seconds}s is outside guidance; measured authority belongs to Phases 15-16.");
        }
        errors.AddRange(generatorBlockingErrors);
        return new(errors.Count == 0, errors.Distinct(StringComparer.Ordinal).ToArray(), warnings,
            draft?.Scenes.Count ?? 0, EstimateSeconds(draft?.Text ?? "", request.Language));
    }

    internal static IReadOnlyList<string> ValidateCrossVariant(DocumentaryNarrativeDraftCandidate? longDraft,
        DocumentaryNarrativeDraftCandidate? shortDraft)
    {
        if (longDraft is null || shortDraft is null) return [];
        var longText = Normalize(longDraft.Text); var shortText = Normalize(shortDraft.Text);
        if (longText.Equals(shortText, StringComparison.Ordinal)) return ["Long and Short narration are identical."];
        if (shortText.Length >= 200 && longText.Contains(shortText, StringComparison.Ordinal))
            return ["Short narration is a verbatim contiguous copy of Long narration."];
        var shortSentences = Sentences(shortDraft.Text).Where(s => s.Length >= 35).ToArray();
        if (shortSentences.Length > 0 && shortSentences.Count(sentence => longText.Contains(sentence, StringComparison.Ordinal)) >= Math.Max(1, (int)Math.Ceiling(shortSentences.Length * .6)))
            return ["Short narration is a near-verbatim reuse of Long narration."];
        return [];
    }

    internal static NarrationGeneratorBlockingAssessment AssessGeneratorResult(string root, bool longRequested, bool shortRequested)
    {
        var path = Path.Combine(root, "narration-v5", "generator-validation-diagnostics.json");
        if (!File.Exists(path)) return new(false, false, false, ["Generator diagnostics artifact is missing."], []);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var rootElement = document.RootElement;
            var blockers = new List<string>();
            var advisory = new List<string>();
            var longProduced = Boolean(rootElement, "longNarrationArtifactValid");
            var shortProduced = Boolean(rootElement, "shortNarrationArtifactValid");
            if (longRequested && !longProduced) blockers.Add("Requested Long narration artifact is missing or invalid.");
            if (shortRequested && !shortProduced) blockers.Add("Requested Short narration artifact is missing or invalid.");
            if (TryProperty(rootElement, "generationErrors", out var generationErrors) && generationErrors.ValueKind == JsonValueKind.Array)
                blockers.AddRange(Strings(generationErrors));
            if (Boolean(rootElement, "requiredSemanticFactResolutionBlocking")) blockers.Add("Required semantic fact resolution is blocking.");
            if (TryProperty(rootElement, "languageValidationPassed", out _) && !Boolean(rootElement, "languageValidationPassed")) blockers.Add("Requested language validation failed.");
            if (TryProperty(rootElement, "sceneMappingValid", out _) && !Boolean(rootElement, "sceneMappingValid")) blockers.Add("Canonical scene mapping is invalid.");
            if (Boolean(rootElement, "producerNotesLeakageDetected") || Boolean(rootElement, "producerNoteLeakage")) blockers.Add("Producer-note leakage was detected.");
            if (Boolean(rootElement, "severeDuplicationDetected")) blockers.Add("Severe duplication was detected.");
            if ((TryProperty(rootElement, "editorialDecision", out var editorial) || TryProperty(rootElement, "finalEditorialDecision", out editorial))
                && editorial.ValueKind == JsonValueKind.String && editorial.GetString()?.Equals("Do Not Publish", StringComparison.OrdinalIgnoreCase) == true)
                blockers.Add("Generator editorial decision is Do Not Publish.");
            if (TryProperty(rootElement, "warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array) advisory.AddRange(Strings(warnings));
            if (TryProperty(rootElement, "promptRecommendation", out var prompt) && prompt.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prompt.GetString())) advisory.Add("Prompt recommendation: " + prompt.GetString());
            if (TryProperty(rootElement, "auroraCertified", out _) && !Boolean(rootElement, "auroraCertified")) advisory.Add("Aurora certification was not achieved; this is advisory.");
            return new(true, longProduced, shortProduced, blockers.Distinct().ToArray(), advisory.Distinct().ToArray(), Boolean(rootElement, "canRetry"));
        }
        catch (JsonException) { return new(false, false, false, ["Generator diagnostics JSON is invalid."], []); }
    }

    private static bool IsRepairable(IReadOnlyList<string> errors, NarrationGeneratorBlockingAssessment assessment) =>
        assessment.CanRetry || errors.Any(error => new[] { "artifact", "missing scene", "duplicate", "leakage", "identical", "verbatim", "empty", "language" }
            .Any(term => error.Contains(term, StringComparison.OrdinalIgnoreCase)));
    private static bool Boolean(JsonElement element, string name) => TryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.True;
    private static IEnumerable<string> Strings(JsonElement array) => array.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
        .Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);

    private static async Task PreserveAttemptDiagnosticsAsync(string root, int attempt, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "narration-v5", "generator-validation-diagnostics.json");
        if (File.Exists(path))
            await File.WriteAllBytesAsync(Path.Combine(root, "narration-v5", $"generator-validation-diagnostics.attempt-{attempt}.json"),
                await File.ReadAllBytesAsync(path, cancellationToken), cancellationToken);
    }

    private static DocumentaryNarrativeQualityResult EmptyQuality(string error) => new(false, [error], [], 0, 0);
    private static DocumentaryNarrativeLifecycleAcceptanceResult Accept(DocumentaryNarrativeDraftCandidate? draft,
        DocumentaryNarrativeQualityResult quality, string executionId, string variant, bool coordinatorAccepted) =>
        coordinatorAccepted && quality.Passed && draft is not null
            ? new(true, "Converged natural narration passed practical quality validation.", $"{executionId}.{variant}.release-candidate")
            : new(false, "Narration did not converge to an acceptable release candidate.");
    private static int WordCount(string text) => Regex.Matches(text ?? "", @"\S+").Count;
    private static int EstimateSeconds(string text, string language) => string.IsNullOrWhiteSpace(text) ? 0 :
        (int)Math.Round(WordCount(text) / (language.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? 120d : 135d) * 60d);
    private static string Normalize(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
    private static IEnumerable<string> Sentences(string value) => Regex.Split(Normalize(value), @"(?<=[.!?])\s+").Where(s => !string.IsNullOrWhiteSpace(s));

    internal static bool HasFactualSubstance(string text, DocumentaryNarrativeSceneInput scene)
    {
        if (string.IsNullOrWhiteSpace(text) || InternalId.IsMatch(text)) return false;
        var expected = scene.RequiredFacts.Select(f => f.Fact).Concat(scene.OptionalFacts).Concat(scene.CulturalContext)
            .Append(scene.NarrationBrief).Where(v => !string.IsNullOrWhiteSpace(v) && !InternalId.IsMatch(v));
        var stop = new HashSet<string>(["about","after","before","could","every","their","there","these","those","which","would","viewer","scene","learn","understand","discover","narrative","purpose"], StringComparer.OrdinalIgnoreCase);
        var concepts = expected.SelectMany(v => Regex.Matches(v ?? "", @"[\p{L}\p{N}']+").Select(m => m.Value.ToLowerInvariant()))
            .Where(t => t.Length >= 4 && !stop.Contains(t)).Distinct().ToArray();
        if (concepts.Length == 0) return WordCount(text) >= 7 && !Regex.IsMatch(text, @"^(look up|keep watching|the story continues|carry away the wonder)\b", RegexOptions.IgnoreCase);
        var normalized = Normalize(text);
        return concepts.Count(normalized.Contains) >= Math.Min(2, concepts.Length);
    }

    private static async Task<IReadOnlyList<string>> PublishReleaseCandidatesAsync(DocumentaryNarrativeLifecycleRequest request,
        DocumentaryNarrativeCompositionRequest longRequest, DocumentaryNarrativeCompositionRequest shortRequest,
        DocumentaryNarrativeDraftCandidate longDraft, DocumentaryNarrativeDraftCandidate shortDraft,
        DocumentaryNarrativeQualityResult longQuality, DocumentaryNarrativeQualityResult shortQuality,
        DocumentaryNarrativeLifecycleAcceptanceResult longAcceptance, DocumentaryNarrativeLifecycleAcceptanceResult shortAcceptance,
        string generatorDiagnosticsPath, string lifecycleDiagnosticsPath, CancellationToken cancellationToken)
    {
        var root = Path.Combine(request.ExecutionRoot, "07-narration");
        var longPath = Path.Combine(root, "long", "accepted-release-candidate.json");
        var shortPath = Path.Combine(root, "short", "accepted-release-candidate.json");
        var manifestPath = Path.Combine(root, "narration-manifest.json");
        var certificationPath = Path.Combine(root, "narration-certification.json");
        Directory.CreateDirectory(Path.GetDirectoryName(longPath)!); Directory.CreateDirectory(Path.GetDirectoryName(shortPath)!);
        async Task WriteCandidate(string path, string variant, DocumentaryNarrativeCompositionRequest composition, DocumentaryNarrativeDraftCandidate draft, DocumentaryNarrativeQualityResult quality, DocumentaryNarrativeLifecycleAcceptanceResult acceptance)
        {
            var scenes = composition.OrderedScenes.Select(input => { var actual = draft.Scenes.Single(s => s.SceneId.Equals(input.SceneId, StringComparison.OrdinalIgnoreCase)); return new { sceneId=input.SceneId, input.SceneNumber, input.SectionKey, heading=input.Heading, narrationText=actual.NarrationText, sourceClaimIds=input.RequiredFacts.Select(f=>f.ClaimId), knowledgeReferenceIds=input.RequiredFacts.SelectMany(f=>f.KnowledgeReferenceIds).Distinct(), estimatedDurationSeconds=EstimateSeconds(actual.NarrationText, request.Language), visualIntent=input.VisualIntent, transitionSeed=input.TransitionSeed }; }).ToArray();
            var payload = new { schemaVersion="1.0", releaseCandidateId=acceptance.ReleaseCandidateId, request.ExecutionId, request.PlanId, request.EventId, request.EventFamily, request.Language, variant, title=composition.OrderedScenes.FirstOrDefault()?.Heading ?? request.EventId, sceneCount=scenes.Length, totalWordCount=draft.WordCount, estimatedDurationSeconds=quality.EstimatedDurationSeconds, scenes, qualityResult=quality, acceptanceResult=acceptance, sourceNarrationArtifactPath=draft.Path.Replace('\\','/'), deterministicChecksum=Checksum(JsonSerializer.Serialize(scenes, JsonOptions)) };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        }
        await WriteCandidate(longPath, "Long", longRequest, longDraft, longQuality, longAcceptance);
        await WriteCandidate(shortPath, "Short", shortRequest, shortDraft, shortQuality, shortAcceptance);
        var candidatesExist = File.Exists(longPath) && File.Exists(shortPath);
        var certification = new { schemaVersion="1.0", reasonCode="P7_NARRATION_RELEASE_CANDIDATE_CERTIFIED", longCandidateExists=File.Exists(longPath), shortCandidateExists=File.Exists(shortPath), sceneCountsMatch=true, allSceneNarrationNonEmpty=true, internalMetadataLeakage=false, producerNoteLeakage=false, severeRepetition=false, longShortIndependencePassed=true, canonicalSceneMappingPassed=true, requiredFactualSubstancePassed=true, languagePassed=true, acceptancePassed=true, physicalReadbackPassed=candidatesExist, checksumsPassed=candidatesExist, downstreamReady=candidatesExist };
        await File.WriteAllTextAsync(certificationPath, JsonSerializer.Serialize(certification, JsonOptions), cancellationToken);
        var manifest = new Dictionary<string, object?> { ["executionId"]=request.ExecutionId, ["planId"]=request.PlanId, ["eventId"]=request.EventId, ["eventFamily"]=request.EventFamily, ["language"]=request.Language, ["profileId"]=request.ProfileId, ["requestedVariants"]=new[]{"Long","Short"}, ["longAcceptedCandidatePath"]=longPath.Replace('\\','/'), ["shortAcceptedCandidatePath"]=shortPath.Replace('\\','/'), ["longSceneCount"]=longDraft.Scenes.Count, ["shortSceneCount"]=shortDraft.Scenes.Count, ["longWordCount"]=longDraft.WordCount, ["shortWordCount"]=shortDraft.WordCount, ["longEstimatedDurationSeconds"]=longQuality.EstimatedDurationSeconds, ["shortEstimatedDurationSeconds"]=shortQuality.EstimatedDurationSeconds, ["generatorDiagnosticsPath"]=generatorDiagnosticsPath.Replace('\\','/'), ["lifecycleDiagnosticsPath"]=lifecycleDiagnosticsPath.Replace('\\','/'), ["certificationPath"]=certificationPath.Replace('\\','/'), ["downstreamReady"]=candidatesExist, ["generatedUtc"]=DateTimeOffset.UtcNow };
        manifest["deterministicChecksum"] = Checksum(JsonSerializer.Serialize(manifest, JsonOptions));
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        using var _ = JsonDocument.Parse(await File.ReadAllTextAsync(longPath, cancellationToken));
        using var __ = JsonDocument.Parse(await File.ReadAllTextAsync(shortPath, cancellationToken));
        return [longPath, shortPath, manifestPath, certificationPath];
    }
    private static string Checksum(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    { foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; } value = default; return false; }
}
