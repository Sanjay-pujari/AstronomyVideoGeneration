# CG-A3 A3.7 — Existing FFmpeg scene composition adapter

## Scope and boundary

A3.7 connects one O2.18 scene request to the existing rendering stack. **Scene composition does not generate visuals. Scene composition does not synthesize narration. Scene composition does not generate subtitles. One scene request produces one finalized scene video. A3.7 does not concatenate scenes into a final variant.** A3.7 performs focused scene output inspection only; A3.9 remains responsible for generalized media certification.

The adapter consumes only finalized image (`image/png`, `image/jpeg`), narration, and SRT/VTT descriptors from `IDocumentaryPhysicalArtifactRegistry`. It checks correlation, non-empty files, content types, and rejects attempt-directory inputs. Visuals are ordered by explicit dependency sequence and then asset ID. Duration ownership is explicit: O2.18 effective duration, then measured finalized narration duration, otherwise failure. Disagreement beyond the configured tolerance fails.

## Existing production services reused

`ExistingFFmpegDocumentarySceneProviderBinding` uses the existing `FfmpegArgumentBuilder`, `RenderingOptions`, `VideoEncodingPreset.IntermediateSegment`, and `IProcessRunner`. The narrow `BuildScene` operation applies the existing aspect-preserving decrease-and-pad scaling, square pixels, configured scale flags, libx264/pixel format/CRF/preset, AAC bitrate, frame rate, and `+faststart`. The existing runner owns process launch, timeout, caller cancellation, and process-tree termination. No second runner, shell framework, subtitle generator, or timeline engine is introduced.

Static, existing camera-motion policy values and scene transition values are translated into the provider request and diagnostic; the adapter makes no cinematic decisions. The current narrow renderer supports deterministic still sequences and subtitle burn-in. Muxed subtitles are explicitly rejected. Narration is passed once; silent scenes receive no artificial audio. The scene duration is supplied with `-t`; narration is never synthesized or split.

## Workspace, inspection, and identity

The binding writes `provider-scene.mp4` below the owned attempt directory and invokes FFmpeg once. The focused inspector uses `IDocumentaryMediaProbe` and validates a non-empty output, MP4 metadata, video presence, positive duration/dimensions/frame rate, required audio, exact dimensions, and configured duration/frame-rate tolerances. The workspace manager atomically finalizes to `SceneVideo`; the existing artifact inspector computes SHA-256 and `sha256:` ContentIdentity, the descriptor validator runs, and the registry receives exactly one descriptor. Physical paths never become content identities.

Diagnostics contain logical identities, requested/measured profile data, policy names, exit/elapsed metadata, and a sanitized stderr hash—not the command or raw stderr. Caller cancellation propagates and prevents registration. There are no adapter retries; O2.18 owns retry attempts.

## Configuration and DI

`DocumentaryProductionAdapters:SceneComposition` adds only `Enabled` (false by default), duration/frame-rate tolerances, and provider-native retention. DI registers the dependency resolver, stable `ExistingFFmpegSceneComposer` binding, focused inspector, asynchronous adapter, and O2.18 result mapper. The adapter registry exposes `SceneComposition` and availability for that operation. Production execution remains disabled by default.

## Failure behavior and tests

Stable failures distinguish unavailable adapters, missing/invalid sources, missing subtitles, rejected profiles, missing FFmpeg, start/timeout/nonzero-exit failures, malformed/missing/empty outputs, stream/profile mismatches, and finalization/registry infrastructure failures. Public failures never contain raw stderr.

Executable tests cover focused metadata, missing/empty/video-less output, cancellation, deterministic multi-image command translation, approved scaling, AAC narration, silent scenes, subtitle-none behavior, and stable provider identity. Tests use fakes and do not call paid providers. A real FFmpeg smoke test was not executed in this environment because the .NET SDK is unavailable.

## Known limitations

Only MP4 and subtitle burn-in are supported by this bridge. General media certification, scene concatenation, variant composition, publishing, and storage handoff are out of scope.
