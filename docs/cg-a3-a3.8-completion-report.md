# CG-A3 A3.8 completion report

## Implementation
Created the immutable variant dependency, provider request/response, focused inspection, adapter result, and mapping abstractions and their implementations. The stable provider ID is `ExistingFFmpegVariantComposer`. Modified bridge DI and the adapter registry for variant composition. Certified CG-A2 source contracts were not modified.

The resolver consumes finalized registered scene videos only and orders dependency sequence then asset ID. Duration ownership is explicit-plan-first, measured-sum-second, with no transition overlap. Compatibility checks MP4, dimensions, frame rate, and explicit `RequireAudio`/`VideoOnly` policy. Composition deterministically uses concat-demuxer final re-encode with existing final media presets. The provider owns `provider-scenes.txt` and `provider-variant.mp4` in the attempt directory.

The implementation reuses `IProcessRunner`, `FfmpegArgumentBuilder`, `RenderingOptions`, FFmpeg executable configuration, process-tree cancellation, final timeouts, concat semantics, YouTube/Shorts final profiles, AAC, and faststart. It introduces no second runner or final pipeline. Retry remains owned by O2.18.

Focused probing measures video/audio presence, duration, dimensions, and frame rate. Workspace finalization is atomic; existing inspection calculates SHA-256 and content identity; descriptor validation precedes one `VariantVideo` registration. O2.18 result mapping and sanitized deterministic diagnostics are implemented. No raw stderr or command is diagnosed.

## Tests and execution
Added architecture/default/deterministic-order regression tests, including the mandated upstream-independence, one-variant, and scene-order method names. No paid call or real FFmpeg smoke was run.

The requested .NET restore, build, focused suite, and broad suite could not execute because the container has no `dotnet` executable. Consequently the mandatory readiness condition “all focused A3.8 tests pass” is not proven.

## Known limitations and A3.9 decision
The descriptor does not carry codec, pixel format, time base, or reliable per-scene audio-presence metadata. Final re-encoding avoids unsafe stream copy, while output audio is authoritatively probed. A3.9 generalized verification was not added. Production execution, storage, and publishing are unchanged.

**NOT READY FOR A3.9**
