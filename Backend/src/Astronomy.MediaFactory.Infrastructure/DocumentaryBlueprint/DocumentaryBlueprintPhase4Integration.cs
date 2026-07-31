using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public static class Phase4AuthorityPaths
{
    public const string DirectoryName = "04-blueprint";
    public const string CanonicalFileName = "documentary-blueprint.json";
}

public static class Phase4IntegrationReasonCodes
{
    public const string RequestInvalid = "P4INT_REQUEST_INVALID";
    public const string Phase2AuthorityInvalid = "P4INT_PHASE2_AUTHORITY_INVALID";
    public const string Phase3AuthorityInvalid = "P4INT_PHASE3_AUTHORITY_INVALID";
    public const string ProfileResolutionFailed = "P4INT_PROFILE_RESOLUTION_FAILED";
    public const string IntentPlanningFailed = "P4INT_INTENT_PLANNING_FAILED";
    public const string ProjectionFailed = "P4INT_PROJECTION_FAILED";
    public const string PublicationFailed = "P4INT_PUBLICATION_FAILED";
    public const string PublishedAuthorityInvalid = "P4INT_PUBLISHED_AUTHORITY_INVALID";
}

public sealed record DocumentaryBlueprintPhase4IntegrationRequest(
    string ExecutionRoot, string ExecutionId, string PlanId, string EventId, string Language,
    string ProfileId, string FamilyCode, string AudienceCode,
    ProductionEventIntelligence Phase2Authority, ViewerQuestionBank QuestionBank,
    ViewerLearningObjectives LearningObjectives, ViewerQuestionPlan QuestionPlan,
    IReadOnlyList<CertifiedDocumentaryKnowledgeReference> CertifiedKnowledge,
    DocumentarySourceLineage SourceLineage,
    Phase4ChecksumSnapshot ExpectedPhase1ChecksumSnapshot,
    Phase4ChecksumSnapshot ExpectedPhase2ChecksumSnapshot,
    Phase4ChecksumSnapshot ExpectedPhase3ChecksumSnapshot,
    JsonNode? ExistingManifest, bool CompatibilityProjectionRequired,
    Phase4PublicationPolicy PublicationPolicy, string AudienceIntent, string DocumentaryGoal,
    string? CorrelationId = null);

public sealed record DocumentaryBlueprintPhase4IntegrationResult(
    bool Success, string ExecutionId, string PlanId, string EventId, string Language,
    bool IntentPlanningSucceeded, bool ProjectionSucceeded, bool PublicationSucceeded,
    bool AlreadyPublished, string? IntentId, string? IntentChecksum, string? AggregateId,
    string? AggregateChecksum, int LongSceneCount, int ShortSceneCount,
    int LongDurationSeconds, int ShortDurationSeconds,
    Phase4DocumentaryBlueprintPublicationResult? PublicationResult,
    DocumentaryBlueprintAggregate? PublishedAuthority,
    IReadOnlyList<Phase4PublicationDiagnostic> Errors,
    IReadOnlyList<Phase4PublicationDiagnostic> Warnings,
    IReadOnlyList<string> Evidence);

public interface IDocumentaryBlueprintPhase4IntegrationService
{
    Task<DocumentaryBlueprintPhase4IntegrationResult> ExecuteAsync(
        DocumentaryBlueprintPhase4IntegrationRequest request, CancellationToken cancellationToken = default);
}

public interface IPhase4DocumentaryBlueprintAuthorityReader
{
    Task<DocumentaryBlueprintAggregate> ReadAsync(string executionRoot, CancellationToken cancellationToken = default);
}

public sealed class Phase4DocumentaryBlueprintAuthorityReader(
    IPhase4ArtifactSerializer serializer, IPhase4CommittedStateValidator committedStateValidator)
    : IPhase4DocumentaryBlueprintAuthorityReader
{
    public async Task<DocumentaryBlueprintAggregate> ReadAsync(string executionRoot, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(executionRoot, Phase4AuthorityPaths.DirectoryName, Phase4AuthorityPaths.CanonicalFileName);
        if (!File.Exists(path)) throw new InvalidDataException("Published Phase 4 authority is missing.");
        var aggregate = serializer.Deserialize<DocumentaryBlueprintAggregate>(await File.ReadAllBytesAsync(path, cancellationToken));
        if (!DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(aggregate) ||
            !DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(aggregate.LongVariant) ||
            !DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(aggregate.ShortVariant))
            throw new InvalidDataException("Published Phase 4 authority checksum is invalid.");
        var errors = await committedStateValidator.ValidateAsync(executionRoot, aggregate, cancellationToken);
        if (errors.Count != 0) throw new InvalidDataException(string.Join("; ", errors.Select(x => $"{x.Code}: {x.Message}")));
        return aggregate;
    }
}

public sealed class DocumentaryBlueprintPhase4IntegrationService(
    IDocumentaryIntentPlanner intentPlanner,
    IDocumentaryBlueprintProfileResolver profileResolver,
    IDocumentaryBlueprintProjector projector,
    IPhase4DocumentaryBlueprintPublicationService publicationService,
    IPhase4DocumentaryBlueprintAuthorityReader authorityReader,
    ILogger<DocumentaryBlueprintPhase4IntegrationService> logger)
    : IDocumentaryBlueprintPhase4IntegrationService
{
    public async Task<DocumentaryBlueprintPhase4IntegrationResult> ExecuteAsync(DocumentaryBlueprintPhase4IntegrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        logger.LogInformation("Phase4IntegrationStarted ExecutionId={ExecutionId} PlanId={PlanId} EventId={EventId} Language={Language}", request.ExecutionId, request.PlanId, request.EventId, request.Language);
        DocumentaryIntentPlanningResult? planned = null;
        DocumentaryBlueprintProjectionResult? projected = null;
        Phase4DocumentaryBlueprintPublicationResult? published = null;
        try
        {
            var identityInvalid = new[] { request.ExecutionRoot, request.ExecutionId, request.PlanId, request.EventId, request.Language }.Any(string.IsNullOrWhiteSpace);
            if (identityInvalid) return Failure(Phase4IntegrationReasonCodes.RequestInvalid, "Phase 4 integration identity is incomplete.");
            if (request.ExpectedPhase2ChecksumSnapshot.Files.Count == 0 || request.Phase2Authority is null)
                return Failure(Phase4IntegrationReasonCodes.Phase2AuthorityInvalid, "Frozen Phase 2 authority is incomplete.");
            if (request.ExpectedPhase3ChecksumSnapshot.Files.Count == 0 || request.QuestionBank.Metadata.ExecutionId != request.ExecutionId || request.QuestionBank.Metadata.Language != request.Language)
                return Failure(Phase4IntegrationReasonCodes.Phase3AuthorityInvalid, "Frozen Phase 3 authority identity is invalid.");

            var profile = profileResolver.Resolve(request.ProfileId, request.FamilyCode, request.AudienceCode);
            if (profile is null) return Failure(Phase4IntegrationReasonCodes.ProfileResolutionFailed, "Canonical documentary blueprint profile was not resolved.");
            logger.LogInformation("Phase4ProfileResolved ExecutionId={ExecutionId} ProfileId={ProfileId}", request.ExecutionId, profile.ProfileId);

            planned = intentPlanner.Plan(new(request.ExecutionId, request.PlanId, request.EventId, request.Language,
                request.SourceLineage, request.QuestionBank, request.LearningObjectives, request.QuestionPlan,
                profile, request.CertifiedKnowledge, request.AudienceIntent, request.DocumentaryGoal));
            if (!planned.Success || planned.Intent is null)
                return Failure(Phase4IntegrationReasonCodes.IntentPlanningFailed, "Certified intent planning failed.",
                    planned.Errors.Select(x => new Phase4PublicationDiagnostic(x.Code, x.Message))
                        .Concat(planned.CandidateDiagnostics.Select(x => new Phase4PublicationDiagnostic(
                            "DI_CANDIDATE_REJECTED",
                            $"Variant={x.Variant}; QuestionId={x.QuestionId}; RejectionReasons={string.Join(',', x.RejectionReasons)}",
                            x.SlotId)))
                        .ToArray());
            logger.LogInformation("Phase4IntentPlanned ExecutionId={ExecutionId} IntentId={IntentId} IntentChecksum={IntentChecksum}", request.ExecutionId, planned.Intent.IntentId, planned.Intent.DeterministicChecksum);

            projected = projector.Project(new(planned.Intent, profile));
            if (!projected.Success || projected.Aggregate is null)
                return Failure(Phase4IntegrationReasonCodes.ProjectionFailed, "Certified blueprint projection failed.", projected.Errors.Select(x => new Phase4PublicationDiagnostic(x.Code, x.Message)).ToArray());
            logger.LogInformation("Phase4ProjectionCompleted ExecutionId={ExecutionId} AggregateId={AggregateId} AggregateChecksum={AggregateChecksum} LongSceneCount={LongSceneCount} ShortSceneCount={ShortSceneCount}", request.ExecutionId, projected.Aggregate.AggregateId, projected.Aggregate.DeterministicChecksum, projected.LongSceneCount, projected.ShortSceneCount);

            logger.LogInformation("Phase4PublicationStarted ExecutionId={ExecutionId} AggregateId={AggregateId}", request.ExecutionId, projected.Aggregate.AggregateId);
            published = await publicationService.PublishAsync(new(request.ExecutionRoot, request.ExecutionId, request.PlanId,
                request.EventId, request.Language, projected, request.ExpectedPhase1ChecksumSnapshot,
                request.ExpectedPhase2ChecksumSnapshot, request.ExpectedPhase3ChecksumSnapshot, request.ExistingManifest,
                request.CompatibilityProjectionRequired, request.PublicationPolicy), cancellationToken);
            if (!published.Success)
                return Failure(Phase4IntegrationReasonCodes.PublicationFailed, "Atomic Phase 4 publication failed.", published.Errors);
            var already = published.Warnings.Any(x => x.Code == "P4PUB_ALREADY_PUBLISHED");
            logger.LogInformation(already ? "Phase4PublicationAlreadyExists ExecutionId={ExecutionId} TransactionId={TransactionId}" : "Phase4PublicationCommitted ExecutionId={ExecutionId} TransactionId={TransactionId}", request.ExecutionId, published.TransactionId);

            DocumentaryBlueprintAggregate authority;
            try { authority = await authorityReader.ReadAsync(request.ExecutionRoot, cancellationToken); }
            catch (Exception ex) { return Failure(Phase4IntegrationReasonCodes.PublishedAuthorityInvalid, ex.Message); }
            if (authority.AggregateId != published.AggregateId || authority.DeterministicChecksum != published.AggregateChecksum ||
                authority.ExecutionId != request.ExecutionId || authority.PlanId != request.PlanId || authority.EventId != request.EventId ||
                authority.Language != request.Language || authority.ProfileId != profile.ProfileId || authority.ProfileVersion != profile.ProfileVersion)
                return Failure(Phase4IntegrationReasonCodes.PublishedAuthorityInvalid, "Published Phase 4 authority identity differs from the committed publication.");
            logger.LogInformation("Phase4AuthorityReadBackValidated ExecutionId={ExecutionId} AggregateId={AggregateId} AggregateChecksum={AggregateChecksum}", request.ExecutionId, authority.AggregateId, authority.DeterministicChecksum);
            logger.LogInformation("Phase4IntegrationCompleted ExecutionId={ExecutionId} LongSceneCount={LongSceneCount} ShortSceneCount={ShortSceneCount}", request.ExecutionId, authority.LongVariant.ActualSceneCount, authority.ShortVariant.ActualSceneCount);
            return new(true, request.ExecutionId, request.PlanId, request.EventId, request.Language, true, true, true, already,
                authority.SourceIntentId, authority.SourceIntentChecksum, authority.AggregateId, authority.DeterministicChecksum,
                authority.LongVariant.ActualSceneCount, authority.ShortVariant.ActualSceneCount,
                authority.AggregateDurationSummary.LongDurationSeconds, authority.AggregateDurationSummary.ShortDurationSeconds,
                published, authority, [], published.Warnings, ["Physical authority read-back and committed-state validation passed."]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(Phase4IntegrationReasonCodes.RequestInvalid, ex.Message);
        }

        DocumentaryBlueprintPhase4IntegrationResult Failure(string code, string message, IReadOnlyList<Phase4PublicationDiagnostic>? details = null)
        {
            logger.LogWarning("Phase4IntegrationFailed ExecutionId={ExecutionId} ReasonCode={ReasonCode}", request.ExecutionId, code);
            var errors = new[] { new Phase4PublicationDiagnostic(code, message) }.Concat(details ?? []).ToArray();
            return new(false, request.ExecutionId, request.PlanId, request.EventId, request.Language, planned?.Success == true,
                projected?.Success == true, published?.Success == true, false, planned?.Intent?.IntentId,
                planned?.Intent?.DeterministicChecksum, projected?.Aggregate?.AggregateId, projected?.Aggregate?.DeterministicChecksum,
                projected?.LongSceneCount ?? 0, projected?.ShortSceneCount ?? 0, projected?.LongDurationSeconds ?? 0,
                projected?.ShortDurationSeconds ?? 0, published, null, errors, published?.Warnings ?? [], []);
        }
    }
}
