using System.Diagnostics;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DocumentaryBlueprintCertificationIntegrationService(DocumentaryProductionCertifier certifier,
    DocumentaryBlueprintEditorialValidator editorialValidator,
    DocumentaryBlueprintCoverageEvaluator coverageEvaluator,
    DocumentaryBlueprintTransitionEvaluator transitionEvaluator,
    DocumentaryBlueprintPauseTestEvaluator pauseTestEvaluator)
    : IDocumentaryBlueprintCertificationIntegrationService
{
    public const string Version = "1.0";

    public Task<DocumentaryBlueprintCertificationIntegrationResult> CertifyAsync(DocumentaryBlueprintCertificationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        var aggregate = request.PublishedAggregate ?? throw new InvalidOperationException("Phase 5 requires PublishedDocumentaryBlueprintAggregate.");
        var variants = new[] { aggregate.LongVariant, aggregate.ShortVariant };
        var coverageResults = variants.Select(coverageEvaluator.Evaluate).ToArray();
        var transitionResults = variants.Select(transitionEvaluator.Evaluate).ToArray();
        var pauseResults = variants.SelectMany(v => pauseTestEvaluator.Evaluate(v, transitionResults.Single(t => t.Variant == v.Variant))).ToArray();
        // The existing production certifier remains the single substantive certification authority.
        var certification = certifier.Certify(request);
        var phase5Blocking = coverageResults.Where(x => !x.IsValid).SelectMany(x => x.Issues)
            .Concat(transitionResults.Where(x => !x.IsValid).SelectMany(x => x.Issues))
            .Concat(pauseResults.Where(x => !x.Passed).SelectMany(x => x.Issues)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (phase5Blocking.Length > 0)
        {
            var blocking = certification.BlockingIssues.Concat(phase5Blocking).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            certification = certification with { CertificationStatus = DocumentaryBlueprintCertificationStatus.Rejected, Passed = false,
                BlockingIssues = blocking, CertifiedVariants = [], RejectedVariants = request.RequestedVariants,
                SceneLevelOutcomes = certification.SceneLevelOutcomes.Select(x => x with { Certified = false }).ToArray(), SemanticChecksum = string.Empty };
            certification = certification with { SemanticChecksum = DocumentaryBlueprintCertificationChecksum.Calculate(certification) };
        }
        var masterScenes = request.Master.Blueprint.Scenes.OrderBy(x => x.SceneNumber).ToArray();
        var contract = new DocumentaryBlueprintEditorialContract($"{certification.CertificationId}.editorial-contract", request.ExecutionId, request.EventId, request.Language, request.Profile,
            certification.CertificationId, certification.SemanticChecksum, certification.SourcePhase4Checksum, certification.CertifiedVariants,
            certification.SceneLevelOutcomes.Where(x => x.Certified).Select(x => x.SceneId).Distinct(StringComparer.Ordinal).ToArray(),
            masterScenes.Select(x => x.SceneId).ToArray(), masterScenes.ToDictionary(x => x.SceneId, x => x.NarrativeStage.ToString()), masterScenes.ToDictionary(x => x.SceneId, x => x.SceneRole.ToString()),
            masterScenes.Select(x => x.ViewerQuestion.Text).ToArray(), request.Master.Coverage.CoveredLearningObjectiveIds,
            masterScenes.SelectMany(x => x.KnowledgeReferences).Select(x => x.KnowledgeEntryId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), request.Master.Coverage.DeferralReasons,
            certification.NonBlockingWarnings, certification.BlockingIssues, ["Consume only certified variants and scene IDs."], certification.Passed, certification.Passed, DateTimeOffset.UtcNow, string.Empty);
        contract = contract with { Checksum = DocumentaryBlueprintCertificationChecksum.Calculate(contract) };
        timer.Stop();
        var diagnostics = new DocumentaryBlueprintCertificationDiagnostics(request.ExecutionId, nameof(DocumentaryProductionCertifier), certification.CertificationVersion,
            nameof(DocumentaryBlueprintCertificationIntegrationService), Version,
            ["04-blueprint/documentary-blueprint.json", "04-blueprint/documentary-blueprint.long.json", "04-blueprint/documentary-blueprint.short.json", "04-blueprint/blueprint-build-report.json"],
            new Dictionary<string, string> { ["master"] = request.Master.Metadata.Checksum, ["long"] = request.Long.Metadata.Checksum, ["short"] = request.Short.Metadata.Checksum },
            new Dictionary<string, int> { ["master"] = request.Master.Blueprint.Scenes.Count, ["long"] = request.Long.Blueprint.Scenes.Count, ["short"] = request.Short.Blueprint.Scenes.Count },
            request.Master.Coverage.CoveredViewerQuestionIds.Count, certification.SceneLevelOutcomes.Count(x => x.Certified), certification.SceneLevelOutcomes.Count(x => !x.Certified),
            certification.BlockingIssues.Count, certification.NonBlockingWarnings.Count, certification.CertifiedVariants, certification.RejectedVariants,
            ["Phase4Authority", "ProductionCertification", "EditorialContract", "CompleteSet"], timer.ElapsedMilliseconds, certification.SourcePhase4Checksum);
        const string version = "1.0";
        var common = (aggregate.ExecutionId, aggregate.PlanId, aggregate.EventId, aggregate.Language, aggregate.ProfileId,
            aggregate.AggregateId, aggregate.DeterministicChecksum, aggregate.LongVariant.DeterministicChecksum, aggregate.ShortVariant.DeterministicChecksum);
        var coverage = new BlueprintCoverageReport(common.ExecutionId, common.PlanId, common.EventId, common.Language, common.ProfileId,
            common.AggregateId, common.DeterministicChecksum, common.Item7, common.Item8, version, coverageResults, coverageResults.All(x => x.IsValid), string.Empty);
        coverage = coverage with { SemanticChecksum = Phase5SemanticChecksum.Calculate(coverage with { SemanticChecksum = string.Empty }) };
        var transitions = new BlueprintTransitionReport(common.ExecutionId, common.PlanId, common.EventId, common.Language, common.ProfileId,
            common.AggregateId, common.DeterministicChecksum, common.Item7, common.Item8, version, transitionResults, transitionResults.All(x => x.IsValid), string.Empty);
        transitions = transitions with { SemanticChecksum = Phase5SemanticChecksum.Calculate(transitions with { SemanticChecksum = string.Empty }) };
        var pause = new BlueprintPauseTestReport(common.ExecutionId, common.PlanId, common.EventId, common.Language, common.ProfileId,
            common.AggregateId, common.DeterministicChecksum, common.Item7, common.Item8, version, pauseResults, pauseResults.Count(x => x.Passed), pauseResults.Count(x => !x.Passed), pauseResults.All(x => x.Passed), string.Empty);
        pause = pause with { SemanticChecksum = Phase5SemanticChecksum.Calculate(pause with { SemanticChecksum = string.Empty }) };
        var intents = variants.SelectMany(v => v.Blueprint.Scenes.OrderBy(x => x.SceneNumber).Select(s => {
            var trace = v.SceneTraceability.Single(t => t.SceneId == s.SceneId);
            return new BlueprintSceneIntent(v.Variant, s.SceneId, s.SceneNumber, s.NarrativeStage, s.SceneRole, trace.PrimaryViewerQuestionId,
                trace.LearningObjectiveId, s.EditorialOutcome, s.KnowledgeReferences.Select(k => k.KnowledgeEntryId).ToArray(), s.EstimatedDurationSeconds,
                s.Transition, aggregate.AggregateId, aggregate.DeterministicChecksum, v.DeterministicChecksum); })).ToArray();
        var sceneIntents = new BlueprintSceneIntentProjection(common.ExecutionId, common.PlanId, common.EventId, common.Language, common.ProfileId,
            common.AggregateId, common.DeterministicChecksum, common.Item7, common.Item8, version, intents, string.Empty);
        sceneIntents = sceneIntents with { SemanticChecksum = Phase5SemanticChecksum.Calculate(sceneIntents with { SemanticChecksum = string.Empty }) };
        var validations = variants.Select(v => { var findings = editorialValidator.Validate(v.Blueprint).Findings;
            return new BlueprintVariantValidation(v.Variant, v.Blueprint.Scenes.Count, v.TotalAllocatedDurationSeconds, findings.All(f => f.Severity != DocumentaryBlueprintValidationSeverity.Error),
                v.Blueprint.Scenes.Select(s => s.SceneId).Distinct(StringComparer.Ordinal).Count() == v.Blueprint.Scenes.Count,
                v.Blueprint.Scenes.Select(s => s.SceneNumber).SequenceEqual(Enumerable.Range(1, v.Blueprint.Scenes.Count)), v.ActualSceneCount == v.ExpectedSceneCount,
                v.SceneTraceability.All(t => !string.IsNullOrWhiteSpace(t.PrimaryViewerQuestionId)), v.SceneTraceability.All(t => !string.IsNullOrWhiteSpace(t.LearningObjectiveId)),
                v.SceneTraceability.All(t => t.KnowledgeSelections.Count > 0), findings, findings.All(f => f.Severity != DocumentaryBlueprintValidationSeverity.Error)); }).ToArray();
        var validation = new BlueprintValidationReport(common.ExecutionId, common.PlanId, common.EventId, common.Language, common.ProfileId,
            common.AggregateId, common.DeterministicChecksum, common.Item7, common.Item8, version, validations,
            aggregate.LongVariant.DeterministicChecksum != aggregate.ShortVariant.DeterministicChecksum, coverage.IsValid, transitions.IsValid, pause.IsValid,
            certification.BlockingIssues, certification.NonBlockingWarnings, certification.Passed && coverage.IsValid && transitions.IsValid && pause.IsValid, string.Empty);
        validation = validation with { SemanticChecksum = Phase5SemanticChecksum.Calculate(validation with { SemanticChecksum = string.Empty }) };
        return Task.FromResult(new DocumentaryBlueprintCertificationIntegrationResult(certification, contract, diagnostics, validation, sceneIntents, coverage, transitions, pause));
    }
}
