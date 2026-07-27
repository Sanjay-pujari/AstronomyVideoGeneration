# CG-A3 A3.6 Completion Report

## Delivery

Created `SubtitleAdapter.cs`, focused inspector/provider tests, this report, and the adapter design document. Modified only the CG-A3 production adapter contract, registry, hosting registration, and DI composition; certified CG-A2 files and contracts were not modified.

The stable provider ID is `ExistingSubtitlePipeline`. Existing O2.18 English/Hindi cues supply certified segmentation, line wrapping, punctuation, and timing. Narration is resolved by the explicit ordered asset dependency through the physical registry and must be finalized, correlated, nonempty audio with measured duration. One narration dependency yields one deterministic SRT/VTT document. No TTS, Azure Speech, transcription, retry, scene/variant composition, FFprobe, storage, or publishing operation occurs.

The byte inspector validates UTF-8, format, contiguous one-based indices, timestamp syntax, ordering, overlap, positive cue duration, 42-character/two-line policy, narration alignment, and NFC/whitespace-normalized exact text reconstruction. SRT and VTT output, English and Devanagari preservation, invalid documents, deterministic naming, cancellation, and narration independence are executable tests.

A3.3 workspace finalization, physical inspection, SHA-256, `sha256:` ContentIdentity, descriptor validation, registry, and sanitized diagnostic services are reused. The O2.18 mapper preserves format, provider, cue count, duration span, identity, length, checksum, attempt, correlation, and stable failure fields. Retry remains owned by O2.18; cancellation propagates.

## Validation status

The container did not provide the .NET SDK, so restore/build/focused/broad tests could not be executed here. No paid-provider test was executed. Although implementation and executable coverage are present, passing focused tests is a required readiness condition.

## Mandatory truthful statements

- ✓ Certified CG-A2 core was not modified.
- ✓ No synchronous CG-A2 subtitle provider was implemented.
- ✓ No blocking async call was introduced.
- ✓ Existing cue timing, segmentation, English/Hindi rules, and line layout were reused.
- ✓ No second timing or segmentation engine was created.
- ✓ Subtitle generation requires finalized narration and never invokes narration synthesis or Azure Speech TTS.
- ✓ Subtitle cues do not drive TTS; one narration artifact produces one subtitle document.
- ✓ Syntax, indices, ordering, overlap, duration, alignment, and reconstructed English/Hindi text are validated from actual bytes.
- ✓ Provider output and deterministic provider filename are attempt-workspace owned.
- ✓ Atomic finalization, SHA-256, ContentIdentity, descriptor validation, registration, diagnostics, stable failure mapping, and O2.18 mapping were implemented.
- ✓ Retry ownership remains with O2.18 and caller cancellation propagates.
- ✓ No scene/variant rendering, FFprobe, storage, or publishing behavior was added.
- ✓ No paid-provider call was executed.
- ✓ A3.7 readiness was explicitly decided.

# NOT READY FOR A3.7

Reason: the environment lacks `dotnet`, so the mandatory focused suite has not yet passed. Submit A3.6 for architectural review; do not implement A3.7.
