using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Canonical semantic identity for Phase 4 authority; deliberately excludes timestamps and diagnostics.</summary>
public static class DocumentaryBlueprintChecksum
{
    public static bool HasValidChecksum(DocumentaryBlueprintArtifact artifact) =>
        string.Equals(artifact.Metadata.Checksum, Calculate(artifact), StringComparison.Ordinal);

    public static string Calculate(DocumentaryBlueprintArtifact artifact) => Calculate(artifact.Metadata.Variant, artifact.Blueprint, artifact.Coverage, artifact.Warnings);

    public static string Calculate(string variant, DocumentaryBlueprint blueprint, BlueprintCoverage coverage, IReadOnlyList<string>? warnings = null)
    {
        var semantic = new
        {
            Variant = variant,
            blueprint.BlueprintId,
            blueprint.KnowledgeId,
            blueprint.SubjectId,
            blueprint.SubjectName,
            blueprint.PublicationFormat,
            blueprint.PrimaryLanguage,
            blueprint.Version,
            Scenes = blueprint.Scenes.Select(s => new
            {
                s.SceneId, s.SceneNumber, s.Title, s.NarrativeStage, s.SceneRole, s.ViewerQuestion,
                s.SceneObjective, s.EditorialOutcome, s.EditorialPriority,
                KnowledgeReferences = s.KnowledgeReferences.OrderBy(k => k.KnowledgeEntryId, StringComparer.Ordinal).ThenBy(k => k.Section, StringComparer.Ordinal).ThenBy(k => k.Purpose, StringComparer.Ordinal),
                s.VisualOpportunities, s.Transition, s.EstimatedDurationSeconds
            }),
            CoveredQuestions = coverage.CoveredViewerQuestionIds.Order(StringComparer.Ordinal),
            DeferredQuestions = coverage.DeferredViewerQuestionIds.Order(StringComparer.Ordinal),
            UncoveredQuestions = coverage.UncoveredViewerQuestionIds.Order(StringComparer.Ordinal),
            CoveredObjectives = coverage.CoveredLearningObjectiveIds.Order(StringComparer.Ordinal),
            DeferredObjectives = coverage.DeferredLearningObjectiveIds.Order(StringComparer.Ordinal),
            SectionQuestions = coverage.SectionQuestionMap.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { x.Key, Values = x.Value.Order(StringComparer.Ordinal) }),
            SectionKnowledge = coverage.SectionKnowledgeMap.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { x.Key, Values = x.Value.OrderBy(v => v.ReferenceId, StringComparer.Ordinal).ThenBy(v => v.ReferenceType, StringComparer.Ordinal) }),
            Deferrals = coverage.DeferralReasons.OrderBy(x => x.Key, StringComparer.Ordinal),
            Warnings = (warnings ?? []).Order(StringComparer.Ordinal)
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(semantic)))).ToLowerInvariant();
    }
}
