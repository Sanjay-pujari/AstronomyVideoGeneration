# CG-A3 A3.6 — Existing Documentary Subtitle Adapter

## Scope and invariant

A3.6 connects O2.18 subtitle requests to the asynchronous production bridge only. Subtitle generation does not synthesize narration. Subtitle cues do not drive TTS. One final narration artifact produces one subtitle document. English and Hindi subtitle generation preserve the certified one-block narration architecture. No scene/variant rendering, FFprobe integration, publishing, storage handoff, or production host was added.

## Reused production behavior

The binding consumes the English/Hindi cues already produced and certified by the documentary media projection pipeline. Consequently the existing cue segmentation, duration-aware timing, punctuation, scene boundaries, and one/two-line wrapping are preserved rather than recomputed. The provider binding performs only deterministic serialization. The certified policy is SRT; VTT is also supported because the certified format enum contains `Vtt`.

Timing comes from the O2.18 cues associated with the finalized narration. The adapter never estimates timing from text and never invokes Azure Speech, transcription, or narration synthesis. SRT uses one-based indices, UTF-8, comma millisecond timestamps, and blank separators; VTT adds `WEBVTT` and uses dot millisecond timestamps.

## Dependency and workspace flow

The first ordered subtitle-plan dependency identifies narration in `IDocumentaryPhysicalArtifactRegistry`; paths are never inferred. The descriptor must match correlation, be nonempty finalized audio, have measured duration, and not reside beneath attempts. Provider output is restricted to the owned attempt directory and named `provider-subtitles.srt` or `provider-subtitles.vtt`.

The actual bytes are inspected for UTF-8, syntax, contiguous numbering, ordered/nonoverlapping positive timing, line limits, narration-duration alignment, and exact normalized reconstruction. Normalization is NFC plus trimming and whitespace collapse; punctuation and script remain significant. Alignment uses `abs(narrationDuration - lastCueEnd) <= AlignmentToleranceMilliseconds` (250 ms by default).

After inspection, A3.3 atomically finalizes the document, computes SHA-256/`sha256:` ContentIdentity through the physical inspector, validates the descriptor, and registers it. Diagnostics contain identities, hashes, measurements, checksum, and counts—not subtitle/narration text or secrets.

## Configuration, retries, cancellation, and failures

`DocumentaryProductionAdapters:Subtitles` is disabled by default and contains only `Enabled`, `RequireExactTextReconstruction`, `AlignmentToleranceMilliseconds`, and `RetainProviderNativeSubtitle`. DI registers the binding, inspector, adapter, mapper, and registry capability. Exactly one stable `ExistingSubtitlePipeline` binding is required.

There is one provider operation per O2.18 attempt and no adapter retry. Caller cancellation propagates. Unsupported input, missing/invalid narration, provider response, missing/empty/malformed output, timing/reconstruction, checksum, and filesystem evidence map to stable `DocumentaryProductionFailureCode` values. External paid services are absent, so real-provider smoke testing was not applicable and was not run.

## Tests and limitations

Executable tests cover SRT/VTT byte inspection, English/Hindi preservation, overlap/number/timestamp rejection, deterministic owned naming, one document per call, and cancellation. The adapter is intentionally not a new alignment or cue segmentation engine. O2.18 currently carries approved cues rather than a narration-block object; narration identity and approved text are reconstructed from those immutable cues, while the final audio is resolved from the explicit plan dependency.
