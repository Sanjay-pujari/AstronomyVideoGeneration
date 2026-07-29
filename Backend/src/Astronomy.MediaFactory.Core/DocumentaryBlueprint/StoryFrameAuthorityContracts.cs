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
    IReadOnlyList<string> ValidationStagesExecuted, long BuildDurationMilliseconds)
{
    // Added as an optional additive property. Older artifacts deserialize with zero and are rejected
    // for regeneration rather than becoming unsafe resume authorities.
    public int GeneratedVariantSceneCount { get; init; }
}

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

/// <summary>Mockable boundary over the production storyboard builder. Implementations must adapt the
/// existing production planner; this is deliberately not a second planning engine.</summary>
public interface ICertifiedStoryFrameBuilder
{
    string BuilderType { get; }
    string BuilderVersion { get; }
    Task<IReadOnlyList<StoryFrameAuthorityFrame>> BuildAsync(
        DocumentaryBlueprintEditorialContract editorialContract,
        IReadOnlyList<string> requestedVariants,
        CancellationToken cancellationToken);
}

public static class StoryFrameAuthorityChecksum
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Authority(StoryFramesAuthority value) => Hash(new
    {
        value.AuthorityId, value.ExecutionId, value.PlanId, value.EventId, value.Language, value.Profile,
        value.SourceCertificationId, value.SourceCertificationChecksum, value.SourceEditorialContractId,
        value.SourceEditorialContractChecksum, value.SourcePhase4Checksum, value.BuilderType, value.BuilderVersion,
        RequestedVariants = value.RequestedVariants,
        // Frames are an ordered semantic sequence. The validator proves this sequence is canonical.
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
        value.SourceEditorialContractChecksum, Variants=value.Variants,
        Scenes=value.Scenes, value.TotalFrameCount });
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options)))).ToLowerInvariant();
}

public static class StoryFrameArtifactValidator
{
    public static IReadOnlyList<string> Validate(StoryFrameIntegrationResult result, StoryFrameIntegrationRequest request)
    {
        var errors = new List<string>(); var a=result.Authority; var e=request.EditorialContract;
        if(string.IsNullOrWhiteSpace(a.AuthorityId)||string.IsNullOrWhiteSpace(a.BuilderType)||string.IsNullOrWhiteSpace(a.BuilderVersion)||a.GeneratedUtc==default) errors.Add("Story-frame authority metadata is incomplete.");
        if (a.ExecutionId!=request.ExecutionId || a.PlanId!=request.PlanId || a.EventId!=request.EventId || a.Language!=request.Language || a.Profile!=request.Profile) errors.Add("Story-frame authority identity does not match request.");
        if (a.SourceCertificationId!=request.Certification.CertificationId || a.SourceCertificationChecksum!=request.Certification.SemanticChecksum || a.SourceEditorialContractId!=e.ContractId || a.SourceEditorialContractChecksum!=e.Checksum || a.SourcePhase4Checksum!=e.SourcePhase4Checksum) errors.Add("Story-frame Phase 5 lineage does not match.");
        if (!e.StoryFrameEligible || !request.Certification.Passed || e.BlockingConstraints.Count>0) errors.Add("Phase 5 authority is not eligible for story frames.");
        if(request.Certification.BlockingIssues.Count>0||request.Certification.CertificationStatus==DocumentaryBlueprintCertificationStatus.Rejected||request.CertificationDiagnostics.BlockingIssueCount!=request.Certification.BlockingIssues.Count||request.CertificationDiagnostics.WarningCount!=request.Certification.NonBlockingWarnings.Count) errors.Add("Phase 5 certification diagnostics do not reconcile.");
        if (a.SemanticChecksum!=StoryFrameAuthorityChecksum.Authority(a)) errors.Add("Story-frame semantic checksum is invalid.");
        if (result.Index.Checksum!=StoryFrameAuthorityChecksum.Index(result.Index)) errors.Add("Story-frame index checksum is invalid.");
        if(a.RequestedVariants.Count==0||a.RequestedVariants.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=a.RequestedVariants.Count||!a.RequestedVariants.SequenceEqual(request.RequestedVariants,StringComparer.OrdinalIgnoreCase)) errors.Add("Requested variants do not match in declared order.");
        if (a.Frames.Count==0 || a.Frames.Select(x=>x.FrameId).Distinct(StringComparer.Ordinal).Count()!=a.Frames.Count) errors.Add("Frames are empty or contain duplicate IDs.");
        foreach(var variant in request.RequestedVariants) foreach(var scene in e.SceneOrder)
            if (!a.Frames.Any(x=>x.Variant.Equals(variant,StringComparison.OrdinalIgnoreCase)&&x.SceneId==scene)) errors.Add($"Certified scene '{scene}' is missing from '{variant}'.");
        if (a.Frames.Any(x=>!e.SceneOrder.Contains(x.SceneId,StringComparer.Ordinal))) errors.Add("Authority contains an uncertified scene.");
        var expected=a.RequestedVariants.SelectMany(v=>e.SceneOrder.Select((s,i)=>(Variant:v,Scene:s,Number:i+1))).ToArray();
        foreach(var item in expected)
        {
            var frames=a.Frames.Where(x=>x.Variant.Equals(item.Variant,StringComparison.OrdinalIgnoreCase)&&x.SceneId==item.Scene).ToArray();
            if(frames.Any(x=>x.SceneNumber!=item.Number)||!frames.Select(x=>x.FrameNumber).SequenceEqual(Enumerable.Range(1,frames.Length))) errors.Add($"Frame sequence is invalid for '{item.Variant}:{item.Scene}'.");
        }
        var canonical=a.RequestedVariants.SelectMany(v=>e.SceneOrder.SelectMany(s=>a.Frames.Where(x=>x.Variant.Equals(v,StringComparison.OrdinalIgnoreCase)&&x.SceneId==s).OrderBy(x=>x.FrameNumber).ThenBy(x=>x.FrameId,StringComparer.Ordinal))).Select(x=>x.FrameId);
        if(!a.Frames.Select(x=>x.FrameId).SequenceEqual(canonical,StringComparer.Ordinal)) errors.Add("Authority frame sequence is not canonical.");
        if(a.Frames.Any(x=>e.NarrativeStages.GetValueOrDefault(x.SceneId)!=x.NarrativeStage||e.SceneRoles.GetValueOrDefault(x.SceneId)!=x.SceneRole)) errors.Add("Frame narrative stage or scene role differs from the editorial contract.");
        if(a.Frames.Any(x=>x.EstimatedStart<0||x.EstimatedDuration<=0||string.IsNullOrWhiteSpace(x.VisualIntent)||string.IsNullOrWhiteSpace(x.NarrativeIntent)||string.IsNullOrWhiteSpace(x.FrameRole)||string.IsNullOrWhiteSpace(x.ShotType)||string.IsNullOrWhiteSpace(x.CameraDirection)||string.IsNullOrWhiteSpace(x.CameraMovement)||string.IsNullOrWhiteSpace(x.Subject)||string.IsNullOrWhiteSpace(x.Setting)||string.IsNullOrWhiteSpace(x.Composition)||string.IsNullOrWhiteSpace(x.Lighting)||string.IsNullOrWhiteSpace(x.Mood)||string.IsNullOrWhiteSpace(x.MotionIntent))) errors.Add("A frame has invalid timing or production intent.");
        foreach(var variant in a.RequestedVariants)
        {
            double end=0; foreach(var frame in a.Frames.Where(x=>x.Variant.Equals(variant,StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber)){if(frame.EstimatedStart<end) errors.Add($"Frame timing overlaps in '{variant}'."); end=frame.EstimatedStart+frame.EstimatedDuration;}
        }
        if(a.Frames.Any(x=>x.ViewerQuestionIds.Except(e.MandatoryViewerQuestions).Any()||x.LearningObjectiveIds.Except(e.LearningObjectives).Any()||x.KnowledgeReferenceIds.Except(e.KnowledgeReferenceConstraints).Any())) errors.Add("A frame contains an uncertified relationship.");
        if (result.Index.TotalFrameCount!=a.Frames.Count || result.Diagnostics.GeneratedFrameCount!=a.Frames.Count || result.Diagnostics.BlockingIssueCount!=a.Frames.Sum(x=>x.BlockingConstraints.Count)) errors.Add("Index or diagnostics do not reconcile.");
        var expectedInputs=new[]{"05-blueprint-certification/blueprint-certification.json","05-blueprint-certification/editorial-contract.json","05-blueprint-certification/certification-diagnostics.json"};
        if (result.Diagnostics.InputArtifactPaths.Count!=3 || !result.Diagnostics.InputArtifactPaths.SequenceEqual(expectedInputs,StringComparer.Ordinal)
            || result.Diagnostics.InputArtifactPaths.Any(p=>Path.IsPathRooted(p)||p.Split('/', '\\').Contains(".."))) errors.Add("Diagnostics must identify the exact workspace-relative Phase 5 inputs.");
        if(result.Index.SourceStoryFramesAuthorityId!=a.AuthorityId||result.Index.SourceStoryFramesChecksum!=a.SemanticChecksum||result.Index.SourceEditorialContractChecksum!=e.Checksum) errors.Add("Index lineage does not reconcile.");
        if(!result.Index.Variants.Select(x=>x.VariantName).SequenceEqual(a.RequestedVariants,StringComparer.OrdinalIgnoreCase)) errors.Add("Index variant order does not reconcile.");
        if(!result.Diagnostics.RequestedVariants.SequenceEqual(a.RequestedVariants,StringComparer.OrdinalIgnoreCase)||result.Diagnostics.FramesPerScene.Count!=expected.Length||result.Diagnostics.GeneratedVariantSceneCount!=expected.Length) errors.Add("Diagnostics variants or variant-scene counts do not reconcile.");
        var requiredStages=new[]{"Phase5CompleteSet","Authority","Variants","Scenes","Frames","Relationships","ProductionIntent","Index","Diagnostics","Checksums"};
        if(requiredStages.Except(result.Diagnostics.ValidationStagesExecuted,StringComparer.Ordinal).Any()||result.Diagnostics.BuildDurationMilliseconds<0) errors.Add("Diagnostics validation stages or duration are invalid.");
        return errors;
    }
}
