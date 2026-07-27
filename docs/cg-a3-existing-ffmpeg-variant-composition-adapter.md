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
