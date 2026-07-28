# CG-A3 A3.10 — Production Execution Host

## Boundary and orchestration

A3.10 is application-layer orchestration in `Astronomy.MediaFactory.ProductionAdapters`. It invokes only the certified adapter ports: visual generation, narration synthesis, subtitle generation, scene composition, media verification, and variant composition. It contains no provider SDK, process, storage, upload, or publishing call. A3.11 real-provider smoke execution and A3.12 storage/publishing remain out of scope.

The coordinator is disabled by default. Disabled execution returns `NotStarted` before workspace creation, diagnostics, registry access, or adapter invocation. The compatibility `IDocumentaryProductionExecutionHost` returns the coordinator's mapped pipeline execution record; unsuccessful or disabled execution may still return null.

## Execution records and evidence

Successful and partial executions are mapped through `IDocumentaryProductionExecutionRecordMapper`. Variant records deterministically contain each scene's visual assets in prompt order, narration, subtitles, verified scene video, and verified final variant. `DocumentaryProductionSceneExecutionResult.VisualArtifacts` is an immutable ordered collection; `VisualArtifact` remains a read-only last-item convenience property.

The registry, not adapter return values, is the dependency source. Every successful stage resolves its finalized artifact by asset kind and correlation before downstream use. Missing registration becomes `SourceArtifactMissing`; the host never registers or finalizes adapter output.

## Voice, narration, and audio policy

The request builder asks the existing `IDocumentaryNarrationVoiceResolver` to resolve `default`, thereby reusing `AzureSpeechOptions.Voices` for English and Hindi rather than embedding placeholder voice IDs. Multiple certified narration blocks are ordered and combined into one scene-level narration request, preserving all text and avoiding per-subtitle-cue TTS.

Verification accepts explicit `requireAudio` and `allowAudio` policy. Narrated scenes and final variants require audio; the builder can represent a silent scene as `RequireAudio=false, AllowAudio=true`. Subtitle strategy remains explicit and only `Embedded` requires an embedded subtitle stream.

## Timeouts, retry, and cancellation

`DocumentaryProductionOperationRunner` owns every adapter attempt. It creates a fresh attempt context, links the caller token, calls `CancelAfter` with the configured operation timeout, and applies the same retry policy to visual, narration, subtitle, scene composition, variant composition, and verification. Attempts preserve execution, correlation, asset, variant, and scene identity while incrementing only attempt number. Deterministic failures are never retried. Host timeout maps to `ProviderTimeout`, or `ProcessTimedOut` for composition. Caller cancellation propagates as `OperationCanceledException` and is never normalized.

Options validation requires 1–10 attempts and, when specified, timeouts of 1–86400 seconds.

## Persistence

Manifest, execution-record, and completion-diagnostic persistence are independent guarded steps. A filesystem exception appends `FileSystemFailure` without replacing an earlier pipeline failure; caller cancellation propagates. A reference is returned only after its corresponding write succeeds. The persisted execution evidence contains the host result, mapped pipeline record, ordered variants/assets, and safe failure evidence; it excludes commands, raw provider payloads, stderr, credentials, and tokens.

## Certification boundary and limitations

No O2.19 implementation was introduced. `RunProductionCertification` remains an intent flag; because no certification port is wired in A3.10, publishing eligibility is not granted when certification is requested. No storage or publishing service is referenced or invoked.

The isolated .NET SDK 10.0.302 restored and built the solution. Operation-runner tests and the existing A3.9 verification regression suite pass. The repository does not yet contain the complete named fake-host matrix requested for final certification, so the evidence does not justify claiming all full-flow, four-variant, persistence, determinism, and non-mutation criteria.

## A3.11 readiness

**NOT READY FOR A3.11**

The implementation gaps identified in the coordinator were closed, but A3.11 remains gated on adding and executing the complete fake-adapter host matrix. No real provider smoke should execute until that evidence exists.

## A3.10 final-closure policy evidence (2026-07-27)

Pipeline success is not publishing authorization. `EligibleForPublishing` is true only when a non-null production certification result reports `Certified == true`; because A3.10 does not execute O2.19, current successful executions remain ineligible.

The common operation runner now normalizes unexpected adapter exceptions after preserving the distinct caller-cancellation path. Normalization uses the certified failure normalizer, so provider/process timeout and filesystem/rejected-request codes remain stable and private exception messages are not returned.

The executed focused filter contains 9 passing operation-runner tests, and the A3.9 verification regression contains 64 passing tests. The complete fake-host matrix and the remaining required named suites are still absent; therefore this evidence does not certify A3.10 and the readiness decision remains **NOT READY FOR A3.11**. Real providers were **Not executed.**

# A3.10 production execution host

## Certification state

**CG-A3 A3.10 — NOT CERTIFIED**  
**NOT READY FOR A3.11**

The production coordinator exists, but certification requires coordinator-level fake-adapter evidence. Architecture and dependency-injection evidence is now executable; it is not a substitute for the missing end-to-end matrix.

## Proven evidence

- The Core assembly does not reference ProductionAdapters.
- The production execution host does not directly launch processes, call cloud/provider clients, register or finalize adapter artifacts, or synchronously block asynchronous work.
- `AddDocumentaryProductionBridge` passes strict service-provider validation and resolves the coordinator, compatibility host, operation runner, request builder, dependency resolver, and execution-record mapper when external boundaries are replaced by inert fakes.
- Host options remain disabled by default and invalid retry/timeout settings are rejected by the production validator.
- Publishing eligibility remains outside pipeline success and requires explicit certification.

## Evidence still required

The one-scene, multi-scene, four-variant, voice-resolution, narration-aggregation, multiple-visual, registry-dependency, verification-gate, retry, timeout, cancellation, persistence, execution-record, determinism, and non-mutation scenarios still require a real coordinator harness and successful behavioral runs. Until those exist, none of those properties are certified by this document.

## Executed coordinator harness evidence (2026-07-28)

The test project now contains a provider-free harness around the real coordinator and compatibility facade. A composite fake implements all six adapter ports, writes deterministic non-empty artifacts, computes SHA-256 identities, and registers them through the real physical artifact registry. The executed four-variant flow proves coordinator sequencing, English/Hindi voice resolution through the request builder, scene and final verification, persisted manifest/diagnostics, complete execution-record mapping, registry-based descriptor consumption, and the invariant that successful media production is not publishing eligibility. Three behavioral full-flow tests pass. The remaining mandated negative and resilience matrices are still outstanding, therefore A3.10 remains not certified and this evidence does not authorize A3.11.

## Resilience execution evidence

The same harness now models success, retryable and non-retryable failures, thrown exceptions, cancellation waits, successful output without registry registration, and verification rejection. Independent concurrent queues prevent one operation class from consuming another class's scripted outcome. Each call captures its immutable request and attempt context and signals start before any cancellation wait.

Coordinator-level tests exercise registry dependency enforcement, scene and final verification gates, preservation of successful upstream evidence, adapter availability, disabled execution, uniform retry, uniform timeout, and caller cancellation. Controlled registry and diagnostics wrappers exercise successful persistence, filesystem failure append semantics, null references for failed writes, and preservation of the original operation failure. Millisecond timeout configuration is a test fallback; explicit per-operation second settings retain precedence.

These additions materially reduce the evidence gap, but do not prove the exact one-scene and multi-scene fixtures, complete record field matrix, deterministic semantic comparison, deep non-mutation matrix, A3.9 regression, or broad regression in the latest run. Accordingly the architecture decision remains **CG-A3 A3.10 — NOT CERTIFIED** and **NOT READY FOR A3.11**. Publishing eligibility remains false, and no external provider, FFmpeg, FFprobe, upload, or publish action was invoked.
