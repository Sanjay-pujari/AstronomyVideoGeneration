using System.Collections.ObjectModel;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.ProductionAdapters;

public enum DocumentaryProductionExecutionMode { Legacy, Shadow, Certified }
public enum DocumentaryPhysicalArtifactKind { VisualImage, NarrationAudio, SubtitleDocument, SceneVideo, VariantVideo, Diagnostic, ProviderIntermediate }
public enum DocumentaryProductionOperationKind { ExecutionPreparation, VisualGeneration, NarrationSynthesis, AudioNormalization, SubtitleGeneration, SceneComposition, VariantComposition, MediaVerification, Checksum, ArtifactFinalization, ManifestPersistence, Cleanup }
public enum DocumentaryProductionFailureCode { ConfigurationMissing, AdapterUnavailable, ProviderUnavailable, ProviderAuthenticationFailed, ProviderRateLimited, ProviderTimeout, ProviderRejectedRequest, ProviderInvalidResponse, ProviderContentPolicyRejected, SourceArtifactMissing, SourceArtifactInvalid, OutputArtifactMissing, OutputArtifactEmpty, OutputFormatInvalid, ChecksumFailed, DurationMeasurementFailed, DimensionMismatch, AudioStreamMissing, VideoStreamMissing, SubtitleMissing, DependencyMissing, ProcessStartFailed, ProcessTimedOut, ProcessExitedWithError, FileSystemFailure, Cancelled }

public sealed record DocumentaryProductionFailure(DocumentaryProductionFailureCode Code, string Message, bool Retryable = false, string? ProviderId = null, string? DiagnosticReference = null);
public sealed record DocumentaryProductionExecutionContext(string ExecutionId, string CorrelationId, DocumentaryProductionExecutionMode ExecutionMode, string WorkspaceRoot, DateTimeOffset StartedAtUtc, IReadOnlyDictionary<string,string> Metadata);
public sealed record DocumentaryProductionAttemptContext(string ExecutionId, string CorrelationId, DocumentaryProductionOperationKind OperationKind, string AssetId, string? VariantId, string? SceneId, int AttemptNumber, string ProviderId, DateTimeOffset StartedAtUtc, TimeSpan Timeout);
public sealed record DocumentaryProductionWorkspace(string Root, string ExecutionRoot, string VariantsDirectory, string AttemptsDirectory, string DiagnosticsDirectory);
public sealed record DocumentaryPhysicalArtifactDescriptor(string AssetId, string ContentIdentity, string PhysicalPath, string ContentType, long Length, string Checksum, long? DurationMilliseconds, int? Width, int? Height, decimal? FrameRate, int? AudioSampleRate, int? AudioChannelCount, string ProviderId, int AttemptCount, string CorrelationId);
public sealed record DocumentaryPhysicalArtifactInspectionRequest(string AssetId, string PhysicalPath, string ContentType, string ProviderId, int AttemptCount, string CorrelationId, bool ProbeMedia = false);
public sealed record DocumentaryMediaProbeResult(bool Succeeded, long? DurationMilliseconds = null, bool? HasVideoStream = null, bool? HasAudioStream = null, bool? HasSubtitleStream = null, int? Width = null, int? Height = null, decimal? FrameRate = null, int? AudioSampleRate = null, int? AudioChannelCount = null, string? ContainerFormat = null, DocumentaryProductionFailure? Failure = null);
public sealed record DocumentaryArtifactMappingContext(DocumentaryMediaAssetPlan Plan, DocumentaryPhysicalArtifactDescriptor Descriptor);
public sealed record DocumentaryRegisteredPhysicalArtifact(DocumentaryPhysicalArtifactDescriptor Descriptor, DocumentaryPhysicalArtifactKind Kind);

public interface IDocumentaryProductionExecutionHost { Task<DocumentaryMediaPipelineExecutionRecord?> ExecuteAsync(DocumentaryMediaPipelineRequest request, CancellationToken cancellationToken); }
public interface IDocumentaryProductionClock { DateTimeOffset UtcNow { get; } }
public interface IDocumentaryExecutionIdGenerator { string Create(); }
public interface IDocumentaryProductionExecutionContextFactory { DocumentaryProductionExecutionContext Create(DocumentaryMediaPipelineRequest request, IReadOnlyDictionary<string,string>? metadata = null); }
public interface IDocumentarySafeFileNameGenerator { string Create(string logicalId, int maximumLength = 100); }
public interface IDocumentaryChecksumService { Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken); }
public interface IDocumentaryContentIdentityFactory { string Create(string checksum); bool IsValid(string contentIdentity); }
public interface IDocumentaryPhysicalArtifactDescriptorValidator { IReadOnlyList<string> Validate(DocumentaryPhysicalArtifactDescriptor descriptor); }
public interface IDocumentaryMediaProbe { Task<DocumentaryMediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken); }
public interface IDocumentaryPhysicalArtifactInspector { Task<DocumentaryPhysicalArtifactDescriptor> InspectAsync(DocumentaryPhysicalArtifactInspectionRequest request, CancellationToken cancellationToken); }
public interface IDocumentaryProductionWorkspaceManager {
 Task<DocumentaryProductionWorkspace> CreateAsync(DocumentaryProductionExecutionContext context, CancellationToken cancellationToken);
 string GetVariantDirectory(DocumentaryProductionWorkspace workspace, string variantId);
 string GetSceneDirectory(DocumentaryProductionWorkspace workspace, string variantId, int sequence);
 string GetAttemptDirectory(DocumentaryProductionWorkspace workspace, DocumentaryProductionOperationKind operation, string assetId, int attempt);
 string GetFinalArtifactPath(DocumentaryProductionWorkspace workspace, string variantId, int? sceneSequence, DocumentaryPhysicalArtifactKind kind, string assetId, string extension);
 Task FinalizeArtifactAsync(DocumentaryProductionWorkspace workspace, string temporaryPath, string finalPath, bool allowReplace, CancellationToken cancellationToken);
 Task<string> QuarantineAttemptAsync(DocumentaryProductionWorkspace workspace, string attemptDirectory, CancellationToken cancellationToken);
 Task CleanupSuccessfulAttemptAsync(DocumentaryProductionWorkspace workspace, string attemptDirectory, CancellationToken cancellationToken);
}
public interface IDocumentaryPhysicalArtifactRegistry { Task RegisterAsync(DocumentaryPhysicalArtifactDescriptor descriptor, DocumentaryPhysicalArtifactKind kind, CancellationToken cancellationToken); Task<DocumentaryPhysicalArtifactDescriptor?> GetAsync(string assetId, CancellationToken cancellationToken); Task<DocumentaryRegisteredPhysicalArtifact?> GetRegisteredAsync(string assetId, CancellationToken cancellationToken); Task<IReadOnlyCollection<DocumentaryPhysicalArtifactDescriptor>> GetAllAsync(string correlationId, CancellationToken cancellationToken); Task PersistAsync(string diagnosticsDirectory, CancellationToken cancellationToken); }
public interface IDocumentaryProductionFailureNormalizer { DocumentaryProductionFailure Normalize(Exception exception, DocumentaryProductionOperationKind operation, bool callerCancelled); }
public interface IDocumentaryProductionDiagnosticsWriter { Task WriteAsync(string diagnosticsDirectory, string fileName, object value, CancellationToken cancellationToken); }
public interface IDocumentaryProductionAdapterRegistry { IDocumentaryProductionVisualAdapter? VisualGeneration { get; } IDocumentaryProductionNarrationAdapter? NarrationSynthesis { get; } IDocumentaryProductionSubtitleAdapter? SubtitleGeneration { get; } IDocumentaryProductionSceneCompositionAdapter? SceneComposition { get; } IDocumentaryProductionVariantCompositionAdapter? VariantComposition { get; } IDocumentaryProductionMediaVerificationAdapter? MediaVerification { get; } bool IsAvailable(DocumentaryProductionOperationKind operation); }
public interface IDocumentaryProductionExecutionRecordMapper { DocumentaryMediaAssetResult MapAsset(DocumentaryArtifactMappingContext context); }

public sealed class DocumentaryProductionAdaptersOptions {
 public const string SectionName="DocumentaryProductionAdapters";
 public bool Enabled {get;set;} public DocumentaryProductionExecutionMode ExecutionMode {get;set;}=DocumentaryProductionExecutionMode.Legacy;
 public string WorkspaceRoot {get;set;}=string.Empty; public bool UseExistingOutputLayout {get;set;}=true; public bool RetainProviderIntermediates {get;set;}
 public bool EnableLegacyFallback {get;set;} public int CleanupTimeoutSeconds {get;set;}=30; public int DefaultOperationTimeoutSeconds {get;set;}=300;
}

internal static class ImmutableMetadata {
 public static IReadOnlyDictionary<string,string> Copy(IReadOnlyDictionary<string,string>? source) => new ReadOnlyDictionary<string,string>(new Dictionary<string,string>(source ?? new Dictionary<string,string>(), StringComparer.Ordinal));
}
