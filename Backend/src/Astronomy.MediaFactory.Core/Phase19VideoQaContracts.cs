namespace Astronomy.MediaFactory.Core;

public static class Phase19ReasonCodes
{
    public const string UpstreamPhase18Invalid = "P19_UPSTREAM_PHASE18_INVALID";
    public const string VideoMissing = "P19_VIDEO_MISSING";
    public const string VideoHashMismatch = "P19_VIDEO_HASH_MISMATCH";
    public const string VideoStreamInvalid = "P19_VIDEO_STREAM_INVALID";
    public const string AudioStreamInvalid = "P19_AUDIO_STREAM_INVALID";
    public const string SubtitleQaFailed = "P19_SUBTITLE_QA_FAILED";
    public const string MotionQaFailed = "P19_MOTION_QA_FAILED";
    public const string FadeQaFailed = "P19_FADE_QA_FAILED";
    public const string TransitionQaFailed = "P19_TRANSITION_QA_FAILED";
    public const string NarrationQaFailed = "P19_NARRATION_QA_FAILED";
    public const string MusicQaFailed = "P19_MUSIC_QA_FAILED";
    public const string Accepted = "P19_FINAL_VIDEO_QA_AUTHORITY_ACCEPTED";
}

public sealed record Phase19StreamEvidence(string Codec, int Width, int Height, string PixelFormat,
    double FramesPerSecond, long DurationMs, int SampleRate, int Channels, string? ChannelLayout,
    long? FrameCount);
public sealed record Phase19MotionSample(long TimestampMs, double MeanAbsoluteLumaDifference);
public sealed record Phase19SceneQaEvidence(string SceneId, string SceneAudioUnitId, int Sequence,
    string MotionType, IReadOnlyList<Phase19MotionSample> MotionSamples, double MotionThreshold,
    bool MotionPassed, bool NarrationPassed, bool FadePassed, bool TransitionPassed);
public sealed record Phase19FormatQaEvidence(string Format, string VideoRelativePath, string VideoSha256,
    long VideoByteLength, long GovernedDurationMs, long PhysicalDurationMs,
    Phase19StreamEvidence VideoStream, Phase19StreamEvidence AudioStream, bool SubtitlePassed,
    bool NarrationPassed, bool MusicPassed, IReadOnlyList<Phase19SceneQaEvidence> Scenes, bool Passed);
public sealed record Phase19Manifest(string SchemaVersion, string Language, string SourcePhase18AuthorityChecksum,
    IReadOnlyList<string> RequestedFormats, string QaPolicyVersion, string DurationValidationMode,
    IReadOnlyList<Phase19FormatQaEvidence> Outputs, string AuthorityChecksum, bool TechnicalQaApproved,
    bool PublicationCommitted, string ValidationStatus, bool DownstreamReady);
public sealed record Phase19PublicationResult(IReadOnlyList<string> InputFiles, IReadOnlyList<string> OutputFiles,
    string ReasonCode, string Reason, string SourcePhase18AuthorityChecksum, string AuthorityChecksum,
    bool PublicationCommitted, bool CommittedReadbackPassed, bool CommittedStateValidationPassed,
    bool SemanticValidationPassed, bool ChecksumValidationPassed, bool ManifestValidationPassed,
    string ValidationStatus, bool TechnicalQaApproved, bool DownstreamReady);

public sealed class Phase19AuthorityValidationException(string reasonCode, string reason,
    IReadOnlyList<string> loadedAuthorityArtifacts) : InvalidOperationException($"{reasonCode}: {reason}")
{
    public string ReasonCode { get; } = reasonCode;
    public string Reason { get; } = reason;
    public IReadOnlyList<string> LoadedAuthorityArtifacts { get; } = loadedAuthorityArtifacts;
}
