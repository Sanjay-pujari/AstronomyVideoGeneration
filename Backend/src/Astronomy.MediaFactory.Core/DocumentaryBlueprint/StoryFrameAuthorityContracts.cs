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
    IReadOnlyList<StoryFrameAuthorityFrame> Frames, DateTimeOffset GeneratedUtc, string SemanticChecksum)
{
    public string AuthorityContractVersion { get; init; } = StoryFrameContractCompatibility.CurrentVersion;
}

public sealed record StoryFrameVariantIndex(string VariantName, int SceneCount, int FrameCount,
    IReadOnlyList<string> OrderedSceneIds, IReadOnlyList<string> OrderedFrameIds);
public sealed record StoryFrameSceneIndex(string Variant, string SceneId, int SceneNumber, string NarrativeStage,
    string SceneRole, IReadOnlyList<string> OrderedFrameIds, int FrameCount, double EstimatedDuration,
    bool NarrationRequired, bool VisualAssetRequired);
public sealed record StoryFrameIndex(string IndexId, string ExecutionId, string EventId, string Language,
    string Profile, string SourceStoryFramesAuthorityId, string SourceStoryFramesChecksum,
    string SourceEditorialContractChecksum, IReadOnlyList<StoryFrameVariantIndex> Variants,
    IReadOnlyList<StoryFrameSceneIndex> Scenes, int TotalFrameCount, DateTimeOffset GeneratedUtc, string Checksum)
{
    public string IndexContractVersion { get; init; } = StoryFrameContractCompatibility.CurrentVersion;
}

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
    public string DiagnosticsContractVersion { get; init; } = StoryFrameContractCompatibility.CurrentVersion;
}

public readonly record struct StoryFrameContractVersion(int Major, int Minor)
{
    public static bool TryParse(string? value, out StoryFrameContractVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor)
            || major < 0 || minor < 0 || value != $"{major}.{minor}") return false;
        version = new(major, minor);
        return true;
    }
}

public static class StoryFrameContractCompatibility
{
    public const string CurrentVersion = "1.1";
    private static readonly HashSet<string> SupportedVersions = new(StringComparer.Ordinal) { "1.0", "1.1" };
    public static bool IsSupported(string? version) => version is not null
        && StoryFrameContractVersion.TryParse(version, out _) && SupportedVersions.Contains(version);
}

public sealed record StoryFrameValidationCompatibilityContext(
    string CurrentBuilderType, string CurrentBuilderVersion,
    string CurrentIntegrationServiceType, string CurrentIntegrationServiceVersion,
    string CurrentAuthorityContractVersion, string CurrentIndexContractVersion,
    string CurrentDiagnosticsContractVersion);

public static class StoryFrameAuthorityIdentity
{
    public static string BuildAuthorityId(string executionId) => $"story-frames-{executionId}";
    public static bool IsExpectedAuthorityId(string? authorityId, string executionId) =>
        string.Equals(authorityId, BuildAuthorityId(executionId), StringComparison.Ordinal);
}

public static class StoryFrameIndexProjector
{
    public static StoryFrameIndex Project(StoryFramesAuthority authority, string editorialChecksum)
    {
        var variants = authority.RequestedVariants.Select(v => { var frames = authority.Frames.Where(f => f.Variant.Equals(v, StringComparison.OrdinalIgnoreCase)).ToArray();
            return new StoryFrameVariantIndex(v, frames.Select(f => f.SceneId).Distinct(StringComparer.Ordinal).Count(), frames.Length,
                frames.Select(f => f.SceneId).Distinct(StringComparer.Ordinal).ToArray(), frames.Select(f => f.FrameId).ToArray()); }).ToArray();
        var scenes = authority.RequestedVariants.SelectMany(v => authority.Frames.Where(f => f.Variant.Equals(v, StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.SceneId, StringComparer.Ordinal).Select(g => new StoryFrameSceneIndex(v, g.Key, g.First().SceneNumber,
                g.First().NarrativeStage, g.First().SceneRole, g.Select(f => f.FrameId).ToArray(), g.Count(), g.Sum(f => f.EstimatedDuration),
                g.Any(f => f.NarrationRequired), g.Any(f => f.ImageRequirements.Count + f.BrollRequirements.Count > 0)))).ToArray();
        var index = new StoryFrameIndex($"story-frame-index-{authority.ExecutionId}", authority.ExecutionId, authority.EventId,
            authority.Language, authority.Profile, authority.AuthorityId, authority.SemanticChecksum, editorialChecksum,
            variants, scenes, authority.Frames.Count, authority.GeneratedUtc, "");
        return index with { Checksum = StoryFrameAuthorityChecksum.Index(index) };
    }
}

public sealed record StoryFrameValidationError(string Code, string Artifact, string Field,
    string? Variant, string? SceneId, string? FrameId, string Expected, string Actual, string Message);
public sealed record StoryFrameValidationResult(bool IsValid, IReadOnlyList<StoryFrameValidationError> Errors);
public sealed record StoryFrameDownstreamReadiness(bool IsEligible,
    IReadOnlyList<string> BlockingReasons, IReadOnlyList<string> Warnings);

public sealed record CertifiedStoryFrameSceneAuthority(string SourceSceneId, string Variant, int SequenceNumber,
    DocumentaryNarrativeStage NarrativeStage, DocumentarySceneRole SceneRole,
    string ViewerQuestionId, string ViewerQuestionText, string LearningObjectiveId, string LearningObjectiveText,
    EditorialOutcome EditorialOutcome, SceneTransition TransitionIntent,
    IReadOnlyList<KnowledgeReference> KnowledgeReferences, int MinimumDurationSeconds,
    int TargetDurationSeconds, int MaximumDurationSeconds, VisualOpportunity? SafeVisualOpportunity,
    string SourceSceneSemanticChecksum);

/// <summary>The sole, immutable input boundary for Phase 6.</summary>
public sealed record Phase6CommittedInputAuthority(
    DocumentaryBlueprintAggregate Phase4Aggregate, string AggregateId, string AggregateChecksum,
    string LongProjectionChecksum, string ShortProjectionChecksum, string ProfileId, string ProfileVersion,
    IReadOnlyList<string> Phase4CommittedValidationEvidence, IReadOnlyList<string> Phase4ManifestEvidence,
    PublishedBlueprintCertification Phase5Authority, string CertificationId, string CertificationChecksum,
    string EditorialContractChecksum, string Phase5PublicationId,
    IReadOnlyList<string> Phase5CommittedValidationEvidence, IReadOnlyList<Phase5ArtifactInventoryEntry> Phase5ManifestEvidence,
    bool StoryFrameEligible, IReadOnlyList<string> AllowedVariants, IReadOnlyList<string> RequestedVariants,
    bool Phase4LineageMatched, bool CertificationAccepted, bool CoverageValid, bool TransitionsValid,
    bool PauseTestValid, bool PublicationCommitted, bool CommittedStateValidationPassed,
    IReadOnlyList<CertifiedStoryFrameSceneAuthority> LongScenes,
    IReadOnlyList<CertifiedStoryFrameSceneAuthority> ShortScenes);

public sealed record Phase6InputAuthorityRequest(string ExecutionRoot, string ExecutionId, string PlanId,
    string EventId, string Language, IReadOnlyList<string> RequestedVariants);
public sealed record Phase6InputAuthorityEvaluation(bool IsValid, string ReasonCode,
    IReadOnlyList<string> Errors, Phase6CommittedInputAuthority? Authority);

public sealed record StoryFrameIntegrationRequest(string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, Phase6CommittedInputAuthority InputAuthority,
    string RuntimeBuilderIdentity, string RuntimeBuilderVersion,
    string RuntimeIntegrationIdentity, string RuntimeIntegrationVersion)
{
    public DocumentaryBlueprintCertification Certification => InputAuthority.Phase5Authority.Certification;
    public DocumentaryBlueprintEditorialContract EditorialContract => InputAuthority.Phase5Authority.EditorialContract;
    public IReadOnlyList<string> RequestedVariants => InputAuthority.RequestedVariants;
    public IReadOnlyList<CertifiedStoryFrameSceneAuthority> LongScenes => InputAuthority.LongScenes;
    public IReadOnlyList<CertifiedStoryFrameSceneAuthority> ShortScenes => InputAuthority.ShortScenes;
}
public sealed record StoryFrameIntegrationResult(StoryFramesAuthority Authority, StoryFrameIndex Index,
    StoryFrameDiagnostics Diagnostics);

public interface IStoryFrameIntegrationService
{
    Task<StoryFrameIntegrationResult> BuildAsync(StoryFrameIntegrationRequest request, CancellationToken cancellationToken);
}

/// <summary>Reports the identity of the builder and integration runtime which may safely reuse
/// persisted Story Frame authorities.</summary>
public interface IStoryFrameRuntimeIdentityProvider
{
    StoryFrameValidationCompatibilityContext GetCompatibilityContext();
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
        value.SourceEditorialContractChecksum, value.SourcePhase4Checksum, value.BuilderType, value.BuilderVersion, value.AuthorityContractVersion,
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
        value.SourceEditorialContractChecksum, value.IndexContractVersion, Variants=value.Variants,
        Scenes=value.Scenes, value.TotalFrameCount });
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options)))).ToLowerInvariant();
}

public static class StoryFrameArtifactValidator
{
    public const double TimingToleranceSeconds = 0.001;
    public const string NarrationOwner = "Phase7";
    private static readonly string[] RequiredStages = ["Phase5CompleteSet", "Authority", "Variants", "Scenes", "Frames", "Relationships", "ProductionIntent", "Index", "Diagnostics", "Checksums"];
    private static readonly HashSet<string> Variants = new(["Long", "Short"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Placeholders = new(["todo", "tbd", "unknown", "fixture-only", "test-only", "placeholder", "null", "n/a"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Validate(StoryFrameIntegrationResult result, StoryFrameIntegrationRequest request) =>
        ValidateDetailed(result, request).Errors.Select(FormatLegacyError).ToArray();
    private static string FormatLegacyError(StoryFrameValidationError error) => $"[{error.Code}] {error.Message}";

    public static StoryFrameDownstreamReadiness GetDownstreamReadiness(StoryFrameIntegrationResult result, StoryFrameIntegrationRequest request,
        StoryFrameValidationCompatibilityContext? compatibility = null)
    {
        var validation = ValidateDetailed(result, request, compatibility);
        return new(validation.IsValid, validation.Errors.Select(FormatLegacyError).ToArray(),
            result.Authority.Frames.SelectMany(f => f.Warnings).Distinct(StringComparer.Ordinal).ToArray());
    }

    public static StoryFrameValidationResult ValidateDetailed(StoryFrameIntegrationResult result, StoryFrameIntegrationRequest request,
        StoryFrameValidationCompatibilityContext? compatibility = null)
    {
        ArgumentNullException.ThrowIfNull(result); ArgumentNullException.ThrowIfNull(request);
        var errors = new List<StoryFrameValidationError>();
        void Add(string code, string artifact, string field, string expected, object? actual, string message,
            string? variant=null, string? scene=null, string? frame=null) => errors.Add(new(code,artifact,field,variant,scene,frame,expected,actual?.ToString()??"<null>",message));
        var a=result.Authority; var index=result.Index; var d=result.Diagnostics; var e=request.EditorialContract;
        static bool Sha(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        static bool UnsafeId(string? value) => string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains("..",StringComparison.Ordinal)
            || value.Contains('/') || value.Contains('\\') || Path.IsPathRooted(value) || (value.Length>1 && value[1]==':');
        static bool InvalidText(string? value) => string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || Placeholders.Contains(value.Trim());
        static bool UnsafeValue(string value) => value.Contains("..",StringComparison.Ordinal) || Path.IsPathRooted(value) || value.StartsWith("//",StringComparison.Ordinal)
            || value.StartsWith("\\\\",StringComparison.Ordinal) || (value.Length>1 && char.IsLetter(value[0]) && value[1]==':')
            || value.Contains("staging",StringComparison.OrdinalIgnoreCase) || value.Contains("backup",StringComparison.OrdinalIgnoreCase)
            || new[]{"password=","apikey=","api_key=","clientsecret=","connectionstring=","bearer "}.Any(x=>value.Contains(x,StringComparison.OrdinalIgnoreCase));
        void Version(string artifact,string field,string? value) { if(!StoryFrameContractCompatibility.IsSupported(value)) Add("SF-COMPAT-001",artifact,field,"explicitly supported contract version",value,"Contract version is not reusable."); }

        foreach(var pair in new[]{("AuthorityId",a.AuthorityId),("ExecutionId",a.ExecutionId),("PlanId",a.PlanId),("EventId",a.EventId),("Language",a.Language),("Profile",a.Profile),("BuilderType",a.BuilderType),("BuilderVersion",a.BuilderVersion)})
            if(UnsafeId(pair.Item2)) Add("SF-AUTH-001","authority",pair.Item1,"safe non-empty identity",pair.Item2,$"{pair.Item1} is invalid.");
        if(!StoryFrameAuthorityIdentity.IsExpectedAuthorityId(a.AuthorityId,request.ExecutionId)) Add("SF-AUTH-001","authority","AuthorityId",StoryFrameAuthorityIdentity.BuildAuthorityId(request.ExecutionId),a.AuthorityId,"Authority identity does not belong to this execution.");
        foreach(var pair in new[]{("ExecutionId",a.ExecutionId,request.ExecutionId),("PlanId",a.PlanId,request.PlanId),("EventId",a.EventId,request.EventId),("Language",a.Language,request.Language),("Profile",a.Profile,request.Profile)})
            if(!string.Equals(pair.Item2,pair.Item3,StringComparison.Ordinal)) Add("SF-AUTH-001","authority",pair.Item1,pair.Item3,pair.Item2,"Authority identity does not match request.");
        if(a.GeneratedUtc==default || a.GeneratedUtc>DateTimeOffset.UtcNow.AddMinutes(5)) Add("SF-AUTH-001","authority","GeneratedUtc","non-default timestamp within five-minute clock skew",a.GeneratedUtc,"Generated timestamp is invalid.");
        Version("authority","AuthorityContractVersion",a.AuthorityContractVersion); Version("index","IndexContractVersion",index.IndexContractVersion); Version("diagnostics","DiagnosticsContractVersion",d.DiagnosticsContractVersion);
        foreach(var pair in new[]{("SemanticChecksum",a.SemanticChecksum),("SourceCertificationChecksum",a.SourceCertificationChecksum),("SourceEditorialContractChecksum",a.SourceEditorialContractChecksum),("SourcePhase4Checksum",a.SourcePhase4Checksum)}) if(!Sha(pair.Item2)) Add("SF-CHECKSUM-001","authority",pair.Item1,"lowercase SHA-256",pair.Item2,"Checksum format is invalid.");
        if(!Sha(index.Checksum)) Add("SF-CHECKSUM-001","index","Checksum","lowercase SHA-256",index.Checksum,"Checksum format is invalid.");
        if(a.SourceCertificationId!=request.Certification.CertificationId || a.SourceCertificationChecksum!=request.Certification.SemanticChecksum || a.SourceEditorialContractId!=e.ContractId || a.SourceEditorialContractChecksum!=e.Checksum || a.SourcePhase4Checksum!=e.SourcePhase4Checksum) Add("SF-LINEAGE-001","authority","Source*","exact Phase 5 lineage","mismatch","Phase 5 lineage does not reconcile.");
        if(!e.StoryFrameEligible || !request.Certification.Passed || e.BlockingConstraints.Count>0) Add("SF-LINEAGE-001","phase5","StoryFrameEligible","certified and eligible",e.StoryFrameEligible,"Phase 5 is not eligible for story frames.");
        if(a.SemanticChecksum!=StoryFrameAuthorityChecksum.Authority(a)) Add("SF-CHECKSUM-001","authority","SemanticChecksum","recomputed checksum",a.SemanticChecksum,"Authority checksum does not reconcile.");

        void ValidateVariantList(IReadOnlyList<string>? values,string artifact,string field,IReadOnlyList<string>? expected=null) {
            if(values is null || values.Count==0){Add("SF-VARIANT-001",artifact,field,"non-empty canonical variants",null,"Variant list is missing.");return;}
            foreach(var v in values) if(string.IsNullOrWhiteSpace(v)||!Variants.Contains(v)) Add("SF-VARIANT-001",artifact,field,"Long or Short",v,"Unknown variant.",v);
            if(values.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=values.Count) Add("SF-VARIANT-001",artifact,field,"no canonical duplicates",string.Join(',',values),"Duplicate canonical variant.");
            if(expected is not null && !values.SequenceEqual(expected,StringComparer.OrdinalIgnoreCase)) Add("SF-VARIANT-001",artifact,field,"declared request order",string.Join(',',values),"Variant order or membership differs."); }
        ValidateVariantList(request.RequestedVariants,"request","RequestedVariants"); ValidateVariantList(a.RequestedVariants,"authority","RequestedVariants",request.RequestedVariants); ValidateVariantList(d.RequestedVariants,"diagnostics","RequestedVariants",a.RequestedVariants);
        var duplicateFrames=a.Frames.GroupBy(f=>f.FrameId,StringComparer.Ordinal).Where(g=>g.Count()>1); foreach(var duplicate in duplicateFrames) Add("SF-FRAME-001","authority","FrameId","unique",duplicate.Key,"Duplicate frame ID.",frame:duplicate.Key);
        foreach(var f in a.Frames) {
            if(!Variants.Contains(f.Variant)||!a.RequestedVariants.Contains(f.Variant,StringComparer.OrdinalIgnoreCase)) Add("SF-VARIANT-001","authority","Frame.Variant","requested variant",f.Variant,"Frame variant is unknown or unrequested.",f.Variant,f.SceneId,f.FrameId);
            if(UnsafeId(f.FrameId)||UnsafeId(f.SceneId)) Add("SF-FRAME-001","authority","FrameId/SceneId","safe identifiers",$"{f.FrameId}/{f.SceneId}","Frame or scene identifier is unsafe.",f.Variant,f.SceneId,f.FrameId);
            foreach(var pair in new[]{("NarrativeStage",f.NarrativeStage),("SceneRole",f.SceneRole),("FrameRole",f.FrameRole),("NarrativeIntent",f.NarrativeIntent),("VisualIntent",f.VisualIntent),("ShotType",f.ShotType),("CameraDirection",f.CameraDirection),("CameraMovement",f.CameraMovement),("Subject",f.Subject),("Setting",f.Setting),("Composition",f.Composition),("Lighting",f.Lighting),("Mood",f.Mood),("MotionIntent",f.MotionIntent),("TransitionIn",f.TransitionIn),("TransitionOut",f.TransitionOut)}) if(InvalidText(pair.Item2)) Add("SF-FRAME-001","authority",pair.Item1,"production-ready value",pair.Item2,"Required frame field is invalid.",f.Variant,f.SceneId,f.FrameId);
            if(!double.IsFinite(f.EstimatedStart)||!double.IsFinite(f.EstimatedDuration)||f.EstimatedStart < -TimingToleranceSeconds||f.EstimatedDuration<=0||!double.IsFinite(f.EstimatedStart+f.EstimatedDuration)) Add("SF-TIME-001","authority","Timing","finite non-negative start and positive duration",$"{f.EstimatedStart}/{f.EstimatedDuration}","Frame timing is invalid.",f.Variant,f.SceneId,f.FrameId);
            ValidateRelationships(f.ViewerQuestionIds,e.MandatoryViewerQuestions,"ViewerQuestionIds",f); ValidateRelationships(f.LearningObjectiveIds,e.LearningObjectives,"LearningObjectiveIds",f); ValidateRelationships(f.KnowledgeReferenceIds,e.KnowledgeReferenceConstraints,"KnowledgeReferenceIds",f);
            ValidateCollection(f.OverlayRequirements,"OverlayRequirements",f); ValidateCollection(f.LowerThirdRequirements,"LowerThirdRequirements",f); ValidateCollection(f.ImageRequirements,"ImageRequirements",f); ValidateCollection(f.BrollRequirements,"BrollRequirements",f); ValidateCollection(f.ProductionNotes,"ProductionNotes",f); ValidateCollection(f.BlockingConstraints,"BlockingConstraints",f); ValidateCollection(f.Warnings,"Warnings",f);
            var narrationFields=new[]{f.NarrativeIntent,f.VisualIntent}.Concat(f.ProductionNotes??[]).Concat(f.OverlayRequirements??[]);
            var markers=new[]{"<speak","<prosody","<voice","WEBVTT","narration.mp3",".wav","voiceName","SSML","-->"};
            if(narrationFields.Any(x=>x is not null&&markers.Any(m=>x.Contains(m,StringComparison.OrdinalIgnoreCase)))) Add("SF-NARR-001","authority","NarrationBoundary","planning metadata only","payload marker","Narration payload leaked into Phase 6.",f.Variant,f.SceneId,f.FrameId);
            if(f.NarrationRequired&&!string.Equals(f.NarrationOwnership,NarrationOwner,StringComparison.Ordinal)) Add("SF-NARR-001","authority","NarrationOwnership",NarrationOwner,f.NarrationOwnership,"Narration ownership is invalid.",f.Variant,f.SceneId,f.FrameId);
            if(f.BlockingConstraints is {Count:>0}) Add("SF-REQ-001","authority","BlockingConstraints","empty",f.BlockingConstraints.Count,"Blocking constraints prevent downstream readiness.",f.Variant,f.SceneId,f.FrameId);
        }
        void ValidateRelationships(IReadOnlyList<string>? actual,IReadOnlyList<string> certified,string field,StoryFrameAuthorityFrame f) { if(actual is null){Add("SF-REL-001","authority",field,"non-null",null,"Relationship collection is null.",f.Variant,f.SceneId,f.FrameId);return;} if(actual.Any(string.IsNullOrWhiteSpace)||actual.Distinct(StringComparer.Ordinal).Count()!=actual.Count||actual.Except(certified,StringComparer.Ordinal).Any()) Add("SF-REL-001","authority",field,"unique certified IDs",string.Join(',',actual),"Relationship collection is invalid.",f.Variant,f.SceneId,f.FrameId); }
        void ValidateCollection(IReadOnlyList<string>? actual,string field,StoryFrameAuthorityFrame f) { if(actual is null){Add("SF-REQ-001","authority",field,"non-null",null,"Requirement collection is null.",f.Variant,f.SceneId,f.FrameId);return;} if(actual.Any(x=>string.IsNullOrWhiteSpace(x)||UnsafeValue(x))||actual.Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=actual.Count) Add("SF-REQ-001","authority",field,"safe unique values",string.Join('|',actual),"Requirement collection is invalid.",f.Variant,f.SceneId,f.FrameId); }
        foreach(var required in new[]{(e.MandatoryViewerQuestions,"ViewerQuestionIds",a.Frames.SelectMany(f=>f.ViewerQuestionIds??[])),(e.LearningObjectives,"LearningObjectiveIds",a.Frames.SelectMany(f=>f.LearningObjectiveIds??[])),(e.KnowledgeReferenceConstraints,"KnowledgeReferenceIds",a.Frames.SelectMany(f=>f.KnowledgeReferenceIds??[]))}) foreach(var missing in required.Item1.Except(required.Item3,StringComparer.Ordinal)) Add("SF-REL-001","authority",required.Item2,"mandatory coverage",missing,"Mandatory relationship is not covered.");
        foreach(var variant in a.RequestedVariants) { double previousEnd=0; foreach(var scene in e.SceneOrder.Select((id,i)=>(id,i))) { var frames=a.Frames.Where(f=>f.Variant.Equals(variant,StringComparison.OrdinalIgnoreCase)&&f.SceneId==scene.id).ToArray(); if(frames.Length==0) Add("SF-SCENE-001","authority","SceneId",scene.id,"missing","Requested variant scene is missing.",variant,scene.id); foreach(var f in frames) { if(f.SceneNumber!=scene.i+1||f.NarrativeStage!=e.NarrativeStages.GetValueOrDefault(scene.id)||f.SceneRole!=e.SceneRoles.GetValueOrDefault(scene.id)) Add("SF-SCENE-001","authority","SceneMetadata","editorial projection",f.SceneNumber,"Scene metadata differs.",variant,scene.id,f.FrameId); if(f.EstimatedStart+TimingToleranceSeconds<previousEnd) Add("SF-TIME-001","authority","EstimatedStart",$">={previousEnd-TimingToleranceSeconds}",f.EstimatedStart,"Frames overlap beyond tolerance.",variant,scene.id,f.FrameId); previousEnd=Math.Max(previousEnd,f.EstimatedStart+f.EstimatedDuration); } if(!frames.Select(f=>f.FrameNumber).SequenceEqual(Enumerable.Range(1,frames.Length))) Add("SF-FRAME-001","authority","FrameNumber","contiguous 1-based sequence",string.Join(',',frames.Select(f=>f.FrameNumber)),"Frame sequence is invalid.",variant,scene.id); } }
        if(a.Frames.Any(f=>!e.SceneOrder.Contains(f.SceneId,StringComparer.Ordinal))) Add("SF-SCENE-001","authority","SceneId","certified scene", "unknown", "Authority contains an uncertified scene.");
        var canonical=a.RequestedVariants.SelectMany(v=>e.SceneOrder.SelectMany(scene=>a.Frames.Where(f=>f.Variant.Equals(v,StringComparison.OrdinalIgnoreCase)&&f.SceneId==scene).OrderBy(f=>f.FrameNumber).ThenBy(f=>f.FrameId,StringComparer.Ordinal))).Select(f=>f.FrameId); if(!a.Frames.Select(f=>f.FrameId).SequenceEqual(canonical,StringComparer.Ordinal)) Add("SF-FRAME-001","authority","Frames","canonical order","different order","Authority frame order is not canonical.");

        var expected=StoryFrameIndexProjector.Project(a,e.Checksum); CompareIndex(expected,index,Add);
        ReconcileDiagnostics(a,e,d,compatibility,Add);
        if(compatibility is not null) { CheckCompat(a.BuilderType,compatibility.CurrentBuilderType,"BuilderType"); CheckCompat(a.BuilderVersion,compatibility.CurrentBuilderVersion,"BuilderVersion"); CheckCompat(d.IntegrationServiceType,compatibility.CurrentIntegrationServiceType,"IntegrationServiceType"); CheckCompat(d.IntegrationServiceVersion,compatibility.CurrentIntegrationServiceVersion,"IntegrationServiceVersion"); }
        void CheckCompat(string stored,string current,string field) { if(string.IsNullOrWhiteSpace(current)||!string.Equals(stored,current,StringComparison.Ordinal)) Add("SF-COMPAT-001","complete-set",field,current,stored,$"Stored {field} is incompatible with the current runtime."); }
        return new(errors.Count==0,errors);
    }

    private static void CompareIndex(StoryFrameIndex expected,StoryFrameIndex actual,Action<string,string,string,string,object?,string,string?,string?,string?> add)
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var expectedSemantic=expected with { GeneratedUtc=default, Checksum="" }; var actualSemantic=actual with { GeneratedUtc=default, Checksum="" };
        if(JsonSerializer.Serialize(expectedSemantic,options)!=JsonSerializer.Serialize(actualSemantic,options)) add("SF-INDEX-001","index","Projection","exact authority projection","mismatch","Index does not exactly project the authority.",null,null,null);
        if(actual.Checksum!=StoryFrameAuthorityChecksum.Index(actual)) add("SF-CHECKSUM-001","index","Checksum","recomputed checksum",actual.Checksum,"Index checksum does not reconcile.",null,null,null);
    }
    private static void ReconcileDiagnostics(StoryFramesAuthority a,DocumentaryBlueprintEditorialContract e,StoryFrameDiagnostics d,StoryFrameValidationCompatibilityContext? compatibility,Action<string,string,string,string,object?,string,string?,string?,string?> add)
    {
        var paths=new[]{"05-editorial/blueprint-certification.json","05-editorial/editorial-contract.json","05-editorial/certification-diagnostics.json"};
        var checksums=new Dictionary<string,string>{{"certification",a.SourceCertificationChecksum},{"editorialContract",a.SourceEditorialContractChecksum},{"phase4",a.SourcePhase4Checksum}};
        var perVariant=a.Frames.GroupBy(f=>f.Variant,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.OrdinalIgnoreCase); var perScene=a.Frames.GroupBy(f=>$"{f.Variant}:{f.SceneId}",StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.OrdinalIgnoreCase);
        bool dictionaries=DictionaryEqual(d.InputArtifactChecksums,checksums)&&DictionaryEqual(d.FramesPerVariant,perVariant)&&DictionaryEqual(d.FramesPerScene,perScene);
        bool counts=d.ExecutionId==a.ExecutionId&&d.BuilderType==a.BuilderType&&d.BuilderVersion==a.BuilderVersion&&d.InputSceneCount==e.SceneOrder.Count&&d.GeneratedSceneCount==a.Frames.Select(f=>f.SceneId).Distinct(StringComparer.Ordinal).Count()&&d.GeneratedVariantSceneCount==perScene.Count&&d.GeneratedFrameCount==a.Frames.Count&&d.NarrationFrameCount==a.Frames.Count(f=>f.NarrationRequired)&&d.VisualFrameCount==a.Frames.Count(f=>f.ImageRequirements.Count+f.BrollRequirements.Count>0)&&d.ImageRequirementCount==a.Frames.Sum(f=>f.ImageRequirements.Count)&&d.BrollRequirementCount==a.Frames.Sum(f=>f.BrollRequirements.Count)&&d.OverlayRequirementCount==a.Frames.Sum(f=>f.OverlayRequirements.Count)&&d.WarningCount==a.Frames.Sum(f=>f.Warnings.Count)&&d.BlockingIssueCount==a.Frames.Sum(f=>f.BlockingConstraints.Count);
        bool stages=d.ValidationStagesExecuted.Distinct(StringComparer.Ordinal).Count()==d.ValidationStagesExecuted.Count&&RequiredStages.All(s=>d.ValidationStagesExecuted.Contains(s,StringComparer.Ordinal));
        if(!d.InputArtifactPaths.SequenceEqual(paths,StringComparer.Ordinal)||d.InputArtifactPaths.Any(p=>Path.IsPathRooted(p)||p.Contains("..")||p.Contains("staging",StringComparison.OrdinalIgnoreCase)||p.Contains("backup",StringComparison.OrdinalIgnoreCase))||!dictionaries||!counts||!stages||d.BuildDurationMilliseconds<0) add("SF-DIAG-001","diagnostics","Reconciliation","exact generated projection","mismatch","Diagnostics do not exactly reconcile.",null,null,null);
        static bool DictionaryEqual<T>(IReadOnlyDictionary<string,T> x,IReadOnlyDictionary<string,T> y)=>x.Count==y.Count&&x.All(kv=>y.TryGetValue(kv.Key,out var value)&&EqualityComparer<T>.Default.Equals(kv.Value,value));
    }
}
