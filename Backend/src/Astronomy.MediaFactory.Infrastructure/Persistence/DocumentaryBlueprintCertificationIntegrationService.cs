using System.Diagnostics;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DocumentaryBlueprintCertificationIntegrationService(DocumentaryProductionCertifier certifier)
    : IDocumentaryBlueprintCertificationIntegrationService
{
    public const string Version = "1.0";

    public Task<DocumentaryBlueprintCertificationIntegrationResult> CertifyAsync(DocumentaryBlueprintCertificationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        // The existing production certifier is the single substantive certification authority.
        var certification = certifier.Certify(request);
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
            ["04-blueprint/documentary-blueprint.json", "04-blueprint/documentary-blueprint.long.json", "04-blueprint/documentary-blueprint.short.json", "04-blueprint/blueprint-build-diagnostics.json"],
            new Dictionary<string, string> { ["master"] = request.Master.Metadata.Checksum, ["long"] = request.Long.Metadata.Checksum, ["short"] = request.Short.Metadata.Checksum },
            new Dictionary<string, int> { ["master"] = request.Master.Blueprint.Scenes.Count, ["long"] = request.Long.Blueprint.Scenes.Count, ["short"] = request.Short.Blueprint.Scenes.Count },
            request.Master.Coverage.CoveredViewerQuestionIds.Count, certification.SceneLevelOutcomes.Count(x => x.Certified), certification.SceneLevelOutcomes.Count(x => !x.Certified),
            certification.BlockingIssues.Count, certification.NonBlockingWarnings.Count, certification.CertifiedVariants, certification.RejectedVariants,
            ["Phase4Authority", "ProductionCertification", "EditorialContract", "CompleteSet"], timer.ElapsedMilliseconds, certification.SourcePhase4Checksum);
        return Task.FromResult(new DocumentaryBlueprintCertificationIntegrationResult(certification, contract, diagnostics));
    }
}
