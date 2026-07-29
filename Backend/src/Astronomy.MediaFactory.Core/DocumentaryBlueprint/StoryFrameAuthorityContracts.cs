using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed record StoryFrameAuthorityFrame(string FrameId, string SceneId, int SceneNumber, int FrameNumber,
    string Variant, string NarrativeStage, string SceneRole, string FrameRole,
    IReadOnlyList<string> ViewerQuestionIds, IReadOnlyList<string> LearningObjectiveIds,
    IReadOnlyList<string> KnowledgeReferenceIds, string NarrativeIntent, string VisualIntent,
    string ShotType, string CameraDirection, string CameraMovement, string Subject, string Setting,
    string Composition, string Lighting, string Mood, string MotionIntent, string TransitionIn,
    string TransitionOut, IReadOnlyList<string> OverlayRequirements, IReadOnlyList<string> LowerThirdRequirements,
    IReadOnlyList<string> ImageRequirements, IReadOnlyList<string> BrollRequirements, bool NarrationRequired,
    string NarrationOwnership, double EstimatedStart, double EstimatedDuration,
    IReadOnlyList<string> ProductionNotes, IReadOnlyList<string> BlockingConstraints, IReadOnlyList<string> Warnings);

public sealed record StoryFramesAuthority(string AuthorityId, string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, string SourceCertificationId, string SourceCertificationChecksum,
    string SourceEditorialContractId, string SourceEditorialContractChecksum, string SourcePhase4Checksum,
    string BuilderType, string BuilderVersion, IReadOnlyList<string> RequestedVariants,
    IReadOnlyList<StoryFrameAuthorityFrame> Frames, DateTimeOffset GeneratedUtc, string SemanticChecksum);

public sealed record StoryFrameVariantIndex(string VariantName, int SceneCount, int FrameCount,
    IReadOnlyList<string> OrderedSceneIds, IReadOnlyList<string> OrderedFrameIds);
public sealed record StoryFrameSceneIndex(string Variant, string SceneId, int SceneNumber, string NarrativeStage,
    string SceneRole, IReadOnlyList<string> OrderedFrameIds, int FrameCount, double EstimatedDuration,
    bool NarrationRequired, bool VisualAssetRequired);
public sealed record StoryFrameIndex(string IndexId, string ExecutionId, string EventId, string Language,
    string Profile, string SourceStoryFramesAuthorityId, string SourceStoryFramesChecksum,
    string SourceEditorialContractChecksum, IReadOnlyList<StoryFrameVariantIndex> Variants,
    IReadOnlyList<StoryFrameSceneIndex> Scenes, int TotalFrameCount, DateTimeOffset GeneratedUtc, string Checksum);

public sealed record StoryFrameDiagnostics(string ExecutionId, string BuilderType, string BuilderVersion,
    string IntegrationServiceType, string IntegrationServiceVersion, IReadOnlyList<string> InputArtifactPaths,
    IReadOnlyDictionary<string, string> InputArtifactChecksums, string SourceCertificationChecksum,
    string SourceEditorialContractChecksum, string SourcePhase4Checksum, IReadOnlyList<string> RequestedVariants,
    int InputSceneCount, int GeneratedSceneCount, int GeneratedFrameCount,
    IReadOnlyDictionary<string, int> FramesPerVariant, IReadOnlyDictionary<string, int> FramesPerScene,
    int NarrationFrameCount, int VisualFrameCount, int ImageRequirementCount, int BrollRequirementCount,
    int OverlayRequirementCount, int WarningCount, int BlockingIssueCount,
    IReadOnlyList<string> ValidationStagesExecuted, long BuildDurationMilliseconds);

public sealed record StoryFrameIntegrationRequest(string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, DocumentaryBlueprintCertification Certification,
    DocumentaryBlueprintEditorialContract EditorialContract,
    DocumentaryBlueprintCertificationDiagnostics CertificationDiagnostics,
    IReadOnlyList<string> RequestedVariants);
public sealed record StoryFrameIntegrationResult(StoryFramesAuthority Authority, StoryFrameIndex Index,
    StoryFrameDiagnostics Diagnostics);

public interface IStoryFrameIntegrationService
{
    Task<StoryFrameIntegrationResult> BuildAsync(StoryFrameIntegrationRequest request, CancellationToken cancellationToken);
}

public static class StoryFrameAuthorityChecksum
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Authority(StoryFramesAuthority value) => Hash(new
    {
        value.AuthorityId, value.ExecutionId, value.PlanId, value.EventId, value.Language, value.Profile,
        value.SourceCertificationId, value.SourceCertificationChecksum, value.SourceEditorialContractId,
        value.SourceEditorialContractChecksum, value.SourcePhase4Checksum, value.BuilderType, value.BuilderVersion,
        RequestedVariants = value.RequestedVariants.Order(StringComparer.OrdinalIgnoreCase),
        Frames = value.Frames.Select(f => new { f.FrameId, f.SceneId, f.SceneNumber, f.FrameNumber, f.Variant,
            f.NarrativeStage, f.SceneRole, f.FrameRole,
            ViewerQuestionIds=f.ViewerQuestionIds.Order(StringComparer.Ordinal), LearningObjectiveIds=f.LearningObjectiveIds.Order(StringComparer.Ordinal),
            KnowledgeReferenceIds=f.KnowledgeReferenceIds.Order(StringComparer.Ordinal), f.NarrativeIntent, f.VisualIntent,
            f.ShotType, f.CameraDirection, f.CameraMovement, f.Subject, f.Setting, f.Composition, f.Lighting,
            f.Mood, f.MotionIntent, f.TransitionIn, f.TransitionOut,
            OverlayRequirements=f.OverlayRequirements.Order(StringComparer.Ordinal), LowerThirdRequirements=f.LowerThirdRequirements.Order(StringComparer.Ordinal),
            ImageRequirements=f.ImageRequirements.Order(StringComparer.Ordinal), BrollRequirements=f.BrollRequirements.Order(StringComparer.Ordinal),
            f.NarrationRequired, f.NarrationOwnership, f.EstimatedStart, f.EstimatedDuration,
            ProductionNotes=f.ProductionNotes.Order(StringComparer.Ordinal), BlockingConstraints=f.BlockingConstraints.Order(StringComparer.Ordinal),
            Warnings=f.Warnings.Order(StringComparer.Ordinal) })
    });
    public static string Index(StoryFrameIndex value) => Hash(new { value.IndexId, value.ExecutionId, value.EventId,
        value.Language, value.Profile, value.SourceStoryFramesAuthorityId, value.SourceStoryFramesChecksum,
        value.SourceEditorialContractChecksum, Variants=value.Variants.OrderBy(x=>x.VariantName,StringComparer.OrdinalIgnoreCase),
        value.Scenes, value.TotalFrameCount });
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options)))).ToLowerInvariant();
}

public static class StoryFrameArtifactValidator
{
    public static IReadOnlyList<string> Validate(StoryFrameIntegrationResult result, StoryFrameIntegrationRequest request)
    {
        var errors = new List<string>(); var a=result.Authority; var e=request.EditorialContract;
        if (a.ExecutionId!=request.ExecutionId || a.PlanId!=request.PlanId || a.EventId!=request.EventId || a.Language!=request.Language || a.Profile!=request.Profile) errors.Add("Story-frame authority identity does not match request.");
        if (a.SourceCertificationId!=request.Certification.CertificationId || a.SourceCertificationChecksum!=request.Certification.SemanticChecksum || a.SourceEditorialContractId!=e.ContractId || a.SourceEditorialContractChecksum!=e.Checksum || a.SourcePhase4Checksum!=e.SourcePhase4Checksum) errors.Add("Story-frame Phase 5 lineage does not match.");
        if (!e.StoryFrameEligible || !request.Certification.Passed || e.BlockingConstraints.Count>0) errors.Add("Phase 5 authority is not eligible for story frames.");
        if (a.SemanticChecksum!=StoryFrameAuthorityChecksum.Authority(a)) errors.Add("Story-frame semantic checksum is invalid.");
        if (result.Index.Checksum!=StoryFrameAuthorityChecksum.Index(result.Index)) errors.Add("Story-frame index checksum is invalid.");
        if (!a.RequestedVariants.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(request.RequestedVariants)) errors.Add("Requested variants do not match.");
        if (a.Frames.Count==0 || a.Frames.Select(x=>x.FrameId).Distinct(StringComparer.Ordinal).Count()!=a.Frames.Count) errors.Add("Frames are empty or contain duplicate IDs.");
        foreach(var variant in request.RequestedVariants) foreach(var scene in e.SceneOrder)
            if (!a.Frames.Any(x=>x.Variant.Equals(variant,StringComparison.OrdinalIgnoreCase)&&x.SceneId==scene)) errors.Add($"Certified scene '{scene}' is missing from '{variant}'.");
        if (a.Frames.Any(x=>!e.SceneOrder.Contains(x.SceneId,StringComparer.Ordinal))) errors.Add("Authority contains an uncertified scene.");
        if (result.Index.TotalFrameCount!=a.Frames.Count || result.Diagnostics.GeneratedFrameCount!=a.Frames.Count || result.Diagnostics.BlockingIssueCount!=a.Frames.Sum(x=>x.BlockingConstraints.Count)) errors.Add("Index or diagnostics do not reconcile.");
        if (result.Diagnostics.InputArtifactPaths.Count!=3 || result.Diagnostics.InputArtifactPaths.Any(Path.IsPathRooted)) errors.Add("Diagnostics must identify three workspace-relative Phase 5 inputs.");
        return errors;
    }
}
