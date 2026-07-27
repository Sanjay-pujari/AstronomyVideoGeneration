# CG-A3 — A3.2 Production Adapter Mapping Specification

**Status:** architectural specification; no implementation  
**Baseline:** approved A3.1 inventory, `docs/cg-a3-existing-production-capability-inventory.md`  
**Scope:** a future infrastructure/application bridge from the six certified O2.18 ports to existing production capabilities. CG-A2, its contracts, DI, configuration files, providers, and publishing code remain unchanged.

The six mapped certified ports are `IDocumentaryVisualAssetProvider`, `IDocumentaryNarrationAssetProvider`, `IDocumentarySubtitleAssetProvider`, `IDocumentarySceneCompositionProvider`, `IDocumentaryVariantCompositionProvider`, and `IDocumentaryRenderVerificationProvider`.

## 1. Normative decisions and execution boundary

### 1.1 Asynchronous Production Execution Bridge

The selected strategy is **B: extend production execution only through a separate infrastructure execution bridge**. `DocumentaryMediaPipelineOrchestrator` remains unchanged and usable for deterministic/fake-provider certification. Production must not implement its synchronous ports by blocking asynchronous calls.

```text
Certified O2.18 execution plan
  → async application-layer production host
  → existing async, cancellable production services
  → normalized physical artifact descriptors
  → O2.18 execution-record materialization
  → O2.19 certification
```

`Task.Result` and `GetAwaiter().GetResult()` are prohibited in normal production adapters. Blocking async calls are specifically prohibited on API, worker, scheduler, and recovery threads. An offline test harness may synchronously drive a completed deterministic task, but that exception is not a production design.

The future application boundary is conceptually:

```csharp
public interface IDocumentaryProductionExecutionHost
{
    Task<DocumentaryMediaPipelineExecutionRecord> ExecuteAsync(
        DocumentaryMediaPipelineRequest request,
        CancellationToken cancellationToken);
}
```

It will validate the O2.18 request; obtain or construct its deterministic execution plan; execute existing services asynchronously; preserve logical identities; normalize physical metadata; build O2.18 asset results, variant records, and the output manifest; and return a complete or partially complete execution record. It must not regenerate knowledge, modify O2.16 materialization or the O2.17 media project, publish, create fake results, invent audit evidence, or hide provider failures. “Partially complete” means successful prior results plus an explicit failed/cancelled operation record, never fabricated remainder data.

### 1.2 Cancellation and timeouts

The caller token enters `ExecuteAsync`; the host links it with a finite operation timeout and passes the linked token to every service/process call. User/host cancellation propagates `OperationCanceledException`, is recorded operationally as `Cancelled`, and is **not** converted into a provider failure result. Expiry of the adapter-owned timeout is distinguished from caller cancellation and mapped to `ProviderTimeout` or `ProcessTimedOut`. No indefinite wait is allowed.

Existing limits are reused: `StellariumOptions.CaptureTimeoutSeconds` (60-second default), `AzureSpeechOptions.TimeoutRetryAttempts`/delay for service-timeout sub-attempts, `RenderingOptions.FfmpegSegmentTimeoutSeconds` (120-second default) for scene FFmpeg, and the renderer's bounded segment timeout calculation. A future bridge must add a finite Azure Speech wall-clock provider timeout and finite FFprobe timeout because neither is presently a complete wall-clock policy. It documents those values in operation diagnostics rather than changing existing options in A3.2.

On cancellation the active child process is terminated by the existing process runner, an atomic final-name move is not performed, and partial output is deleted or moved to an attempt quarantine. Completed artifacts from earlier operations remain. Cleanup itself uses a short independent cleanup token.

## 2. Identity, artifact, workspace, checksum, and lifecycle

### 2.1 Two identity planes

Logical identity is immutable CG-A2 data: `AssetId`, `SourceInstructionId`, `VariantId`, `SceneId`, and `CorrelationId`. `AssetId` is never replaced by a path; `SourceInstructionId` is never replaced by a provider request ID; and paths never become deterministic architectural identities.

Physical identity is measured production data: `ContentIdentity`, `PhysicalPath`, `ContentType`, `Length`, `Checksum`, measured duration, dimensions, frame rate, sample rate, and channel count. The future infrastructure-only (not CG-A2 contract) `DocumentaryPhysicalArtifactDescriptor` concept has `AssetId`, `ContentIdentity`, `PhysicalPath`, `ContentType`, `Length`, `Checksum`, `DurationMilliseconds`, `Width`, `Height`, `FrameRate`, `AudioSampleRate`, `AudioChannelCount`, `ProviderId`, `AttemptCount`, and `CorrelationId`.

`ContentIdentity` is `sha256:<64 lowercase hexadecimal digits>` for the bytes of the finalized normalized artifact. This is path-independent and content-addressable. Duplicate bytes may share a content identity while retaining distinct logical asset IDs. Temporary paths, cloud ETags, provider request IDs, and random temporary names are never `ContentIdentity`.

### 2.2 Correlation, logging, and telemetry

Correlation flows from O2.18 request metadata → host logging scope → every service invocation → workspace metadata → process diagnostics → normalized result → execution manifest → O2.19. Where a service has no correlation parameter, the bridge uses an `ILogger` scope, `Activity` tag/baggage, immutable adapter-local operation context, and workspace manifest. It does not mutate prompts and uses correlation in the workspace directory only as specified below.

Each O2.18 asset attempt emits exactly one structured completion event and one duration metric, with: `CorrelationId`, `ExecutionId`, `VariantType`, `VariantId`, `SceneId`, `AssetId`, `SourceInstructionId`, `ProviderId`, `Attempt`, `Operation`, `DurationMilliseconds`, `Outcome`, `FailureCode`, and `PhysicalPath`. Start events/spans are permitted, but completion is not duplicated by wrappers. Provider latency, normalization latency, bytes, retry/sub-attempt counts, process exit/timeout, fallback use, and verification failures are span/metric fields. Azure/OpenAI/speech keys, OAuth tokens, SSML containing sensitive narration, and full sensitive responses are never logged. Provider diagnostics are sanitized and retained separately from stable codes.

### 2.3 Deterministic workspace and naming

The host owns creation of:

```text
{WorkspaceRoot}/{safe-CorrelationId}/{safe-ExecutionId}/
  variants/{VariantType}/
    scenes/{SceneSequence:D4}/
      visuals/ narration/ subtitles/ scene-video/
    output/
  attempts/{Operation}/{safe-AssetId}/{Attempt:D2}/tmp/
  diagnostics/
```

Final names are `{safe-AssetId}.png|.jpg|.wav|.srt`, `{safe-AssetId}.scene.mp4`, and `{safe-AssetId}.variant.mp4`. Safe names are deterministic: Unicode normalize Form KC, replace each run outside `[A-Za-z0-9._-]` with `_`, trim dots/underscores, and, when empty, reserved, colliding, or over 100 characters, use the first 72 safe characters plus `-` plus the first 20 lowercase hex characters of SHA-256 over the UTF-8 logical ID. The descriptor/manifest records logical ID ↔ path. Writes use random names only below `tmp`, flush/close, validate, then atomically move to the deterministic final path. A retry replaces only after successful validation.

The host owns the root and final directories; an adapter owns its attempt/temp directory. On success retain final normalized artifacts and remove temporary FFmpeg/provider files; retain provider-native intermediates only when configured. On failure retain sanitized diagnostics and quarantine useful failed outputs outside final directories. On cancellation quarantine or delete partials. Publishing reads only finalized manifest paths, never temp/quarantine paths.

### 2.4 SHA-256

SHA-256 is computed by streaming the closed final normalized visual, narration, subtitle, scene-video, and variant-video file after completion and validation, but before constructing the O2.18 result, O2.19 certification, storage, or publishing. Read/hash failure is `ChecksumFailed`. The lowercase digest populates `Checksum`; `ContentIdentity` is `sha256:<digest>`. Cloud ETags may remain provider metadata but are noncanonical.

## 3. Retry model

An **orchestrator attempt** is the O2.18 policy's complete semantic attempt. A **provider retry** repeats a semantic request and is disabled below the O2.18 layer. A **transport retry** retries connection-level traffic without changing the semantic request and may remain in an SDK. A **process retry** reruns FFmpeg/FFprobe/Stellarium and is disabled unless explicitly shown. Adapters report transport sub-attempts where observable; only O2.18 attempts populate `AttemptCount`. This prevents multiplying outer attempts by nested semantic retries.

| Operation | O2.18 attempts | Existing internal retries | Effective maximum | Timeout | Cancellation |
| --- | ---: | ---: | ---: | --- | --- |
| Stellarium visual | policy `MaximumVisualAttempts` | 0 process retries | outer maximum | capture option per attempt | linked token; kill process |
| Azure/OpenAI visual | policy `MaximumVisualAttempts` | SDK transport only | outer × documented transport requests; one semantic generation each | finite provider timeout | linked token |
| Local/infographic visual | policy `MaximumVisualAttempts` | 0 | outer maximum | finite adapter operation timeout | linked token |
| Azure narration | policy `MaximumNarrationAttempts` | `TimeoutRetryAttempts + 1` service-timeout sub-attempts | outer × configured sub-attempts, explicitly reported | new finite wall-clock bound | linked token; no result conversion |
| Subtitle | 1 per orchestration attempt | 0; one deterministic validate/regenerate pass only | 1 generation + 1 validation correction | host file-operation timeout | linked token for I/O |
| Scene composition | policy `MaximumCompositionAttempts` | 0 FFmpeg reruns | outer maximum | existing bounded FFmpeg timeout | linked token; kill process |
| Variant composition | policy `MaximumCompositionAttempts` | 0 FFmpeg reruns | outer maximum | existing bounded process timeout | linked token; kill process |
| Verification | 1 | 0 by default; optional one retry solely for transient lock/process-start | 1, exceptionally 2 infrastructure probes | new finite FFprobe timeout | linked token; kill process |

Fallback is not a hidden retry: it is allowed only by visual routing policy, consumes the same O2.18 attempt unless policy explicitly allocates a new attempt, and records requested/actual provider and reason.

## 4. Shared failure taxonomy and matrix

Stable codes are: `ConfigurationMissing`, `ProviderUnavailable`, `ProviderAuthenticationFailed`, `ProviderRateLimited`, `ProviderTimeout`, `ProviderRejectedRequest`, `ProviderInvalidResponse`, `ProviderContentPolicyRejected`, `SourceArtifactMissing`, `SourceArtifactInvalid`, `OutputArtifactMissing`, `OutputArtifactEmpty`, `OutputFormatInvalid`, `ChecksumFailed`, `DurationMeasurementFailed`, `DimensionMismatch`, `AudioStreamMissing`, `VideoStreamMissing`, `SubtitleMissing`, `DependencyMissing`, `ProcessStartFailed`, `ProcessTimedOut`, `ProcessExitedWithError`, `FileSystemFailure`, and `Cancelled`. Exception text is sanitized `FailureMessage`/diagnostics, never the stable code.

| Existing failure/exception or condition | O2.18 result status | Stable failure code | Retryable | Owner |
| --- | --- | --- | --- | --- |
| Missing endpoint/key/required option | Failed | `ConfigurationMissing` | No | host/configuration |
| Azure authentication/401/403 | Failed | `ProviderAuthenticationFailed` | No | operator |
| Azure 429/rate limit | Failed | `ProviderRateLimited` | Yes, policy/backoff | O2.18 attempt policy |
| Azure Speech service/wall timeout | Failed | `ProviderTimeout` | Yes | speech sub-attempt then O2.18 |
| Unsupported configured voice after allowed client voice candidates | Failed | `ProviderRejectedRequest` | No | configuration |
| OpenAI/image content-policy rejection | Failed | `ProviderContentPolicyRejected` | No unless prompt policy changes upstream | host |
| Provider malformed/empty response | Failed | `ProviderInvalidResponse` | Maybe | O2.18 policy |
| Stellarium executable absent | Failed | `DependencyMissing` | No | operator |
| Stellarium cannot start | Failed | `ProcessStartFailed` | Maybe | O2.18 policy |
| Stellarium capture timeout | Failed | `ProcessTimedOut` | Yes | O2.18 policy |
| Visual output absent / zero bytes | Failed | `OutputArtifactMissing` / `OutputArtifactEmpty` | Yes | O2.18 policy |
| Source visual/audio/SRT absent | Failed | `SourceArtifactMissing` | No until dependency repaired | host |
| Invalid source media/SRT | Failed | `SourceArtifactInvalid` | No | host |
| FFmpeg executable absent | Failed | `DependencyMissing` | No | operator |
| FFmpeg start failure | Failed | `ProcessStartFailed` | Maybe | O2.18 policy |
| FFmpeg timeout | Failed | `ProcessTimedOut` | Yes | O2.18 policy |
| FFmpeg nonzero exit | Failed | `ProcessExitedWithError` | classified diagnostics | O2.18 policy |
| FFprobe executable absent | Failed verification | `DependencyMissing` | No | operator |
| FFprobe timeout/nonzero exit | Failed verification | `ProcessTimedOut` / `ProcessExitedWithError` | transient only | verifier infrastructure |
| FFprobe invalid JSON/required property absent | Failed verification | `ProviderInvalidResponse` | No | verifier |
| Audio stream absent | Failed verification | `AudioStreamMissing` | No | composition |
| Video stream absent | Failed verification | `VideoStreamMissing` | No | composition |
| Dimension mismatch | Failed verification | `DimensionMismatch` | No | composition |
| Subtitle sidecar absent/empty/invalid | Failed verification | `SubtitleMissing` / `SourceArtifactInvalid` | No | composition/host |
| Output format/container invalid | Failed | `OutputFormatInvalid` | classified | adapter |
| Duration unavailable | Failed | `DurationMeasurementFailed` | transient probe only | adapter/verifier |
| SHA-256 read failure | Failed | `ChecksumFailed` | transient I/O only | adapter |
| Expected checksum mismatch | Failed verification | `ChecksumFailed` | No | verifier |
| File access/path/disk failure | Failed | `FileSystemFailure` | transient only | host/adapter |
| Caller cancellation | Operationally cancelled; no provider failure | `Cancelled` in execution record | No retry in cancelled run | host |

## 5. Visual port — `ExistingDocumentaryVisualAssetProvider`

This future **CompositeAdapter, Moderate** route consumes `DocumentaryVisualGenerationRequest`. It preserves `AssetPlan.AssetId`/`SourceInstructionId`, exact `VisualPrompt`, requested dimensions/format, attempt, and correlation. Routing is deterministic:

| Asset type | Primary | Permitted fallback |
| --- | --- | --- |
| `SkySimulationImage` | `StellariumVisualGenerationService` | none unless explicit request policy permits |
| `TelescopeViewImage` | Stellarium focused-view capture | `CelestialAssetProvider` only for explicitly representative, not simulated, imagery |
| `StarChartImage` | `AstronomyInfographicRenderer` / existing star-chart path | Stellarium chart-style capture |
| `ScientificDiagramImage` | `AstronomyInfographicRenderer` | none unless prompt explicitly permits generated illustration |
| `HistoricalIllustrationImage` | `AzureOpenAICinematicImageGenerator` | approved `FileVisualAssetProvider` local asset |
| `VisualImage` | `AzureOpenAICinematicImageGenerator` | `FileVisualAssetProvider`, then `CelestialAssetProvider`, when policy permits |

The bridge maps the exact documentary prompt and scene context (scene identity, subject, style, knowledge references, aspect/dimensions) into the candidate's existing prompt/request: Stellarium script/capture parameters for simulation; cinematic image prompt/request; infographic render input; or local/celestial lookup request. It invokes the existing async method selected by that input (the A3.1 public service operation; no command-generation duplication), receiving its existing file-path/asset response. Because public signatures differ and some direct focused/chart operations require future exposure, A3.3 must bind the exact overload without modifying the O2.18 contract.

| Route | Intermediate existing request | Existing method | Existing result |
| --- | --- | --- | --- |
| Stellarium simulation/focused/chart capture | `AstronomyContext` plus deterministic output directory (the service internally constructs its script/capture request) | `StellariumVisualGenerationService.PrepareVisualsAsync` | `IReadOnlyCollection<string>` output paths |
| Cinematic/historical generation | `AICinematicAssetRequest` | `AzureOpenAICinematicImageGenerator.GenerateAsync` | `AICinematicProviderResult` |
| Scientific/star-chart infographic | `QuestionDrivenVisualSpec`, existing source asset paths, and optional `AstronomyInfographicRenderVariant` | `AstronomyInfographicRenderer.RenderAsync` | completed `Task`; output at supplied `finalPath` |
| Approved local fallback | `AstronomyContext` plus deterministic output directory | `FileVisualAssetProvider.PrepareVisualsAsync` | `IReadOnlyCollection<string>` output paths |
| Representative celestial fallback | `CelestialAssetRequest` | `CelestialAssetProvider.GetAssetAsync` | `CelestialAsset` |

Where `PrepareVisualsAsync` could produce multiple files, the adapter must require the deterministically selected file corresponding to this asset request and reject an ambiguous collection as `ProviderInvalidResponse`; it may not manufacture multiple O2.18 results from one plan.

Post-processing validates regular file, nonzero bytes, decodable requested image format, and exact dimensions; converts only when the requested O2.18 format requires it; atomically finalizes; then measures length/dimensions and hashes. Duration/sample/audio fields are zero. The result maps logical IDs unchanged, actual service plus model/mode to `ProviderId`, descriptor `ContentIdentity`, length, width/height, checksum, outer attempt, correlation, and stable failure fields.

Every result/diagnostic records `RequestedProvider`, `ActualProvider`, `FallbackUsed`, and `FallbackReason`. Fallback never occurs silently; thumbnail services are prohibited. The host owns retries/timeouts; Stellarium capture timeout and SDK transport retries remain subordinate. Cancellation flows to capture/HTTP. Native downloads/screenshots are intermediates; final image is retained and temps cleaned under §2.

### Port mapping table

| Mapping stage | Source contract/member | Target contract/member | Transformation | Validation |
| --- | --- | --- | --- | --- |
| Request identity | request `AssetPlan`, `CorrelationId` | operation context | copy, never derive from path | nonblank; exact correlation |
| Provider input | `VisualPrompt`, dimensions, format | Stellarium/image/infographic/local request | deterministic route and scene context | supported type/format/size |
| Provider invocation | routed request | selected existing async service | await with linked token | one semantic call/attempt |
| Provider output | existing path/asset result | descriptor draft | record actual/requested provider | response/path present |
| Physical file validation | output path | final image | decode and optional format normalization | exists, nonzero, format/dimensions |
| Metadata probe | decoded image | width/height/length | authoritative image inspection | requested dimensions |
| Checksum | final bytes | `Checksum`, `ContentIdentity` | SHA-256 | 64 hex digits |
| Duration | not applicable | zero | none | zero required |
| Result identity | plan + descriptor | `DocumentaryVisualGenerationResult` asset | logical IDs unchanged | correlation/attempt exact |
| Failure mapping | exception/condition | status/code/message | §4 classification | code allow-list |
| Retry accounting | `Attempt` + SDK diagnostics | `AttemptCount` | outer only; transport separate | maximum policy |
| Correlation | request | scope/activity/manifest/result | copy | end-to-end equality |

## 6. Narration port — `ExistingAzureSpeechNarrationAssetProvider`

This future composite adapter maps one `DocumentaryNarrationSynthesisRequest` / one complete `DocumentaryNarrationBlock` to **exactly one primary Azure Speech synthesis request**. It never iterates subtitle cues or splits by SRT. Provider-limit chunking is allowed only inside the Azure service, must preserve ordered text, and must reassemble into one provider-native artifact; chunks are subparts, not O2.18 attempts.

Voice source of truth is `AzureSpeechOptions.GetPreferredVoices`: English uses the configured English list (current first/default evidence `en-US-AriaNeural`); Hindi uses the configured `Voices["hi"]` list (current first/default `hi-IN-SwaraNeural`). The conflicting `DefaultVoiceName` (`hi-IN-MadhurNeural`) is not used for documentary language resolution. An explicit valid `VoiceProfileId` may select within the language's configured list; unsupported selection fails rather than silently crossing language. Selected voice is stored in provider metadata/`ProviderId` (for example `AzureSpeech:<voice>`), diagnostics, and logs.

Flow: request → language/voice resolution → existing `SsmlBuilder` → `AzureSpeechSynthesisService`/`AzureSpeechClient` async synthesis → provider-native MP3/WAV → existing FFmpeg audio normalization → finalized **WAV, 48 kHz, stereo, linear PCM** → existing FFprobe/audio probe → SHA-256 → `DocumentaryNarrationSynthesisResult`. The normalized WAV, not native media, is the O2.18 asset and scene-composition input. Native output is optional diagnostic/intermediate.

The ordinary intermediate call is the complete block text plus deterministic output directory passed to `AzureSpeechSynthesisService.SynthesizeAsync`, returning the native output path (`string`); an explicit SSML path uses `AzureSpeechClient.SynthesizeWavSsmlAsync`, returning `byte[]`. `SynthesizeMp3Async` (`byte[]`) is reused where the established synthesis service selects MP3. In every case, existing `SsmlBuilder`/client owns SSML and voice behavior; the adapter does not build a competing speech client.

Duration is measured from normalized WAV. Estimates, word count, and planned narration duration cannot support a successful result; missing/invalid measurement is `DurationMeasurementFailed`. Validate PCM/container, nonzero length, 48 kHz, two channels, and positive duration. Host owns outer attempt and wall timeout; Azure client service-timeout retries are reported sub-attempts. Cancellation propagates and partial WAV is quarantined/deleted.

### Port mapping table

| Mapping stage | Source contract/member | Target contract/member | Transformation | Validation |
| --- | --- | --- | --- | --- |
| Request identity | `AssetPlan`, block, correlation | context | exact copy | one block; IDs nonblank |
| Provider input | block text/language/voice profile | SSML + Azure request | resolve configured language voice; build SSML | text unchanged; supported voice |
| Provider invocation | SSML | Azure async synthesis | one primary request; await token | finite wall timeout |
| Provider output | native bytes/file | native intermediate | ordered reassembly only if provider limit | exists, nonzero |
| Physical file validation | native media | normalized WAV | existing FFmpeg normalization | PCM/48 kHz/stereo |
| Metadata probe | normalized WAV | descriptor audio fields | existing FFprobe/audio probe | required fields present |
| Checksum | final WAV | checksum/content identity | SHA-256 | recomputable |
| Duration | probe duration | result measured duration | milliseconds, no estimates | positive and available |
| Result identity | plan + descriptor | synthesis result/asset | original IDs, selected voice provider | exact correlation |
| Failure mapping | Azure/FFmpeg/probe errors | status/code/message | §4 | cancellation distinguished |
| Retry accounting | request attempt + client sub-attempts | attempt/diagnostics | outer count only in asset | no multiplication |
| Correlation | request | scope/activity/workspace/result | copy | exact equality |

## 7. Subtitle port — `ExistingDocumentarySubtitleAssetProvider`

A future `IExistingSubtitleGenerationService` will expose the smallest reusable kernel from Phase 14 measured-duration logic; the adapter will call it. It must be extraction, not copied private methods, and existing orchestrators will eventually use the same kernel. No extraction or implementation occurs in A3.2. Ordinary subtitle splitting is legacy-only and disabled in Shadow/Certified unless explicit legacy fallback policy enables it.

Certified behavior: narration/cue text is neither rewritten nor paraphrased; Hindi duplicate-text rewriting is disabled; word-count duration fallback is disabled; supplied order and text are preserved (line wrapping only may change presentation); actual measured narration duration is mandatory. Cues begin at/after zero, are monotonic and non-overlapping, do not exceed audio duration, and the last ends exactly at measured duration within configured tolerance. Empty/mismatched cues fail. Primary output is UTF-8 SRT. Return cue count, SRT content identity/length/checksum, actual final cue end/duration, language, scene/logical identities, and correlation.

No provider retry exists. Within one orchestration attempt, a deterministic validation/regeneration pass may occur once without changing source text. Host token covers I/O; no indefinite operation. On success retain SRT; on failure retain sanitized validation diagnostics and no final artifact.

### Port mapping table

| Mapping stage | Source contract/member | Target contract/member | Transformation | Validation |
| --- | --- | --- | --- | --- |
| Request identity | plan, variant, scene, correlation | context | copy | exact IDs/correlation |
| Provider input | cues + measured narration duration | extracted Phase 14 kernel input | wrap lines only; allocate measured time | positive measured duration |
| Provider invocation | kernel input | `IExistingSubtitleGenerationService` future operation | deterministic generation | no provider retry |
| Provider output | SRT text/cue model | temp SRT | UTF-8 serialize | parse succeeds |
| Physical file validation | temp SRT | final SRT | atomic finalize | exists/nonempty |
| Metadata probe | parsed SRT | cue count/final end | parse timestamps | order/overlap/bounds |
| Checksum | final SRT | checksum/content identity | SHA-256 | recomputable |
| Duration | measured audio + final cue | result duration/final end | milliseconds | ends within tolerance |
| Result identity | plan + descriptor | subtitle result/asset | preserve source/scene IDs | language/correlation exact |
| Failure mapping | invalid cue/I/O | failed result | source invalid/output/file codes | stable code only |
| Retry accounting | orchestration attempt | diagnostics | zero provider retry; correction noted | at most one correction |
| Correlation | request | context/scope/manifest/result | copy | exact equality |

## 8. Scene composition port — `ExistingFfmpegSceneCompositionProvider`

Classification is **ThinAdapter or CompositeAdapter; thickness Moderate until direct invocation is proven**. Strategy **A** is selected: future work exposes a reusable single-scene rendering operation from `FfmpegSceneRenderer`; it must reuse that renderer's command-generation/process behavior rather than duplicate FFmpeg logic. It takes no unrelated persisted plan or database category at its adapter boundary.

One `DocumentarySceneCompositionRequest` maps visual descriptor paths in plan order, normalized narration WAV, SRT, effective scene timing, transition, aspect/dimensions, frame rate, workspace, and deterministic output path to that operation. Exactly one O2.18 scene plan yields one scene MP4. Inputs' logical identities/checksums are verified before render. Existing process runner gets the linked token and existing bounded segment timeout and performs one process attempt.

The current reusable boundary is `SceneRenderingRequest` → `FfmpegSceneRenderer.RenderScenesAsync` → `SceneRenderingResponse`. Strategy A narrows/exposes that same internal rendering kernel as a single-scene async operation; until then, the adapter must not call the batch method with invented persisted identifiers and claim exact semantics.

After render, FFprobe must confirm readable MP4, video and audio streams, requested width/height/frame rate tolerance, 48-kHz stereo audio policy, and positive/effective duration; SHA-256 follows. Result includes content identity, length, measured/effective duration, dimensions, frame rate, process/provider identity, outer attempt, correlation, and failures. Temp frames/SRT copies/command files are cleaned on success; sanitized command/exit diagnostics are retained on failure.

### Port mapping table

| Mapping stage | Source contract/member | Target contract/member | Transformation | Validation |
| --- | --- | --- | --- | --- |
| Request identity | plan/media scene/correlation | single-scene context | copy | scene/asset IDs exact |
| Provider input | visual/narration/subtitle assets + timing/layout | reusable `FfmpegSceneRenderer` single-scene input | resolve descriptor paths; minimum recipe in memory | source hashes/files valid |
| Provider invocation | single-scene input | existing FFmpeg engine/process runner | async one render | finite process timeout |
| Provider output | scene response/path | descriptor draft | record command/process identity | expected output path |
| Physical file validation | scene MP4 | finalized scene MP4 | atomic move | exists/nonzero/readable |
| Metadata probe | MP4 | video/audio metadata | FFprobe | streams/dimensions/rates |
| Checksum | final MP4 | checksum/content identity | SHA-256 | recomputable |
| Duration | FFprobe | effective duration | milliseconds | positive/expected tolerance |
| Result identity | plan + descriptor | scene composition result | logical IDs unchanged | one result per scene |
| Failure mapping | source/process/probe errors | failed result | §4 | no guessed metadata |
| Retry accounting | `Attempt` | asset attempt | outer count only | no process rerun |
| Correlation | request | scope/process diagnostics/result | copy | exact equality |

## 9. Variant composition port — `ExistingFfmpegVariantCompositionProvider`

This future **CompositeAdapter, Moderate** consumes only ordered scene-video results from scene composition plus variant dimensions, frame rate, required audio policy, subtitle association policy, workspace, and output path. It must not rebuild scenes from images, narration, or cue inputs.

The current `FfmpegVideoRenderService.RenderAsync` public flow builds segments; future work must expose its existing concat/transition/finalization kernel as one reusable async attempt accepting scene MP4 paths. It reuses `RenderManifestBuilder`, FFmpeg argument building, concat/transition, process runner, and final encoding; it does not create a second FFmpeg engine. Input order must equal O2.18 scene order and scene count.

The current intermediate/public mapping is `RenderManifest` → `FfmpegVideoRenderService.RenderAsync` → final path (`string`). The future extracted input is the minimum in-memory finalization projection of that manifest containing ordered scene paths and variant output properties; no database persistence or source-scene reconstruction is allowed.

Probe the final MP4 and return path through the descriptor, effective duration, scene count, length, checksum, width/height/frame rate, audio sample rate/channels, outer attempt and correlation. Host owns composition retries; renderer/process performs no silent rerun. Cancellation kills the process; combined/temp/concat files are cleaned on success and quarantined with diagnostics on failure.

### Port mapping table

| Mapping stage | Source contract/member | Target contract/member | Transformation | Validation |
| --- | --- | --- | --- | --- |
| Request identity | asset plan/media variant/correlation | finalization context | copy | variant/asset IDs exact |
| Provider input | ordered `SceneAssets`, dimensions/rates | concat/finalization input | resolve only scene MP4 paths and policy | count/order/source hashes |
| Provider invocation | finalization input | extracted existing `FfmpegVideoRenderService` kernel | await one FFmpeg attempt | finite process timeout |
| Provider output | final path | descriptor draft | deterministic output | expected path |
| Physical file validation | MP4 | final variant MP4 | atomic finalize | exists/nonzero/readable |
| Metadata probe | MP4 | all video/audio fields | FFprobe | streams and expected properties |
| Checksum | final MP4 | checksum/content identity | SHA-256 | recomputable |
| Duration | FFprobe | effective duration | milliseconds | expected range |
| Result identity | plan + descriptor | variant result | preserve variant/asset IDs | scene count exact |
| Failure mapping | process/probe/input failure | failed result | §4 | stable code |
| Retry accounting | `Attempt` | asset attempt | outer only | no hidden render retry |
| Correlation | request | scope/diagnostics/result | copy | exact equality |

## 10. Verification port — `ExistingFfprobeRenderVerificationProvider`

This is a **CompositeAdapter, Moderate**, combining `PrePublishValidationService`, existing FFprobe/process helpers, SRT parsing, and existing file/hash utilities. `PrePublishValidationService` alone is explicitly insufficient and remains the independent publishing gate.

Its existing validation portion maps `PrePublishValidationRequest` → `PrePublishValidationService.ValidateAsync` → `PrePublishValidationReport`; strict FFprobe helper output, recomputed hash, trusted render-manifest scene count, and parsed SRT measurements are then composed into `DocumentaryRenderVerificationResult` rather than inferred from that report.

For every variant, verify: exists; nonzero length; readable container; video stream; audio stream; exact expected width/height; frame rate within documented tolerance (recommended absolute ±0.01 fps or equivalent rational comparison); expected sample rate/channels; measured effective duration inside request min/max; recomputed checksum equal to expected; scene count when available from trusted render manifest; and required subtitle association.

Subtitle modes are `EmbeddedSubtitle`, `SidecarSubtitle`, and `Either`; current default is `SidecarSubtitle`. A sidecar can satisfy verification without an MP4 subtitle stream only if it exists, is nonempty, parses, has monotonic non-overlapping cues, ends within output duration, and its manifest association has the same variant and correlation. `Either` accepts either fully valid form; embedded mode requires a probed subtitle stream.

No guessed/planned duration fallback is accepted. Missing FFprobe, invalid JSON, missing required metadata, or unavailable measurement fails; missing values never become defaults. Verification has no implicit retry, except one explicitly logged infrastructure retry may handle a temporary file lock or process-start condition. Cancellation propagates rather than becoming verification failure.

### Port mapping table

| Mapping stage | Source contract/member | Target contract/member | Transformation | Validation |
| --- | --- | --- | --- | --- |
| Request identity | variant, variant asset, correlation | verification context | copy | same variant/correlation |
| Provider input | expected fields/checksum/sidecar association | prepublish + probe/hash/SRT inputs | resolve finalized paths only | manifest association valid |
| Provider invocation | inputs | prepublish validator + FFprobe/helpers | await independently; aggregate | finite probe timeout |
| Provider output | reports/probe JSON | verification measurements | strict parse | all required properties |
| Physical file validation | variant/sidecar paths | checks | inspect regular files | exist/nonzero |
| Metadata probe | MP4/SRT | actual stream/cue fields | FFprobe + SRT parser | tolerances/policies |
| Checksum | final MP4 bytes | checksum-valid flag | recompute SHA-256 | constant/exact comparison |
| Duration | FFprobe duration | actual duration | milliseconds | request range |
| Result identity | request + measurements | verification result | retain variant/correlation | no invented data |
| Failure mapping | failed check/tool error | rejected/failed verification | aggregate stable codes | unavailable means fail |
| Retry accounting | verification call | diagnostics | default one; exception noted | max two probes only allowed cases |
| Correlation | request/sidecar manifest | scope/activity/result | copy and compare | all equal |

## 11. Cross-port artifact matrix

| Asset type | Provider-native artifact | Normalized artifact | O2.18 format | Probe required | Checksum |
| --- | --- | --- | --- | --- | --- |
| Visual image | screenshot/generated/downloaded/local image | decoded dimension-correct PNG/JPEG | requested PNG/JPEG | image decoder dimensions/format | SHA-256 normalized bytes |
| Narration audio | Azure MP3 or 24-kHz mono WAV | PCM WAV 48 kHz stereo | WAV | FFprobe/audio stream + measured duration | SHA-256 normalized WAV |
| Subtitle document | cue model/SRT text | UTF-8 validated SRT | SRT | strict SRT parse/timestamps | SHA-256 final SRT |
| Scene video | FFmpeg temporary MP4 | probed final scene MP4 | MP4 | FFprobe video/audio/duration/layout | SHA-256 final MP4 |
| Variant video | concat/transition intermediates | probed final variant MP4 | MP4 | full FFprobe + scene/sidecar association | SHA-256 final MP4 |

## 12. Per-port operational summary

| Port | O2.18 request/result | Existing implementation / intermediate / invocation / result | Post-process and owners |
| --- | --- | --- | --- |
| Visual | `DocumentaryVisualGenerationRequest` → `DocumentaryVisualGenerationResult` | composite routes to existing visual prompt, Stellarium capture/script, image generation, infographic, or local request; invokes selected existing async generation/capture method; receives path/asset response | decode/normalize/probe/hash; O2.18 visual attempts; provider/capture timeout; host workspace/cleanup |
| Narration | `DocumentaryNarrationSynthesisRequest` → `DocumentaryNarrationSynthesisResult` | SSML/synthesis input → `AzureSpeechSynthesisService`/`AzureSpeechClient` async method → native audio bytes/path | normalize WAV, FFprobe duration, hash; O2.18 narration retry plus documented speech sub-attempts; host wall timeout |
| Subtitle | `DocumentarySubtitleGenerationRequest` → `DocumentarySubtitleGenerationResult` | future extracted Phase 14 kernel input/operation → cue/SRT output | strict parse/finalize/hash; no provider retry; host I/O timeout |
| Scene | `DocumentarySceneCompositionRequest` → `DocumentarySceneCompositionResult` | future single-scene input → exposed existing `FfmpegSceneRenderer` operation → path/response | FFprobe/hash; O2.18 composition attempts; existing process timeout |
| Variant | `DocumentaryVariantCompositionRequest` → `DocumentaryVariantCompositionResult` | ordered MP4 finalization input → exposed concat/finalization kernel in `FfmpegVideoRenderService` → final path | full probe/hash; O2.18 composition attempts; existing process timeout |
| Verification | `DocumentaryRenderVerificationRequest` → `DocumentaryRenderVerificationResult` | prepublish request + strict FFprobe/hash/SRT inputs → existing validator/helpers → report/probe data | aggregate every required measurement; default no retry; finite host probe timeout |

For all rows, logical result identity is copied, physical fields are measured, correlation is scope/activity/context/result data, completion telemetry follows §2, failures follow §4, and final/temporary/diagnostic cleanup follows §2. Existing signatures that are not currently a direct semantic match require the specified future extraction/exposure, not behavioral duplication.

## 13. Configuration and execution modes

Reuse unchanged: `AzureOpenAI`, `AzureOpenAIForImage`, `AzureSpeech`, `Rendering`, `Stellarium`, `VisualIntelligence`, `CelestialAssets`, `PublicMediaStorage`, `AzureBlob`, `Publishing`, `YouTube`, `Meta`, `MetaPublishing`, `Localization`, `Scheduler`, `SubtitleTtsOptions`, and `ProductionPipeline`.

The following is documentation only and is **not** added to appsettings in A3.2:

```json
"DocumentaryProductionAdapters": {
  "Enabled": true,
  "ExecutionMode": "Shadow",
  "VisualProvider": "ExistingCompositeVisual",
  "NarrationProvider": "AzureSpeech",
  "SubtitleProvider": "ExistingMeasuredSubtitle",
  "SceneComposer": "ExistingFfmpegScene",
  "VariantComposer": "ExistingFfmpegVariant",
  "RenderVerifier": "ExistingCompositeFfprobe",
  "UseExistingOutputLayout": true,
  "RetainProviderIntermediates": false,
  "EnableLegacyFallback": false
}
```

Future `Legacy` runs only the existing pipeline and existing gate; `Shadow` runs the async bridge and O2.19 without publishing its output or changing legacy publication; `Certified` makes bridge output publish-eligible only after certification. Rollout must never invoke both paths' paid providers for the same request without explicit authorization.

## 14. Publishing gate

Publishing stays outside CG-A2 and the execution host. Future eligibility is:

```text
existing PrePublishValidationService passed
AND O2.19 status == Certified
AND publishing is enabled and requested
AND platform credentials and platform format validation passed
```

This augments, never replaces, the current pre-publish gate. O2.19 rejection retains finalized artifacts, diagnostics, and certification result and prohibits all publication/storage handoff intended for publication. Publishing must consume only manifest-declared finalized files.

## 15. Decision log

| Topic | Decision | Reason | Existing evidence | Impact | Alternative rejected |
| --- | --- | --- | --- | --- | --- |
| Async bridge strategy | Separate async production host; option B | preserves cancellation and avoids sync-over-async | A3.1: ports sync, candidates async | future application/infrastructure boundary | blocking port adapters |
| Narration granularity | one block = one primary synthesis request | preserves block semantics/voice continuity | Azure call is per service invocation; not per cue | chunking internal only | per-SRT-cue TTS |
| English voice source | configured English list, current first `en-US-AriaNeural` | one configuration authority | A3.1 narration findings | selected voice audited | hard-coded adapter voice |
| Hindi voice source | `Voices["hi"]`, current first `hi-IN-SwaraNeural` | resolves conflicting defaults | selector ignores `DefaultVoiceName` (`Madhur`) | deterministic Hindi selection | ambiguous/default-property selection |
| Narration format | normalized WAV/48-kHz/stereo/PCM | exact composition artifact | native Azure output is 24-kHz mono | normalization required | native MP3 as O2.18 success |
| Measured-duration owner | post-normalization FFprobe/audio helper | duration must match consumed bytes | synthesis returns no duration | probe failure fails narration | estimate/planned duration |
| Subtitle text preservation | exact source; wrapping only | certified traceability | Phase 14 and ordinary splitter exist | extracted kernel needs certified mode | rewriting/paraphrase |
| Hindi rewrite | disabled in Certified | exact source semantics | Phase 14 optional duplicate rewrite | no duplicate mutation | existing rewrite in certified output |
| Word-count fallback | disabled in Certified | not physical evidence | Phase 14 has 155-wpm fallback | missing measurement fails | estimated successful timing |
| Visual routing | deterministic table in §5 | semantic providers differ | A3.1 visual matrix | composite adapter | defaulting all to one provider |
| Visual fallback | only explicit semantic/policy fallbacks, fully recorded | prevents silent substitution | candidates have different fidelity | auditable actual provider | thumbnails/silent fallback |
| Scene invocation | expose existing single-scene operation (A) | avoids persistence coupling/duplication | current renderer takes plan IDs | moderate future extraction | synthesize persisted DB plan; copy commands |
| Variant from scene MP4s | expose existing concat/finalization kernel | port requires composed scenes | current public renderer rebuilds segments | CompositeAdapter Moderate | regenerate source assets; new engine |
| Subtitle verification | Sidecar default; embedded/either supported | current output uses SRT sidecar | no current embedded check | strict associated SRT accepted | require embedded only / ignore subtitles |
| Checksum | SHA-256 finalized normalized bytes | stable/path-independent | existing file/hash utilities selected | all assets hashed | ETag/path checksum |
| Physical ContentIdentity | `sha256:<lowerhex>` | content-addressable and portable | physical path is current service output | descriptor maps path separately | file path/provider request ID |
| Retry ownership | O2.18 owns semantic attempts; nested transport only | prevents multiplicative retries | speech has timeout sub-attempts; renderers one process | explicit matrices/telemetry | stacked generic retry policies |
| Publishing gate | existing validation AND O2.19 Certified AND request/config/platform checks | preserves gate and adds certification | A3.1 publishing flow | rejected output retained, not published | replace prepublish gate / publish on render success |

## 16. Completion report and A3.3 readiness

| Required report item | Outcome |
| --- | --- |
| Files created | `docs/cg-a3-production-adapter-mapping-specification.md` |
| Files modified | none (other than creation above) |
| Async execution decision | separate Asynchronous Production Execution Bridge; option B |
| Production execution host specification | future `IDocumentaryProductionExecutionHost.ExecuteAsync` in §1 |
| Identity / ContentIdentity strategy | immutable CG identities; path-independent `sha256:<digest>` in §2 |
| Workspace / naming / checksum strategy | deterministic hierarchy/sanitization; atomic finalization; SHA-256 in §2 |
| Retry / cancellation strategy | single semantic owner, bounded sub-attempts; linked token propagation in §§1, 3 |
| Failure taxonomy | stable allow-list and mapping matrix in §4 |
| Visual / narration / subtitle mappings | §§5–7 |
| Scene / variant / verification mappings | §§8–10 |
| Configuration / publishing gate | §§13–14; documentation only |
| Port mapping tables | one twelve-stage table in each of §§5–10 |
| Failure / retry / artifact matrices | §§4, 3, 11 |
| Decision log | §15, including reason, evidence, impact, and rejected alternative |
| Blocking questions | none for the mapping; exact future public overload names and numeric host/FFprobe wall timeouts are A3.3 implementation choices constrained by this specification |
| A3.3 readiness decision | all stated readiness dimensions are resolved below |

The async strategy, all six mappings, narration normalization/duration, certified subtitle semantics, deterministic visual routing, single-scene strategy, scene-video-only finalization, strict verification, failures/retries, identity/correlation, and workspace/naming/checksum requirements are complete.

## READY FOR A3.3

✓ A3.2 adapter mapping specification completed  
✓ Certified CG-A2 core was not modified  
✓ No production adapter was implemented  
✓ No production provider was replaced  
✓ Existing provider behavior was reused by design  
✓ Async production execution strategy was defined  
✓ Blocking async calls were prohibited  
✓ Logical asset identity was separated from physical path  
✓ Correlation propagation was defined  
✓ Deterministic workspace and naming were defined  
✓ SHA-256 checksum strategy was defined  
✓ Retry ownership was defined  
✓ Cancellation behavior was defined  
✓ Failure-code mapping was defined  
✓ Visual routing was defined  
✓ Narration block-to-TTS mapping was defined  
✓ Per-SRT-cue TTS was prohibited  
✓ English voice mapping was defined  
✓ Hindi voice mapping was defined  
✓ Narration normalization was defined  
✓ Measured narration duration was required  
✓ Subtitle text preservation was defined  
✓ Hindi text rewriting was disabled for certified mode  
✓ Word-count duration fallback was disabled for certified mode  
✓ Scene composition reused the existing FFmpeg engine  
✓ Variant composition consumes pre-rendered scene videos  
✓ No second FFmpeg engine was proposed  
✓ FFprobe verification was fully specified  
✓ Verified SRT sidecar behavior was defined  
✓ Guessed media metadata was prohibited  
✓ Existing pre-publish validation was retained  
✓ O2.19 publishing gate was defined  
✓ No paid-provider calls were executed  
✓ A3.3 readiness was explicitly decided

**STOP.** Submit this A3.2 specification for architectural review. Do not implement A3.3, adapters, subtitle extraction, FFmpeg changes, DI/configuration changes, or paid-provider smoke tests as part of A3.2.
