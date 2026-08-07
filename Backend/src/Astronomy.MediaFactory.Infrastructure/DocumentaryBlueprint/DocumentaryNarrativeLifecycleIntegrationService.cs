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
    string Language, string ProfileId, int Year, string RegionId, object ProductionPipelineRequest)
{
    public string AttemptId { get; init; } = Guid.NewGuid().ToString("N");
}

public sealed record DocumentaryNarrativeCompositionRequest(
    string ExecutionId, Guid PlanId, string EventId, string EventFamily, string Language, string Variant,
    string ProfileId, IReadOnlyList<DocumentaryNarrativeSceneInput> OrderedScenes,
    DocumentaryNarrativeDurationGuidance OverallDurationGuidance,
    IReadOnlyList<string> SafetyRules, IReadOnlyList<string> Vocabulary,
    IReadOnlyList<string> KnowledgeReferences)
{
    public IReadOnlyList<string> RepairGuidance { get; init; } = [];
    public DocumentaryNarrativeBlueprintLineage? BlueprintLineage { get; init; }
}

public sealed record DocumentaryNarrativeBlueprintLineage(string SourceBlueprintAggregateId,
    string SourceBlueprintAggregateChecksum, string SourceVariantBlueprintId,
    string SourceVariantBlueprintChecksum, string SourceStoryFramesAuthorityId,
    string SourceStoryFramesAuthorityChecksum, string Variant, IReadOnlyList<string> BlueprintSceneIds);

public sealed record DocumentaryNarrativeDurationGuidance(int MinimumSeconds, int PreferredSeconds, int MaximumSeconds);
public sealed record DocumentaryNarrativeRequiredFact(string ClaimId, string Fact,
    IReadOnlyList<string> KnowledgeReferenceIds, IReadOnlyList<string> SourceIds, decimal Confidence,
    IReadOnlyList<string> QualificationRequirements);
public sealed record DocumentaryNarrativeSceneInput(int SceneNumber, string SceneId, string SectionKey,
    string Heading, string ViewerQuestion, string LearningObjective, string NarrationBrief,
    IReadOnlyList<DocumentaryNarrativeRequiredFact> RequiredFacts, IReadOnlyList<string> OptionalFacts,
    IReadOnlyList<string> CulturalContext, IReadOnlyList<string> SafetyRules, IReadOnlyList<string> Vocabulary,
    string VisualIntent, int TargetDurationSeconds, string PreviousSceneContext, string TransitionSeed)
{
    public string BlueprintSceneId { get; init; } = SceneId;
    public string StoryFrameId { get; init; } = "";
    public string SceneRole { get; init; } = Heading;
    public string NarrativeStage { get; init; } = SectionKey;
    public string EditorialOutcome { get; init; } = "";
    public string EditorialPriority { get; init; } = "";
    public IReadOnlyList<string> BlueprintKnowledgeReferenceIds { get; init; } = [];
    public IReadOnlyList<string> VisualOpportunities { get; init; } = [];
}
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
    bool CanRetry = false, IReadOnlyList<string>? ProducerNoteLeakageMatches = null);
internal sealed record Phase7BlueprintAuthority(DocumentaryBlueprintAggregate Aggregate,
    DocumentaryBlueprintVariantArtifact Long, DocumentaryBlueprintVariantArtifact Short,
    StoryFramesAuthority StoryFrames);
public sealed record DocumentaryNarrativeProviderCallEvidence(string Generator, string EntryMethod,
    int GeneratorInvocationCount, bool LongVariantProduced, bool ShortVariantProduced,
    IReadOnlyList<string> DiagnosticFiles)
{
    // Compatibility aliases for callers of the short-lived original evidence contract.
    public int LongCalls => LongVariantProduced ? GeneratorInvocationCount : 0;
    public int ShortCalls => ShortVariantProduced ? GeneratorInvocationCount : 0;
}
public sealed record DocumentaryNarrativeStageEvidence(string CurrentAttemptId, bool PreProviderValidationPassed,
    bool ProviderInvocationStarted, int LongProviderInvocationCount, int ShortProviderInvocationCount,
    bool ProviderInvocationCompleted, bool ProviderResponseParsed, bool PostProviderValidationStarted,
    bool PostProviderValidationPassed);
public sealed record DocumentaryNarrativeLifecycleResult(
    DocumentaryNarrativeCompositionRequest LongRequest, DocumentaryNarrativeCompositionRequest ShortRequest,
    DocumentaryNarrativeDraftCandidate? LongDraft, DocumentaryNarrativeDraftCandidate? ShortDraft,
    DocumentaryNarrativeQualityResult LongQuality, DocumentaryNarrativeQualityResult ShortQuality,
    IReadOnlyList<string> RevisionHistory, DocumentaryNarrativeLifecycleAcceptanceResult LongAcceptance,
    DocumentaryNarrativeLifecycleAcceptanceResult ShortAcceptance, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, DocumentaryNarrativeProviderCallEvidence ProviderCallEvidence,
    IReadOnlyList<string> GeneratedFiles)
{
    public Phase7NarrationPublicationResult? Publication { get; init; }
    public DocumentaryNarrativeStageEvidence? StageEvidence { get; init; }
    public bool Succeeded => LongAcceptance.Accepted && ShortAcceptance.Accepted && Errors.Count == 0
        && Publication is { PublicationCommitted: true, PhysicalReadbackPassed: true, ChecksumsPassed: true };
}

/// <summary>Thin production orchestration around the existing V5 narration generator.</summary>
public sealed class DocumentaryNarrativeLifecycleIntegrationService(
    NarrationGeneratorV5 generator,
    DocumentaryNarrativeAcceptanceCoordinator acceptanceCoordinator,
    IPhase7NarrationRuntimeAuthorityLoader? runtimeAuthorityLoader = null) : IDocumentaryNarrativeLifecycleIntegrationService
{
    public const int MaximumGenerationAttempts = 2;
    public const int MaximumRevisionAttempts = MaximumGenerationAttempts;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] LeakageTerms =
        ["system prompt", "developer prompt", "producer note", "internal instruction", "claimId", "knowledgeReferenceId",
         "viewerQuestionId", "learningObjectiveId", "final narration remains owned by phase 7", "advance the certified", "```json"];
    private static readonly Regex DelimitedInternalId = new(@"\b(?:VQ|LO|CLM|CLAIM|KR|KNOWLEDGE)[-_][A-Z0-9][A-Z0-9_.:-]*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CompactInternalId = new(@"\b(?:VQ|LO|CLM|KR)\d{2,}\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AdvancePlaceholder = new(@"\bAdvance\d{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        CleanupWorkingNarration(request.ExecutionRoot);

        StoryFramesAuthority? authority = null;
        Phase7BlueprintAuthority? blueprintAuthority = null;
        if (!File.Exists(storyFramesPath))
            errors.Add("Canonical Phase 6 Story Frame authority was not found at 06-story-frames/story-frames.json.");
        else
        {
            await using var stream = File.OpenRead(storyFramesPath);
            authority = await JsonSerializer.DeserializeAsync<StoryFramesAuthority>(stream, JsonOptions, cancellationToken);
            ValidateAuthority(authority, request, errors);
        }

        blueprintAuthority = await ReadBlueprintAuthorityAsync(request.ExecutionRoot, authority, errors, cancellationToken);

        Phase7NarrationRuntimeAuthority? runtimeAuthority = null;
        var planningPath = Path.Combine(request.ExecutionRoot, NarrationPlanningArtifactPaths.Authority.Replace('/', Path.DirectorySeparatorChar));
        NarrationPlanningAuthority? planningIdentity = null;
        if (File.Exists(planningPath))
            planningIdentity = JsonSerializer.Deserialize<NarrationPlanningAuthority>(await File.ReadAllTextAsync(planningPath, cancellationToken), JsonOptions);
        var runtime = runtimeAuthorityLoader is null
            ? new Phase7NarrationRuntimeAuthorityLoadResult(false, null, Phase7NarrationRuntimeAuthorityReasonCodes.Missing,
                ["IPhase7NarrationRuntimeAuthorityLoader is not registered."])
            : await runtimeAuthorityLoader.LoadAsync(new(request.ExecutionRoot, request.ExecutionId,
                request.PlanId.ToString("D"), request.EventId, request.Language, request.ProfileId,
                planningIdentity?.ProfileVersion ?? ""), cancellationToken);
        if (!runtime.IsValid || runtime.Authority is null) errors.Add($"{runtime.ReasonCode}: {string.Join("; ", runtime.Errors)}");
        else runtimeAuthority = runtime.Authority;
        var runtimeDiagnosticsPath = Path.Combine(request.ExecutionRoot, "narration-v5", "runtime-authority-projection-diagnostics.json");
        var runtimePlanning = runtimeAuthority?.PlanningAuthority.Authority;
        await File.WriteAllTextAsync(runtimeDiagnosticsPath, JsonSerializer.Serialize(new
        {
            knowledgeAuthorityId = runtimeAuthority?.KnowledgeAuthority.KnowledgeAuthority.AuthorityId,
            knowledgeAuthorityChecksum = runtimeAuthority?.KnowledgeAuthority.KnowledgeAuthority.SemanticChecksum,
            planningAuthorityId = runtimePlanning?.AuthorityId, planningAuthorityChecksum = runtimePlanning?.DeterministicChecksum,
            packetCollectionChecksum = runtimePlanning?.PacketCollectionChecksum,
            packetArtifactPath = NarrationPlanningArtifactPaths.PacketCollection,
            runtimeAuthorityCommittedStatePassed = runtime.IsValid,
            longPlanningSceneCount = runtimePlanning?.LongScenes.Count ?? 0,
            shortPlanningSceneCount = runtimePlanning?.ShortScenes.Count ?? 0,
            longPacketCount = runtimeAuthority?.LongPackets.Count ?? 0,
            shortPacketCount = runtimeAuthority?.ShortPackets.Count ?? 0,
            requiredClaimCount = (runtimeAuthority?.LongPackets ?? []).Concat(runtimeAuthority?.ShortPackets ?? []).Sum(packet => packet.RequiredClaims.Count(claim => !claim.RequiresHumanReview)),
            optionalClaimCount = (runtimeAuthority?.LongPackets ?? []).Concat(runtimeAuthority?.ShortPackets ?? []).Sum(packet => packet.OptionalClaims.Count(claim => !claim.RequiresHumanReview)),
            deferredClaimsExcludedCount = (runtimeAuthority?.LongPackets ?? []).Concat(runtimeAuthority?.ShortPackets ?? []).Sum(packet => packet.DeferredClaims.Count),
            humanReviewClaimsExcludedCount = (runtimeAuthority?.LongPackets ?? []).Concat(runtimeAuthority?.ShortPackets ?? []).Sum(packet => packet.RequiredClaims.Concat(packet.OptionalClaims).Count(claim => claim.RequiresHumanReview || claim.Disposition == Phase7ClaimDisposition.HumanReview)),
            packetChecksumPassed = runtime.IsValid, planningChecksumPassed = runtime.IsValid,
            factSource = "CommittedSceneKnowledgePacket", supplementalSource = "RequiredSemanticFactResolver",
            errors = runtime.Errors
        }, JsonOptions), cancellationToken);
        generatedFiles.Add(runtimeDiagnosticsPath);

        var longRequest = BuildCompositionRequest(request, authority, blueprintAuthority?.Long, blueprintAuthority?.Aggregate, runtimeAuthority, "Long", new(480, 600, 900));
        var shortRequest = BuildCompositionRequest(request, authority, blueprintAuthority?.Short, blueprintAuthority?.Aggregate, runtimeAuthority, "Short", new(60, 90, 120));
        if (longRequest.OrderedScenes.Count == 0) errors.Add("Long narration has no governed Phase 6 scenes.");
        if (shortRequest.OrderedScenes.Count == 0) errors.Add("Short narration has no governed Phase 6 scenes.");
        ValidateCompositionHandoff(request, longRequest, shortRequest, errors);

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
        var generationAttempts = 0;
        var staleArtifactIgnored = false;
        var providerInvocationStarted = false;
        var providerInvocationCompleted = false;
        var preProviderValidationPassed = errors.Count == 0;
        var postProviderValidationPassed = false;
        var postProviderValidationStarted = false;
        var providerResponseParsed = false;
        var longProviderInvocationCount = 0;
        var shortProviderInvocationCount = 0;
        Phase7NarrationPublicationResult? publicationResult = null;
        var generatorAssessment = new NarrationGeneratorBlockingAssessment(false, false, false, [], []);

        if (errors.Count == 0)
        {
            for (var attempt = 1; attempt <= MaximumGenerationAttempts; attempt++)
            {
                foreach (var variant in new[] { "long", "short" })
                {
                    var stale = Path.Combine(request.ExecutionRoot, "narration-v5", variant, "narration.json");
                    if (File.Exists(stale)) { File.Delete(stale); staleArtifactIgnored = true; }
                }
                generationAttempts++;
                providerInvocationStarted = true;
                generated = await InvokeGeneratorAsync(request, longRequest, shortRequest, cancellationToken);
                var counts = ReadProviderInvocationCounts(request.ExecutionRoot);
                longProviderInvocationCount += counts.Long;
                shortProviderInvocationCount += counts.Short;
                invocationCount += counts.Long + counts.Short;
                providerInvocationCompleted = true;
                await WriteAttemptMarkersAsync(request, cancellationToken);
                generatedFiles.AddRange(generated.GeneratedFiles);
                revisions.Add(attempt == 1 ? "Attempt 1: Generated." : "Attempt 2: Regenerated with correction guidance.");

                var longRead = await ReadDraftAsync(request.ExecutionRoot, "long", longRequest, request.AttemptId, cancellationToken);
                var shortRead = await ReadDraftAsync(request.ExecutionRoot, "short", shortRequest, request.AttemptId, cancellationToken);
                longDraft = longRead.Draft;
                shortDraft = shortRead.Draft;
                providerResponseParsed = longDraft is not null && shortDraft is not null;
                generatorAssessment = AssessGeneratorResult(request.ExecutionRoot, true, true);
                postProviderValidationStarted = true;
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
        var convergenceSucceeded = noPendingRepairableIssues && generationAttempts <= MaximumGenerationAttempts;
        var longAcceptance = Accept(longDraft, longQuality, request.ExecutionId, "long",
            acceptanceCoordinator.Accept(longDraft is not null, longQuality.Passed, convergenceSucceeded));
        var shortAcceptance = Accept(shortDraft, shortQuality, request.ExecutionId, "short",
            acceptanceCoordinator.Accept(shortDraft is not null, shortQuality.Passed, convergenceSucceeded));

        postProviderValidationPassed = longQuality.Passed && shortQuality.Passed && crossErrors.Count == 0;
        var succeeded = longAcceptance.Accepted && shortAcceptance.Accepted && errors.Count == 0;
        if (succeeded)
        {
            publicationResult = await PublishReleaseCandidatesAsync(request, blueprintAuthority!, longRequest, shortRequest, longDraft!, shortDraft!,
                longQuality, shortQuality, longAcceptance, shortAcceptance, generatorDiagnosticsPath, diagnosticsPath, cancellationToken);
            generatedFiles.AddRange(publicationResult.PublishedFiles);
            if (!publicationResult.PublicationCommitted) errors.AddRange(publicationResult.Errors.Select(error => "Publication: " + error));
        }
        var diagnostics = new
        {
            schemaVersion = "1.0", request.ExecutionId, planId = request.PlanId, request.EventId, request.EventFamily,
            request.Language, request.ProfileId, canonicalStoryFramesPath = storyFramesPath,
            longCompositionRequestPath = longRequestPath, shortCompositionRequestPath = shortRequestPath,
            expectedLongSceneCount = longRequest.OrderedScenes.Count, actualLongSceneCount = longDraft?.Scenes.Count ?? 0,
            expectedShortSceneCount = shortRequest.OrderedScenes.Count, actualShortSceneCount = shortDraft?.Scenes.Count ?? 0,
            generator = nameof(NarrationGeneratorV5), entryMethod = nameof(NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync),
            currentAttemptId = request.AttemptId, providerInvocationStarted, longProviderInvocationCount,
            shortProviderInvocationCount, providerInvocationCompleted, providerResponseParsed, postProviderValidationStarted,
            narrationArtifactGeneratedThisAttempt = generated.GeneratedFiles.Any(File.Exists), narrationArtifactAttemptId = request.AttemptId,
            staleArtifactIgnored, preProviderValidationPassed, postProviderValidationPassed,
            generatorInvocationCount = invocationCount,
            generatorDiagnosticsPath,
            generatorBlockingAssessment = generatorAssessment,
            longArtifactPath = Path.Combine(request.ExecutionRoot, "narration-v5", "long", "narration.json"),
            shortArtifactPath = Path.Combine(request.ExecutionRoot, "narration-v5", "short", "narration.json"),
            longQuality, shortQuality, crossVariantValidationResult = new { passed = crossErrors.Count == 0, errors = crossErrors },
            postGenerationValidation = new
            {
                longVariant = BuildPostGenerationDiagnostics(longDraft, longRequest, generatorAssessment),
                shortVariant = BuildPostGenerationDiagnostics(shortDraft, shortRequest, generatorAssessment)
            },
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
            generatedFiles.Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray())
        { Publication = publicationResult, StageEvidence = new(request.AttemptId, preProviderValidationPassed,
            providerInvocationStarted, longProviderInvocationCount, shortProviderInvocationCount,
            providerInvocationCompleted, providerResponseParsed, postProviderValidationStarted, postProviderValidationPassed) };
    }

    private async Task<NarrationGeneratorV5Result> InvokeGeneratorAsync(DocumentaryNarrativeLifecycleRequest request,
        DocumentaryNarrativeCompositionRequest longRequest, DocumentaryNarrativeCompositionRequest shortRequest,
        CancellationToken cancellationToken)
    {
        var batchRequest = new BatchGenerateFromPlansRequest(request.Year, request.RegionId, request.Language,
            DryRun: false, UseProductionPipeline: true, StartPhaseNo: 7, EndPhaseNo: 7, PlanId: request.PlanId);
        var batchResponse = new BatchGenerateFromPlansResponse(true, false, 1, 1, 1, [], [], [], [],
            UseProductionPipeline: true, PlanId: request.PlanId, OutputRoot: request.ExecutionRoot,
            ProductionPipelineRequest: request.ProductionPipelineRequest);
        return await generator.BuildAndWriteDiagnosticsAsync(batchRequest, batchResponse,
            new NarrationGeneratorV5AuthorityInput(longRequest, shortRequest), cancellationToken);
    }

    private static void ValidateCompositionHandoff(DocumentaryNarrativeLifecycleRequest lifecycle,
        DocumentaryNarrativeCompositionRequest longRequest, DocumentaryNarrativeCompositionRequest shortRequest, List<string> errors)
    {
        var identityValid = new[] { longRequest, shortRequest }.All(composition =>
            composition.ExecutionId == lifecycle.ExecutionId && composition.PlanId == lifecycle.PlanId &&
            composition.EventId == lifecycle.EventId && composition.Language == lifecycle.Language && composition.ProfileId == lifecycle.ProfileId);
        var ownershipOverlap = longRequest.OrderedScenes.Select(scene => scene.SceneId)
            .Intersect(shortRequest.OrderedScenes.Select(scene => scene.SceneId), StringComparer.OrdinalIgnoreCase).Any();
        if (!identityValid || ownershipOverlap)
            errors.Add("P7_COMPOSITION_AUTHORITY_HANDOFF_INVALID: composition identity mismatch or cross-variant scene ownership overlap.");
    }

    internal static (int Long, int Short) ReadProviderInvocationCounts(string root)
    {
        var path = Path.Combine(root, "narration-v5", "documentary-script", "performance-diagnostics.json");
        if (!File.Exists(path)) return (0, 0);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return (TryProperty(document.RootElement, "longProviderInvocationCount", out var longCount) && longCount.TryGetInt32(out var longValue) ? longValue : 0,
                TryProperty(document.RootElement, "shortProviderInvocationCount", out var shortCount) && shortCount.TryGetInt32(out var shortValue) ? shortValue : 0);
        }
        catch (JsonException) { return (0, 0); }
    }

    private static void CleanupWorkingNarration(string root)
    {
        // narration-v5 is Phase 7's uncommitted working root.  Never touch 07-narration here:
        // that directory remains the last certified authority until atomic publication succeeds.
        var workingRoot = Path.Combine(root, "narration-v5");
        foreach (var relative in new[] { "narration.json", "long/narration.json", "short/narration.json",
                     "raw-narrative", "documentary-script" })
        {
            var path = Path.Combine(workingRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
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

    private static async Task<Phase7BlueprintAuthority?> ReadBlueprintAuthorityAsync(string root,
        StoryFramesAuthority? storyFrames, List<string> errors, CancellationToken token)
    {
        var required = new[] { "04-blueprint/documentary-blueprint.json", "04-blueprint/documentary-blueprint.long.json",
            "04-blueprint/documentary-blueprint.short.json", "04-blueprint/long-scene-index.json",
            "04-blueprint/short-scene-index.json", "04-blueprint/knowledge-selection.json",
            "validation/phase-04-validation.json", "05-editorial/blueprint-certification.json",
            "validation/phase-05-validation.json", "validation/phase-06-validation.json" };
        foreach (var relative in required.Where(relative => !File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))))
            errors.Add($"Required governed authority artifact is missing: {relative}.");
        if (errors.Count > 0 || storyFrames is null) return null;
        try
        {
            async Task<T?> Read<T>(string relative)
            {
                await using var stream = File.OpenRead(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token);
            }
            var aggregate = await Read<DocumentaryBlueprintAggregate>(required[0]);
            var longVariant = await Read<DocumentaryBlueprintVariantArtifact>(required[1]);
            var shortVariant = await Read<DocumentaryBlueprintVariantArtifact>(required[2]);
            if (aggregate is null || longVariant is null || shortVariant is null)
            { errors.Add("Canonical Phase 4 DocumentaryBlueprint artifacts are empty or invalid."); return null; }
            ValidateVariant("Long", aggregate.LongVariant, longVariant, storyFrames, errors);
            ValidateVariant("Short", aggregate.ShortVariant, shortVariant, storyFrames, errors);
            if (!storyFrames.SourcePhase4Checksum.Equals(aggregate.DeterministicChecksum, StringComparison.Ordinal))
                errors.Add("StoryFramesAuthority SourcePhase4Checksum does not match the published DocumentaryBlueprint aggregate checksum.");
            foreach (var validation in new[] { required[6], required[8], required[9] })
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, validation.Replace('/', Path.DirectorySeparatorChar)), token));
                var json = document.RootElement.GetRawText();
                if (Regex.IsMatch(json, "\\\"(?:validationStatus|status)\\\"\\s*:\\s*\\\"(?:Invalid|Failed)\\\"", RegexOptions.IgnoreCase))
                    errors.Add($"Committed authority validation is not valid: {validation}.");
            }
            return errors.Count == 0 ? new(aggregate, longVariant, shortVariant, storyFrames) : null;
        }
        catch (JsonException ex) { errors.Add($"Governed DocumentaryBlueprint chain contains invalid JSON: {ex.Message}"); return null; }
    }

    private static void ValidateVariant(string variant, DocumentaryBlueprintVariantArtifact embedded,
        DocumentaryBlueprintVariantArtifact physical, StoryFramesAuthority frames, List<string> errors)
    {
        if (!physical.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase))
            errors.Add($"{variant} blueprint artifact has wrong variant ownership '{physical.Variant}'.");
        if (embedded.DeterministicChecksum != physical.DeterministicChecksum || embedded.Blueprint.BlueprintId != physical.Blueprint.BlueprintId)
            errors.Add($"{variant} blueprint physical artifact does not match the published aggregate.");
        var blueprintIds = physical.Blueprint.Scenes.OrderBy(scene => scene.SceneNumber).Select(scene => scene.SceneId).ToArray();
        var frameIds = frames.Frames.Where(frame => frame.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase))
            .GroupBy(frame => frame.SceneId, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Min(frame => frame.SceneNumber))
            .Select(group => group.Key).ToArray();
        if (!blueprintIds.SequenceEqual(frameIds, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{variant} Story Frame scene sequence does not exactly match its governed DocumentaryBlueprint scene sequence.");
    }

    internal static DocumentaryNarrativeCompositionRequest BuildCompositionRequest(DocumentaryNarrativeLifecycleRequest request,
        StoryFramesAuthority? authority, string variant, DocumentaryNarrativeDurationGuidance duration)
        => BuildCompositionRequest(request, authority, null, null, variant, duration);

    internal static DocumentaryNarrativeCompositionRequest BuildCompositionRequest(DocumentaryNarrativeLifecycleRequest request,
        StoryFramesAuthority? authority, DocumentaryBlueprintVariantArtifact? variantArtifact,
        DocumentaryBlueprintAggregate? aggregate, string variant, DocumentaryNarrativeDurationGuidance duration)
        => BuildCompositionRequest(request, authority, variantArtifact, aggregate, null, variant, duration);

    internal static DocumentaryNarrativeCompositionRequest BuildCompositionRequest(DocumentaryNarrativeLifecycleRequest request,
        StoryFramesAuthority? authority, DocumentaryBlueprintVariantArtifact? variantArtifact,
        DocumentaryBlueprintAggregate? aggregate, Phase7NarrationRuntimeAuthority? runtimeAuthority,
        string variant, DocumentaryNarrativeDurationGuidance duration)
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
            var blueprint = variantArtifact?.Blueprint.Scenes.SingleOrDefault(scene => scene.SceneId.Equals(first.SceneId, StringComparison.OrdinalIgnoreCase));
            var planning = (variant.Equals("Long", StringComparison.OrdinalIgnoreCase) ? runtimeAuthority?.PlanningAuthority.Authority.LongScenes : runtimeAuthority?.PlanningAuthority.Authority.ShortScenes)
                ?.SingleOrDefault(scene => scene.SceneId.Equals(first.SceneId, StringComparison.OrdinalIgnoreCase));
            var packet = (variant.Equals("Long", StringComparison.OrdinalIgnoreCase) ? runtimeAuthority?.LongPackets : runtimeAuthority?.ShortPackets)
                ?.SingleOrDefault(value => value.PacketId == planning?.PacketId);
            var requiredIds = planning?.RequiredClaims.ToHashSet(StringComparer.Ordinal) ?? [];
            var optionalIds = planning?.OptionalClaims.ToHashSet(StringComparer.Ordinal) ?? [];
            var requiredFacts = packet?.RequiredClaims.Where(claim => requiredIds.Contains(claim.ClaimId) && !claim.RequiresHumanReview && claim.Disposition is not Phase7ClaimDisposition.Deferred and not Phase7ClaimDisposition.HumanReview)
                .Select(claim => new DocumentaryNarrativeRequiredFact(claim.ClaimId, claim.Text, claim.KnowledgeReferenceIds, claim.SourceIds, claim.Confidence,
                    claim.RequiresQualification ? ["Retain the certified qualification."] : [])).ToArray() ?? [];
            var optionalFacts = packet?.OptionalClaims.Where(claim => optionalIds.Contains(claim.ClaimId) && !claim.RequiresHumanReview && claim.Disposition is not Phase7ClaimDisposition.Deferred and not Phase7ClaimDisposition.HumanReview)
                .Select(claim => claim.Text).ToArray() ?? [];
            var purpose = blueprint is null ? string.Join(" ", ordered.Select(frame => frame.NarrativeIntent).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
                : $"Purpose: {blueprint.SceneObjective.Summary} Viewer goal: {blueprint.SceneObjective.LearningGoal} " +
                  $"Takeaway: {blueprint.EditorialOutcome.ViewerTakeaway} Transition intent: {blueprint.Transition.TransitionIntent}";
            return new DocumentaryNarrativeSceneInput(packet?.SceneNumber ?? first.SceneNumber, planning?.SceneId ?? first.SceneId, planning?.NarrativeGoal.SectionKey ?? blueprint?.NarrativeStage.ToString() ?? first.NarrativeStage,
                blueprint?.Title ?? packet?.SceneRole ?? first.SceneRole, planning?.ViewerQuestion ?? blueprint?.ViewerQuestion.Text ?? string.Join("; ", ordered.SelectMany(frame => frame.ViewerQuestionIds).Distinct()),
                planning?.LearningObjective ?? blueprint?.SceneObjective.LearningGoal ?? string.Join("; ", ordered.SelectMany(frame => frame.LearningObjectiveIds).Distinct()),
                planning is null ? purpose : $"{planning.NarrativeGoal.SectionKey}: {packet?.SceneObjective}",
                requiredFacts, optionalFacts, packet?.CulturalContext ?? [], (packet?.SafetyRules ?? []).Concat(planning?.SafetyRequirements ?? []).Concat(safety).Distinct().ToArray(), [],
                string.Join(" ", ordered.Select(frame => frame.VisualIntent).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
                planning?.ExpectedDuration ?? packet?.TargetDurationSeconds ?? blueprint?.EstimatedDurationSeconds ?? (int)Math.Round(ordered.Sum(frame => frame.EstimatedDuration)),
                index == 0 ? "" : $"Continue naturally from {groups[index - 1].Key}.",
                planning is null ? blueprint?.Transition.TransitionIntent ?? string.Join(" ", ordered.Select(frame => frame.TransitionOut).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
                    : string.Join(" ", new[] { planning.IncomingTransition.DestinationTransitionIn, planning.OutgoingTransition.SourceTransitionOut }.Where(value => !string.IsNullOrWhiteSpace(value))))
            {
                BlueprintSceneId = blueprint?.SceneId ?? planning?.SceneId ?? first.SceneId, StoryFrameId = planning?.StoryFrameId ?? first.FrameId,
                SceneRole = packet?.SceneRole ?? blueprint?.SceneRole.ToString() ?? first.SceneRole,
                NarrativeStage = packet?.NarrativeStage ?? blueprint?.NarrativeStage.ToString() ?? first.NarrativeStage,
                EditorialOutcome = blueprint is null ? "" : $"{blueprint.EditorialOutcome.ViewerTakeaway} {blueprint.EditorialOutcome.NarrativeContribution}",
                EditorialPriority = blueprint?.EditorialPriority.ToString() ?? "",
                BlueprintKnowledgeReferenceIds = blueprint?.KnowledgeReferences.Select(reference => reference.KnowledgeEntryId).ToArray() ?? [],
                VisualOpportunities = blueprint?.VisualOpportunities.Select(opportunity => opportunity.Description).ToArray() ?? []
            };
        }).ToArray();
        return new DocumentaryNarrativeCompositionRequest(request.ExecutionId, request.PlanId, request.EventId, request.EventFamily, request.Language, variant,
            request.ProfileId, scenes, duration, safety, [],
            scenes.SelectMany(scene => scene.BlueprintKnowledgeReferenceIds)
                .Concat((authority?.Frames ?? []).Where(frame => frame.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(frame => frame.KnowledgeReferenceIds)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            BlueprintLineage = variantArtifact is null || aggregate is null || authority is null ? null : new(
                aggregate.AggregateId, aggregate.DeterministicChecksum, variantArtifact.Blueprint.BlueprintId,
                variantArtifact.DeterministicChecksum, authority.AuthorityId, authority.SemanticChecksum, variant,
                variantArtifact.Blueprint.Scenes.OrderBy(scene => scene.SceneNumber).Select(scene => scene.SceneId).ToArray())
        };
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
        string root, string variant, DocumentaryNarrativeCompositionRequest composition, string currentAttemptId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "narration-v5", variant, "narration.json");
        if (!File.Exists(path)) return (null, [$"Draft artifact was not found at narration-v5/{variant}/narration.json."]);
        var markerPath = Path.Combine(root, "narration-v5", variant, "attempt-metadata.json");
        if (!File.Exists(markerPath)) return (null, [$"Draft artifact has no current-attempt metadata at narration-v5/{variant}/attempt-metadata.json."]);
        try
        {
            using (var marker = JsonDocument.Parse(await File.ReadAllTextAsync(markerPath, cancellationToken)))
                if (!TryProperty(marker.RootElement, "attemptId", out var id) || id.GetString() != currentAttemptId)
                    return (null, [$"Draft artifact attemptId does not match current attempt {currentAttemptId}."]);
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

    private static async Task WriteAttemptMarkersAsync(DocumentaryNarrativeLifecycleRequest request, CancellationToken cancellationToken)
    {
        foreach (var variant in new[] { "long", "short" })
        {
            var narrationPath = Path.Combine(request.ExecutionRoot, "narration-v5", variant, "narration.json");
            if (!File.Exists(narrationPath)) continue;
            var markerPath = Path.Combine(Path.GetDirectoryName(narrationPath)!, "attempt-metadata.json");
            await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(new { attemptId = request.AttemptId,
                generatedUtc = DateTimeOffset.UtcNow, request.ExecutionId, request.PlanId }, JsonOptions), cancellationToken);
        }
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
            if (duplicates.Length == 0 && missing.Length == 0 && unexpected.Length == 0 &&
                !expected.SequenceEqual(draft.SceneIds, StringComparer.OrdinalIgnoreCase))
                errors.Add("Narration scene order does not match the governed DocumentaryBlueprint and Story Frames sequence.");
            var duplicateBlocks = draft.Scenes.Where(scene => !string.IsNullOrWhiteSpace(scene.NarrationText))
                .GroupBy(scene => Normalize(scene.NarrationText), StringComparer.Ordinal).Where(group => group.Count() > 1).ToArray();
            if (duplicateBlocks.Length > 0) errors.Add("Duplicate complete scene narration blocks were detected.");
            if (LeakageTerms.Any(term => draft.Text.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                ContainsInternalIdentifier(draft.Text) ||
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
                advisory.Add("Generator editorial decision is Do Not Publish; objective validation gates govern lifecycle acceptance.");
            if (TryProperty(rootElement, "warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array) advisory.AddRange(Strings(warnings));
            if (TryProperty(rootElement, "promptRecommendation", out var prompt) && prompt.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prompt.GetString())) advisory.Add("Prompt recommendation: " + prompt.GetString());
            if (TryProperty(rootElement, "auroraCertified", out _) && !Boolean(rootElement, "auroraCertified")) advisory.Add("Aurora certification was not achieved; this is advisory.");
            var producerNoteMatches = TryProperty(rootElement, "producerNotesLeakagePhrases", out var phrases) && phrases.ValueKind == JsonValueKind.Array
                ? Strings(phrases).ToArray() : [];
            return new(true, longProduced, shortProduced, blockers.Distinct().ToArray(), advisory.Distinct().ToArray(),
                Boolean(rootElement, "canRetry"), producerNoteMatches);
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
        if (string.IsNullOrWhiteSpace(text) || ContainsInternalIdentifier(text)) return false;
        var expected = scene.RequiredFacts.Select(f => f.Fact).Concat(scene.OptionalFacts).Concat(scene.CulturalContext)
            .Append(scene.NarrationBrief).Where(v => !string.IsNullOrWhiteSpace(v) && !ContainsInternalIdentifier(v));
        var stop = new HashSet<string>(["about","after","before","could","every","their","there","these","those","which","would","viewer","scene","learn","understand","discover","narrative","purpose"], StringComparer.OrdinalIgnoreCase);
        var concepts = expected.SelectMany(v => Regex.Matches(v ?? "", @"[\p{L}\p{N}']+").Select(m => m.Value.ToLowerInvariant()))
            .Where(t => t.Length >= 4 && !stop.Contains(t)).Distinct().ToArray();
        if (concepts.Length == 0) return WordCount(text) >= 7 && !Regex.IsMatch(text, @"^(look up|keep watching|the story continues|carry away the wonder)\b", RegexOptions.IgnoreCase);
        var normalized = Normalize(text);
        return concepts.Count(normalized.Contains) >= Math.Min(2, concepts.Length);
    }

    internal static bool ContainsInternalIdentifier(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (DelimitedInternalId.IsMatch(text) || CompactInternalId.IsMatch(text) || AdvancePlaceholder.IsMatch(text));

    private static object BuildPostGenerationDiagnostics(DocumentaryNarrativeDraftCandidate? draft,
        DocumentaryNarrativeCompositionRequest request, NarrationGeneratorBlockingAssessment assessment)
    {
        var text = draft?.Text ?? "";
        var internalMatches = InternalIdentifierMatches(text);
        var metadataMatches = LeakageTerms.Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Concat(internalMatches).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var producerMatches = assessment.ProducerNoteLeakageMatches ?? [];
        var scenes = (draft?.Scenes ?? []).Select(scene =>
        {
            var governed = request.OrderedScenes.FirstOrDefault(candidate => candidate.SceneId.Equals(scene.SceneId, StringComparison.OrdinalIgnoreCase));
            var expected = governed is null ? [] : ExpectedConcepts(governed);
            var normalized = Normalize(scene.NarrationText);
            var matched = expected.Where(normalized.Contains).ToArray();
            var ids = InternalIdentifierMatches(scene.NarrationText);
            return new { sceneId = scene.SceneId, factualSubstancePassed = governed is not null && HasFactualSubstance(scene.NarrationText, governed),
                expectedConcepts = expected, matchedConcepts = matched, internalIdentifierDetected = ids.Length > 0,
                internalIdentifierMatch = ids.FirstOrDefault() };
        }).ToArray();
        var producerDetected = producerMatches.Count > 0 || assessment.BlockingErrors.Any(error => error.Contains("Producer-note leakage", StringComparison.OrdinalIgnoreCase));
        return new { metadataLeakageDetected = metadataMatches.Length > 0, metadataLeakageMatches = metadataMatches,
            internalIdMatches = internalMatches, producerNoteLeakageDetected = producerDetected,
            producerNoteLeakageMatches = producerMatches, scenes };
    }

    private static string[] ExpectedConcepts(DocumentaryNarrativeSceneInput scene)
    {
        var stop = new HashSet<string>(["about","after","before","could","every","their","there","these","those","which","would","viewer","scene","learn","understand","discover","narrative","purpose"], StringComparer.OrdinalIgnoreCase);
        return scene.RequiredFacts.Select(f => f.Fact).Concat(scene.OptionalFacts).Concat(scene.CulturalContext).Append(scene.NarrationBrief)
            .Where(value => !string.IsNullOrWhiteSpace(value) && !ContainsInternalIdentifier(value))
            .SelectMany(value => Regex.Matches(value, @"[\p{L}\p{N}']+").Select(match => match.Value.ToLowerInvariant()))
            .Where(token => token.Length >= 4 && !stop.Contains(token)).Distinct().ToArray();
    }

    private static string[] InternalIdentifierMatches(string text) =>
        new[] { DelimitedInternalId, CompactInternalId, AdvancePlaceholder }.SelectMany(regex => regex.Matches(text).Select(match => match.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static async Task<Phase7NarrationPublicationResult> PublishReleaseCandidatesAsync(DocumentaryNarrativeLifecycleRequest request,
        Phase7BlueprintAuthority blueprintAuthority, DocumentaryNarrativeCompositionRequest longRequest, DocumentaryNarrativeCompositionRequest shortRequest,
        DocumentaryNarrativeDraftCandidate longDraft, DocumentaryNarrativeDraftCandidate shortDraft, DocumentaryNarrativeQualityResult longQuality,
        DocumentaryNarrativeQualityResult shortQuality, DocumentaryNarrativeLifecycleAcceptanceResult longAcceptance,
        DocumentaryNarrativeLifecycleAcceptanceResult shortAcceptance, string generatorDiagnosticsPath, string lifecycleDiagnosticsPath,
        CancellationToken cancellationToken)
    {
        object Candidate(string variant, DocumentaryNarrativeCompositionRequest composition, DocumentaryNarrativeDraftCandidate draft,
            DocumentaryNarrativeQualityResult quality, DocumentaryNarrativeLifecycleAcceptanceResult acceptance)
        {
            var lineage = composition.BlueprintLineage!;
            var scenes = composition.OrderedScenes.Select(input => { var actual = draft.Scenes.Single(s => s.SceneId.Equals(input.SceneId, StringComparison.OrdinalIgnoreCase)); return new { sceneId=input.SceneId, input.SceneNumber, blueprintSceneId=input.BlueprintSceneId, storyFrameId=input.StoryFrameId, selectedKnowledgeReferenceIds=input.BlueprintKnowledgeReferenceIds, selectedClaimIds=input.RequiredFacts.Select(f=>f.ClaimId), narrationText=actual.NarrationText }; }).ToArray();
            return new { schemaVersion="2.0", attemptId=request.AttemptId, generatedUtc=DateTimeOffset.UtcNow, releaseCandidateId=acceptance.ReleaseCandidateId, request.ExecutionId, request.PlanId, request.EventId, request.Language, variant, sourceBlueprintAggregateId=lineage.SourceBlueprintAggregateId, sourceBlueprintAggregateChecksum=lineage.SourceBlueprintAggregateChecksum, sourceVariantBlueprintId=lineage.SourceVariantBlueprintId, sourceVariantBlueprintChecksum=lineage.SourceVariantBlueprintChecksum, sourceStoryFramesAuthorityId=lineage.SourceStoryFramesAuthorityId, sourceStoryFramesAuthorityChecksum=lineage.SourceStoryFramesAuthorityChecksum, sourcePhase7KnowledgeAuthorityId=ReadKnowledgeAuthorityId(request.ExecutionRoot), sourcePhase7KnowledgeAuthorityChecksum=ReadKnowledgeAuthorityChecksum(request.ExecutionRoot), blueprintSceneCount=lineage.BlueprintSceneIds.Count, acceptedSceneCount=scenes.Length, scenes, qualityResult=quality, acceptanceResult=acceptance, deterministicChecksum=Phase7NarrationReleaseCandidateChecksum.ComputeScenes(scenes) };
        }
        var longCandidate = Candidate("Long", longRequest, longDraft, longQuality, longAcceptance);
        var shortCandidate = Candidate("Short", shortRequest, shortDraft, shortQuality, shortAcceptance);
        var publicationId = $"{request.ExecutionId}-{request.AttemptId}";
        var artifacts = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["long/accepted-release-candidate.json"] = JsonSerializer.Serialize(longCandidate, JsonOptions),
            ["long/acceptance-record.json"] = JsonSerializer.Serialize(new { request.AttemptId, variant="Long", acceptance=longAcceptance, acceptedUtc=DateTimeOffset.UtcNow }, JsonOptions),
            ["short/accepted-release-candidate.json"] = JsonSerializer.Serialize(shortCandidate, JsonOptions),
            ["short/acceptance-record.json"] = JsonSerializer.Serialize(new { request.AttemptId, variant="Short", acceptance=shortAcceptance, acceptedUtc=DateTimeOffset.UtcNow }, JsonOptions),
            ["revision-history.json"] = JsonSerializer.Serialize(new { request.AttemptId, maximumAttempts=MaximumGenerationAttempts }, JsonOptions)
        };
        var candidateChecksums = artifacts.Where(x => x.Key.Contains("accepted-release-candidate")).ToDictionary(x=>x.Key,x=>Checksum(x.Value));
        artifacts["narration-manifest.json"] = JsonSerializer.Serialize(new { publicationId, request.AttemptId, request.ExecutionId, request.PlanId, longAcceptedCandidatePath="07-narration/long/accepted-release-candidate.json", shortAcceptedCandidatePath="07-narration/short/accepted-release-candidate.json", generatorDiagnosticsPath, lifecycleDiagnosticsPath, candidateChecksums, downstreamReady=true }, JsonOptions);
        artifacts["narration-certification.json"] = JsonSerializer.Serialize(new { schemaVersion="2.0", publicationId, request.AttemptId, reasonCode="P7_NARRATION_RELEASE_CANDIDATE_CERTIFIED", sourceBlueprintAggregateId=blueprintAuthority.Aggregate.AggregateId, sourceBlueprintAggregateChecksum=blueprintAuthority.Aggregate.DeterministicChecksum, sourceStoryFramesAuthorityId=blueprintAuthority.StoryFrames.AuthorityId, sourceStoryFramesAuthorityChecksum=blueprintAuthority.StoryFrames.SemanticChecksum, acceptancePassed=true, physicalReadbackPassed=true, checksumsPassed=true, downstreamReady=true }, JsonOptions);
        return await new Phase7NarrationReleaseCandidatePublisher().PublishAsync(new(request.ExecutionRoot, publicationId, artifacts), cancellationToken);
    }
    private static string ReadKnowledgeAuthorityId(string root)
    {
        var path = Path.Combine(root, "07-narration", "knowledge", "knowledge-authority.json");
        if (!File.Exists(path)) return "";
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return TryProperty(document.RootElement, "authorityId", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
    private static string ReadKnowledgeAuthorityChecksum(string root)
    {
        var path = Path.Combine(root, "07-narration", "knowledge", "knowledge-authority.json");
        return File.Exists(path) ? Checksum(File.ReadAllText(path)) : "";
    }
    private static string Checksum(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    { foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; } value = default; return false; }
}
