using System.Collections.Concurrent;
using System.Security.Cryptography;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

internal static class DocumentaryProductionExecutionHostTestFixtures
{
 public static string CreateWorkspaceRoot() { var path = Path.Combine(Path.GetTempPath(), "astronomy-a3-10", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
 public static DocumentaryPhysicalArtifactDescriptor Descriptor(string root, string assetId, DocumentaryPhysicalArtifactKind kind, int sequence = 1)
 {
  var extension = kind == DocumentaryPhysicalArtifactKind.VisualImage ? "png" : kind == DocumentaryPhysicalArtifactKind.NarrationAudio ? "wav" : kind == DocumentaryPhysicalArtifactKind.SubtitleDocument ? "srt" : "mp4";
  var path = Path.Combine(root, $"{sequence:D3}-{assetId}.{extension}"); File.WriteAllText(path, $"A3.10|{kind}|{assetId}");
  var sum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
  return new(assetId, "sha256:" + sum, path, kind == DocumentaryPhysicalArtifactKind.VisualImage ? "image/png" : kind == DocumentaryPhysicalArtifactKind.NarrationAudio ? "audio/wav" : kind == DocumentaryPhysicalArtifactKind.SubtitleDocument ? "application/x-subrip" : "video/mp4", new FileInfo(path).Length, sum, kind is DocumentaryPhysicalArtifactKind.VisualImage or DocumentaryPhysicalArtifactKind.SubtitleDocument ? null : 1000, kind is DocumentaryPhysicalArtifactKind.VisualImage or DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo ? 1920 : null, kind is DocumentaryPhysicalArtifactKind.VisualImage or DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo ? 1080 : null, kind is DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo ? 30 : null, kind is DocumentaryPhysicalArtifactKind.NarrationAudio or DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo ? 48000 : null, kind is DocumentaryPhysicalArtifactKind.NarrationAudio or DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo ? 2 : null, "deterministic-fake", 1, "correlation-a3-10");
 }
}

internal enum FakeProductionAdapterOutcomeKind { Success, RetryableFailure, NonRetryableFailure, ThrowException, WaitUntilCancelled, SuccessWithoutRegistration, VerificationRejected }
internal sealed record FakeProductionAdapterOutcome(FakeProductionAdapterOutcomeKind Kind, DocumentaryProductionFailure? Failure = null, Exception? Exception = null)
{
 public static FakeProductionAdapterOutcome Success { get; } = new(FakeProductionAdapterOutcomeKind.Success);
}

internal sealed class DocumentaryProductionExecutionHarnessOptions
{
 public bool HostEnabled { get; init; } = true;
 public bool IncludeVisualAdapter { get; init; } = true;
 public bool IncludeNarrationAdapter { get; init; } = true;
 public bool IncludeSubtitleAdapter { get; init; } = true;
 public bool IncludeSceneCompositionAdapter { get; init; } = true;
 public bool IncludeVariantCompositionAdapter { get; init; } = true;
 public bool IncludeVerificationAdapter { get; init; } = true;
 public bool FailManifestPersistence { get; init; }
 public string? FailDiagnosticsFileName { get; init; }
 public bool CancelManifestPersistence { get; init; }
 public bool CancelExecutionRecordPersistence { get; init; }
 public Func<IDocumentaryProductionClock>? ClockFactory { get; init; }
 public bool ContinueOtherVariantsAfterVariantFailure { get; init; } = true;
 public bool VerifySceneVideos { get; init; } = true;
 public bool VerifyFinalVariants { get; init; } = true;
 public bool PersistArtifactManifest { get; init; } = true;
 public bool PersistExecutionRecord { get; init; } = true;
 public int MaximumAttemptsPerOperation { get; init; } = 1;
 public int OperationTimeoutMilliseconds { get; init; } = 5000;
 public Func<DocumentaryMediaPipelineRequest>? RequestFactory { get; init; }
}

internal sealed class DocumentaryProductionExecutionHostHarness : IAsyncDisposable
{
 public DocumentaryMediaPipelineRequest Request { get; }
 public IDocumentaryProductionExecutionCoordinator Coordinator { get; }
 public IDocumentaryProductionExecutionHost CompatibilityHost { get; }
 public FakeDocumentaryProductionAdapterRegistry AdapterRegistry { get; }
 public ControlledPhysicalArtifactRegistry ArtifactRegistry { get; }
 public RecordingDocumentaryProductionDiagnosticsWriter DiagnosticsWriter { get; }
 public RecordingWorkspaceManager WorkspaceManager { get; }
 public string WorkspaceRoot { get; }
 public DocumentaryProductionExecutionHostOptions HostOptions { get; }
 public DocumentaryProductionAdaptersOptions BridgeOptions { get; }
 public IReadOnlyList<string> InvocationOrder => AdapterRegistry.InvocationOrder.ToArray();

 public DocumentaryProductionExecutionHostHarness(DocumentaryProductionExecutionHarnessOptions? settings = null)
 {
  settings ??= new();
  WorkspaceRoot = Path.Combine(Path.GetTempPath(), "astronomy-a3-10", Guid.NewGuid().ToString("N"));
  Request = settings.RequestFactory?.Invoke() ?? CreateRequest();
  BridgeOptions = new() { Enabled = true, ExecutionMode = DocumentaryProductionExecutionMode.Certified, WorkspaceRoot = WorkspaceRoot, DefaultOperationTimeoutSeconds = Math.Max(1, (int)Math.Ceiling(settings.OperationTimeoutMilliseconds / 1000d)) };
  HostOptions = new()
  {
   Enabled = settings.HostEnabled,
   MaximumAttemptsPerOperation = settings.MaximumAttemptsPerOperation,
   ContinueOtherVariantsAfterVariantFailure = settings.ContinueOtherVariantsAfterVariantFailure,
   VerifySceneVideos = settings.VerifySceneVideos,
   VerifyFinalVariants = settings.VerifyFinalVariants,
   PersistArtifactManifest = settings.PersistArtifactManifest,
   PersistExecutionRecord = settings.PersistExecutionRecord,
   OperationTimeoutMilliseconds = settings.OperationTimeoutMilliseconds
  };
  ArtifactRegistry = new ControlledPhysicalArtifactRegistry { ThrowOnPersist = settings.FailManifestPersistence, CancelOnPersist = settings.CancelManifestPersistence };
  DiagnosticsWriter = new RecordingDocumentaryProductionDiagnosticsWriter { ThrowOnFileName = settings.FailDiagnosticsFileName, CancelOnFileName = settings.CancelExecutionRecordPersistence ? "documentary-production-execution.json" : null };
  AdapterRegistry = new FakeDocumentaryProductionAdapterRegistry(ArtifactRegistry, settings);
  var clock = settings.ClockFactory?.Invoke() ?? new SystemDocumentaryProductionClock();
  var attemptFactory = new DocumentaryProductionAttemptContextFactory(clock);
  var runner = new DocumentaryProductionOperationRunner(attemptFactory, new DocumentaryProductionFailureNormalizer());
  var contextFactory = new DocumentaryProductionExecutionContextFactory(clock, new DocumentaryExecutionIdGenerator(), Options.Create(BridgeOptions));
  WorkspaceManager = new RecordingWorkspaceManager(new DocumentaryProductionWorkspaceManager(new DocumentarySafeFileNameGenerator(), new DocumentaryChecksumService()));
  var voices = new DocumentaryNarrationVoiceResolver(Options.Create(new AzureSpeechOptions { Voices = new Dictionary<string, string> { { "en", "en-IN-NeerjaNeural" }, { "hi", "hi-IN-SwaraNeural" } } }));
  Coordinator = new DocumentaryProductionExecutionCoordinator(Options.Create(HostOptions), Options.Create(BridgeOptions), contextFactory, WorkspaceManager, DiagnosticsWriter, ArtifactRegistry, AdapterRegistry, attemptFactory, new DocumentaryProductionExecutionRequestBuilder(voices), new DocumentaryProductionExecutionDependencyResolver(ArtifactRegistry), new DocumentaryProductionExecutionRecordMapper(), runner, clock);
  CompatibilityHost = new DocumentaryProductionExecutionHost(Coordinator);
 }

 public Task<DocumentaryProductionExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default) => Coordinator.ExecuteAsync(Request, cancellationToken);
 public ValueTask DisposeAsync() { if (Directory.Exists(WorkspaceRoot)) Directory.Delete(WorkspaceRoot, true); return ValueTask.CompletedTask; }
 internal static DocumentaryMediaPipelineRequest CreateRequest() { var project = DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Orion()); return new(project, new DocumentaryMediaPipelinePolicy(DocumentaryMediaPipelineMode.Execute), new(project.Metadata.CreatedUtc, "a3.10-harness", project.Metadata.CorrelationId, $"{project.MediaProjectId}.execution.1")); }
}

internal sealed class RecordingDocumentaryProductionDiagnosticsWriter : IDocumentaryProductionDiagnosticsWriter
{
 readonly DocumentaryProductionDiagnosticsWriter inner = new();
 public string? ThrowOnFileName { get; set; }
 public string? CancelOnFileName { get; set; }
 public ConcurrentQueue<string> Files { get; } = new();
 public async Task WriteAsync(string directory, string fileName, object value, CancellationToken token)
 {
  if (fileName == CancelOnFileName) throw new OperationCanceledException(token);
  if (fileName == ThrowOnFileName) throw new IOException("Configured diagnostics persistence failure.");
  await inner.WriteAsync(directory, fileName, value, token);
  Files.Enqueue(Path.Combine(directory, fileName));
 }
}

internal sealed class ControlledPhysicalArtifactRegistry : IDocumentaryPhysicalArtifactRegistry
{
 readonly DocumentaryPhysicalArtifactRegistry inner = new();
 public bool ThrowOnPersist { get; set; }
 public bool CancelOnPersist { get; set; }
 public int AccessCount { get; private set; }
 public Task RegisterAsync(DocumentaryPhysicalArtifactDescriptor descriptor, DocumentaryPhysicalArtifactKind kind, CancellationToken token) { AccessCount++; return inner.RegisterAsync(descriptor, kind, token); }
 public Task<DocumentaryPhysicalArtifactDescriptor?> GetAsync(string id, CancellationToken token) { AccessCount++; return inner.GetAsync(id, token); }
 public Task<DocumentaryRegisteredPhysicalArtifact?> GetRegisteredAsync(string id, CancellationToken token) { AccessCount++; return inner.GetRegisteredAsync(id, token); }
 public Task<IReadOnlyCollection<DocumentaryPhysicalArtifactDescriptor>> GetAllAsync(string correlation, CancellationToken token) { AccessCount++; return inner.GetAllAsync(correlation, token); }
 public Task PersistAsync(string directory, CancellationToken token) { AccessCount++; if (CancelOnPersist) throw new OperationCanceledException(token); if (ThrowOnPersist) throw new IOException("Configured manifest failure."); return inner.PersistAsync(directory, token); }
}

internal sealed class RecordingWorkspaceManager(IDocumentaryProductionWorkspaceManager inner) : IDocumentaryProductionWorkspaceManager
{
 public int CreateCount { get; private set; }
 public Task<DocumentaryProductionWorkspace> CreateAsync(DocumentaryProductionExecutionContext context, CancellationToken token) { CreateCount++; return inner.CreateAsync(context, token); }
 public string GetVariantDirectory(DocumentaryProductionWorkspace w, string id) => inner.GetVariantDirectory(w, id);
 public string GetSceneDirectory(DocumentaryProductionWorkspace w, string id, int sequence) => inner.GetSceneDirectory(w, id, sequence);
 public string GetAttemptDirectory(DocumentaryProductionWorkspace w, DocumentaryProductionOperationKind operation, string id, int attempt) => inner.GetAttemptDirectory(w, operation, id, attempt);
 public string GetFinalArtifactPath(DocumentaryProductionWorkspace w, string id, int? sequence, DocumentaryPhysicalArtifactKind kind, string asset, string extension) => inner.GetFinalArtifactPath(w, id, sequence, kind, asset, extension);
 public Task FinalizeArtifactAsync(DocumentaryProductionWorkspace w, string temporary, string final, bool replace, CancellationToken token) => inner.FinalizeArtifactAsync(w, temporary, final, replace, token);
 public Task<string> QuarantineAttemptAsync(DocumentaryProductionWorkspace w, string directory, CancellationToken token) => inner.QuarantineAttemptAsync(w, directory, token);
 public Task CleanupSuccessfulAttemptAsync(DocumentaryProductionWorkspace w, string directory, CancellationToken token) => inner.CleanupSuccessfulAttemptAsync(w, directory, token);
}

internal sealed class FakeDocumentaryProductionAdapterRegistry : IDocumentaryProductionAdapterRegistry, IDocumentaryProductionVisualAdapter, IDocumentaryProductionNarrationAdapter, IDocumentaryProductionSubtitleAdapter, IDocumentaryProductionSceneCompositionAdapter, IDocumentaryProductionVariantCompositionAdapter, IDocumentaryProductionMediaVerificationAdapter
{
 readonly IDocumentaryPhysicalArtifactRegistry artifacts;
 readonly DocumentaryProductionExecutionHarnessOptions settings;
 public ConcurrentQueue<FakeProductionAdapterOutcome> VisualOutcomes { get; } = new();
 public ConcurrentQueue<FakeProductionAdapterOutcome> NarrationOutcomes { get; } = new();
 public ConcurrentQueue<FakeProductionAdapterOutcome> SubtitleOutcomes { get; } = new();
 public ConcurrentQueue<FakeProductionAdapterOutcome> SceneCompositionOutcomes { get; } = new();
 public ConcurrentQueue<FakeProductionAdapterOutcome> VariantCompositionOutcomes { get; } = new();
 public ConcurrentQueue<FakeProductionAdapterOutcome> SceneVerificationOutcomes { get; } = new();
 public ConcurrentQueue<FakeProductionAdapterOutcome> VariantVerificationOutcomes { get; } = new();
 public ConcurrentQueue<string> InvocationOrder { get; } = new();
 public ConcurrentQueue<DocumentaryProductionAttemptContext> Attempts { get; } = new();
 public ConcurrentQueue<DocumentaryVisualGenerationRequest> VisualRequests { get; } = new();
 public ConcurrentQueue<DocumentaryNarrationSynthesisRequest> NarrationRequests { get; } = new();
 public ConcurrentQueue<DocumentarySubtitleGenerationRequest> SubtitleRequests { get; } = new();
 public ConcurrentQueue<DocumentarySceneCompositionRequest> SceneCompositionRequests { get; } = new();
 public ConcurrentQueue<DocumentaryVariantCompositionRequest> VariantCompositionRequests { get; } = new();
 public ConcurrentQueue<DocumentaryMediaVerificationRequest> VerificationRequests { get; } = new();
 public TaskCompletionSource<bool> VisualStarted { get; } = Signal();
 public TaskCompletionSource<bool> NarrationStarted { get; } = Signal();
 public TaskCompletionSource<bool> SubtitleStarted { get; } = Signal();
 public TaskCompletionSource<bool> SceneCompositionStarted { get; } = Signal();
 public TaskCompletionSource<bool> VariantCompositionStarted { get; } = Signal();
 public TaskCompletionSource<bool> SceneVerificationStarted { get; } = Signal();
 public TaskCompletionSource<bool> VariantVerificationStarted { get; } = Signal();
 public IDocumentaryProductionVisualAdapter? VisualGeneration => settings.IncludeVisualAdapter ? this : null;
 public IDocumentaryProductionNarrationAdapter? NarrationSynthesis => settings.IncludeNarrationAdapter ? this : null;
 public IDocumentaryProductionSubtitleAdapter? SubtitleGeneration => settings.IncludeSubtitleAdapter ? this : null;
 public IDocumentaryProductionSceneCompositionAdapter? SceneComposition => settings.IncludeSceneCompositionAdapter ? this : null;
 public IDocumentaryProductionVariantCompositionAdapter? VariantComposition => settings.IncludeVariantCompositionAdapter ? this : null;
 public IDocumentaryProductionMediaVerificationAdapter? MediaVerification => settings.IncludeVerificationAdapter ? this : null;

 public FakeDocumentaryProductionAdapterRegistry(IDocumentaryPhysicalArtifactRegistry artifacts, DocumentaryProductionExecutionHarnessOptions settings) { this.artifacts = artifacts; this.settings = settings; }
 public bool IsAvailable(DocumentaryProductionOperationKind operation) => operation switch { DocumentaryProductionOperationKind.VisualGeneration => VisualGeneration is not null, DocumentaryProductionOperationKind.NarrationSynthesis => NarrationSynthesis is not null, DocumentaryProductionOperationKind.SubtitleGeneration => SubtitleGeneration is not null, DocumentaryProductionOperationKind.SceneComposition => SceneComposition is not null, DocumentaryProductionOperationKind.VariantComposition => VariantComposition is not null, DocumentaryProductionOperationKind.MediaVerification => MediaVerification is not null, _ => false };
 static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
 void Capture(string name, DocumentaryProductionAttemptContext attempt, TaskCompletionSource<bool> signal) { InvocationOrder.Enqueue(name); Attempts.Enqueue(attempt); signal.TrySetResult(true); }
 static FakeProductionAdapterOutcome Next(ConcurrentQueue<FakeProductionAdapterOutcome> queue) => queue.TryDequeue(out var value) ? value : FakeProductionAdapterOutcome.Success;
 static DocumentaryProductionFailure Failure(FakeProductionAdapterOutcome outcome, DocumentaryProductionFailureCode retryCode = DocumentaryProductionFailureCode.ProviderTimeout) => outcome.Failure ?? (outcome.Kind == FakeProductionAdapterOutcomeKind.RetryableFailure ? new(retryCode, retryCode == DocumentaryProductionFailureCode.ProcessTimedOut ? "The production process timed out." : "The provider operation timed out.", true) : new(DocumentaryProductionFailureCode.ProviderRejectedRequest, "The provider rejected the request."));
 static async Task Apply(FakeProductionAdapterOutcome outcome, CancellationToken token) { if (outcome.Kind == FakeProductionAdapterOutcomeKind.ThrowException) throw outcome.Exception ?? new InvalidOperationException("Configured adapter exception."); if (outcome.Kind == FakeProductionAdapterOutcomeKind.WaitUntilCancelled) await Task.Delay(Timeout.InfiniteTimeSpan, token); }
 async Task<DocumentaryPhysicalArtifactDescriptor> Make(DocumentaryMediaAssetPlan p, DocumentaryPhysicalArtifactKind kind, DocumentaryProductionWorkspace w, DocumentaryProductionAttemptContext a, string contentType, bool register, long? duration = null, int? width = null, int? height = null, decimal? fps = null, int? sample = null, int? channels = null)
 {
  var dir = Path.Combine(w.ExecutionRoot, "fake"); Directory.CreateDirectory(dir);
  var extension = kind == DocumentaryPhysicalArtifactKind.VisualImage ? "png" : kind == DocumentaryPhysicalArtifactKind.NarrationAudio ? "wav" : kind == DocumentaryPhysicalArtifactKind.SubtitleDocument ? "srt" : "mp4";
  var path = Path.Combine(dir, p.AssetId.Replace('/', '_') + "." + extension);
  await File.WriteAllTextAsync(path, $"deterministic|{kind}|{p.AssetId}");
  var sum = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
  var descriptor = new DocumentaryPhysicalArtifactDescriptor(p.AssetId, "sha256:" + sum, path, contentType, new FileInfo(path).Length, sum, duration, width, height, fps, sample, channels, register ? "registry-marker" : "returned-marker", a.AttemptNumber, p.CorrelationId);
  if (register) await artifacts.RegisterAsync(descriptor, kind, default);
  return descriptor;
 }

 public async Task<DocumentaryProductionVisualAdapterResult> GenerateAsync(DocumentaryVisualGenerationRequest r, DocumentaryProductionExecutionContext e, DocumentaryProductionAttemptContext a, DocumentaryProductionWorkspace w, CancellationToken t)
 {
  VisualRequests.Enqueue(r); Capture($"VisualGeneration:{r.AssetPlan.VariantType}:{r.AssetPlan.SceneId}:{r.VisualPrompt.VisualPromptId}", a, VisualStarted); var o = Next(VisualOutcomes); await Apply(o, t);
  if (o.Kind is FakeProductionAdapterOutcomeKind.RetryableFailure or FakeProductionAdapterOutcomeKind.NonRetryableFailure) return DocumentaryProductionVisualAdapterResult.Failed(Failure(o), "fake", "fake");
  var d = await Make(r.AssetPlan, DocumentaryPhysicalArtifactKind.VisualImage, w, a, "image/png", o.Kind != FakeProductionAdapterOutcomeKind.SuccessWithoutRegistration, width: r.Width, height: r.Height);
  return DocumentaryProductionVisualAdapterResult.Success(d, "fake", "returned-marker");
 }
 public async Task<DocumentaryProductionNarrationAdapterResult> SynthesizeAsync(DocumentaryNarrationSynthesisRequest r, DocumentaryProductionExecutionContext e, DocumentaryProductionAttemptContext a, DocumentaryProductionWorkspace w, CancellationToken t)
 {
  NarrationRequests.Enqueue(r); Capture($"NarrationSynthesis:{r.AssetPlan.VariantType}:{r.AssetPlan.SceneId}", a, NarrationStarted); var o = Next(NarrationOutcomes); await Apply(o, t);
  if (o.Kind is FakeProductionAdapterOutcomeKind.RetryableFailure or FakeProductionAdapterOutcomeKind.NonRetryableFailure) return DocumentaryProductionNarrationAdapterResult.Failed(Failure(o), r.Language, r.AssetFormat);
  var d = await Make(r.AssetPlan, DocumentaryPhysicalArtifactKind.NarrationAudio, w, a, "audio/wav", o.Kind != FakeProductionAdapterOutcomeKind.SuccessWithoutRegistration, r.NarrationBlock.EstimatedDurationMilliseconds, sample: r.SampleRate, channels: r.ChannelCount);
  return DocumentaryProductionNarrationAdapterResult.Success(d, r.VoiceProfileId, r.Language, r.AssetFormat, false);
 }
 public async Task<DocumentaryProductionSubtitleAdapterResult> GenerateAsync(DocumentarySubtitleGenerationRequest r, DocumentaryProductionExecutionContext e, DocumentaryProductionAttemptContext a, DocumentaryProductionWorkspace w, CancellationToken t)
 {
  SubtitleRequests.Enqueue(r); Capture($"SubtitleGeneration:{r.AssetPlan.VariantType}:{r.SceneId}", a, SubtitleStarted); var o = Next(SubtitleOutcomes); await Apply(o, t);
  if (o.Kind is FakeProductionAdapterOutcomeKind.RetryableFailure or FakeProductionAdapterOutcomeKind.NonRetryableFailure) return DocumentaryProductionSubtitleAdapterResult.Failed(Failure(o), r.Language, r.AssetFormat);
  var d = await Make(r.AssetPlan, DocumentaryPhysicalArtifactKind.SubtitleDocument, w, a, "application/x-subrip", o.Kind != FakeProductionAdapterOutcomeKind.SuccessWithoutRegistration);
  return DocumentaryProductionSubtitleAdapterResult.Success(d, r.Language, r.AssetFormat, r.SubtitleCues.Count, 0, r.MeasuredNarrationDurationMilliseconds, "fake");
 }
 public async Task<DocumentaryProductionSceneCompositionAdapterResult> ComposeAsync(DocumentarySceneCompositionRequest r, DocumentaryProductionExecutionContext e, DocumentaryProductionAttemptContext a, DocumentaryProductionWorkspace w, CancellationToken t)
 {
  SceneCompositionRequests.Enqueue(r); Capture($"SceneComposition:{r.AssetPlan.VariantType}:{r.MediaScene.SceneId}", a, SceneCompositionStarted); var o = Next(SceneCompositionOutcomes); await Apply(o, t);
  if (o.Kind is FakeProductionAdapterOutcomeKind.RetryableFailure or FakeProductionAdapterOutcomeKind.NonRetryableFailure) return DocumentaryProductionSceneCompositionAdapterResult.Failed(Failure(o, DocumentaryProductionFailureCode.ProcessTimedOut), r.MediaScene.SceneId, r.AssetPlan.VariantType.ToString(), DocumentarySceneSubtitleMode.BurnIn);
  var d = await Make(r.AssetPlan, DocumentaryPhysicalArtifactKind.SceneVideo, w, a, "video/mp4", o.Kind != FakeProductionAdapterOutcomeKind.SuccessWithoutRegistration, r.EffectiveSceneDurationMilliseconds, r.Width, r.Height, r.FrameRate, 48000, 2);
  return DocumentaryProductionSceneCompositionAdapterResult.Success(d, "fake", r.MediaScene.SceneId, r.AssetPlan.VariantType.ToString(), r.EffectiveSceneDurationMilliseconds, r.Width, r.Height, r.FrameRate, true, DocumentarySceneSubtitleMode.BurnIn, "fake");
 }
 public async Task<DocumentaryProductionVariantCompositionAdapterResult> ComposeAsync(DocumentaryVariantCompositionRequest r, DocumentaryProductionExecutionContext e, DocumentaryProductionAttemptContext a, DocumentaryProductionWorkspace w, CancellationToken t)
 {
  VariantCompositionRequests.Enqueue(r); Capture($"VariantComposition:{r.MediaVariant.VariantId}", a, VariantCompositionStarted); var o = Next(VariantCompositionOutcomes); await Apply(o, t);
  if (o.Kind is FakeProductionAdapterOutcomeKind.RetryableFailure or FakeProductionAdapterOutcomeKind.NonRetryableFailure) return DocumentaryProductionVariantCompositionAdapterResult.Failed(Failure(o, DocumentaryProductionFailureCode.ProcessTimedOut), r);
  var duration = r.SceneAssets.Sum(x => x.DurationMilliseconds); var d = await Make(r.AssetPlan, DocumentaryPhysicalArtifactKind.VariantVideo, w, a, "video/mp4", o.Kind != FakeProductionAdapterOutcomeKind.SuccessWithoutRegistration, duration, r.Width, r.Height, r.FrameRate, r.AudioSampleRate, r.AudioChannelCount);
  return new(true, d, null, "fake", "returned-marker", r.MediaVariant.VariantId, r.MediaVariant.VariantType, r.VideoFormat, r.SceneAssets.Count, duration, r.Width, r.Height, r.FrameRate, true, true, "fake");
 }
 public async Task<DocumentaryProductionMediaVerificationAdapterResult> VerifyAsync(DocumentaryMediaVerificationRequest r, DocumentaryProductionExecutionContext e, DocumentaryProductionAttemptContext a, DocumentaryProductionWorkspace w, CancellationToken t)
 {
  VerificationRequests.Enqueue(r); var scene = r.ArtifactKind == DocumentaryPhysicalArtifactKind.SceneVideo; var signal = scene ? SceneVerificationStarted : VariantVerificationStarted; Capture($"MediaVerification:{r.ArtifactKind}:{r.VariantId}:{r.SceneId}", a, signal); var o = Next(scene ? SceneVerificationOutcomes : VariantVerificationOutcomes); await Apply(o, t); var d = await artifacts.GetAsync(r.AssetId, t);
  if (o.Kind is FakeProductionAdapterOutcomeKind.RetryableFailure or FakeProductionAdapterOutcomeKind.NonRetryableFailure) return new(false, false, d, null, Failure(o), "fake", "fake", r.AssetId, r.ArtifactKind, r.AssetType, r.AssetFormat);
  var verified = o.Kind != FakeProductionAdapterOutcomeKind.VerificationRejected;
  return new(true, verified, d, null, null, "fake", "fake", r.AssetId, r.ArtifactKind, r.AssetType, r.AssetFormat, "mp4", d?.DurationMilliseconds, d?.Width, d?.Height, d?.FrameRate, true, r.RequireAudio, false, d?.AudioSampleRate, d?.AudioChannelCount, r.VerificationProfileId, verified ? "verified-safe-evidence" : "rejected-safe-evidence");
 }
}
