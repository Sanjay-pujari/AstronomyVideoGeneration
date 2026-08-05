using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7NarrationAuthorityOrchestrator(
    IPhase7KnowledgeService knowledgeService,
    IPhase7KnowledgeCommittedStateEvaluator knowledgeCommittedStateEvaluator,
    IPhase7ScenePacketInputAuthorityEvaluator packetInputEvaluator,
    IPhase7SceneKnowledgePacketBuilder packetBuilder,
    IPhase7SceneKnowledgePacketValidator packetValidator,
    IPhase7NarrationPlanningInputAuthorityEvaluator planningInputEvaluator,
    INarrationPlanningAuthorityBuilder planningBuilder,
    INarrationPlanningValidator planningValidator,
    IPhase7NarrationPlanningPublicationService planningPublicationService,
    IPhase7NarrationPlanningCommittedStateEvaluator planningCommittedStateEvaluator,
    IPhase7NarrationDraftAuthorityService draftAuthorityService) : IPhase7NarrationAuthorityOrchestrator
{
    public async Task<Phase7NarrationAuthorityOrchestrationResult> ExecuteAsync(
        Phase7NarrationAuthorityOrchestrationRequest request, CancellationToken token = default)
    {
        var stages = new List<Phase7AuthorityStageResult>();
        var files = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var blockers = new List<string>();
        string? last = null;
        string? failed = null;
        string? planningId = null;
        string? planningChecksum = null;
        string? draftId = null;
        string? draftChecksum = null;
        int longDraft = 0, shortDraft = 0;
        string? draftValidationReason = null;
        IReadOnlyList<NarrationDraftValidationGate> draftGates = [];

        Phase7AuthorityStageResult Add(Phase7AuthorityStageResult stage)
        {
            stages.Add(stage);
            files.AddRange(stage.OutputFiles);
            warnings.AddRange(stage.Warnings);
            errors.AddRange(stage.Errors);
            blockers.AddRange(stage.BlockingIssues);
            if (stage.Success) last = stage.StageCode; else failed = stage.StageCode;
            return stage;
        }
        Phase7NarrationAuthorityOrchestrationResult Finish(bool success) => new(success, request.ExecutionId, request.PlanId,
            request.EventId, request.Language, request.ProfileId, request.ProfileVersion, stages, last, failed,
            files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.Ordinal).ToArray(),
            errors.Distinct(StringComparer.Ordinal).ToArray(), blockers.Distinct(StringComparer.Ordinal).ToArray(),
            new(0,0,0,0,0,0,0), planningId, planningChecksum, draftId, draftChecksum, longDraft, shortDraft,
            draftValidationReason, draftGates);

        var knowledgeRequest = new Phase7InputAuthorityRequest(request.ExecutionRoot, request.ExecutionId, request.PlanId,
            request.EventId, request.Language, request.ProfileId, request.RequestedVariants)
        { ExpectedProfileVersion = request.ProfileVersion, EventType = request.EventType, ContentCategory = request.ContentCategory,
          CanonicalProfileIdentity = request.CanonicalProfileIdentity };
        var k = await knowledgeService.ExecuteAsync(knowledgeRequest, request.OverwriteExisting, token);
        Add(new("KnowledgeAuthority", "P7.1A Knowledge Authority", k.IsValid, k.AlreadyPublished ? "Reused" : k.IsValid ? "Succeeded" : "Failed",
            k.ReasonCode, k.AlreadyPublished, k.PublicationCommitted, k.CommittedStateValidationPassed, k.IsValid ? KnowledgeOutputs(request.ExecutionRoot) : [], k.Warnings, k.Errors, k.Errors));
        if (!k.IsValid) return Finish(false);

        var knowledgeCommitted = await knowledgeCommittedStateEvaluator.EvaluateAsync(new(request.ExecutionRoot, request.ExecutionId,
            request.PlanId, request.EventId, request.Language), token);
        if (!knowledgeCommitted.IsValid || knowledgeCommitted.Authority is null)
        {
            Add(new("KnowledgeCommittedState", "P7.1A Committed-State Evaluation", false, "Failed",
                knowledgeCommitted.ReasonCode, false, false, false, [], knowledgeCommitted.Warnings, knowledgeCommitted.Errors, knowledgeCommitted.Errors));
            return Finish(false);
        }

        var packetInput = await packetInputEvaluator.EvaluateAsync(new(request.ExecutionRoot, request.ExecutionId, request.PlanId,
            request.EventId, request.Language, request.ProfileId, request.ProfileVersion), token);
        if (!packetInput.IsValid || packetInput.Authority is null)
        {
            Add(new("SceneKnowledgePackets", "P7.1B-A Scene Knowledge Packets", false, "Failed", packetInput.ReasonCode,
                false, false, false, [], packetInput.Warnings, packetInput.Errors, packetInput.Errors));
            return Finish(false);
        }
        var longPackets = packetBuilder.Build(packetInput.Authority, "Long");
        var shortPackets = packetBuilder.Build(packetInput.Authority, "Short");
        var packetValidation = packetValidator.Validate(packetInput.Authority, longPackets, shortPackets);
        var packetCollection = new SceneKnowledgePacketCollection(longPackets, shortPackets, "");
        packetCollection = packetCollection with { DeterministicChecksum = NarrationPlanningCanonicalizer.PacketCollectionChecksum(packetCollection) };
        Add(new("SceneKnowledgePackets", "P7.1B-A Scene Knowledge Packets", packetValidation.IsValid, packetValidation.IsValid ? "Valid" : "Failed",
            packetValidation.ReasonCode, false, false, false, [], packetInput.Warnings, packetValidation.Errors, packetValidation.Errors)
        { LongCount = longPackets.Count, ShortCount = shortPackets.Count, FailedGateCount = packetValidation.Gates.Count(g => !g.Passed), PassedGateCount = packetValidation.Gates.Count(g => g.Passed) });
        if (!packetValidation.IsValid) return Finish(false);

        var planningRequest = new Phase7NarrationPlanningInputAuthorityRequest(request.ExecutionRoot, request.ExecutionId, request.PlanId,
            request.EventId, request.Language, request.ProfileId, request.ProfileVersion, packetCollection, packetValidation);
        var planningInput = await planningInputEvaluator.EvaluateAsync(planningRequest, token);
        if (!planningInput.IsValid || planningInput.Authority is null)
        {
            Add(new("NarrationPlanningAuthority", "P7.1B-BA Narration Planning Authority", false, "Failed", planningInput.ReasonCode,
                false, false, false, [], planningInput.Warnings, planningInput.Errors, planningInput.Errors));
            return Finish(false);
        }
        var builtPlanning = planningBuilder.Build(planningInput.Authority);
        var planningValidation = builtPlanning.Authority is null ? null : planningValidator.Validate(planningInput.Authority, builtPlanning.Authority);
        var planningSuccess = builtPlanning.IsValid && builtPlanning.Authority is not null && planningValidation is { IsValid: true };
        Add(new("NarrationPlanningAuthority", "P7.1B-BA Narration Planning Authority", planningSuccess, planningSuccess ? "Valid" : "Failed",
            planningSuccess ? planningValidation!.ReasonCode : builtPlanning.ReasonCode, false, false, false, [],
            planningInput.Warnings.Concat(builtPlanning.Warnings).ToArray(), builtPlanning.Errors.Concat(planningValidation?.Errors ?? []).ToArray(), builtPlanning.BlockingIssues)
        { LongCount = builtPlanning.Authority?.LongScenes.Count ?? 0, ShortCount = builtPlanning.Authority?.ShortScenes.Count ?? 0, FailedGateCount = planningValidation?.Gates.Count(g => !g.Passed) ?? 0, PassedGateCount = planningValidation?.Gates.Count(g => g.Passed) ?? 0 });
        if (!planningSuccess) return Finish(false);

        var pub = await planningPublicationService.ExecuteAsync(new(planningRequest, request.OverwriteExisting, request.RetryFailedOnly), token);
        planningId = pub.AuthorityId; planningChecksum = pub.AuthorityChecksum;
        Add(new("NarrationPlanningPublication", "P7.1B-BB Narration Planning Publication", pub.Success, pub.Reused ? "Reused" : pub.Success ? "Committed" : "Failed",
            pub.ReasonCode, pub.Reused, pub.PublicationCommitted, pub.CommittedStateValidationPassed, pub.ArtifactPaths, pub.Warnings, pub.Errors, pub.Errors)
        { LongCount = pub.LongPlanningSceneCount, ShortCount = pub.ShortPlanningSceneCount });
        if (!pub.Success) return Finish(false);
        var planningCommitted = await planningCommittedStateEvaluator.EvaluateAsync(planningRequest, token);
        if (!planningCommitted.IsValid || planningCommitted.Authority is null)
        {
            Add(new("NarrationPlanningCommittedState", "P7.1B-BB Committed-State Evaluation", false, "Failed", planningCommitted.ReasonCode,
                false, false, false, [], planningCommitted.Warnings, planningCommitted.Errors, planningCommitted.Errors));
            return Finish(false);
        }

        var draft = await draftAuthorityService.ExecuteAsync(new(planningRequest,
            new(request.ExecutionRoot, request.ExecutionId, request.PlanId, request.EventId, request.Language)), token);
        draftId = draft.Authority?.AuthorityId; draftChecksum = draft.Authority?.DeterministicChecksum;
        longDraft = draft.Authority?.LongScenes.Count ?? 0; shortDraft = draft.Authority?.ShortScenes.Count ?? 0;
        draftValidationReason = draft.ValidationReason; draftGates = draft.Validation?.Gates ?? [];
        Add(new("NarrationDraftAuthority", "P7.1C-A Narration Draft Authority", draft.Success, draft.Success ? "Valid" : "Failed",
            draft.Success ? draft.ValidationReason : FirstFailure(draft), false, false, false, [], draft.Warnings, draft.Errors, draft.BlockingIssues)
        { LongCount = longDraft, ShortCount = shortDraft, FailedGateCount = draftGates.Count(g => !g.Passed), PassedGateCount = draftGates.Count(g => g.Passed) });
        return Finish(draft.Success);
    }

    private static string FirstFailure(Phase7NarrationDraftAuthorityServiceResult r) =>
        r.InputEvaluationReason != NarrationDraftReasonCodes.InputValid ? r.InputEvaluationReason :
        r.BuildReason != NarrationDraftReasonCodes.AuthorityValid ? r.BuildReason : r.ValidationReason;

    private static IReadOnlyList<string> KnowledgeOutputs(string root) =>
    [
        Path.Combine(root,"07-narration","knowledge","knowledge-authority.json"),
        Path.Combine(root,"07-narration","knowledge","knowledge-resolution-report.json"),
        Path.Combine(root,"07-narration","knowledge","knowledge-diagnostics.json"),
        Path.Combine(root,"validation","phase-07-knowledge-validation.json"),
        Path.Combine(root,"phase-manifest.json"),
        Path.Combine(root,".phase-07-knowledge-publication.json")
    ];
}
