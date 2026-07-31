using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Blueprint = Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint;
using BlueprintViewerQuestion = Astronomy.MediaFactory.Core.DocumentaryBlueprint.ViewerQuestion;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Thin Phase 4 adapter. Editorial construction remains owned by <see cref="DocumentaryBlueprintBuilder"/>.</summary>
public sealed class DocumentaryBlueprintIntegrationService(DocumentaryBlueprintBuilder builder) : IDocumentaryBlueprintIntegrationService
{
    public const string Version = "1.0";

    public Task<DocumentaryBlueprintIntegrationResult> BuildAsync(DocumentaryBlueprintIntegrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var attention = request.QuestionPlan.QuestionsRequiringEditorialAttention.ToHashSet(StringComparer.Ordinal);
        var accepted = request.QuestionBank.Questions.Where(q => !attention.Contains(q.QuestionId)).OrderBy(q => q.Order).ToArray();
        var highUncovered = request.QuestionBank.Questions.Where(q => q.Priority == "High" && attention.Contains(q.QuestionId)).Select(q => q.QuestionId).ToArray();
        if (highUncovered.Length > 0) throw new InvalidOperationException("Phase 4 cannot defer mandatory High-priority questions: " + string.Join(", ", highUncovered));
        if (accepted.Length == 0) throw new InvalidOperationException("Phase 4 requires at least one grounded Viewer Question.");

        var scenes = accepted.Select((q, index) => MapScene(q, index + 1, request)).ToArray();
        var blueprintId = Id("bp", request.Profile, request.Language, request.EventId, string.Join(',', accepted.Select(q => q.QuestionId).Order()));
        var metadata = new DocumentaryBlueprintMetadata(DateTimeOffset.UnixEpoch, nameof(DocumentaryBlueprintIntegrationService), Version,
            request.QuestionBank.Metadata.Version, "1.0", request.ExecutionId);
        var buildRequest = new DocumentaryBlueprintBuildRequest(blueprintId, request.QuestionBank.Metadata.Checksum,
            request.EventId, request.EventTitle, BlueprintPublicationFormat.LongDocumentary, request.Language, Version, metadata, scenes);
        var built = builder.Build(buildRequest); // The single authoritative builder invocation.

        var coverage = Coverage(request, accepted, scenes, attention);
        var longBlueprint = Project(built, BlueprintPublicationFormat.LongDocumentary, built.Scenes);
        var shortScenes = SelectShortArc(built.Scenes);
        var shortBlueprint = Project(built, BlueprintPublicationFormat.ShortDocumentary, shortScenes);
        var intelligenceChecksum = Hash(JsonSerializer.Serialize(request.ProductionIntelligence));
        var master = Artifact("Master", built, coverage, request, intelligenceChecksum);
        var longArtifact = Artifact("Long", longBlueprint, coverage, request, intelligenceChecksum);
        var shortIds = shortScenes.Select(s => s.SceneId).ToHashSet(StringComparer.Ordinal);
        var shortCoverage = coverage with { SectionQuestionMap = coverage.SectionQuestionMap.Where(x => shortIds.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value), SectionKnowledgeMap = coverage.SectionKnowledgeMap.Where(x => shortIds.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value) };
        var shortArtifact = Artifact("Short", shortBlueprint, shortCoverage, request, intelligenceChecksum);
        started.Stop();
        var diagnostics = new BlueprintBuildDiagnostics(builder.GetType().FullName!, Version, GetType().FullName!,
            ["plan-input/production-event-intelligence.json", "03-questions/viewer-question-bank.json", "03-questions/learning-objectives.json", "03-questions/question-plan.json"],
            new Dictionary<string, string> { ["viewerQuestionBank"] = request.QuestionBank.Metadata.Checksum, ["productionIntelligence"] = intelligenceChecksum },
            request.QuestionBank.Questions.Count, request.LearningObjectives.Objectives.Count, built.Scenes.Count, longBlueprint.Scenes.Count, shortBlueprint.Scenes.Count,
            coverage, scenes.Sum(s => s.KnowledgeReferences.Count), attention.Order().ToArray(), [], [], [],
            [request.Profile, request.Language, request.EventId, blueprintId], started.ElapsedMilliseconds);
        return Task.FromResult(new DocumentaryBlueprintIntegrationResult(master, longArtifact, shortArtifact, diagnostics));
    }

    private static DocumentarySceneBlueprintInput MapScene(Astronomy.MediaFactory.Core.ViewerQuestion q, int order, DocumentaryBlueprintIntegrationRequest request)
    {
        var role = q.Category switch { "Recognition" => DocumentarySceneRole.RecognitionGuide, "ScientificExplanation" => DocumentarySceneRole.ScientificExplanation, "ObservationGuidance" or "TimingGuidance" or "LocationGuidance" or "PracticalViewingAdvice" => DocumentarySceneRole.PracticalObservation, _ => order == 1 ? DocumentarySceneRole.OpeningHook : DocumentarySceneRole.CoreDiscovery };
        var stage = role switch { DocumentarySceneRole.ScientificExplanation => DocumentaryNarrativeStage.Science, DocumentarySceneRole.PracticalObservation => DocumentaryNarrativeStage.Observation, DocumentarySceneRole.RecognitionGuide => DocumentaryNarrativeStage.Recognition, _ => DocumentaryNarrativeStage.Discovery };
        var refs = q.KnowledgeReferences.Select(r => new KnowledgeReference(r.ReferenceId, r.ReferenceType, r.SourceArtifact, true)).ToArray();
        var objective = request.LearningObjectives.Objectives.FirstOrDefault(o => o.ViewerQuestionIds.Contains(q.QuestionId));
        return new DocumentarySceneBlueprintInput(Id("section", request.Profile, request.Language, request.EventId, q.Category, q.QuestionId, string.Join(',', refs.Select(r => r.KnowledgeEntryId).Order())), order,
            q.ExpectedLearningOutcome, stage, role, new BlueprintViewerQuestion(q.QuestionText),
            new SceneObjective(q.ExpectedLearningOutcome, objective?.Text ?? q.ExpectedLearningOutcome, q.QuestionText, "Sustain grounded curiosity"),
            new EditorialOutcome(q.ExpectedLearningOutcome, q.Category, true, true, true, role == DocumentarySceneRole.PracticalObservation, false),
            q.Priority == "High" ? EditorialPriority.Critical : EditorialPriority.Medium, refs, [],
            new SceneTransition("Advance the documentary arc", q.QuestionText, "Preserve evidence and viewer orientation"), role == DocumentarySceneRole.PracticalObservation ? 25 : 20);
    }

    private static BlueprintCoverage Coverage(DocumentaryBlueprintIntegrationRequest request, IReadOnlyList<Astronomy.MediaFactory.Core.ViewerQuestion> accepted, IReadOnlyList<DocumentarySceneBlueprintInput> scenes, HashSet<string> attention)
    {
        var acceptedIds = accepted.Select(q => q.QuestionId).ToHashSet(StringComparer.Ordinal);
        var map = scenes.Zip(accepted).ToDictionary(x => x.First.SceneId, x => (IReadOnlyList<string>)[x.Second.QuestionId]);
        var knowledge = scenes.Zip(accepted).ToDictionary(x => x.First.SceneId, x => x.Second.KnowledgeReferences);
        var coveredObjectives = request.LearningObjectives.Objectives.Where(o => o.ViewerQuestionIds.Any(acceptedIds.Contains)).Select(o => o.ObjectiveId).ToArray();
        var deferredObjectives = request.LearningObjectives.Objectives.Where(o => !coveredObjectives.Contains(o.ObjectiveId)).Select(o => o.ObjectiveId).ToArray();
        return new(acceptedIds.Order().ToArray(), attention.Order().ToArray(), [], coveredObjectives, deferredObjectives, map, knowledge,
            attention.ToDictionary(x => x, _ => "Requires editorial attention and was not promoted to a factual claim."));
    }

    private static IReadOnlyList<DocumentarySceneBlueprint> SelectShortArc(IReadOnlyList<DocumentarySceneBlueprint> scenes)
        => scenes.GroupBy(s => s.SceneRole is DocumentarySceneRole.PracticalObservation ? "observe" : s.SceneRole is DocumentarySceneRole.ScientificExplanation ? "explain" : s.SceneNumber == 1 ? "hook" : "close")
            .Select(g => g.First()).Take(4).Select((s, i) => new DocumentarySceneBlueprint(s.SceneId, i + 1, s.Title, s.NarrativeStage, s.SceneRole, s.ViewerQuestion, s.SceneObjective, s.EditorialOutcome, s.EditorialPriority, s.KnowledgeReferences, s.VisualOpportunities, s.Transition, s.EstimatedDurationSeconds)).ToArray();
    private static Blueprint Project(Blueprint b, BlueprintPublicationFormat format, IReadOnlyList<DocumentarySceneBlueprint> scenes)
        => new(Id("bp", b.BlueprintId, format.ToString()), b.KnowledgeId, b.SubjectId, b.SubjectName, format, b.PrimaryLanguage, b.Version, b.Metadata, scenes);
    private static DocumentaryBlueprintArtifact Artifact(string variant, Blueprint blueprint, BlueprintCoverage coverage, DocumentaryBlueprintIntegrationRequest r, string intelligenceChecksum)
    {
        var checksum = DocumentaryBlueprintChecksum.Calculate(variant, blueprint, coverage);
        return new(new(r.ExecutionId, r.EventId, r.Language, r.Profile, variant, Version, checksum, DateTimeOffset.UtcNow, r.QuestionBank.Metadata.Checksum, intelligenceChecksum), blueprint, coverage, []);
    }
    public static bool HasValidChecksum(DocumentaryBlueprintArtifact artifact)
        => DocumentaryBlueprintChecksum.HasValidChecksum(artifact);
    private static string Id(string prefix, params string[] values) => prefix + "-" + Hash(string.Join('|', values.Select(v => v.Trim().ToLowerInvariant())))[..16];
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
