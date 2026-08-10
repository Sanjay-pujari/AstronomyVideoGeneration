# Phase 17 Motion Layer Authority Audit

**Status:** implementation-readiness audit only; no production, test, configuration, media, Phase 16, scene, TTS, or SRT changes were made.  
**Audit baseline:** 2026-08-10 repository state.  
**Decision:** Phase 17 is not presently a governed consumer of Phase 16 and cannot be certified as timing authority-aligned.

## Executive conclusion

The active non-preview route is a small metadata projector embedded in
`ProductionPipelineExecutionService`, not a mature standalone motion engine. It reads the
shared compatibility file `timing/scene-duration-plan.json`, takes at most five Short and nine
Long rows, requires the audio file named by every row to exist, selects a semantic profile from
the scene ID/recommendation, and writes scale/pan/easing metadata. It does **not** read the
numbered Phase 16 authority, start/end windows, Phase 16 gates, checksums, or Phase 10
certification. It does not render video or invoke FFmpeg.

There are two more-developed motion implementations already present:

* the preview policy in `PhaseMotionLayerV2PreviewAsync` produces deterministic V2 semantic
  types and normalized scale/pan values; and
* `Astronomy.MediaFactory.Rendering.MotionProfileSelector` plus `SmoothMotionRenderer`
  contains the cleanest mature deterministic profile/easing algorithm, although the latter
  emits an FFmpeg `zoompan` filter and is therefore a Phase 18 renderer concern.

Phase 18 currently consumes either `motion/motion-plan-v2-preview.json` (preferred merely when
the file exists) or `motion/motion-plan.json`, then overrides scene duration from Phase 15 TTS,
selects other visual sources, strengthens some motion values, creates transition timing, and
renders with FFmpeg. Thus the current handoff is neither authoritative nor lossless.

The minimal path is to reuse the semantic selection/profile values, place a strict Phase 16 and
certified-visual adapter ahead of them, publish language-scoped transactional semantic metadata,
and make Phase 18 consume it without recalculating duration or motion. Do not create another
motion engine.

---

## 1. Current call graph

All locations below are in
`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`
unless another file is named.

| Concern | Class / method | Exact current location | Finding |
|---|---|---:|---|
| Registry route | `ProductionPipelineExecutionService` phase table | 433 | Phase 17 maps to `PhaseMotionLayerV1Async`; Phase 18 maps at 434. |
| Entry / preview branch | `PhaseMotionLayerV1Async` | 11072-11192 | `MotionPreviewOnly` diverts to V2 preview; otherwise V1 runs. |
| Timing loading | `PhaseMotionLayerV1Async` | 11084, 11111-11117 | Parses only `timing/scene-duration-plan.json`. |
| Scene loading | `BuildMotionPlanItems` | 11380-11408 | Reads `{scene-assets-v3}/{short|long}/scene-manifest-v3.json`; matches manifest by `sceneId`, then falls back by array position and finally `{sceneId}.png`. |
| Duration resolution | `BuildMotionPlanItems` | 11394 | Reads `sceneDurationSec` directly; no Phase 16 DTO. Preview fallback chain is 11288-11310. |
| Policy selection | `ResolveMotionPurpose`, `ResolveMotionProfile` | 11508-11525 | Scene-ID substring/ordinal and requested profile select role/profile. |
| Plan creation | `BuildMotionPlanItems` / `MotionPlanItem` | 11380-11408, 11506 | Creates one flat record per selected timing row. |
| Per-scene motion | `ResolveMotionDefaults` | 11493-11504 | Fixed profile start/end zoom and X/Y pan. |
| Keyframe/debug samples | `BuildMotionRc1DebugItem`, `MotionValues`, `EasedProgress` | 11424-11466; easing helper at 14680-14688 | Generates all per-frame diagnostic samples at 30 fps, but these are not canonical keyframes. |
| Zoom/pan logic | `ResolveMotionDefaults` | 11493-11504 | Zoom and two-axis pan only. No parallax/tilt/orbit/camera path. |
| Transition intent | `BuildMotionPlanItems` | 11405 | Every V1 row says duration `0`, transition `cut`. Actual transitions are Phase 18, 14173-14244. |
| Artifact writing | `PhaseMotionLayerV1Async` | 11128-11139, 11169-11189 | Writes plan, debug, diagnostics, validation directly. |
| Validation | `PhaseMotionLayerV1Async`, `ValidateMotionRc1Debug` | 11100-11141, 11468-11475 | Checks roots/files, 5/9 counts, duration/audio, supported profile, old paths, and three metadata strings. |
| Result projection | `PhaseMotionLayerV1Async` | 11190-11192 | Throws on errors and returns three paths; no typed publication result. Note that `motion-debug.json` is written but omitted from the returned list. |
| Phase 18 read | `PhaseVideoAssemblyV1Async`, `ReadVideoAssemblyItems` | 12561-12588, 13284-13355 | Chooses preview plan by file existence and reads its items dynamically. |
| Phase 18 render | `RenderVideoAssemblyAsync`, `BuildPhase18MotionFilter` | 13694-13750, 14144-14171 | Re-derives duration, emits FFmpeg filter, renders clips. |

`EasedProgress` supports `EaseOutCubic` and otherwise sine ease-in/out. V1 debug is the only
place the complete per-frame sequence is materialized; the plan contains endpoints.

## 2. Current responsibilities

| Capability | Current Phase 17? | Evidence / classification |
|---|---|---|
| Ken Burns | **Semantic equivalent only** | Endpoint zoom/pan metadata; no named Ken Burns operation. |
| Zoom | **Yes** | Profile endpoint percentages. |
| Pan | **Yes, X and Y** | Profile endpoint percentages. |
| Tilt / orbit / parallax | **No** | Parallax/advanced is explicitly rejected in RC1 validation. |
| Crop animation / framing | **No** | No source dimensions, crop rectangle, focus, or bounds calculation. |
| Camera path | **No** | Only start/end scalar transforms. |
| Keyframes | **No canonical keyframes** | Per-frame values are debug-only. |
| Transition timing / crossfade | **No effective ownership** | V1 emits `cut`/zero; Phase 18 invents crossfades. |
| Scene-duration calculation | **No probing, but wrong authority consumption** | Copies compatibility `sceneDurationSec`; preview can fall back to manifest or fixed 5/8 seconds. |
| Subtitle timing / TTS alignment | **No** | Paths are reported in diagnostics only. |
| Audio inspection | **Existence check only** | Audio paths from compatibility timing are mandatory. This is still an ownership violation. |
| Video / FFmpeg | **No** | Phase 17 writes JSON only. |
| Motion metadata | **Yes** | This is its substantive output. |

Target responsibility should remain provider-neutral deterministic motion metadata: per-scene
transforms, normalized keyframes, safe framing, policy/version, and optional transition intent
bound to upstream duration. Narration, TTS, subtitle retiming, scene duration selection, SRT,
rendering, and mixing remain out of scope.

## 3. Phase 16 input authority

The governed implementation is `Phase16DurationCalibrationPublisher.ExecuteAsync` in
`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase16DurationCalibrationPublisher.cs`.
It commits beneath `16-duration-calibration/{language}` (lines 122-175) and provides:

* `calibrated-scene-timeline.json`: root schema/language, Phase 14/15 checksums, policy versions,
  `short`, `long`, and `authorityChecksum` (lines 140-148);
* `subtitle-timeline.json`, `short/final.srt`, and `long/final.srt` (144-150);
* `phase16-manifest.json`, `phase16-authority-diagnostics.json`, and
  `phase16-publication-report.json` (152-160);
* `validation/phase-16-validation.json` (167-174).

The actual scene contract is `Phase16CalibratedScene` in
`Backend/src/Astronomy.MediaFactory.Core/Phase14AudioSyncAuthority.cs:60-67`. Required Phase 17
mapping fields are exactly:

* `SceneAudioUnitId`, `SceneId`, `Format`, `Sequence`, `Language`;
* `FinalSceneDurationMs`, `SceneStartMs`, `SceneEndMs`;
* `SubtitleSegmentIds` (lineage/safe-layout reference only);
* `AudioSha256` (lineage only; Phase 17 must not open or probe the audio);
* source Phase 14/15 checksums and planned-duration lineage.

Phase 17 must gate on matching `authorityChecksum` and all committed/readback/semantic/checksum/
manifest/downstream flags in the Phase 16 timeline, manifest, report, and validation. The subtitle
timeline/SRT is **not required to generate camera motion**. Load `subtitle-timeline.json` only if a
future certified subtitle-safe-region contract genuinely maps segments to regions; never parse
SRT to obtain duration.

## 4. Legacy timing inputs

| Input | Current use | Classification / target |
|---|---|---|
| `timing/scene-duration-plan.json` | Sole V1 and preview timing input | **Compatibility now; remove from active authority path.** Phase 16 generates it as an explicit compatibility projection at publisher line 266. |
| `tts/{language}` and language timeline | Diagnostics paths only in Phase 17; row `audioPath` must exist | **Legacy for Phase 17.** Remove both requirement and path projection. |
| `sync/` | Not read by active Phase 17 | **Upstream legacy/not an input.** |
| `narration/subtitles/{language}` / SRT | Diagnostics paths only | **Legacy diagnostic reference.** Not canonical and must not be timed here. |
| Phase 15 timeline | Not parsed by Phase 17 | **Not an input.** Its checksum arrives transitively through Phase 16. |
| manifest/fixed preview duration | Preview fallback (5 seconds Short, 8 seconds Long) | **Obsolete for governed generation.** |

Answer: current production V1 does not itself recalculate duration from TTS or SRT, but it does
depend on an audio-bearing compatibility plan and physical audio existence. Preview can re-derive
duration from manifest/fixed defaults. Both fail the direct Phase 16 requirement.

## 5. Visual authority inputs

Current Phase 17 loads only:

* `scene-assets-v3/short/scene-manifest-v3.json` plus image paths;
* `scene-assets-v3/long/scene-manifest-v3.json` plus image paths.

It does **not** load Phase 8's governed `08-scene-assets/scene-asset-manifest.json`, Phase 9
publication evidence, or Phase 10 certification/validation. `ValidateSceneAssetsV3Format`
(service lines 2633-2677) demonstrates that the numbered Phase 8 manifest is already preferred
for scene identity, whereas Phase 17 bypasses it.

Target selection is the committed Phase 8 Short and Phase 9 Long asset entries certified by
Phase 10, mapped by exact `(Format, SceneId)` and ordered by Phase 16 `Sequence`. Exact Phase
8 inputs are `08-scene-assets/scene-asset-manifest.json` and
`08-scene-assets/phase8-publication-report.json`; exact Phase 9 inputs are
`09-long-scenes/long-scene-image-manifest.json` and
`09-long-scenes/phase9-publication-report.json`; Phase 10 commits
`10-scene-validation/scene-asset-certification.json`,
`phase10-authority-diagnostics.json`, and `phase10-publication-report.json`
(`SceneAssetCertificationService.cs:21-24,43-55,83-108`). Hero, Thumbnail, Gallery,
comparison Story Frames V4, and arbitrary directory order are not motion-scene inputs.

### Phase 10 safety evidence

The governed certification proves dimensions, physical checksums, lineage, scientific evidence,
scene sets, Phase 8/9 Long equivalence, publication state, and downstream readiness
(`SceneAssetCertification.cs:13-27`; `SceneAssetCertificationService.cs:39-81`). Phase 8 asset
entries carry dimensions, aspect, physical checksum, expected/verified astronomy-object lists,
scientific-geometry status, and an optional evidence path
(`SceneAssetsV3.cs:243-256`). Phase 9 carries equivalent physical and scientific lineage
(`LongSceneImageAuthority.cs:32-38`). Neither governed contract publishes safe rectangles, object
bounding boxes, focus regions, overlay zones, or crop constraints. The legacy variant manifest
contains `SafeAreaMetadata` (service lines 3359-3375), but that is not the governed per-asset
Phase 10 contract and the active Phase 17 path does not consume it. Consequently Phase 10 can
certify the source image but cannot presently prove motion safety. Extend the visual authority once
with certified regions before enabling non-static governed motion; do not rerun image analysis in
Phase 17.

## 6. Short behavior

* Hard-coded expected/taken count: **5**, not Orion's 4.
* Visual root: portrait `scene-assets-v3/short`; Phase 8 validation requires width < height.
* Same V1 profile table as Long; no Short-specific intensity, speed, safe margin, subtitle area,
  or 9:16 crop rule.
* Phase 17 does not inspect dimensions. Phase 18 selects configured Short canvas, normally
  `ShortVideoWidth`/`ShortVideoHeight` (defaults 1080x1920 in `Contracts.cs:408-409`).
* Transition intent is `cut`/0; Phase 18 invents crossfades.

## 7. Long behavior

* Hard-coded expected/taken count: **9**, not Orion's 12.
* Visual root: landscape-intended `scene-assets-v3/long`, but Phase 17 performs no aspect check.
* Same V1 profile table and amplitude as Short; there is no slower Long cinematic policy.
* Phase 18 defaults Long canvas from `VideoWidth`/`VideoHeight` (1280x720 at
  `Contracts.cs:406-407`) and may optionally choose unrelated Story Frames V4.

## 8. Scene-count authority

V1 truncates with `.Take(expectedCount)` and then demands exactly 5/9. Preview instead takes the
maximum of compatibility-duration and manifest counts, which can silently tolerate mismatches.
Neither is correct. Target counts are the complete ordered Phase 16 Short/Long arrays, and the
certified visual mapping must be a bijection. For Orion that currently means 4/12, but those
numbers must never become new constants.

## 9. Motion contracts

### V1 `MotionPlanItem` (service line 11506)

`Format`, `SceneId`, `Purpose`, `SceneImagePath`, `AudioPath`, `SceneDurationSec`,
`TransitionDurationSec`, `Transition`, `MotionStyle`, `MotionProfile`, `ZoomStart`, `ZoomEnd`,
`PanXStart`, `PanXEnd`, `PanYStart`, `PanYEnd`, `Easing`.

The JSON root adds `version`, `sourceDurationPlanVersion`, and Short/Long `{sceneCount, items}`.
There is no `MotionPlan`, `SceneMotionPlan`, `MotionCue`, `Keyframe`, `CameraTransform`,
`PanZoom`, or `KenBurns` authority DTO on this route.

### V2 preview `MotionV2PreviewItem` (service line 11378)

`MotionVersion`, `MotionPreviewOnly`, `AudioRequired`, `SceneId`, `Format`, `MotionType`,
`DurationSec`, `StartScale`, `EndScale`, `StartX`, `EndX`, `Easing`, `ValidationPassed`. It lacks
Y pan, visual identity, transition, policy checksum, and governance.

### Mature rendering models

`MotionProfileSelector` returns `MotionProfile` with kind, type, easing, scale and X/Y endpoints,
description, and validation. `SmoothMotionRenderer` maps it to FFmpeg. Reuse the semantic profile,
not the filter string, as Phase 17 authority.

## 10. Motion types

V1's active semantic profiles are `Hook`, `Discovery`, `SkyGuide`, `ViewingTip`, `Closing`,
`BestTime`, and fallback `static`; these are roles, not camera types. Their effects are zoom in,
zoom out, diagonal X/Y drift, horizontal pan+zoom, or static.

V2 preview camera types are `SlowZoomIn`, `SlowZoomOut`, `PanLeft`, `PanRight`, `PushToObject`,
and `None`. `MotionProfileSelector` supports the same conjunction family. No active Phase 17 type
implements parallax, orbit, tilt, pan up/down, or depth layers.

## 11. Randomness and seed

V1 and V2 selection contain no `Random`, GUID, clock, or process state. Selection is deterministic
from scene ID, request recommendation/role, item index/count, and strength. However debug contains
`generatedUtc`, making debug bytes nondeterministic, and neither plan identity nor serialization is
governed. No seed is needed for the current algorithm. If variation is added, derive a documented
seed from Phase 16 checksum + visual checksum + format + language + scene identity + policy
version; never persist clock/GUID/process randomness in semantic authority.

## 12. Duration binding

Current V1 copies compatibility seconds and has no millisecond equality validation, scene
start/end, or Phase 16 checksum. Preview may use timing, manifest, or fixed defaults. Therefore
`MotionDurationMs == Phase16.FinalSceneDurationMs` is not guaranteed.

Target entries carry `DurationMs`, `SceneStartMs`, and `SceneEndMs` verbatim. Validate exact integer
equality and `end-start == duration`. Transition overlap is a separate field and must never mutate
the narration window.

## 13. Keyframe model

V1 authority has endpoints without explicit time. Debug expands them to integer frame numbers at
30 fps. V2 also has endpoints without a time coordinate. Phase 18 converts duration to frames.

Use normalized keyframe time `[0,1]` with an exact `0` first and `1` last, monotonic unique times,
semantic transforms, and named easing. This is frame-rate/provider neutral; Phase 18 binds it to
the Phase 16 duration and its chosen frame rate. Keep `DurationMs` alongside it for the exact
authority binding.

## 14. Safe crop

There is no Phase 17 crop rectangle, source dimension, scale/pan bound, black-border validation,
or focus-aware clamping. Static fallback exists only when a profile is unsupported, not when
motion is unsafe. Phase 18 pre-scales/crops and computes bounded-looking zoompan expressions, but
that is renderer logic and does not prove object preservation.

Required validator: scale must cover the output aspect at every keyframe; translated crop must
remain inside source bounds; certified required regions must remain visible; otherwise deterministically
reduce translation/zoom and finally use `Static`.

## 15. Focus and astronomy-object preservation

No focus/object/constellation metadata is loaded. Scene-ID roles can select a gentler `SkyGuide`
pan but cannot know where the object is. Thus the current route may crop an astronomy object,
constellation center, grouping geometry, horizon, or label. A certified focus region and required
overlay regions must become inputs. Preserve constellation context/horizon flags where visual
authority supplies them; never infer positions from filenames.

## 16. Overlay and text safety

Current Phase 17 is unaware of image overlays and subtitle placement. It merely prints SRT paths
in diagnostics. Required image text/labels need certified overlay rectangles. Subtitle safety
should be a format/language layout exclusion region, not SRT retiming. If no certified safe move
exists, choose `Static`.

## 17. Aspect-ratio behavior

Phase 17 neither identifies image dimensions nor target canvas. Phase 8 validation at least checks
Short portrait; Phase 18 pre-scales to 2160x3840 for portrait and 2560x1440 for landscape before
zoompan. Canonical entries must record source `Width`/`Height`, format target aspect, safe crop,
and the approved source hash. Square Hero/Gallery/Thumbnail assets are excluded.

## 18. Motion intensity and perceptual limits

V1 exact endpoint policy:

| Profile | Scale % start→end | Pan X % | Pan Y % | Easing |
|---|---:|---:|---:|---|
| Hook | 100→115 | 0→0 | 0→0 | EaseOutCubic |
| Discovery | 100→108 | -3→3 | 2→-2 | EaseInOutSine |
| SkyGuide | 104→108 | -3→3 | 0→0 | EaseInOutSine |
| ViewingTip | 102→106 | -2→2 | -2→2 | EaseInOutSine |
| Closing | 110→100 | 0→0 | 0→0 | EaseInOutSine |
| BestTime | 100→104 | 0→0 | 0→0 | EaseInOutSine |
| static | 100→100 | 0→0 | 0→0 | EaseInOutSine |

V2 default scale deltas range 0-18% and pan endpoints up to 5%; Experimental reaches 30% scale
and ±8% pan. The dedicated conjunction selector is gentler: at most 5% scale delta, sky-guide
scale 1.04→1.08 and X -0.03→0.03, viewing-tip X ±0.018.

There is no configured pixels/sec, percent/sec, duration threshold, or Short/Long speed policy.
The same absolute endpoint delta over 10s and 50s produces very different speed. Target policy
should cap normalized translation/scale velocity and total amplitude, use slightly stronger Short
and slower Long profiles only as explicit versioned policy, and reduce amplitude for long windows
rather than accumulating unbounded travel.

## 19. Easing

V1 uses cubic ease-out for Hook and sine ease-in/out for everything else. V2 uses sine
ease-in/out. Easing is metadata in the plan and is also baked into V1 debug samples. Phase 18
interprets sine specially and treats every other value as cubic ease-out. The mature renderer
supports sine or linear. Canonical authority should use a closed enum and Phase 18 must implement
it exactly; filters remain non-authoritative render diagnostics.

## 20. Transition ownership

Phase 17 V1 says `cut`, zero seconds. Phase 18 ignores that effective intent and constructs 0.9s
for the first transition, then alternating 0.66/0.80-ish durations around a 0.72s default, labels
multiple transition types, but renders all with FFmpeg `xfade=transition=fade` (service
14173-14244). It also adds opening/closing pauses and final fade-to-black.

Recommendation: Phase 17 may own provider-neutral `TransitionOut` intent plus a duration/maximum
overlap constrained by Phase 16 windows. Phase 18 owns actual overlap arithmetic and rendering.
Opening/closing editorial pauses and final fade remain Phase 18 unless a later frozen authority
explicitly assigns them. Do not let transition overlap alter Phase 16 scene start/end lineage.

## 21. Subtitle awareness

No SRT is read by Phase 17 and no subtitle timing is changed. The selected Phase 15 SRT path in
diagnostics is informational legacy. Target may consume a certified subtitle exclusion zone and
carry `SubtitleSegmentIds` solely for lineage; it must not inspect cue duration or rewrite SRT.

## 22. Audio awareness

V1 requires every compatibility `audioPath` to exist and writes it into each motion row. Preview
records missing audio as warnings. Neither probes duration. Target removes `AudioPath` and all
physical audio reads; `AudioSha256` may be carried from Phase 16 as immutable lineage only.

## 23. Current artifacts

| Path | Shape / scope | Status | Consumer |
|---|---|---|---|
| `motion/motion-plan.json` | JSON V1, combined Short+Long | De facto compatibility | Phase 18 |
| `motion/motion-debug.json` | JSON, per-frame samples and UTC | Diagnostic | Humans; Phase 18 later overwrites this path during render, an ownership collision |
| `validation/phase-17-motion-diagnostics.json` | JSON language/path/count/error diagnostics | Diagnostic | Operators |
| `validation/phase-17-validation.json` | JSON status and paths | Weak current validation | Pipeline |
| `motion/motion-plan-v2-preview.json` | JSON preview Short+Long | Preview compatibility; selected by Phase 18 if stale file exists | Phase 18 |
| `motion/motion-debug-v2-preview.json` | JSON preview debug | Diagnostic | Humans |
| `validation/phase-17-motion-v2-diagnostics.json` | JSON preview diagnostics | Diagnostic | Operators |

No current manifest, authority checksum, publication report, transaction, or language-scoped
authority exists.

## 24. Recommended canonical artifacts

Use the smallest pattern consistent with Phase 16:

```text
17-motion/{language}/
  short/motion-plan.json
  long/motion-plan.json
  phase17-manifest.json
  phase17-authority-diagnostics.json
  phase17-publication-report.json
validation/phase-17-validation.json
```

Separate format plans avoid a redundant combined plan while preserving clear consumers. The
manifest lists/hashes both plans; diagnostics need not be a checksum input. Do not add separate
keyframe files. During migration only, write `motion/motion-plan.json` as an explicitly marked
compatibility projection after canonical commit/readback.

Language scope is required because Phase 16 durations differ by TTS language even if visual and
motion style do not. Reuse identical geometry across languages is permissible only as an internal
optimization; each language authority has its own timing binding/checksum.

## 25. Recommended motion-plan contract

Root: schema version, language, format, motion policy/version, ordered scene count,
`sourcePhase16AuthorityChecksum`, `sourceVisualAuthorityChecksum`, serialization version,
authority checksum, and entries. Per scene:

* identity: `SceneId`, `SceneAudioUnitId`, `Format`, `Sequence`, `Language`;
* timing lineage: `DurationMs`, `SceneStartMs`, `SceneEndMs`, `SubtitleSegmentIds`, optionally
  lineage-only `AudioSha256`, `SourcePhase16AuthorityChecksum`;
* visual identity: repository-relative `VisualAssetPath`, `VisualAssetSha256`, `Width`, `Height`,
  `SourceVisualAuthorityChecksum`;
* semantic motion: `MotionType`, `StartTransform`, `EndTransform`, normalized `Keyframes`, closed
  `Easing`, `SafeArea`, `FocusRegion`, `TransitionIn`, `TransitionOut`, `MotionPolicyVersion`.

Transforms should use scale plus normalized translation in a documented coordinate space. Avoid
FFmpeg expressions, frame numbers, absolute machine paths, and audio paths.

## 26. Phase 18 contract

Current Phase 18 expects a dynamic JSON root at `motion/motion-plan*.json`, Short/Long `items`, and
reads `sceneId`, image/audio paths, purpose, motion style/type, scale/zoom endpoints, X/Y pan, and
easing. It does not use V1 transition fields. It selects visuals independently, resolves audio
independently, and recalculates duration from compatibility plan and grouped Phase 15 cues
(`ReadVideoAssemblyItems`, 13284-13355). `RenderVideoAssemblyAsync` overrides durations from TTS
again (13694-13703).

Target Phase 18 reads the language-scoped committed manifest plus the appropriate format plan,
validates Phase 17 checksum/gates, uses the exact visual path/hash and duration, converts normalized
keyframes/easing to renderer commands, and implements transition intent. It must not select a
preview file by existence, pick a different visual, choose fallback motion, or expand duration.

## 27. FFmpeg and render ownership

Active Phase 17 makes no FFmpeg call and must remain metadata-only. `SmoothMotionRenderer` and
`BuildPhase18MotionFilter` are FFmpeg-specific and belong in Phase 18. Phase 18 also owns clip
creation, xfade, narration concatenation, audio mixing, subtitle burn-in, muxing, and final fade.
Reuse the selector/profile algorithm in Phase 17; keep filter construction in Phase 18.

## 28. Validation rules

Current rules are limited to input existence, 5/9 count, image/audio existence, positive seconds,
known V1 profile, no old paths, plan/debug existence, and nonempty purpose/profile/easing with no
parallax/advanced label.

Canonical validation must fail closed on:

1. all Phase 16 committed evidence/checksums/gates;
2. all Phase 10/visual authority evidence/checksums/gates;
3. exact bijective scene mapping and authority order;
4. one entry per scene and no duplicates;
5. exact millisecond duration/start/end equality with Phase 16;
6. existing approved visual, exact hash, positive dimensions, correct format/aspect;
7. finite positive scale, normalized translations, valid crop at every keyframe, no empty border;
8. monotonic normalized keyframes with endpoints 0 and 1;
9. focus/required overlay regions remain visible, with Static fallback accepted;
10. transition duration nonnegative and bounded without changing narration windows;
11. supported motion/easing policy and deterministic recomputation;
12. candidate semantic/checksum/manifest validation, candidate readback, committed readback, and
    downstream readiness.

## 29. Transaction and governance

Current Phase 17 creates final directories and overwrites files directly. There is no staging,
candidate validation/readback, atomic directory move, backup/rollback, committed readback,
authority checksum, manifest, publication report, or reuse. `generatedUtc` prevents byte-stable
debug.

Reuse Phase 16's transaction shape: language-scoped `.staging/{transaction}/{language}` and
`.backup`, validate/read candidate, move committed directory, validate committed readback, roll
back on failure, then publish compatibility and validation. Compute authority checksum only from
semantic normalized inputs/outputs, not timestamps or absolute paths.

## 30. Cleanup ownership

Phase 17 owns only `17-motion/{language}` and its Phase 17 validation projection; during migration
it may own the explicitly listed `motion/` compatibility files. Cleanup must be exact and
language-scoped. It must never delete or rewrite `16-duration-calibration`, `08-scene-assets`,
Phase 9/10 roots, `scene-assets-v3`, `tts`, `sync`, narration/subtitles, or Phase 18 output.

## 31. Standalone execution and immutability

`startPhaseNo=17,endPhaseNo=17` currently can run only if the compatibility timing file and V3
roots/audio files already exist; it does not prove the frozen Phase 8/9/10/16 gates. Target
standalone execution must require committed authorities and make no provider/render calls.

Certification tests should hash every file under the Phase 16 language root and every selected
visual authority/artifact before and after Phase 17 and require identical path/hash maps. This
audit did not execute Phase 17 or hash generated fixtures because the mission forbids rendering,
regeneration, and implementation execution with mutable outputs.

## 32. Input lineage

Genuine target timing files, based on the implemented Phase 16 publisher:

* `16-duration-calibration/{language}/calibrated-scene-timeline.json`;
* `16-duration-calibration/{language}/phase16-manifest.json`;
* `16-duration-calibration/{language}/phase16-publication-report.json`;
* `validation/phase-16-validation.json`.

`subtitle-timeline.json` is conditional safe-layout lineage, not duration input. Exact visual
lineage is the Phase 8 manifest/report listed in section 5, Phase 9 manifest/report for Long, and
all three files under `10-scene-validation/`. Short physical assets come from Phase 8 entries;
Long physical assets come from Phase 9 entries and must checksum-match their Phase 8 source.
The current route genuinely loads only the compatibility V3 manifests/images.

## 33. Reuse and invalidation

Current Phase 17 always overwrites and has no reuse result. Target reuse identity is a canonical
hash of Phase 16 checksum, visual authority/certification checksums, ordered scene IDs, selected
visual hashes/dimensions and safe-region metadata, motion policy version, format, language, schema,
and serializer version.

Regenerate on any timing checksum, visual bytes/selection/order, scene mapping, policy, safe/focus
region, language duration, schema, or serializer change. A clock/debug change must not invalidate
semantic authority. A Phase 16 change always invalidates reuse through
`sourcePhase16AuthorityChecksum`.

## 34. Result projection

Current validation provides only status, paths, `validationPassed`, old-path information, and
errors; the method returns paths or throws. It has none of the requested governed projection.

Add one typed `Phase17PublicationResult`, analogous to `Phase16PublicationResult`, as the single
source for API/validation fields: `reasonCode`, `generated`, `reused`, `regenerated`, candidate
validation/readback, `publicationCommitted`, `committedReadbackPassed`, committed-state status,
`authorityChecksum`, manifest/validation statuses, semantic/checksum/manifest booleans,
`sourcePhase16AuthorityChecksum`, visual checksum(s), and `downstreamReady`.

## 35. Failure codes

Current failures are free-form strings wrapped by `Phase 17 Motion Layer V1 failed`; there are no
stable Phase 17 codes. Adopt at minimum:

* `P17_UPSTREAM_PHASE16_INVALID`
* `P17_VISUAL_AUTHORITY_INVALID`
* `P17_SCENE_MAPPING_INVALID`
* `P17_MOTION_PLAN_INVALID`
* `P17_CANDIDATE_VALIDATION_FAILED`
* `P17_COMMIT_FAILED`
* `P17_COMMITTED_READBACK_FAILED`
* `P17_MOTION_AUTHORITY_ACCEPTED`

Add a distinct checksum/lineage code only if repository conventions require it; do not proliferate
codes for every validator predicate.

## 36. Test inventory

Only two direct motion tests were found:

| File / test | Behavior |
|---|---|
| `Backend/tests/Astronomy.MediaFactory.Tests/MotionRenderingTests.cs:8`, `SmoothMotionRenderer_UsesMotionLayerV2SineEasingInterpolation` | Asserts FFmpeg filter uses frame-driven sine easing and expected scale endpoints. |
| same file:19, `MotionProfileSelector_AssignsMotionLayerV2ProfilesForPlanetaryConjunction` | Asserts deterministic V2 Hook/Cause/SkyGuide/ViewingTip/Closing profile/type mapping. |

Four tests in `ProductionPipelineExecutionServiceTests.cs:1869-1894` cover **Phase 18** resolution,
mismatch diagnostics, precedence, and warning for V2 strength. They do not test Phase 17
publication.

No Phase 17 tests were found for Short/Long plans, Phase 16 binding, 4/12 authority counts, crop,
focus, approved visual selection, seed, transition, cleanup, reuse, transaction, standalone run,
immutability, or result projection.

## 37. Obsolete tests

There are no direct Phase 17 tests to delete. Future migration must not preserve tests merely to
assert shared `timing/scene-duration-plan.json`, 5/9 counts, audio existence, manifest/fixed preview
duration, legacy `motion/` authority, or Phase 17 FFmpeg (which does not currently exist). Keep the
two mature algorithm tests, adapting them to semantic contracts; Phase 18 strength tests remain
Phase 18 compatibility tests until the handoff is strict.

## 38. Governing-document changes

`docs/architecture/PipelineArchitecture.md:41` describes Phase 17 as motion/filter planning and is
directionally aligned but names the old inputs/outputs. In contrast,
`Architecture/RC2-Phase-Output-Contract-v1.0.md:704-731` calls Phase 17 “Production QA” and Phase
18 “Completion”, conflicting with the runtime registry's Motion Layer / Cinematic Assembly names.
After implementation ownership is approved, update both to the frozen registry, numbered
authority, timing boundaries, and semantic handoff. Do not change Phase 16 semantics.

## 39. Current-versus-target matrix

| Dimension | Current Phase 17 | Target Phase 17 |
|---|---|---|
| Timing source | Shared Phase 16 compatibility plan | Committed language Phase 16 authority |
| Visual source | V3 manifests/images | Phase 8 Short + Phase 9 Long, Phase 10 certified |
| Count source | V1 5/9 constants; preview max arrays | Complete Phase 16 arrays + visual bijection |
| Motion type | V1 role profiles / preview V2 types | Versioned semantic camera enum |
| Randomness | None; UTC debug | Deterministic semantic bytes; stable seed if added |
| Duration | Compatibility seconds; preview fallbacks | Exact `FinalSceneDurationMs` |
| Safe crop | None | Certified bounds/focus plus Static fallback |
| Subtitle usage | Diagnostic Phase 15 paths | Exclusion region/IDs only; never timing |
| Audio usage | Physical existence required | No read/probe; checksum lineage only |
| Transitions | `cut`/0 | Optional bounded semantic intent |
| Rendering | None | None |
| Artifacts | Unnumbered combined plan/debug/validation | Language/format numbered authority + governance |
| Transaction | Direct overwrite | Staged validate/readback/atomic commit/rollback |
| Cleanup | No explicit boundary | Own language root + validation only |
| Reuse | None | Canonical checksum identity |
| Phase 18 handoff | Dynamic, lossy, overridden | Strict manifest/plan; no re-derivation |

## 40. Code-reuse classification

| Component | Classification | Reason |
|---|---|---|
| `MotionProfileSelector` semantic policy | **REUSE ALGORITHM ONLY** | Mature deterministic/gentle profile selection, but limited to conjunction and render DTO. |
| `MotionProfile` model/enums | **REUSE WITH PHASE16 ADAPTER** | Useful semantic core; add authority identity, timing, visual/safety fields. |
| `SmoothMotionRenderer` | **REUSE AS-IS in Phase 18** | Correct renderer concern; never canonical Phase 17 output. |
| `ResolveMotionDefaults` / easing | **REUSE ALGORITHM ONLY** | Broader role table, but percentage conventions and intensity need normalization/safety. |
| V2 type/value selection | **REUSE ALGORITHM ONLY** | Deterministic and semantic, but preview defaults/strength can be unsafe. |
| `BuildMotionPlanItems` | **REUSE WITH PHASE16 ADAPTER** | Replace input/count/audio/path logic; retain basic mapping skeleton only. |
| `MotionPlanItem` V1 | **RETAIN COMPATIBILITY ONLY** | Phase 18 migration projection. |
| `motion/motion-plan.json` | **RETAIN COMPATIBILITY ONLY** | Publish after commit until Phase 18 migrates. |
| V1 debug sampler | **REUSE ALGORITHM ONLY** | Useful validation diagnostics; remove UTC from semantic identity. |
| Preview fixed/manifest duration | **OBSOLETE** | Violates Phase 16 authority. |
| Phase 17 audio/SRT/TTS path checks | **REMOVE FROM ACTIVE AUTHORITY PATH** | Ownership violation. |
| Phase 18 duration/profile/visual fallbacks | **REMOVE FROM ACTIVE AUTHORITY PATH** | Defeat strict handoff. |
| Phase 18 FFmpeg/filter/crossfade | **REUSE AS-IS with strict input adapter** | Correct phase ownership, subject to exact semantic interpretation. |

## 41. Minimal implementation plan

1. Add a strict loader that validates committed Phase 16 timeline/manifest/report/validation and
   returns the actual typed scene rows/checksum.
2. Load Phase 8 Short, Phase 9 Long, and Phase 10 certification; resolve one approved image and
   certified safety metadata by exact format/scene ID.
3. Validate a bijection and use Phase 16 sequence/count, never 5/9 or directory position.
4. Bind `FinalSceneDurationMs`, start/end, audio checksum, and subtitle IDs without opening audio,
   SRT, TTS, or the compatibility plan.
5. Adapt existing deterministic selector/profile algorithms to versioned semantic transforms and
   normalized keyframes; use a stable derived seed only if variation is introduced.
6. Clamp against source aspect, crop, focus, overlay, and subtitle-safe regions; fall back to
   Static when no safe transform exists.
7. Validate candidate semantics/checksums/manifests and publish the language-scoped numbered root
   transactionally with rollback and committed readback.
8. After commit, project the exact V1 compatibility shape only while Phase 18 needs it.
9. Give Phase 18 a strict Phase 17 adapter and remove its duration, visual, profile, preview-file,
   and transition-intent fallbacks; retain rendering/FFmpeg there.
10. Add authority, standalone, reuse/invalidation, result, immutable-upstream, Short 4/Long 12,
    crop/focus, transform, and Phase 18 contract tests; then update governing documents.

## 42. Files expected to change in implementation (not changed by this audit)

Exact likely set, kept deliberately small:

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`
  — route adapter and Phase 18 strict consumption; ideally shrink embedded helpers.
* new `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase17MotionAuthorityPublisher.cs`
  — loader, validation, reuse, transaction, publication.
* `Backend/src/Astronomy.MediaFactory.Core/Phase14AudioSyncAuthority.cs` or a new adjacent
  `Phase17MotionAuthority.cs` — typed plan/publication contracts (prefer the new focused file).
* `Backend/src/Astronomy.MediaFactory.Rendering/MotionProfileSelector.cs` — only if decoupling the
  semantic selector from `RenderPlanScene` cannot be done in the Phase 17 adapter.
* `Backend/tests/Astronomy.MediaFactory.Tests/Phase17MotionAuthorityTests.cs` (new).
* `Backend/tests/Astronomy.MediaFactory.Tests/Phase17FilesystemOwnershipTests.cs` (new).
* `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs` — strict
  Phase 18 handoff/compatibility tests.
* `docs/architecture/PipelineArchitecture.md` and
  `Architecture/RC2-Phase-Output-Contract-v1.0.md` — registry/ownership/output contract alignment.

Do not change `Phase16DurationCalibrationPublisher`, Phase 16 contracts/semantics, TTS/subtitle
generation, scene generation, or configuration merely to certify Phase 17.

## 43. Risks

* **Astronomy crop:** no current focus geometry; panning can lose the primary object, constellation
  center, horizon, or grouping.
* **Overlay/subtitle conflict:** baked labels and language-dependent subtitle exclusion areas may
  leave no safe motion; Static must be valid.
* **Nondeterminism:** UTC debug, future random variation, absolute paths, or noncanonical JSON can
  destabilize reuse.
* **Legacy Phase 18 dependency:** it selects preview by existence and overrides all key inputs.
* **Duration mismatch:** seconds rounding and Phase 18 TTS expansion can depart from Phase 16 ms.
* **Transition ambiguity:** Phase 18 currently invents and renders transitions while Phase 17 says
  cut; transition overlap can obscure scene-window semantics.
* **English/Hindi divergence:** same visual/style but different Phase 16 duration and speed; plans
  must remain language scoped.
* **Visual variants:** current manifest fallback/position and Phase 18 Story Frames option can select
  an uncertified/different image.
* **Portrait/landscape:** one amplitude table and absent dimensions make 9:16 and 16:9 crop safety
  materially different.
* **Policy fragmentation:** V1, V2 preview, dedicated renderer selector, and Phase 18 overrides can
  produce four different motions for one scene.

## 44. Certification criteria

Phase 17 is done only when all of the following are automated and pass:

* standalone 17→17 consumes the committed language Phase 16 gates/checksum;
* count/order is authority-driven (including Orion Short 4 and Long 12 fixtures);
* exact one-to-one timing/visual/motion mapping by stable ID;
* every `DurationMs`, start, and end exactly equals Phase 16;
* no TTS/audio/SRT duration inspection or recalculation;
* every source is approved/certified, correct format, hash, dimensions, and authority checksum;
* deterministic repeated inputs yield identical semantic plan/checksum;
* keyframes are valid, crop stays in bounds, no black borders, required focus/overlays remain
  visible, and unsafe cases deterministically become Static;
* transition intent is valid and does not change Phase 16 narration windows;
* canonical authority is staged, candidate-validated/read, atomically committed or rolled back,
  committed-read, and projected with all result flags;
* reuse/invalidation covers both timing and visual authorities, policy, regions, order, language,
  and schema;
* before/after hashes prove Phase 16 and all visual authorities/assets unchanged;
* Phase 18 consumes the exact plan without re-deriving duration, visual, motion, or subtitle timing;
* `downstreamReady == true` derives only from the accepted committed state.

## 45. Remaining uncertainties

1. The coordinate-space and schema for new certified focus/safe/overlay regions is not defined;
   governed Phase 8/9/10 contracts do not currently contain rectangles.
2. Product ownership must approve whether transitions are Phase 17 intent or wholly Phase 18;
   repository evidence supports Phase 18 rendering but not the current invention of intent.
3. The runtime registry conflicts with the frozen RC2 output-contract document; the runtime route
   was treated as the audit source of truth.
4. Decide the migration removal date for the combined `motion/motion-plan.json` compatibility file
   so a stale V2 preview can no longer hijack Phase 18.

## 46. Final recommendation and direct answers

* **Does Phase 17 use Phase 16 final duration directly?** No. It reads the shared compatibility
  seconds plan and never validates the Phase 16 checksum or millisecond scene row.
* **Does it inspect TTS/SRT for timing?** It does not parse either, but requires compatibility audio
  files and reports Phase 15 paths; preview may fall back to manifest/fixed duration. Remove these.
* **Which mature engine exists?** `MotionProfileSelector` + `SmoothMotionRenderer`, supplemented by
  the V2 preview type/value policy. Reuse semantic selection; keep FFmpeg rendering in Phase 18.
* **Is motion deterministic?** Selection and endpoint interpolation are deterministic; publication
  and debug bytes are not governed, and debug includes UTC.
* **Are objects/focus protected?** No.
* **Which exact scene assets are used?** Current V3 Short/Long manifest images, not Phase 10
  certification and not Hero/Thumbnail/Gallery.
* **Does Phase 17 render?** No; it writes metadata/debug JSON only.
* **What should it own?** The language-scoped numbered semantic plans, manifest, diagnostics,
  publication report, and validation projection described above.
* **What should Phase 18 consume?** The committed manifest and exact format plan, including timing,
  selected visual identity, transforms/keyframes/easing, safety, and transition intent.
* **Adapt or rebuild?** Adapt. The algorithms are sufficient for V1 certification once authority,
  safety, deterministic publication, and the Phase 18 boundary are added. Building another motion
  engine would worsen the existing policy fragmentation.
