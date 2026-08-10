namespace Astronomy.MediaFactory.Core;

public static class Phase18ReasonCodes
{
    public const string UpstreamPhase15Invalid = "P18_UPSTREAM_PHASE15_INVALID";
    public const string UpstreamPhase16Invalid = "P18_UPSTREAM_PHASE16_INVALID";
    public const string UpstreamPhase17Invalid = "P18_UPSTREAM_PHASE17_INVALID";
    public const string LineageMismatch = "P18_LINEAGE_MISMATCH";
    public const string VisualPhysicalEvidenceInvalid = "P18_VISUAL_PHYSICAL_EVIDENCE_INVALID";
    public const string AudioPhysicalEvidenceInvalid = "P18_AUDIO_PHYSICAL_EVIDENCE_INVALID";
    public const string SubtitlePhysicalEvidenceInvalid = "P18_SUBTITLE_PHYSICAL_EVIDENCE_INVALID";
    public const string RenderFailed = "P18_RENDER_FAILED";
    public const string VideoValidationFailed = "P18_VIDEO_VALIDATION_FAILED";
    public const string CandidateValidationFailed = "P18_CANDIDATE_VALIDATION_FAILED";
    public const string CommitFailed = "P18_COMMIT_FAILED";
    public const string CommittedReadbackFailed = "P18_COMMITTED_READBACK_FAILED";
    public const string Accepted = "P18_VIDEO_ASSEMBLY_AUTHORITY_ACCEPTED";
}

public enum Phase18SubtitleMode { SidecarOnly, BurnInAndSidecar }
public sealed record Phase18VideoPolicy(string Version, string Codec, string Encoder, string PixelFormat,
    int FramesPerSecond, string Preset, int Crf, int ShortWidth, int ShortHeight, int LongWidth, int LongHeight);
public sealed record Phase18AudioPolicy(string Version, string Codec, int SampleRate, int Channels, int Bitrate);
public sealed record Phase18SubtitlePolicy(string Version, Phase18SubtitleMode EnglishMode,
    Phase18SubtitleMode HindiMode, string? BurnInFontFamily);
public sealed record Phase18MediaEvidence(string Format, string VideoRelativePath, string SubtitleRelativePath,
    long GovernedDurationMs, long PhysicalDurationMs, int Width, int Height, string VideoCodec,
    string PixelFormat, string AudioCodec, int AudioSampleRate, int AudioChannels, string VideoSha256,
    long VideoByteLength, string SubtitleSha256, long SubtitleByteLength,
    IReadOnlyList<string> SourceAudioSha256);
public sealed record Phase18Manifest(string SchemaVersion, string Language, IReadOnlyList<string> RequestedFormats,
    string SourcePhase15AuthorityChecksum, string SourcePhase16AuthorityChecksum,
    string SourcePhase17AuthorityChecksum, string RenderPolicyVersion, string CodecPolicyVersion,
    string AudioPolicyVersion, string SubtitlePolicyVersion, string ToolchainIdentity,
    IReadOnlyList<Phase18MediaEvidence> Outputs, string AuthorityChecksum, bool PublicationCommitted,
    string ValidationStatus, bool DownstreamReady);
public sealed record Phase18PublicationResult(IReadOnlyList<string> InputFiles, IReadOnlyList<string> OutputFiles,
    string ReasonCode, string Reason, bool Generated, bool Reused, bool Regenerated,
    bool CandidateValidationPassed, bool CandidateReadbackPassed, bool PublicationCommitted,
    bool CommittedReadbackPassed, bool CommittedStateValidationPassed,
    string SourcePhase15AuthorityChecksum, string SourcePhase16AuthorityChecksum,
    string SourcePhase17AuthorityChecksum, string AuthorityChecksum, string ManifestValidationStatus,
    string ValidationStatus, bool SemanticValidationPassed, bool ChecksumValidationPassed,
    bool ManifestValidationPassed, bool DownstreamReady);

/// <summary>A Phase 18 view of the facts owned by the frozen Phase 15 artifacts.</summary>
public sealed record Phase18Phase15AuthoritySnapshot(string Language, string AuthorityChecksum,
    string SourcePhase14AuthorityChecksum, bool PublicationCommitted, bool CandidateValidationPassed,
    bool CandidateReadbackPassed, bool CommittedReadbackPassed, bool CommittedStateValidationPassed,
    bool SemanticValidationPassed, bool ChecksumValidationPassed, bool ManifestValidationPassed,
    string ValidationStatus, bool DownstreamReady, IReadOnlyList<string> LoadedAuthorityArtifacts);

/// <summary>Unambiguous Phase 18 interpretation of the three distinct upstream authorities.</summary>
public sealed record Phase18AuthorityLineageValidation(
    string Phase15AuthorityChecksum,
    string Phase15SourcePhase14AuthorityChecksum,
    string Phase16AuthorityChecksum,
    string Phase16SourcePhase15AuthorityChecksum,
    string Phase17AuthorityChecksum,
    string Phase17SourcePhase16AuthorityChecksum,
    bool Phase15To16LineagePassed,
    bool Phase16To17LineagePassed,
    bool OverallLineagePassed);

public sealed record Phase18SceneLineageRow(string SceneAudioUnitId, string SceneId, string Format,
    int Sequence, string Language, string AudioSha256, long DurationMs, long SceneStartMs, long SceneEndMs);

public sealed class Phase18AuthorityValidationException : InvalidOperationException
{
    public Phase18AuthorityValidationException(string reasonCode, string reason,
        IReadOnlyList<string> loadedAuthorityArtifacts) : base($"{reasonCode}: {reason}")
    { ReasonCode = reasonCode; Reason = reason; LoadedAuthorityArtifacts = loadedAuthorityArtifacts; }
    public string ReasonCode { get; }
    public string Reason { get; }
    public IReadOnlyList<string> LoadedAuthorityArtifacts { get; }
}
