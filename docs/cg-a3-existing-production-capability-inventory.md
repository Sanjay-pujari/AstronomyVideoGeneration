# CG-A3 — A3.1 Existing Production Capability Inventory

**Inspection date:** 2026-07-27  
**Scope:** repository evidence only; documentation-only inspection. “Production-used” below means registered in a host or reached by an API/worker/orchestrator, not merely that a class exists.

## 1. Completion report and inspected areas

- **Files created:** `docs/cg-a3-existing-production-capability-inventory.md`. **Files modified:** none other than this new report.
- **Areas inspected:** all projects under `Backend/src`, `Backend/Astronomy.SscIntelligence`, `Backend/tests`, both host `Program.cs` files, all host `appsettings*.json`, `Architecture`, and `docs`. Repository discovery used `find` and repository-wide `rg`; source review concentrated on contracts, Rendering, Infrastructure/Persistence, Publishing, scheduling, API endpoints, and tests.
- **CG-A2 ports found:** all six, synchronously declared in `Astronomy.MediaFactory.Core.DocumentaryBlueprint` in `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryMediaProviderRegistry.cs` (`IDocumentaryVisualAssetProvider.Generate`, `IDocumentaryNarrationAssetProvider.Synthesize`, `IDocumentarySubtitleAssetProvider.Generate`, both `Compose` ports, and `IDocumentaryRenderVerificationProvider.Verify`). Their only implementations found are `DocumentaryMediaPipelineFakeProviders` in tests; production DI does not bind them [source: `DocumentaryMediaProviderRegistry.cs`, interfaces and registry; caller: `DocumentaryMediaPipelineOrchestrator.ExecuteProviders`; test: `DocumentaryMediaPipelineFakeProviders.cs`, `DocumentaryMediaPipeline*Tests.cs`].
- **Candidate services found:** the selected production candidates are `StellariumVisualGenerationService` plus `AzureOpenAICinematicImageGenerator`/local visual dependencies; `AzureSpeechSynthesisService`/`AzureSpeechClient`; subtitle methods embedded in `PipelineOrchestrator` and `ProductionPipelineExecutionService`; `FfmpegSceneRenderer`; `FfmpegVideoRenderService`; and `PrePublishValidationService`. Storage/publishing candidates are documented in §11.
- **Decision:** the six boundaries have selected candidates, registrations, inputs/outputs, and risks; the current production system is `PipelineOrchestrator` for the ordinary API/worker path, while `ProductionPipelineExecutionService` is the explicitly authoritative Phase 7 path for RC2. These paths overlap but their ownership is stated in source [source/caller: `Rc2ContentPlanningBatchOrchestrator.cs:99`; registration: `ServiceCollectionExtensions.cs:614-616,864`].

## 2. Certified CG-A2 port declarations and contracts

All ports are in project `Astronomy.MediaFactory.Core`, namespace `Astronomy.MediaFactory.Core.DocumentaryBlueprint`, source `DocumentaryMediaProviderRegistry.cs`. All methods are synchronous, expose **no `CancellationToken`**, and use result/failure data rather than a declared exception type. The orchestrator owns bounded retry loops: visual uses `MaximumVisualAttempts`, narration `MaximumNarrationAttempts`, and composition `MaximumCompositionAttempts`; verification has no retry loop [source: `DocumentaryMediaPipelineContracts.cs`, `DocumentaryMediaPipelinePolicy`; caller: `DocumentaryMediaPipelineOrchestrator.ExecuteVariant`; tests: `DocumentaryMediaPipelineHardeningTests.cs`, `DocumentaryMediaPipelineRejectionPathTests.cs`].

| Port / exact signature | Request and result contract | Identity, correlation, failure and cancellation |
|---|---|---|
| `IDocumentaryVisualAssetProvider.Generate(DocumentaryVisualGenerationRequest)` → `DocumentaryVisualGenerationResult` | Asset plan, exact `DocumentaryVisualPrompt`, dimensions, format, attempt; result wraps status and `DocumentaryMediaAssetResult`. | Plan carries `AssetId`, `SourceInstructionId`, dependencies and knowledge references; request/result carry correlation. Asset result carries `ContentIdentity`, checksum, provider, attempt and failure fields. No cancellation. |
| `IDocumentaryNarrationAssetProvider.Synthesize(DocumentaryNarrationSynthesisRequest)` → `DocumentaryNarrationSynthesisResult` | Plan, complete `DocumentaryNarrationBlock`, voice profile, language, WAV format, 48 kHz/stereo policy, attempt; result adds measured duration. | Same identity/correlation fields; no cancellation. Failure is status/code/message plus asset failure fields. |
| `IDocumentarySubtitleAssetProvider.Generate(DocumentarySubtitleGenerationRequest)` → `DocumentarySubtitleGenerationResult` | Plan, variant/scene/language, supplied cues, measured narration duration, SRT; result adds cue count. | Source instruction and deterministic asset identity remain in the plan/result; exact correlation required; no retry counter and no cancellation. |
| `IDocumentarySceneCompositionProvider.Compose(DocumentarySceneCompositionRequest)` → `DocumentarySceneCompositionResult` | Plan, scene, visual/narration/subtitle results, measured/planned/effective duration, transition, dimensions, frame rate, attempt; result adds effective duration. | Inputs are asset-result identities, checksums and correlation; no cancellation. Composition retry is orchestrator policy. |
| `IDocumentaryVariantCompositionProvider.Compose(DocumentaryVariantCompositionRequest)` → `DocumentaryVariantCompositionResult` | Plan, variant, ordered scene results, dimensions/frame/audio settings/MP4, attempt; result adds scene count/effective duration. | Deterministic variant asset ID and exact correlation; no cancellation. Composition retry is orchestrator policy. |
| `IDocumentaryRenderVerificationProvider.Verify(DocumentaryRenderVerificationRequest)` → `DocumentaryRenderVerificationResult` | Variant asset plus expected scene count, dimensions, frame/audio properties and duration range; result reports actuals, stream flags, checksum validity and failures. | Variant result supplies content identity/checksum/correlation. No cancellation or retry. |

The common `DocumentaryMediaAssetResult` is the result contract for physical identity: `AssetId`, type/format/status, provider, `ContentIdentity`, length, duration, dimensions/frame/audio values, checksum, failure code/message, attempts and correlation [source: `DocumentaryMediaPipelineContracts.cs`, record `DocumentaryMediaAssetResult`; validation caller: `DocumentaryMediaPipelineOrchestrator.ValidAsset`; tests: `DocumentaryMediaPipelineIdentityTests.cs`].

## 3. Port-mapping matrix

| CG-A2 port | Candidate implementation | Reuse classification | Adapter thickness | Confidence | Primary risk |
|---|---|---|---|---|---|
| `IDocumentaryVisualAssetProvider` | Composite of `StellariumVisualGenerationService`, `AzureOpenAICinematicImageGenerator`, `FileVisualAssetProvider`/`CelestialAssetProvider` | `CompositeAdapter` | `Moderate` | **High** — all are production-DI services with tests, and routing inputs exist. | No candidate preserves CG-A2 asset/source identity itself. |
| `IDocumentaryNarrationAssetProvider` | `AzureSpeechSynthesisService` + `AzureSpeechClient` (+ WAV `AzureTtsAudioGenerationService` where required) | `CompositeAdapter` | `Moderate` | **High** — registered production client/service and focused tests. | Main service emits 24-kHz mono MP3, while CG-A2 requests WAV 48-kHz stereo and measured duration. |
| `IDocumentarySubtitleAssetProvider` | `ProductionPipelineExecutionService.BuildNarrationSrtFromCleanFiles` / `BuildNarrationSrtFromSceneDurationPlan` logic | `CompositeAdapter` | `Significant` | **Medium** — behavior and tests exist, but it is private logic embedded in the authoritative large service. | Extraction/coordination needed; Hindi logic may rewrite narration text. |
| `IDocumentarySceneCompositionProvider` | `FfmpegSceneRenderer.RenderScenesAsync` | `ThinAdapter` | `Minimal` | **High** — reusable `ISceneRenderer`, scoped DI, API/test coverage. | It accepts persisted recipe/plan IDs and only two category codes, not CG-A2 request objects. |
| `IDocumentaryVariantCompositionProvider` | `FfmpegVideoRenderService.RenderAsync` (long and short manifest paths) | `ThinAdapter` | `Minimal` | **High** — production `IVideoRenderService`, cancellation and extensive tests. | It builds segments as part of final render and has no CG identity/checksum result. |
| `IDocumentaryRenderVerificationProvider` | `PrePublishValidationService.ValidateAsync` plus renderer probe helpers | `CompositeAdapter` | `Moderate` | **High** for selection, **Medium** for contract coverage — it is the registered publish gate, but checks only duration/video/audio. | Missing dimensions, rates, codecs, subtitle/sidecar and checksum validation; process-start/JSON errors escape. |

## 4. Candidate-service inventory

The following records use the required schema. “None found” means no repository member was located. All production classes listed are async and accept `CancellationToken` unless explicitly noted; CG-A2 ports remain synchronous.

### INV-VIS-01 — visual routing and realization

- **Capability group / class / interface:** visual generation; `StellariumVisualGenerationService : IVisualAssetProvider`; complementary `AzureOpenAICinematicImageGenerator : IAICinematicImageGenerator`, `FileVisualAssetProvider : IVisualAssetProvider`, `CelestialAssetProvider`, and `AstronomyInfographicRenderer`.
- **Project / namespace / source:** Rendering (`Astronomy.MediaFactory.Rendering`: `StellariumVisualGenerationService.cs`, `FileVisualAssetProvider.cs`, `CelestialAssetProvider.cs`), ContentGen (`AzureOpenAICinematicImageGenerator.cs`), Infrastructure.Persistence (`AstronomyInfographicRenderer.cs`).
- **Caller / DI / lifetime:** `PipelineOrchestrator` consumes `IVisualAssetProvider`; scoped binding selects `StellariumVisualGenerationService` at `ServiceCollectionExtensions.cs:507`; cinematic, celestial, and infographic services are also registered in that extension. API visual endpoints call the visual-generation/capture services [source: `Program.cs:925-930,1017-1048`; tests: `StellariumVisualGenerationServiceTests.cs`, `AzureOpenAiAICinematicImageGeneratorTests.cs`, `AstronomyInfographicRendererTests.cs`].
- **Configuration / options:** `Stellarium`/`StellariumOptions`; `AzureOpenAIForImage` and Azure OpenAI options; `CelestialAssets`/`CelestialAssetsOptions`; Visual Intelligence options. Host options registration is in `ServiceCollectionExtensions`; host JSON supplies the sections.
- **Input / output / mode:** visual requests/scenes/prompts to async file paths/assets. Cancellation is forwarded. Stellarium launches/captures; Azure calls image generation; local providers read/write image files.
- **Retry / timeout / effects / files:** Stellarium options own capture timeout; image client retry is provider-level; output directories and screenshot/image names are implementation-specific. Logging exists; no common telemetry record, checksum, CG `ContentIdentity`, `SourceInstructionId`, or CG correlation result.
- **Production usage / tests / limitations / legacy:** registered and called in production flow; focused unit tests exist (**Moderate** test confidence; no paid real-provider test was run). Thumbnail composers are production-used for thumbnails but `NotSuitable` for scene assets. No service is marked legacy by source.
- **Proposed port / classification / thickness / open questions:** visual port; `CompositeAdapter`, `Moderate`. Open question for A3.2 contract design: route CG asset type without losing the exact prompt identity; provider R&D is not indicated.

### INV-NAR-01 — Azure Speech

- **Class/interface/project/source:** `AzureSpeechSynthesisService : ISpeechSynthesisService` and `AzureSpeechClient : IAzureSpeechClient`, Rendering project/namespace, `AzureSpeechSynthesisService.cs`, `AzureSpeechClient.cs`; `SsmlBuilder : ISsmlBuilder` in `SsmlBuilder.cs`.
- **Caller/DI/lifetime:** `PipelineOrchestrator` calls `ISpeechSynthesisService`; `ProductionPipelineExecutionService` optionally consumes `IAzureSpeechClient`; all are scoped (`ServiceCollectionExtensions.cs:517,522`). `AzureTtsAudioGenerationService` also calls the client [source: those constructors; tests: `AzureSpeechSynthesisServiceTests.cs`].
- **Config/options:** `AzureSpeech`/`AzureSpeechOptions`; validation is imperative in `EnsureSpeechConfigurationIsUsable`, not options startup validation. Required subscription keys are key plus region/endpoint, or managed-identity region/resource ID; secrets are intentionally omitted.
- **Input/output/async/cancellation:** full supplied string + directory → `narration.txt` and `narration.mp3`; `SynthesizeToFileAsync` copies it. Client returns bytes and supports cancellation. Output is `Audio24Khz160KBitRateMonoMp3`; its separate SSML WAV method is `Riff24Khz16BitMonoPcm`.
- **Retry/timeout/failure:** client retries Azure `ServiceTimeout` for `TimeoutRetryAttempts + 1`, delaying `TimeoutRetryDelayMs`, and voice-falls back only on unsupported-voice errors. Cancellation is honored with `WaitAsync`; there is no separate wall-clock timeout. Service logs and wraps failures in `InvalidOperationException`.
- **Identity/checksum/duration/correlation/telemetry:** none in these service contracts; duration and checksum are not returned. Logging exists; no dedicated telemetry contract. External effect is Azure Speech plus filesystem writes.
- **Production/tests/limitations/status:** known production path and tests (**Strong** unit confidence, **Weak** real-provider/e2e evidence). Current format differs from CG policy. Not legacy.
- **Port/classification/thickness/open question:** narration port; `CompositeAdapter`, `Moderate`; measured duration, conversion, deterministic names, identity/checksum must be coordinated from existing tools.

### INV-SUB-01 — subtitle generation

- **Class/member/project/source:** private subtitle pipelines in `PipelineOrchestrator.WriteSubtitleArtifactsAsync`/`BuildSplitSubtitleBlocks` (`Core/PipelineOrchestrator.cs`) and `ProductionPipelineExecutionService.BuildNarrationSrtFromCleanFiles`/`BuildNarrationSrtFromSceneDurationPlan` (`Infrastructure/Persistence/ProductionPipelineExecutionService.cs`); no standalone subtitle interface was found.
- **Caller/DI/lifetime:** invoked internally by their owning scoped orchestrators; no separate registration. `PipelineOrchestrator.WriteSceneNarrationArtifactsAsync` is the caller; Phase 14 calls the production SRT methods.
- **Config/input/output:** `SubtitleTtsOptions` is consumed by the Phase 14 implementation. Inputs are narration files/text and scene duration plan; output is SRT plus JSON diagnostics/manifest. Both are async at file-writing boundary, cancellation passed to async writes; splitting helpers are synchronous.
- **Behavior:** supplied narration is split, not regenerated in the ordinary path. Phase 14 Hindi duplicate handling can rewrite duplicate cue/narration text before SRT generation (`ApplyHindiCueDuplicateRewriteBeforeSrtGeneration`), so exact supplied-text semantics are not universal. Timing uses scene audio duration from the scene-duration plan; fallback code estimates speech from word count at 155 wpm, an explicit blocking risk. Cues are contiguous and monotonic; Phase 14 allocates cue durations and records zero drift. Ordinary path wraps at two lines × 42 characters and rejects unbreakable >84-character chunks [source: `PipelineOrchestrator.cs:2399-2479`; Phase 14 source: `ProductionPipelineExecutionService.cs:6250-6500`].
- **Retry/timeout/side effects/naming:** no retry/timeout; writes `.srt` and diagnostics under run/plan workspace. SRT only; no VTT writer found. Renderer supports a scaffold/sidecar and scene renderer burns an SRT with FFmpeg. No checksum/CG identity/correlation result; logging/diagnostics exist.
- **Tests/usage/limits/classification:** production-embedded; subtitle behavior is covered by `PipelineOrchestratorSceneNarrationTests.cs` and documentary projection tests, with broader production execution tests (**Moderate**). No isolated integration contract. Subtitle port: `CompositeAdapter`, `Significant`.

### INV-REN-01 — scene composition

- **Class/interface/project/source:** `FfmpegSceneRenderer : ISceneRenderer`, Infrastructure.Persistence, `FfmpegSceneRenderer.cs`.
- **Caller/DI:** scoped `ISceneRenderer` registration (`ServiceCollectionExtensions.cs:711`); API/production plan flows call `RenderScenesAsync`; tests `FfmpegRenderingTests.cs` and scene planning tests.
- **Input/output:** `SceneRenderingRequest` selects explicit database plan IDs/region; result is `SceneRenderingResponse`. It reads `scene-*.recipe.json`, capability documents, images and WAV; writes `rendered-scenes/scene-NNN.mp4`, working-frame SRTs, and a render manifest. Async/cancellable.
- **Render behavior:** builds FFmpeg arguments internally and executes through `IProcessRunner`; image-to-video motion renderer is selected from recipe motion, audio attached, subtitles burned, dimensions/duration come from recipes; validates output. Batch is limited to `RareEventAlert` and `CosmicStoryShort`. Existing output can be skipped unless overwrite is true. Failures become warnings/failed counts; no outer retry; timeout is max(120 seconds, configured segment timeout). Workspace is retained for diagnostics, not automatically cleaned.
- **Identity/checksum/correlation/logging/telemetry:** plan/scene numbers and paths are retained; CG identities/checksum/correlation are absent. Structured logging and JSON manifests, but no dedicated telemetry type.
- **Production/tests/status/classification:** registered production service, **Moderate** tests, not legacy. Scene port: `ThinAdapter`, `Minimal`; category/persisted-plan coupling is the limitation.

### INV-REN-02 — long/short final composition

- **Class/interface/source:** `FfmpegVideoRenderService : IVideoRenderService`, Rendering, `FfmpegVideoRenderService.cs`; uses `RenderManifestBuilder`, `FfmpegArgumentBuilder`, `IProcessRunner`, motion/ending composers.
- **Caller/DI:** `PipelineOrchestrator` calls it for long render; `ShortsVideoRenderService` coordinates short artifacts and the same renderer. Scoped registration at `ServiceCollectionExtensions.cs:523`; helper registrations are in the same extension. Tests: `FfmpegRenderingTests.cs`, `MotionRenderingTests.cs`, `ShortsVideoRenderServiceTests.cs`.
- **Input/output:** `RenderManifest` → final path string; async/cancellable. It creates `render-manifest.json`, concat lists, caption metadata, `subtitles.scaffold.srt`, command/log and encoding/performance diagnostics, intermediate segments/combined file, final MP4.
- **Behavior:** segment duration is locked to probed audio where per-scene audio exists; otherwise narration duration is divided across scenes. Long dimensions are landscape and short dimensions portrait through manifest/options; configured FPS/presets apply. It uses looped images, zoom/pan/motion filters, transitions/xfade where eligible, AAC audio and final encode. Same renderer handles languages; no language branch in the renderer. Process timeouts and cancellation are supported; failures throw with diagnostics. Partial intermediates are retained for diagnosis; no checksum generation.
- **Identity/correlation/logging/telemetry:** filenames/path identity only; no CG content/source identity or result correlation/checksum. Extensive reports and structured logs are present.
- **Usage/tests/status/classification:** known production long and short usage, **Strong** unit/integration-style command tests, no paid-provider requirement. Variant port: `ThinAdapter`, `Minimal`; also `InternalDependency` for scene generation embedded in final rendering.

### INV-VER-01 — FFprobe validation

- **Class/source/caller:** `PrePublishValidationService : IPrePublishValidationService`, Core, `PrePublishValidationService.cs`; called by `PipelineOrchestrator` before publish and registered scoped at `ServiceCollectionExtensions.cs:601`; tests `PrePublishValidationServiceTests.cs`.
- **Config/input/output:** `RenderingOptions.FfprobePath` and `PublishingValidationOptions`; `PrePublishValidationRequest` → persisted `pre-publish-validation-report.json`. Async/cancellable. It resolves blank path to executable name `ffprobe`.
- **Checks:** file presence/nonzero, minimum duration, any video stream, any audio stream, visual placeholders/missing visuals, narration/visual object alignment, short map, and fatal FFmpeg log. It does **not** validate container, codecs, width/height, FPS, audio rate/channels, subtitle stream/sidecar, checksum, scene count or duration tolerance beyond minimum.
- **Failure matrix:** missing input returns zeros; nonzero FFprobe exit returns `(0,false,false)` and validation errors. Missing executable makes `Process.Start` throw; timeout is not configured (only cancellation); invalid JSON/missing JSON properties throw; no video/audio becomes report error. No embedded subtitle check exists, so absence of subtitle is ignored. There is no silent guessed duration in this class, but renderer `ProbeMediaDurationSecondsAsync` fallback to locked duration after failed segment probe is a risk [source: `FfmpegVideoRenderService.cs:178-181`].
- **Identity/retry/logging/tests/classification:** pipeline run ID is in report; no CG correlation/content identity/checksum; no retry; logs completion. **Moderate** tests. Verification port: `CompositeAdapter`, `Moderate`.

### INV-STO-01 — public storage

- **Class/interface/source:** `AzureBlobPublicMediaStorageService : IPublicMediaStorageService`, Publishing, `AzureBlobPublicMediaStorageService.cs`; separate `AzureBlobStorageService : IStorageService` in `AzureBlobStorageService.cs`.
- **Caller/DI/config:** Meta/public-media publish flow calls the public service; scoped registration at `ServiceCollectionExtensions.cs:542`; `PublicMediaStorage`/`PublicMediaStorageOptions`. The general blob service uses `AzureBlob`/`AzureBlobOptions`. Tests: `PublicMediaStorageServiceTests.cs`.
- **I/O/behavior:** async/cancellable file upload → storage result/public URL; Azure blob side effect; prefix/container naming, SAS expiry/public base URL are configured. Azure SDK transfer retry/concurrency values exist in blob options; result has URL/path information. No documentary scene/variant identity model or checksum storage was located.
- **Usage/classification:** registered production path, **Moderate** tests; `InternalDependency`, `Minimal` for later handoff, not a CG-A2 media port. Duplicate general/public services have distinct purposes.

### INV-PUB-01 — publishing

- **Classes/source:** `ContentPublishService : IContentPublishService`, `YouTubePublishingService`/`YouTubePublishService`, `FacebookVideoPublishService`, `FacebookReelPublishService`, `InstagramReelPublishService`, `MetaPublishService`, and `ShortFormPublishingService`, all Publishing project.
- **Caller/DI:** `PipelineOrchestrator`, API `/api/youtubepublish/{pipelineRunId}`, `RunOperationsService`, and worker/scheduler path; registrations `ServiceCollectionExtensions.cs:568,583` and adjacent publisher registrations. OAuth entry is `YouTubeOAuthController`; Meta OAuth/token and token health services are registered.
- **Eligibility/retry/recovery:** current gate is configured publishing enabled/requested plus `PrePublishValidationReport.Passed`; YouTube also requires enabled credentials, and shorts run `YouTubeShortsValidation`. `ShortFormPublishingService` filters target enablement, skips already-published records, enforces cooldown and uses `TransientRetryHelper`; YouTube/Meta options own upload/retry/poll settings. Pipeline recovery/scheduler can replay failed runs. The narrow future certification gate is immediately after `PrePublishValidationService.ValidateAsync` returns `Passed` and before `PipelineOrchestrator` enables its publish stages / calls `IContentPublishService`, `IYouTubePublishingService`, `IShortFormPublishingService`, or `IMetaPublishService` [source: `PipelineOrchestrator.cs`, `GetFailedEnabledPublishStagesAsync` and publish stages; tests: `PublishingFlowTests.cs`, `ShortFormPublishingServiceTests.cs`, `MetaPublishingTests.cs`, `YouTubePublishingIntegrationTests.cs`].
- **Identity/side effects/tests:** run/short IDs, content type, target platform, external post IDs/URLs and statuses are persisted; language/CG variant/scene identity is not a publishing contract. External API and upload effects; structured logging/correlation via `Activity`. **Strong** unit tests, **Weak** real-platform evidence. `InternalDependency`, no CG-A2 media port.

## 5. Current-flow diagrams

### 5.1 Current real documentary flow

```text
API Program.cs MapPost /api/pipelines/run (or /api/events/{eventId}/generate)
→ PipelineOrchestrator.RunAsync
→ context/scene selection services
→ script service
→ IVisualAssetProvider (DI: StellariumVisualGenerationService)
→ ISpeechSynthesisService.SynthesizeAsync (AzureSpeechSynthesisService)
→ PipelineOrchestrator.WriteSceneNarrationArtifactsAsync
  → WriteSubtitleArtifactsAsync / BuildSplitSubtitleBlocks
→ IVideoRenderService.RenderAsync (FfmpegVideoRenderService; builds scene segments and final)
→ IShortsVideoRenderService for short variant
→ IPrePublishValidationService.ValidateAsync (PrePublishValidationService/ffprobe)
→ public-media storage when Meta needs a URL
→ YouTube/IContentPublishService + IShortFormPublishingService + IMetaPublishService
```
Evidence: `Program.cs:5042,5320,5600`; constructor and stage calls in `PipelineOrchestrator.cs`; DI at `ServiceCollectionExtensions.cs:507,522-523,542,568,583,601,864`; `PipelineOrchestratorSceneNarrationTests.cs`, `PublishingFlowTests.cs`.

### 5.2 Long-video flow

```text
PipelineOrchestrator
→ AzureSpeechSynthesisService: narration.txt + narration.mp3
→ scene narration text/audio entries + subtitles.srt/diagnostics
→ RenderManifest (landscape scenes, audio, optional music/subtitle scaffold)
→ FfmpegVideoRenderService.RenderAsync
→ segment-*.mp4 → concat/transition combined video → final MP4
→ PrePublishValidationService → YouTube long / ContentPublishService / optional Facebook full video
```
The renderer uses actual probed scene audio to lock segment duration when supplied [source: `FfmpegVideoRenderService.cs:135-151`; caller/tests: `PipelineOrchestrator.cs`, `FfmpegRenderingTests.cs`].

### 5.3 Short-video flow

```text
PipelineOrchestrator
→ ShortsVideoRenderService
→ short sequence/map + portrait RenderManifest
→ same FfmpegVideoRenderService.RenderAsync
→ short MP4 + thumbnail
→ PrePublishValidationService (short minimum + short-sequence-map)
→ ShortFormPublishingService
→ YouTubeShortsPlatformPublisher / InstagramReelsPlatformPublisher / Facebook publisher
```
Evidence: `ShortsVideoRenderService.cs`; `ShortFormPublishingService.cs`; registrations and `ShortsVideoRenderServiceTests.cs`/`ShortFormPublishingServiceTests.cs`.

### 5.4 English narration flow

```text
complete script or per-scene narration string
→ AzureSpeechSynthesisService.SynthesizeAsync
→ AzureSpeechClient.SynthesizeMp3Async
→ language detector selects en voices
→ SsmlBuilder.BuildSsml (paragraph/sentence/comma breaks)
→ one Azure request per service call → narration.mp3
```
The ordinary orchestrator also builds **per-scene narration entries**, synthesizing each scene entry, then splits that exact text into subtitle cues. It is not TTS-per-cue in that flow [source: `PipelineOrchestrator.cs`, `WriteSceneNarrationArtifactsAsync` and scene audio calls; client source above; test: `PipelineOrchestratorSceneNarrationTests.cs`]. Phase 14 supports modes configured by `SubtitleTtsOptions`; A3.4 must ensure each CG-A2 `DocumentaryNarrationBlock` maps to one synthesis request and never iterate its SRT cues.

### 5.5 Hindi narration flow

```text
Hindi scene narration file/string
→ AzureSpeechClient detects Devanagari
→ AzureSpeechOptions.GetPreferredVoices("hi")
→ hi-IN voice + Hindi prosody in SsmlBuilder/AzureSpeechClient
→ scene audio
→ Phase 14 duration plan
→ BuildNarrationSrtFromCleanFiles
→ optional duplicate-Hindi cue rewrite → Hindi SRT
```
Granularity is per calling scene/file in Phase 14, not inherently per cue; duplicate-Hindi logic can rewrite text [source: `AzureSpeechOptions.cs`; `AzureSpeechClient.cs`; `ProductionPipelineExecutionService.cs`, `ApplyHindiCueDuplicateRewriteBeforeSrtGeneration`; tests: `ProductionPipelineExecutionServiceTests.cs`].

### 5.6 Visual routing flow

```text
visual request/scene intent
→ IVisualAssetProvider DI default: StellariumVisualGenerationService
→ Stellarium script/capture path
Alternative production capabilities:
  cinematic prompt → AzureOpenAICinematicImageGenerator
  celestial/local lookup → CelestialAssetProvider / FileVisualAssetProvider
  scientific chart/diagram → AstronomyInfographicRenderer / sky-map execution services
  thumbnail only → thumbnail composition services (excluded as scene provider)
```
No single existing member maps CG documentary asset enum to all branches; the future visual adapter must compose these registered services [source/callers/tests: §4 INV-VIS-01].

### 5.7 Storage and publishing flow

```text
PrePublishValidationService.ValidateAsync
→ if Passed and publishing/request flags enabled
→ optional AzureBlobPublicMediaStorageService upload → public/SAS URL for Meta
→ ContentPublishService / YouTube / ShortFormPublishingService / MetaPublishService
→ TransientRetryHelper + platform poll/verification
→ repository publication record
→ PipelineRecoveryService / scheduler re-entry for failed jobs
```
Evidence: `PipelineOrchestrator.cs`; `AzureBlobPublicMediaStorageService.cs`; `ShortFormPublishingService.cs`; `PipelineRecoveryService.cs`; scheduling classes; corresponding publishing/recovery tests.

## 6. Visual-type routing matrix

| Documentary asset type | Existing implementation | Fallback | Suitability | Notes |
|---|---|---|---|---|
| `VisualImage` | `AzureOpenAICinematicImageGenerator` | `FileVisualAssetProvider`/celestial local asset | `ThinAdapter` candidate within composite | Generated cinematic imagery is tested; preserve exact prompt and CG identity. |
| `SkySimulationImage` | `StellariumVisualGenerationService` plus Stellarium script/capture services | existing capture/local output | `ThinAdapter` candidate within composite | Strongest direct semantic match. |
| `StarChartImage` | sky-map/infographic execution (`AstronomyInfographicRenderer`) | Stellarium capture | `InternalDependency` | Repository term differs; no CG port implementation. |
| `TelescopeViewImage` | Stellarium focused view/capture | celestial/local asset | `InternalDependency` | Route only when request semantics match a simulated telescope view. |
| `ScientificDiagramImage` | `AstronomyInfographicRenderer` | `Not found` | `InternalDependency` | Evidence supports infographics, not every scientific diagram category. |
| `HistoricalIllustrationImage` | Azure OpenAI cinematic image generation | local file selection | `InternalDependency` | No history-specific production provider found; suitability depends on supplied prompt, not a dedicated class. |

Sources/callers/tests are the files listed in INV-VIS-01. **No dedicated historical-illustration service was found.** Thumbnail services are `NotSuitable` for this matrix because their contracts optimize platform thumbnails rather than documentary scene instructions.

## 7. Mandatory narration findings

| Finding | Repository evidence |
|---|---|
| English configured voice | `AzureSpeechOptions.PrimaryVoice` default `en-US-AriaNeural`; `Voices["en"]` is also available; owner `AzureSpeechOptions.GetPreferredVoices`/client. |
| Hindi configured voice | `Voices["hi"]` defaults to `hi-IN-SwaraNeural` and is the first Hindi candidate used by `GetPreferredVoices("hi")`; the separate `DefaultVoiceName` defaults to `hi-IN-MadhurNeural` but is not read by that selector. Owner is the same as above. |
| Voice / SSML owner | `AzureSpeechClient` selects voices; `SsmlBuilder` owns XML language, prosody, emphasis and comma/sentence/paragraph breaks. |
| Request granularity | One request per `SynthesizeAsync`/client invocation. Ordinary complete narration call is whole script; scene path calls per scene. Phase 14 is per scene/file. No production evidence of Azure calls per SRT cue was found. |
| Output format / sample / channels | MP3 is Azure `Audio24Khz160KBitRateMonoMp3` (24 kHz mono); WAV method is 24-kHz 16-bit mono PCM. CG-A2 expects WAV 48 kHz stereo. |
| Measured duration | FFprobe in renderer/scene-duration plan; synthesis service itself returns none. Phase 14 has fallback word-count estimation, not measurement. |
| Checksum | Not found in speech services. |
| Retry / timeout | Azure client owns service-timeout retry; cancellation is honored, but no independent wall timeout. |
| Naming / directory | `narration.txt` and `narration.mp3` in caller-provided output directory; per-scene/Phase 14 filenames are plan-owned. |
| Per-SRT-cue risk / A3.4 correction | Current subtitle splitting does not prove per-cue TTS in the ordinary path. A3.4 must explicitly bind one CG narration block to one synthesis request, then derive cues from that block and measured audio; do not synthesize each cue. |

Evidence: `AzureSpeechOptions.cs`, `AzureSpeechClient.cs`, `AzureSpeechSynthesisService.cs`, `SsmlBuilder.cs`; callers above; `AzureSpeechSynthesisServiceTests.cs`.

## 8. Mandatory subtitle findings

| Finding | Result |
|---|---|
| Source text | Existing narration text/file; ordinary path splits it. Hindi Phase 14 may rewrite duplicate cues and update narration file. |
| Owner | Embedded private methods in the two production orchestrators; no standalone writer. |
| Timing | Ordinary path derives sequential cue timing from scene entries; Phase 14 uses `AudioDurationSec` in scene-duration plan and allocates cue duration, ending last cue exactly at scene end. |
| Planned/measured duration | Phase 14 prefers plan audio duration (produced from audio probing) but its fallback estimates from word count. |
| Scaling | Cue-duration allocator uses text/options inside each scene; not a general supplied-cue scaling port. |
| Wrapping/max | Ordinary path max 42 chars × 2 lines; Phase 14 uses `SubtitleTtsOptions.SubtitleMaxCharsPerLine` and `SubtitleMaxLines`. |
| Overlap/monotonic/final validation | Sequential `cueStart = cueEnd`, scene end forced for last cue, diagnostics record drift; validation/rejection logic exists. |
| Formats | SRT yes; VTT `Not found`; sidecar yes; burn-in yes in `FfmpegSceneRenderer`; final renderer writes `subtitles.scaffold.srt` but embedded subtitle validation is absent. |
| Language/variant | English and Hindi share mechanics, Hindi has duplicate rewrite. Long/short use their own artifacts/manifests; no separate VTT. |

Evidence: `PipelineOrchestrator.cs:2356-2488`; `ProductionPipelineExecutionService.cs:6250-6500`; `FfmpegSceneRenderer.cs`; tests listed in INV-SUB-01.

## 9. Required rendering findings

| Finding | Scene | Final variant |
|---|---|---|
| Service / command / runner | `FfmpegSceneRenderer`, internal `BuildFfmpegArgs`, `IProcessRunner` | `FfmpegVideoRenderService`, `RenderManifestBuilder` + `FfmpegArgumentBuilder`/internal builders, `IProcessRunner` |
| Manifest | recipe/capability inputs and rendered-scenes manifest | `RenderManifest` plus `render-manifest.json`, command/encoding/performance reports |
| Assets/artifacts | image/frame + WAV + generated SRT → `scene-NNN.mp4` | ordered visuals or scene segments + audio/music → segments, combined, final MP4 |
| Dimensions/FPS | recipe/config | manifest/options; landscape long, portrait short; configured FPS |
| Audio/subtitles | attaches narration; burns generated SRT | AAC audio; subtitle scaffold/metadata; no verified embedded subtitle track |
| Transitions/motion | recipe-selected motion | zoom/pan/Ken Burns-like motion and eligible xfade; final ending composer |
| Duration | recipe duration, validated | probed audio locks segment; transitions adjust aggregate duration |
| Checksum/retry | none/no outer retry | none/no service retry; pipeline stage may retry |
| Cancellation/cleanup | process cancellation; retains workspace/diagnostics | process cancellation/timeouts; retains intermediates/diagnostics |
| Tests/caller | `FfmpegRenderingTests`, API/plan production caller | `FfmpegRenderingTests`, `MotionRenderingTests`, `ShortsVideoRenderServiceTests`; `PipelineOrchestrator` |

## 10. Required verification findings

`PrePublishValidationService` is the selected FFprobe service. `RenderingOptions.FfprobePath` resolves blank to `ffprobe`; its process call extracts format duration and merely detects video/audio stream types. Dimension, frame-rate, sample-rate, channel, codec, container, subtitle and checksum validation are **Not found**. Failure behaviors are: missing executable/start error throws; cancellation stops wait; nonzero exit silently normalizes probe facts to zero/false then yields validation errors; invalid JSON or missing properties throws; absent video/audio yields errors; absent subtitle is ignored. Fallback is none in validation. Tests are `PrePublishValidationServiceTests.cs` (**Moderate**). A verified SRT sidecar is produced elsewhere but is not currently verified here; embedded subtitle support is not demonstrated. The renderer’s failed segment probe fallback to planned/locked duration is a **blocking guessed-duration risk** [source: `PrePublishValidationService.cs:83-102`; `FfmpegVideoRenderService.cs:178-181`; registrations/callers above].

## 11. Storage and publishing findings

Azure public upload uses `AzureBlobPublicMediaStorageService`; general artifact upload uses the separate `AzureBlobStorageService`. Local artifacts are rooted in run/plan output directories. Blob names derive from configured prefix/path; result supplies public/SAS URL. Checksums are not stored by the located public-media contract. Run association is carried by pipeline/output paths and repository records; content type is in pipeline models; platform and parent short ID are in publication records. CG language/variant/scene identity is not propagated by storage [source: both storage files and options; registration `ServiceCollectionExtensions.cs:542`; test `PublicMediaStorageServiceTests.cs`].

Publishing supports YouTube video/thumbnail, Facebook full video/Reel, and Instagram Reel through the classes in INV-PUB-01. Validation, token health/OAuth, transient retry, idempotent replay/cooldown, scheduler and recovery are present. Current eligibility is configuration/request + prepublish pass + platform credential/format validation. The narrow future O2.19 gate is the boolean transition after prepublish validation and before publish-stage enablement in `PipelineOrchestrator`; it was not changed [source/tests: INV-PUB-01].

## 12. Application orchestration and ownership

- **API/manual:** `Program.cs` endpoints `/api/pipelines/run`, `/api/events/{eventId}/generate`, and `/api/youtubepublish/{pipelineRunId}`.
- **Worker/scheduler:** `PipelineSchedulerService` queues via `PipelineRunQueue`; `OrchestratorPipelineRunExecutor` calls `PipelineOrchestrator`; registrations are in `ServiceCollectionExtensions`. Recovery is `PipelineRecoveryService` and API recovery handling.
- **Authoritative Phase path:** `ProductionPipelineExecutionService : IProductionPipelineExecutionService, IProductionPhaseRunner`; RC2 explicitly says Phase 7 is exclusively owned by it.
- **Workspace/cleanup:** orchestrators create output/plan directories; renderers retain diagnostics/intermediates. No universal transactional cleanup was found.
- **Logging/telemetry:** `ILogger`, scopes/`Activity`, pipeline stage recorder/monitoring and diagnostic JSON. Correlation is `Activity` or run ID in the current system, not CG correlation.
- **Retry/cancellation:** pipeline stage executor owns stage attempts; individual Speech/publishing clients own transient retries; process/render cancellation is caller token; the synchronous CG-A2 orchestrator owns only bounded media-attempt counts.

Sources: `Program.cs`, `PipelineOrchestrator.cs`, `ProductionPipelineExecutionService.cs`, `Infrastructure/Scheduling/*.cs`, `PipelineStageExecutor.cs`, `PipelineRecoveryService.cs`; tests `PipelineSchedulerTests.cs`, `PipelineRecoveryEngineTests.cs`, `PipelineStageInstrumentationTests.cs`, `ProductionPipelineExecutionServiceTests.cs`.

## 13. Configuration matrix

No credential values were copied.

| Section | Options type | Registered in | Consumed by | Validation | Duplicate risk |
|---|---|---|---|---|---|
| `AzureOpenAI`, `AzureOpenAIForImage` | Azure OpenAI option types | `ServiceCollectionExtensions` | content/image generators | imperative/startup checks present in host ecosystem | Two purpose-specific sections; reuse unchanged. |
| `AzureSpeech` | `AzureSpeechOptions` | same | speech client/service, production execution | imperative required-key validation | overlaps `Speech`; reuse `AzureSpeech` unchanged. |
| `Speech` | `SpeechOptions` | same | speech-related configuration consumers | no strict startup validation located | duplicate voice/rate settings; internal fallback only. |
| `Rendering` | `RenderingOptions` | same | FFmpeg renderer, ffprobe validation, shorts | host logs executable availability; imperative checks | combines FFmpeg/FFprobe/working paths; reuse unchanged. |
| `Stellarium` | `StellariumOptions` | same | Stellarium generation/capture/script services | runtime validation/timeouts | multiple Stellarium implementations; reuse unchanged. |
| `VisualIntelligence` | visual option types | same | visual planning/routing | option-dependent | overlaps image/thumbnail concerns; reuse unchanged. |
| `CelestialAssets`, `CelestialAssetPack` | respective asset options | same | local/retrieval providers | runtime | overlapping local asset stores; reuse unchanged. |
| `PublicMediaStorage` | `PublicMediaStorageOptions` | same | public blob storage/Meta | runtime enabled/provider/key checks | overlaps `AzureBlob`; reuse unchanged. |
| `AzureBlob` | `AzureBlobOptions` | same | general blob storage | runtime | distinct general storage. |
| `Publishing` | `PublishingOptions` | same | orchestrator/content publish | runtime eligibility | overlaps target/platform sections. |
| `PublishingTargets` | `PublishingTargetsOptions` | same | publish target selection | none located | overlap. |
| `PlatformPublishing` | `PlatformPublishingOptions` | same | short-form service | numeric behavior runtime | overlap; reuse unchanged. |
| `YouTube` | `YouTubeOptions` | same | OAuth/upload/thumbnail | token/health and runtime checks | two YouTube service generations. |
| `Meta`, `MetaPublishing` | `MetaOptions`, `MetaPublishingOptions` | same | OAuth and Meta publishers | runtime/token checks | option split is intentional. |
| `Localization` | localization options | same | narration/content orchestration | runtime | none material. |
| `Scheduler`, `Scheduling` | scheduler option types | same | scheduler hosted services | runtime | duplicate naming risk. |
| `SubtitleTtsOptions` | `SubtitleTtsOptions` | same | Phase 14 production execution | normalization/runtime validation | embedded subtitle/TTS coupling. |
| `ProductionPipeline` | `ProductionPipelineOptions` | same | production/recovery orchestration | defaults/runtime | overlaps scheduler recovery. |

Evidence: `ServiceCollectionExtensions.cs` options/registrations; option declarations under `Contracts/*Options.cs` and `Core/ProductionPipelineOptions.cs`; section names in API and Worker `appsettings*.json`. Sections named `FFmpeg`, `FFprobe`, `VideoRender`, `ImageGeneration`, and `VisualGeneration` were **Not found as top-level host sections**; they are represented chiefly by `Rendering`, Azure image, and visual sections. Minimal later bridge configuration, if contract binding cannot be convention-based, is one CG-A3 provider-binding section containing only implementation selection and workspace mapping; it was **not added**.

## 14. Test inventory

| Candidate | Unit | Integration/e2e | Real-provider/manual evidence | Missing | Confidence |
|---|---|---|---|---|---|
| Visual composite | Stellarium, image generator, infographic, resolver tests | API endpoint tests are limited | production registration; no paid smoke run | cross-provider documentary routing | **Moderate** |
| Azure Speech | `AzureSpeechSynthesisServiceTests` | orchestrator narration tests | production registration; no live Azure run inspected | 48-kHz stereo conversion, measured duration/checksum | **Strong** unit / **Weak** real |
| Subtitle embedded methods | scene narration and production execution tests | production execution tests | production code path | standalone contract, VTT, sidecar verification | **Moderate** |
| `FfmpegSceneRenderer` | FFmpeg/recipe tests | local process-style tests where tools exist | production DI/API | CG request translation/categories | **Moderate** |
| `FfmpegVideoRenderService` | extensive FFmpeg/motion/short tests | local render tests | production orchestrator | CG identity/checksum | **Strong** |
| `PrePublishValidationService` | focused tests | publishing-flow tests | production gate | full stream metadata/subtitle/checksum/error matrix | **Moderate** |
| Public storage | `PublicMediaStorageServiceTests` | mocked Azure behavior | production DI | checksum and CG identity | **Moderate** |
| Publishing | platform/publishing/token tests | `YouTubePublishingIntegrationTests` | production DI/worker path | safe live platform evidence | **Strong** unit / **Weak** real |

No paid-provider smoke tests were run. Test sources are under `Backend/tests/Astronomy.MediaFactory.Tests`; registrations and callers provide production-usage evidence.

## 15. Duplication and conflict analysis

| Conflict | Evidence | Recommendation |
|---|---|---|
| Stellarium/default, Azure cinematic, local/celestial, infographic, thumbnail visual systems | Rendering/ContentGen/Infrastructure visual classes and registrations | **Preferred reuse candidate:** composite by documentary type. **Internal fallback:** local/celestial. **Exclude from CG-A3:** thumbnail services as scene providers. |
| `AzureSpeechSynthesisService`, direct `AzureSpeechClient`, `AzureTtsAudioGenerationService`, weekly TTS | constructors/registrations and tests | **Preferred reuse candidate:** Azure client/service composite. **Internal fallback:** existing WAV helper. **Legacy retention:** weekly/category-specific flows. |
| Two embedded subtitle writers | two orchestrators | **Preferred reuse candidate:** Phase 14 measured-plan implementation. **Internal fallback:** ordinary orchestrator splitter. |
| `FfmpegSceneRenderer`, segment rendering inside `FfmpegVideoRenderService`, weekly renderers | relevant files/tests | **Preferred reuse candidate:** `FfmpegSceneRenderer` for scene port; `FfmpegVideoRenderService` for variant. **Legacy retention:** category/weekly paths. |
| Prepublish FFprobe plus private renderer probe helpers/YouTube shorts probe | sources above | **Preferred reuse candidate:** prepublish validation as boundary; **Internal fallback:** renderer probes; do not present helpers as CG verifier. |
| General and public Azure Blob services | two publishing storage files | **Preferred reuse candidate:** public service for public-media handoff; **Internal fallback:** general blob for persistence. |
| YouTube publish service generations and Meta full/short publishers | Publishing files | **Preferred reuse candidate:** orchestrated `ContentPublishService`/`ShortFormPublishingService`; **Legacy retention:** compatibility service retained. |
| `PipelineOrchestrator` and `ProductionPipelineExecutionService` | registrations and RC2 ownership comment | **Preferred reuse candidate:** current caller according to entry path; CG-A3 binding must target A2 execution separately. **Legacy retention:** neither is deleted; exclude neither during inventory. |

## 16. Evidence-based blocking gaps

| Gap | Affected port | Evidence | Severity | Adapter impact | New provider R&D? |
|---|---|---|---|---|---|
| No production implementation of any CG-A2 interface | all six | only fake implements ports; production DI has no registry binding | High | Minimal–Moderate mappings | No |
| Existing async cancellation cannot pass through synchronous ports | all | port signatures lack token; candidates accept token | High | contract boundary risk; A2 core cannot be changed here | No |
| No CG `ContentIdentity`/source instruction/correlation result propagation | all | candidate contracts return paths/domain results, not `DocumentaryMediaAssetResult` | High | adapter result normalization | No |
| Narration output mismatch and no synthesis duration/checksum | narration | Azure service writes 24-kHz mono MP3; returns path only | High | conversion/probe/hash coordination | No |
| Subtitle implementation embedded; Hindi may rewrite source | subtitle | private Phase 14 methods and duplicate rewrite | High | significant extraction/semantic guard | No |
| Estimated word-count duration fallback | subtitle/narration/scene | `BuildFallbackPhase14SceneDurationPlanItems` estimates at 155 wpm | High | forbid fallback for CG results | No |
| Scene renderer restricted to persisted plan IDs/two categories | scene | `FfmpegSceneRenderer.ValidateSelectedPlans`, `SupportedBatchCategories` | Medium | translation/extraction risk | No |
| Verification lacks required media properties | verification | `PrePublishValidationService.ProbeAsync` checks only duration/video/audio | High | coordinate existing probe/process/json/hash logic | No |
| FFprobe missing/invalid JSON throws; nonzero exit loses diagnostics | verification | direct `Process.Start`, `JsonDocument.Parse`, tuple fallback | High | failure normalization | No |
| No subtitle verification (embedded or sidecar) | verification | verifier never checks subtitle; SRT merely exists elsewhere | High | sidecar association/validation | No |
| No deterministic CG output naming/checksum | visual/narration/render/storage | candidates use run-specific conventional filenames and no common hash | Medium | adapter workspace/naming/hash | No |

## 17. A3.2 readiness decision

All six ports are mapped; candidates, current callers, DI/configuration, inputs/outputs, test evidence and main risks are identified. The overlapping orchestration paths have explicit current ownership rather than unresolved ambiguity. Therefore:

**READY FOR A3.2**

## 18. Mandatory statements

✓ A3.1 repository-wide inspection completed

✓ Certified CG-A2 core was not modified

✓ No new provider was implemented

✓ No adapter was implemented

✓ No production service was replaced

✓ Existing visual capabilities were inventoried

✓ Existing Azure Speech capabilities were inventoried

✓ Existing subtitle capabilities were inventoried

✓ Existing FFmpeg scene-rendering capabilities were inventoried

✓ Existing FFmpeg variant-rendering capabilities were inventoried

✓ Existing FFprobe capabilities were inventoried

✓ Existing storage capabilities were inventoried

✓ Existing publishing capabilities were inventoried

✓ Existing configuration and DI were inventoried

✓ Existing tests were inventoried

✓ Production usage was distinguished from class existence

✓ Reuse candidates were selected using repository evidence

✓ Adapter thickness was assessed

✓ Duplication risks were documented

✓ Blocking gaps were documented

✓ A3.2 readiness was explicitly decided

STOP. Do not implement A3.2. Submit this inventory for architectural review.
