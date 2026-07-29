using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryBlueprintCertificationChecksum
{
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();

    public static string Calculate(DocumentaryBlueprintCertification value) => Hash(new
    {
        value.CertificationId, value.ExecutionId, value.PlanId, value.EventId, value.Language, value.Profile,
        value.SourcePhase4Checksum, value.SourceMasterBlueprintChecksum, value.SourceLongBlueprintChecksum,
        value.SourceShortBlueprintChecksum, value.CertificationVersion, value.CertifierType,
        value.CertificationStatus, value.Passed,
        BlockingIssues = value.BlockingIssues.Order(StringComparer.Ordinal),
        Warnings = value.NonBlockingWarnings.Order(StringComparer.Ordinal),
        CertifiedVariants = value.CertifiedVariants.Order(StringComparer.Ordinal),
        RejectedVariants = value.RejectedVariants.Order(StringComparer.Ordinal),
        Scenes = value.SceneLevelOutcomes.OrderBy(x => x.Variant, StringComparer.Ordinal).ThenBy(x => x.Sequence).Select(x => new { x.SceneId, x.Variant, x.Sequence, x.NarrativeStage, x.SceneRole, x.Certified, References = x.KnowledgeReferenceIds.Order(StringComparer.Ordinal) }),
        Coverage = value.CoverageOutcomes.Order(StringComparer.Ordinal), Knowledge = value.KnowledgeReferenceOutcomes.Order(StringComparer.Ordinal), Editorial = value.EditorialOutcomes.Order(StringComparer.Ordinal)
    });

    public static string Calculate(DocumentaryBlueprintEditorialContract value) => Hash(new
    {
        value.ContractId, value.ExecutionId, value.EventId, value.Language, value.Profile,
        value.SourceCertificationId, value.SourceCertificationChecksum, value.SourcePhase4Checksum,
        AllowedVariants = value.AllowedVariants.Order(StringComparer.Ordinal), CertifiedScenes = value.CertifiedSceneIds.Order(StringComparer.Ordinal),
        value.SceneOrder, Stages = value.NarrativeStages.OrderBy(x => x.Key), Roles = value.SceneRoles.OrderBy(x => x.Key),
        Questions = value.MandatoryViewerQuestions.Order(StringComparer.Ordinal), Objectives = value.LearningObjectives.Order(StringComparer.Ordinal),
        Knowledge = value.KnowledgeReferenceConstraints.Order(StringComparer.Ordinal), Deferred = value.DeferredItems.OrderBy(x => x.Key),
        Warnings = value.ApprovedEditorialWarnings.Order(StringComparer.Ordinal), Blocking = value.BlockingConstraints.Order(StringComparer.Ordinal),
        Requirements = value.DownstreamRequirements.Order(StringComparer.Ordinal), value.NarrationEligible, value.StoryFrameEligible
    });

    public static string SourcePhase4(DocumentaryBlueprintCertificationRequest request) => Hash(new
    {
        Master = request.Master.Metadata.Checksum, Long = request.Long.Metadata.Checksum, Short = request.Short.Metadata.Checksum,
        request.Phase4Diagnostics.BuilderVersion, Variants = request.RequestedVariants.Order(StringComparer.Ordinal)
    });
}
