namespace Astronomy.MediaFactory.Core;

public static class Phase14ReasonCodes
{
    public const string UpstreamMissing = "P14_UPSTREAM_AUTHORITY_MISSING";
    public const string UpstreamInvalid = "P14_UPSTREAM_AUTHORITY_INVALID";
    public const string SceneLineageInvalid = "P14_SCENE_LINEAGE_INVALID";
    public const string NarrationFidelityFailed = "P14_NARRATION_FIDELITY_FAILED";
    public const string SceneMappingInvalid = "P14_SCENE_MAPPING_INVALID";
    public const string CuePlanInvalid = "P14_CUE_PLAN_INVALID";
    public const string UnitTooLarge = "P14_SCENE_AUDIO_UNIT_TOO_LARGE";
    public const string CandidateInvalid = "P14_CANDIDATE_VALIDATION_FAILED";
    public const string CommitFailed = "P14_COMMIT_FAILED";
    public const string ReadbackFailed = "P14_COMMITTED_READBACK_FAILED";
    public const string Accepted = "P14_AUDIO_SYNC_AUTHORITY_ACCEPTED";
}

public enum AudioSyncBreakReason { None, Sentence, Paragraph, Scene, ExplicitNarrationBreak }

public sealed record SubtitleSegment(string SubtitleSegmentId, int SequenceWithinScene,
    string SceneAudioUnitId, string SceneId, IReadOnlyList<string> SentenceIds, string Text,
    string TextChecksum, int EstimatedReadingDurationMs, string Line1, string? Line2,
    int? SourceCharacterStart, int? SourceCharacterEnd, AudioSyncBreakReason BreakReason);

public sealed record SceneAudioUnit(string SceneAudioUnitId, int Sequence, string Format,
    string Language, string SceneId, string NarrationBeatId, IReadOnlyList<string> SentenceIds,
    int SentenceStartIndex, int SentenceEndIndex, string Text, string TextChecksum,
    int EstimatedSpeechDurationMs, int PauseBeforeMs, int PauseAfterMs,
    AudioSyncBreakReason BreakReason, IReadOnlyList<SubtitleSegment> SubtitleSegments,
    string VoiceProfileRef, string SpeechStyleRef, bool MayCrossSceneBoundary,
    IReadOnlyList<string> SourceNarrationAuthorityRefs, IReadOnlyList<string> SourceSceneAuthorityRefs);

public sealed record Phase14AudioSyncStream(string Format, int NarratedSceneCount,
    IReadOnlyList<SceneAudioUnit> SceneAudioUnits, string SourceNarrationChecksum,
    string CuePlanNarrationChecksum, bool TextFidelityPassed);

public sealed record Phase14AudioSyncAuthority(string SchemaVersion, string PlanId,
    string ExecutionId, string EventId, string Language, string Phase7AuthorityChecksum,
    string SceneAuthorityChecksum, string SyncPolicyVersion, string GroupingPolicyVersion,
    string GroupingPolicyChecksum, string RequestIdentity, Phase14AudioSyncStream ShortStream,
    Phase14AudioSyncStream LongStream, string AuthorityChecksum, string PublicationState);

/// <summary>The single committed Phase 14 result projected to the pipeline/API.</summary>
public sealed record Phase14PublicationResult(IReadOnlyList<string> LoadedAuthorityArtifacts,
    IReadOnlyList<string> OutputFiles, string ReasonCode, string Reason,
    bool PublicationCommitted, bool CommittedStateValidationPassed, string AuthorityChecksum,
    string ManifestValidationStatus, string ValidationStatus, bool SemanticValidationPassed,
    bool ChecksumValidationPassed, bool ManifestValidationPassed, bool DownstreamReady);


/// <summary>The single committed Phase 15 result projected to validation and the API.</summary>
public sealed record Phase15PublicationResult(IReadOnlyList<string> LoadedAuthorityArtifacts,
    IReadOnlyList<string> OutputFiles, string ReasonCode, string Reason, bool Generated, bool Reused,
    bool Regenerated, bool CandidateValidationPassed, bool CandidateReadbackPassed,
    bool PublicationCommitted, bool CommittedReadbackPassed, bool CommittedStateValidationPassed,
    string SourcePhase14AuthorityChecksum, string AuthorityChecksum, string ManifestValidationStatus,
    string ValidationStatus, bool SemanticValidationPassed, bool ChecksumValidationPassed,
    bool ManifestValidationPassed, bool DownstreamReady);
