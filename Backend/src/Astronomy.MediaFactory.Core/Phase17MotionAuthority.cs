namespace Astronomy.MediaFactory.Core;

public enum Phase17MotionType { Static, SlowZoomIn, SlowZoomOut, PanLeft, PanRight, PushToObject }
public enum Phase17Easing { Linear, EaseInOutSine, EaseOutCubic }
public enum Phase17SafetyDecision { CertifiedRegionSafe, FullFrameTransformSafe, StaticFallbackNoCertifiedFocus, StaticFallbackCropUnsafe }
public enum Phase17TransitionType { Cut }
public sealed record Phase17NormalizedTransform(double Scale, double TranslateX, double TranslateY);
public sealed record Phase17Keyframe(double NormalizedTime, Phase17NormalizedTransform Transform);
public sealed record Phase17NormalizedRegion(double X, double Y, double Width, double Height);
public sealed record Phase17Transition(Phase17TransitionType Type, long DurationMs);
public sealed record Phase17MotionEntry(string SceneId, string SceneAudioUnitId, string Format, int Sequence,
    string Language, long DurationMs, long SceneStartMs, long SceneEndMs, IReadOnlyList<string> SubtitleSegmentIds,
    string AudioSha256, string VisualAssetPath, string VisualAssetSha256, int Width, int Height,
    string TargetAspectFamily, string SourcePhase16AuthorityChecksum, string SourceVisualAuthorityChecksum,
    string Phase10CertificationChecksum, string SemanticRole, Phase17MotionType MotionType,
    Phase17NormalizedTransform StartTransform, Phase17NormalizedTransform EndTransform,
    IReadOnlyList<Phase17Keyframe> Keyframes, Phase17Easing Easing, Phase17NormalizedRegion? SafeArea,
    Phase17NormalizedRegion? FocusRegion, IReadOnlyList<Phase17NormalizedRegion> RequiredVisibleRegions,
    Phase17SafetyDecision SafetyDecision, bool SafetyFallbackApplied, Phase17Transition TransitionIn,
    Phase17Transition TransitionOut, string MotionPolicyVersion, string SafetyPolicyVersion);
public sealed record Phase17MotionPlan(string SchemaVersion, string Language, string Format, int SceneCount,
    string MotionPolicyVersion, string SafetyPolicyVersion, string SourcePhase16AuthorityChecksum,
    string SourceVisualAuthorityChecksum, IReadOnlyList<Phase17MotionEntry> Entries, string AuthorityChecksum);
public sealed record Phase17PublicationResult(IReadOnlyList<string> LoadedAuthorityArtifacts,
    IReadOnlyList<string> OutputFiles, string ReasonCode, string Reason, bool Generated, bool Reused,
    bool Regenerated, bool CandidateValidationPassed, bool CandidateReadbackPassed, bool PublicationCommitted,
    bool CommittedReadbackPassed, bool CommittedStateValidationPassed, bool SemanticValidationPassed,
    bool ChecksumValidationPassed, bool ManifestValidationPassed, string SourcePhase16AuthorityChecksum,
    string SourceVisualAuthorityChecksum, string AuthorityChecksum, string ValidationStatus, bool DownstreamReady);
