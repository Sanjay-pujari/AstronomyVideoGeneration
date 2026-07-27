# CG-A3 A3.8 — Existing FFmpeg variant composition adapter

## Scope and boundary
A3.8 consumes only finalized `SceneVideo` artifacts. Variant composition does not generate visuals. Variant composition does not synthesize narration. Variant composition does not generate subtitles. Variant composition does not render scenes. One variant request produces one finalized variant video. It does not publish, upload, or host execution.

## Design
The dependency resolver reads descriptors from `IDocumentaryPhysicalArtifactRegistry`, rejects temporary, missing, empty, mismatched, or unmeasured MP4 scenes, and orders them by certified dependency sequence then ordinal asset ID. With no existing variant overlap operation, expected duration is the explicit O2.18 plan duration, falling back to the sum of measured scene durations; no overlap is invented.

The compatibility validator requires requested dimensions and frame rate (within configured tolerance). `RequireAudio` and `VideoOnly` are explicit; unknown audio metadata is checked again on output. Mixed-audio normalization is not supported.

`ExistingFFmpegDocumentaryVariantProviderBinding` uses the existing `FfmpegArgumentBuilder`, `IProcessRunner`, `RenderingOptions`, final YouTube/Shorts encoding presets, concat demuxer, AAC policy, faststart, cancellation, and final-render timeouts. It writes UTF-8 `provider-scenes.txt` and `provider-variant.mp4` below the attempt directory. The deterministic mode is `ConcatDemuxerFinalReencode`; stream copy and transitions are not selected because the existing certified final profile re-encodes.

## Finalization and inspection
The focused inspector reuses `IDocumentaryMediaProbe` and checks a nonempty file, video stream, audio policy, duration, dimensions, and frame rate. A3.8 performs focused variant output inspection required for safe adapter finalization. A3.9 remains responsible for generalized media verification and certification.

The adapter atomically finalizes through the workspace manager, reuses artifact inspection for lowercase SHA-256 and `sha256:` content identity, validates the descriptor, registers one `VariantVideo`, writes sanitized JSON diagnostics, and maps to O2.18. Caller cancellation propagates; one invocation performs no generic retry. Process and provider failures use stable bridge codes.

## Configuration and DI
`DocumentaryProductionAdapters:VariantComposition` is disabled by default and controls duration/frame-rate tolerance and intermediate retention. Bridge DI registers the resolver, validator, binding, inspector, adapter, and result mapper. The adapter registry advertises `VariantComposition` without enabling production execution.

## Testing and limitations
Tests are process-fake/reflection based; paid providers are not called. Real FFmpeg smoke testing is not part of automated A3.8. Codec, pixel-format, time-base, and audio-layout metadata are unavailable in the certified descriptor, so the existing final re-encode path is deliberately used rather than stream copy. Generalized verification remains A3.9 scope.

## Component certification closure

The resolver component matrix covers absent and unregistered inputs; duplicate assets; missing or ambiguous dependency metadata; variant membership; asset type, format, and correlation; descriptor content type, correlation, file existence/length and ownership; positive duration, dimensions, and frame rate; and request/plan/variant correlation. Checksums must be exactly 64 lowercase hexadecimal characters, and content identity must equal `sha256:` plus that checksum. Registry descriptors and requests remain unchanged. Explicit plan duration owns the output duration; zero plan duration uses the measured scene sum. Equal sequences are ordered by ordinal asset ID.

The provider-binding tests exercise the real `FfmpegArgumentBuilder` through a recording `IProcessRunner`. Landscape output owns the final-long timeout and portrait output owns the final-short timeout. `RequireAudio` emits an audio mapping and AAC encoding; `VideoOnly` emits `-an`. The UTF-8-without-BOM `provider-scenes.txt` contains one `file` directive per scene, preserves order, has a deterministic trailing newline, supports spaces, and represents apostrophes with FFmpeg concat lexer's close/escaped/reopen form (`'\''`). The binding—not `OutputPath`—owns deterministic `provider-variant.mp4` beneath `OutputDirectory`. It rejects malformed requests before process invocation and safely maps missing executables, process-start I/O, timeout, nonzero exit, process `ExceptionText`, missing output, and empty output. Caller cancellation propagates.

Focused inspector tests cover successful metadata projection, optional audio, missing/empty files, safe probe failures, video-stream presence, positive duration/dimensions/frame rate, cancellation before and during probing, and the policy that probe exceptions are normalized at the adapter boundary. DI tests resolve the complete A3.8 graph, confirm registry exposure and disabled-by-default options, and prove duplicate stable provider IDs are rejected without invocation. Source and reflection architecture tests prohibit blocking async calls, direct `Process.Start`, shell invocation, upstream generators, storage/publishing, and generalized A3.9 verification dependencies; the Core project reference boundary is also checked.

The previously advertised `RetainConcatList` setting was removed because isolated concat-list retention is not supported by the workspace cleanup abstraction. `RetainProviderNativeVideo` remains the truthful attempt-retention policy. **No real FFmpeg smoke test was executed.**

## A3.8 adapter-level certification closure (2026-07-27)

The certification suite now directly constructs `ExistingDocumentaryVariantCompositionAdapter` with the real workspace manager, checksum and content-identity services, physical artifact inspector and validator, registry, dependency resolver, compatibility validator, diagnostics writer, and failure normalizer. Only the FFmpeg provider binding and focused video inspector are replaced with controllable fakes. The sources are registered, finalized `SceneVideo` files outside the attempts tree; no visual, narration, subtitle, scene-composition, storage, publishing, or generalized verification service participates.

The executed full-flow coverage proves deterministic sequence ordering; landscape 1920x1080 `YouTubeLongFinal` and portrait 1080x1920 `ShortsFinal` profiles; `RequireAudio` and `VideoOnly`; provider output ownership; provider and inspector cancellation; inspector exception normalization; atomic finalization; SHA-256 and `sha256:` identity construction; descriptor validation and idempotent registry replay; sanitized JSON diagnostics; and O2.18 result mapping. A3.8 sums finalized scene durations only when the plan has no explicit duration and applies no transition-overlap subtraction because no certified variant-level overlap operation exists.

Provider-native output is owned by the attempt directory and removed after success when retention is disabled. Finalization precedes final descriptor validation, so a finalized but unregistered file can remain available for quarantine when validation fails. Encoded-byte determinism across FFmpeg versions is deliberately not asserted. No real FFmpeg smoke test was executed; provider behavior is certified with deterministic process and adapter fakes. A3.9 generalized media verification remains out of scope.
