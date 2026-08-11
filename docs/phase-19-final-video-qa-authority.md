# Phase 19 final-video QA authority

Phase 19 is a read-only technical certification of the committed Phase 18 package. It selects MP4
and SRT inputs only from `phase18-manifest.json`, verifies Phase 18 governance and physical identity,
and uses the configured FFprobe/FFmpeg tools only for probing, decoding, bounded audio analysis, and
frame sampling. It does not render, retime, remix, or modify upstream media.

## Frozen Phase 18 compatibility adapters

* Phase 19 follows `requestedFormats` and does not independently require Short plus Long. The current
  frozen Phase 18 publisher still always declares both formats; Short-only or Long-only production is
  therefore an upstream contract-version limitation rather than a Phase 19 restriction.
* Phase 18 gives SRT files first-class byte-length and checksum evidence in each manifest output. Its
  frozen schema gives ASS files a canonical path in `phase18-authority-diagnostics.json` but no ASS
  checksum field. Phase 19 consequently verifies manifest-governed SRT identity and validates the
  diagnostics-declared ASS as a contained regular file with deterministic structure, event, style,
  bottom-alignment, margin, and line-count checks.
* Phase 18 does not expose exact narration duration on each media-output row. Phase 19 reads the
  committed Phase 15 timeline strictly for narration-window lineage, binds each row to the Phase 17
  `SceneAudioUnitId` and audio checksum, and never uses Phase 15 to discover the final media.

The lightweight luma-difference metric proves material encoded change for non-static scenes. It does
not claim to infer exact pan direction; declared transform direction remains governed by the immutable
Phase 17/18 lineage. Phase 20 remains the owner of manual/editorial publication approval.
