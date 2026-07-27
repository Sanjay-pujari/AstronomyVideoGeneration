# CG-A3 A3.7 completion report

## Delivery

Created `SceneCompositionAdapter.cs`, its executable focused tests, this report, and the adapter design document. Modified bridge contracts/registry/DI and exposed the narrow `FfmpegArgumentBuilder.BuildScene` operation. The stable provider ID is `ExistingFFmpegSceneComposer`.

The implementation reuses the existing executable setting (`RenderingOptions.FfmpegPath`), `FfmpegArgumentBuilder`, `IProcessRunner`, intermediate media profile, workspace manager, artifact inspector, SHA-256 identity factory, descriptor validator, registry, and diagnostics writer. It introduces asynchronous scene adapter, dependency resolver, provider binding/request/response, explicit subtitle mode, focused video inspector, immutable result, and O2.18 mapper.

## Behavior

Finalized visuals are correlation/type/path checked and deterministically ordered. Final narration is validated and muxed once; finalized subtitle input is validated and burned in when present. No upstream provider is called. Effective O2.18 duration owns timing, with narration fallback and tolerance validation. Existing aspect-preserving scale/pad, square-pixel, libx264/yuv420p, AAC, configured frame-rate/CRF/preset/bitrate, and faststart policies are reused. Motion and transition policy are translated without reinvention.

The binding creates one deterministic attempt-owned `provider-scene.mp4`, executes one process with the existing runner and timeout, and performs no retry. Caller cancellation propagates to the runner, which owns process-tree termination. The scene-specific probe measures stream presence, duration, dimensions, frame rate, and audio. Atomic finalization precedes SHA-256/ContentIdentity creation, validation, registry insertion, sanitized diagnostics, and O2.18 result mapping.

Failure mapping includes adapter/dependency/source/subtitle/profile/process/output/stream/dimension/duration/frame-rate codes. Raw stderr is represented only by a SHA-256 hash. Retry ownership remains with O2.18.

## Verification status

The executable suite includes focused inspector, cancellation, deterministic command, scaling/profile, silent audio, subtitle-none, and identity coverage. Architecture review confirms no synchronous CG-A2 provider, upstream adapter dependency, variant composition, publishing/storage behavior, shell execution, blocking async bridge call, second process framework, or general A3.9 verifier was added. CG-A2 files were not modified.

The requested restore/build/focused/broad test commands could not execute because `dotnet` is absent from the environment. Consequently the mandatory end-to-end upstream-independence and single-scene regression criteria are not certified here, and no real FFmpeg smoke was executed. No paid-provider call was made.

## Readiness decision

The implementation is submitted for architectural review, but all focused tests could not be run in this environment.

**NOT READY FOR A3.8**
