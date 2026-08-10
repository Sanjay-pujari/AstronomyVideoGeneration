# Phase 18 Cinematic Video Assembly Authority Audit

**Audit date:** 2026-08-10  
**Scope:** active `ProductionPipelineExecutionService` route and directly related renderer/tests  
**Constraint:** documentation-only audit. No production, test, configuration, upstream authority, subtitle, audio, motion, or video artifact was changed. No FFmpeg/ffprobe or TTS process was invoked.

## Executive conclusion

Phase 18 is a mature but **legacy, non-authoritative assembly path**. It renders real MP4s, and its clip rendering, FFmpeg process execution, subtitle burn-in, mixing, probing, and cleanup contain reusable mechanics. It does **not** consume the committed Phase 17 authority directly. Instead it selects a legacy/preview motion JSON by file existence, independently resolves visuals, groups Phase 15 cue audio by scene, overrides scene duration from actual TTS, invents motion defaults and crossfades, appends a four-second outro, rewrites the selected SRT, and publishes directly into compatibility folders without a transaction or Phase 18 authority manifest.

The certification path should therefore be an **authority adapter around the existing renderer**, not another video pipeline. Preserve the physical-rendering primitives; replace every editorial/input decision with strict, typed Phase 15/16/17 consumption. In particular, current Orion `Static`, `Cut/0`, 120,000 ms Short, and 600,000 ms Long authorities cannot be rendered faithfully by the active route.

Direct answers to the audit gate:

* **Phase 17 visual/motion/duration directly?** No.
* **TTS duration override?** Yes, twice: item construction and render-time override.
* **Independent visual search/selection?** Yes.
* **Invented motion/transitions?** Yes; default motion and unconditional crossfades.
* **Phase 16 final SRT unchanged?** No; a legacy SRT is selected and rewritten.
* **Audio attachment?** Individual Phase 15-compatible timeline items are concatenated in timeline order into an MP3, then mixed/muxed once at final-video level; audio is not attached to each scene clip.
* **Calibrated silent visual tails?** No. The current narration-driven duration model eliminates per-scene calibrated tails and adds/pads a separate four-second outro.
* **Renderer to preserve?** Scene loop/image rendering, process argument-list execution, filter/codec primitives, concat/crossfade mechanisms (only behind governed transition dispatch), audio padding/mux primitives, subtitle burn primitive, probes, and staging cleanup.
* **Canonical owner?** `18-video-assembly/{language}` with format final MP4s and language-transaction metadata.
* **Phase 19 handoff?** A committed Phase 18 manifest/publication/validation package, never arbitrary legacy directories.

## 1. Current active call graph

All line references below are to `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` unless another file is named.

| Concern | Class / method | Lines | Actual role |
|---|---|---:|---|
| Registry | `ProductionPipelineExecutionService` phase table | 435-436 | Phase 18 maps to `PhaseVideoAssemblyV1Async`; Phase 19 follows. |
| Entry/result files | `PhaseVideoAssemblyV1Async` | 12461-13144 | Creates roots, selects inputs, renders, burns subtitles, validates, writes diagnostics, returns generated paths. |
| Short assembly | `PhaseVideoAssemblyV1Async` -> `RenderVideoAssemblyAsync` | 12548, 12567; 13593-13742 | Reads at most five legacy items and renders Short. |
| Long assembly | same | 12549, 12568; 13593-13742 | Reads at most nine items and renders Long (omitted in preview). |
| Visual selection | `ReadVideoAssemblyItems`; `ResolveStoryFrameV4VideoAssemblyImagePath`; `ResolveVideoAssemblySceneImageFromManifest` | 13188-13225; 13309-13339; 13403-13459 | Story-frame semantic/normalized matching or motion item/manifest/position fallbacks. |
| Story-frame enumeration | `LoadStoryFrameV4Manifest` | 13271-13300 | Enumerates PNG files when manifest mapping is absent. |
| Motion loading/fallback | `PhaseVideoAssemblyV1Async`; `BuildDefaultPhase18MotionPlan` | 12474-12490, 12533-12545; 13990-14031 | Preview wins by existence; absent plan causes directory/TTS-derived default plan. |
| Item and order projection | `ReadVideoAssemblyItems` | 13197-13257 | Uses JSON array order and `Take(expectedCount)`, not Phase 17 `Sequence`. |
| Duration calculation | `ReadVideoAssemblyItems`; `RenderVideoAssemblyAsync`; `OverrideRenderSceneDurationsFromTtsTimeline` | 13191-13245; 13595-13597; 13494-13515 | Prefers grouped cue-level TTS, then audio duration plan/item values, then 3 seconds. |
| Audio selection | `ResolveVideoAssemblyAudioPath`; convention fallback | 13219; 13461-13492 | Finds audio by scene ID, numeric prefix, position, then image/name convention. |
| Combined narration | `ReadCanonicalTtsTimelineItems`; `ConcatenateNarrationTrackAsync` | 13647-13653; 14157-14165 | Re-encodes ordered scene/cue audio to one MP3. |
| Subtitle selection | `ResolvePhase15SrtPath` call | 12588-12595 | Selects compatibility Phase 15/legacy narration SRT, not Phase 16 final SRT. |
| Subtitle rewrite | `RecalculatePhase18SceneBasedSubtitleTimingAsync` | 13764-13856 | Parses, redistributes/reindexes timing, overwrites the SRT and writes diagnostics. |
| Scene filter | `BuildPhase18MotionFilter` | 14044-14070 | Builds scale/crop/zoompan/trim/fps/pixel-format filter. |
| Scene clip FFmpeg | `RenderVideoAssemblyAsync` | 13609-13640 | One silent H.264 MP4 per scene. |
| Transition policy | `BuildPhase18TimelinePolishPlan` | 14073-14098 | Invents 0.9/0.66-0.80 second transitions and labels. |
| Transition render | `CrossfadeSceneClipsAsync` | 14112-14136 | Monolithic `xfade` chain, video only. |
| Cut concatenation primitive | `ConcatenateSceneClipsAsync` | 14138-14144 | Concat demuxer + stream copy; present but not active in Phase 18. |
| Audio mixing | `BuildPhase18FinalAudioMixArgs` | 13861-13885 | Trims/pads narration, optionally loops/mixes/ducks music, produces AAC. |
| Final mux/write | `RenderVideoAssemblyAsync` | 13676-13715 | Adds clone-tail/outro/fade and writes `final.mp4`. |
| Subtitle burn/write | `BurnInSubtitlesAsync` | 13901-13917 | Re-encodes video with libass subtitles, copies audio, replaces final file. |
| Physical probes | `ProbeAudioDurationSecondsAsync`, `ProbeVideoDimensionsAsync`, `HasAudioStreamAsync`, silent-tail probe | calls 12572-12585, 12644-12647, 13656-13724; helper 13887-13897, 13953-13960 | Duration, dimensions, stream, and end-silence checks. |
| Validation/diagnostics | `PhaseVideoAssemblyV1Async` | 12640-13144 | Ad-hoc booleans/errors and JSON reports; no authority manifest/checksum/readback. |
| Compatibility copy | same | 12632-12635 | Copies results to `video/{short,long}`. |
| Result projection | phase runner plus returned files | 12719, 12974-12976, 13137-13144; aggregate projection 15305-15331 | Existence/phase success drives video flags and final paths. |
| Phase 19 consumer | `PhaseVideoQaProductionReviewAsync` | 14168-14272 | Reads legacy video, sync/TTS/timing/motion/assets and Phase 18 diagnostics directly. |

`SmoothMotionRenderer.BuildZoomPanFilter` is a separate mature primitive at `Backend/src/Astronomy.MediaFactory.Rendering/SmoothMotionRenderer.cs:5-30`. It computes frame-driven linear or sine-eased zoom/pan. The active embedded Phase 18 path does **not** call it; it calls its own `BuildPhase18MotionFilter`.

## 2. Current responsibility inventory

| Capability | Current? | Evidence / qualification |
|---|---|---|
| Scene visual selection/image lookup | **Yes** | Story Frame V4 or legacy image/manifest/convention fallbacks. |
| Motion-plan generation | **Yes, fallback** | Builds a default plan if legacy plan is absent. |
| Motion interpretation | **Yes** | Converts selected strings/endpoints to FFmpeg zoompan. |
| Duration recalculation/audio probing | **Yes** | Cue aggregation, actual media probes, render-time override. |
| Audio selection | **Yes** | Scene ID/prefix/position/convention matching. |
| Narration-track generation | **Yes** | Concatenates scene/cue files into MP3. |
| Subtitle timing/regeneration | **Yes** | Overwrites SRT with scene-based retiming. |
| Scene clips | **Yes** | Silent temporary MP4 per scene. |
| Transitions/crossfades | **Yes** | Always generated for multi-scene videos. |
| Audio mixing | **Yes** | Narration padding, optional music/noise, ducking and fallback mix. |
| Final concatenation | **Yes** | `xfade` graph; concat-demuxer helper is inactive. |
| Short/Long MP4 | **Yes** | Compatibility root outputs. |
| Subtitle burn-in | **Conditional** | Controlled by `EnableSubtitles`; no embedded subtitle stream. |
| Subtitle sidecar | **No owned copy** | It mutates an upstream/compatibility SRT in place. |
| Thumbnail/gallery insertion | **No** | No reads of `12-thumbnails` or `13-gallery`. |
| Hero/introduction | **Implicit only** | First normal scene is called hero; no Phase 11 asset is inserted. |
| Outro | **Yes, invented** | Four-second cloned visual tail, background audio padding, one-second fade to black. |

Phase 18 currently owns too much. Target ownership is physical rendering/composition, governed audio placement, application/copy of final subtitles, final files, physical validation, and transactional publication only. Visual choice, motion semantics, duration, narration boundaries, subtitle text/timing, and scientific/editorial choices must be upstream-owned.

## 3. Phase 15 input usage and target rule

### Current

The route reads a language-scoped compatibility TTS timeline (`ResolveLanguageScopedTtsTimelinePath` at 12472), dynamically resolves audio, and later reloads canonical-looking timeline items (13647). It requires all entries, concatenates them with concat demuxer and `libmp3lame -q:a 4` (14157-14165), and treats the new `video-assembly/{language}/{format}/narration-track.mp3` as the mux source. Actual audio duration becomes video timing authority.

### Target

Load and validate:

* `15-tts/{language}/tts-timeline.json`
* `15-tts/{language}/phase15-manifest.json`
* `15-tts/{language}/phase15-publication-report.json`
* `15-tts/{language}/validation/phase-15-validation.json` (or the repository's language validation location)
* every referenced canonical scene MP3

Bind audio by `SceneAudioUnitId`, additionally checking `SceneId`, `Format`, and `Sequence`. The **per-scene file identity is canonical**. A combined narration track may be created in transaction staging as a render optimization or published as an explicitly derived compatibility projection, but must never define scene duration, order, identity, or authority.

The safest semantic model is one governed render job per scene: Phase 17 visual duration, Phase 15 audio beginning at scene start, then silence to scene end. This can be implemented with current FFmpeg primitives by padding each scene audio to the governed scene duration and encoding uniform scene clips, or by placing each original audio file at `SceneStartMs` in a final filtergraph. Per-scene padded clips are simpler to validate and concatenate for `Cut/0`.

## 4. Phase 16 input usage and target rule

### Current

Phase 18 reads shared `timing/scene-duration-plan.json`, but then prefers grouped Phase 15 cue durations. It selects a compatibility SRT, rewrites it, and validates SRT end against narration rather than the calibrated visual timeline.

### Minimal target dependency

Even though Phase 17 carries scene windows/durations, Phase 18 must load Phase 16 directly for lineage and subtitles:

* `16-duration-calibration/{language}/calibrated-scene-timeline.json` — cross-check scene identity/order/window and obtain `SourcePhase15AuthorityChecksum`.
* `16-duration-calibration/{language}/{short,long}/final.srt` — exact final subtitle authority.
* the subtitle timeline artifact referenced by the manifest — cue-count/text/timing validation.
* `phase16-manifest.json`, `phase16-publication-report.json`, and Phase 16 validation.

Phase 17 is rendering timing authority; Phase 16 is the independent lineage and subtitle authority. Require `Phase17.DurationMs == Phase16.FinalSceneDurationMs`, and matching starts/ends. Do not use Phase 16 to recalculate rendering durations.

Before rendering, require the final SRT to exist, match its manifest SHA-256/length, parse successfully, and have cue identity/count/times equal to the Phase 16 subtitle timeline. Phase 18 may copy, mux, or burn those exact bytes/semantics; it may not split, merge, retime, rewrite, or reindex them.

## 5. Phase 17 input usage and required fields

### Current

None of the committed numbered Phase 17 package is loaded. Phase 18 selects `motion/motion-plan-v2-preview.json` merely if it exists, otherwise `motion/motion-plan.json` (12474-12490). It expects loose `short.items`/`long.items`, truncates them to 5/9, and discards authoritative checksum, dimensions, sequence, keyframes, safety, and transition duration.

### Target files

* `17-motion/{language}/short/motion-plan.json`
* `17-motion/{language}/long/motion-plan.json`
* `17-motion/{language}/phase17-manifest.json`
* `17-motion/{language}/phase17-publication-report.json`
* `17-motion/{language}/validation/phase-17-validation.json` (or manifest-declared location)

The typed contract exists in `Backend/src/Astronomy.MediaFactory.Core/Phase17MotionAuthority.cs:3-29`. Each job needs: `SceneId`, `SceneAudioUnitId`, `Format`, `Sequence`, `Language`, `DurationMs`, `SceneStartMs`, `SceneEndMs`, `AudioSha256`, `VisualAssetPath`, `VisualAssetSha256`, `Width`, `Height`, `TargetAspectFamily`, both source checksums, motion and safety policy versions, `MotionType`, `StartTransform`, `EndTransform`, normalized `Keyframes`, `Easing`, safe/focus/required-visible regions where applicable, `TransitionIn`, and `TransitionOut`. The plan-level authority checksum and policy versions are also required.

Sort strictly by `Sequence`; reject duplicates, gaps, format/language disagreement, non-contiguous windows, or an array order that disagrees with sequence. Never use filename or directory order.

## 6. Cross-phase lineage and physical evidence

Fail closed before creating render candidates unless:

1. all three upstream publication reports say committed/readback/semantic/checksum/manifest validation passed and `downstreamReady=true`;
2. `Phase17.SourcePhase16AuthorityChecksum == Phase16.AuthorityChecksum`;
3. `Phase16.SourcePhase15AuthorityChecksum == Phase15.AuthorityChecksum`;
4. every Phase 15/16/17 row matches on language, format, `SceneAudioUnitId`, `SceneId`, and `Sequence`;
5. Phase 17 duration/window equals Phase 16 final duration/window;
6. Phase 17 `AudioSha256` equals the bound Phase 15 audio identity;
7. every visual exists, SHA-256 equals `VisualAssetSha256`, and probed dimensions equal Phase 17 `Width`/`Height`;
8. every audio exists and matches manifest path/hash/length and probe policy;
9. Phase 16 SRT and subtitle timeline pass the checks in section 4.

There must be no missing-file substitution. A physically missing or changed visual/audio/SRT is an invalid upstream evidence failure, not a reason to search for another file.

## 7. Visual selection audit

The active path references `scene-assets-v3` (12470), Story Frame V4 roots/manifests (12477-12486), legacy asset roots (12497-12502), dynamic item image paths and a manifest resolver (13216-13218), directory enumeration (13289-13294), normalized/semantic fallbacks (13309-13339), and a default-plan enumeration sorted by filename (13998-14014). These are all forbidden on the canonical path.

There is no active Phase 18 use of `08-scene-assets`, `09-long-scenes`, `11-hero`, `12-thumbnails`, or `13-gallery`, but its generic/legacy resolvers are still independent selection. Target selection is exactly `Phase17.VisualAssetPath + Phase17.VisualAssetSha256`; compatibility resolvers may remain callable only outside authority publication.

## 8. Duration and timestamp audit

Current precedence is grouped cue-level actual TTS duration -> shared duration-plan audio duration -> loose item audio/scene duration -> 3 seconds (13191-13245). `RenderVideoAssemblyAsync` repeats the TTS override (13595-13597). It then adds transition overlap into source clip durations and demands pre-outro video equals narration within 100 ms (13661-13674). Finally it adds four seconds (13676-13712). Thus Short/Long total is narration plus four seconds, not the frozen 120/600-second timelines.

Target rules:

* `renderedSceneDuration = Phase17.DurationMs = Phase16.FinalSceneDurationMs`.
* Audio may be probed only to validate `actualAudioDuration <= sceneDuration` (with a documented codec probe tolerance); it may never expand or shrink a scene.
* Any violation fails upstream evidence. Never stretch, speed, trim, or silently repair speech.
* At 30 fps, one frame is **33.333 ms**. Validate each boundary and final duration within at most one frame plus a narrowly documented container timestamp rounding allowance. Do not retain the current arbitrary 100 ms tolerance.
* Current Orion expected totals are Short 120,000 ms and Long 600,000 ms, not approximately 95/399 seconds of speech.

## 9. Audio, silence, and `-shortest`

Current model is **B/C**: render silent clips, crossfade, concatenate authoritative-looking audio into one MP3, mix/pad it to narration+outro, and mux once. It does not use `-shortest`. That avoids early container termination, but `-t` is widely used: scene clips (13638), mix output (13879-13884), and final mux (13710-13712). `atrim` is used in final mixing. In the current duration model these operations deliberately constrain media to re-derived totals.

Target model should retain the useful `apad` concept but prohibit trimming speech. A scene with `sceneDuration > audioDuration` continues visually while the audio becomes silence. Audio padding (or a final timeline with gaps) must preserve the full calibrated video. Do not use `-shortest`. Validate both video/container duration and last required narration sample/cue. It is valid for an unpadded audio stream to end before video; for interoperability, publishing a padded AAC stream to total video duration is preferable and deterministic.

Current narration is transcoded MP3 -> combined MP3 -> AAC, so it is not byte-preserved. Content/timing remain the authority; a declared codec policy may transcode levels/formats, but must not alter speech rate/content/boundaries. There is no `loudnorm` or compressor on narration itself. Optional side-chain compression affects background music. No governed normalization policy exists today.

## 10. Subtitle policy

Current subtitles are enabled/disabled globally and, when enabled, burned into pixels. `BurnInSubtitlesAsync` copies the unsubtitled MP4, runs libass `subtitles=...` with Arial/style values, encodes H.264/yuv420p, copies audio, and replaces `final.mp4` (13901-13917). There is no embedded subtitle stream and no Phase 18-owned sidecar. Worse, the source SRT is rewritten first (12590-12596, 13764-13856).

Recommended target artifact policy:

* Always publish an exact-byte `final.srt` sidecar copied from Phase 16 for each requested format and record its upstream hash.
* Make burn-in a versioned `subtitlePolicy` (`SidecarOnly` or `BurnInAndSidecar`) rather than an implicit setting. Platform publishing can consume the sidecar.
* If burn-in is selected, preserve the SRT bytes and render them without semantic rewriting. Record font/library/style policy in identity.
* Do not claim an embedded subtitle stream unless one is explicitly produced/validated.

Arial availability and Devanagari shaping/fallback are certification risks. A packaged Unicode font and a Hindi render fixture are required before Hindi burn-in can be certified.

## 11. Motion interpretation

`BuildPhase18MotionFilter` supports named `SlowZoomIn`, `SlowZoomOut`, `PanLeft`, `PanRight`, and `PushToObject`, plus a default branch. It forces minimum zoom deltas, minimum 108% pan scale, and synthesized Push-to-object pan deltas (14055-14064). It supports sine explicitly; every non-sine easing becomes cubic (14050-14063). It ignores normalized keyframe arrays entirely. Unknown motion/easing silently falls into a substitute. This is not faithful semantic interpretation.

`SmoothMotionRenderer` is cleaner and reusable for endpoint interpolation, but currently supports only sine vs default linear and still does not consume arbitrary keyframes. It should either be extended behind a strict Phase 17 adapter or its math should be reused in a new typed filter builder. Unknown motion/easing must fail.

For current Orion all 16 scenes are `Static`. Static must be a scale/crop/hold with no changing transform. The active default branch can still interpolate endpoints and Phase 18 validation even demands zoom and pan on every scene (12743-12745), so current code cannot certify Static.

Future non-static support must evaluate the exact normalized Phase 17 keyframes at their normalized times using the declared easing. It must not enlarge zoom, invent pan, or alter endpoints. Safety regions are validation inputs, not permission to choose a different motion. `EaseOutCubic`, declared by the Phase 17 enum, needs an explicit implementation. Unknown enum/schema values fail.

## 12. Transitions, intro/outro, and music

Phase 17 currently permits only `Cut` with duration zero (`Phase17TransitionType`). Active Phase 18 ignores it and creates every transition: 0.9 seconds first, then approximately 0.66-0.80 seconds, always rendered as FFmpeg `xfade=fade` regardless of diagnostic label (14073-14098, 14112-14135). Transition overlap changes duration arithmetic.

Canonical behavior for current authority is direct cut/zero only, most safely uniform scene clips + concat demuxer/stream copy. Future transitions require an explicitly supported Phase 17 type/duration and deterministic overlap arithmetic. Unknown or inconsistent adjacent transition declarations fail. Phase 18 must never invent a default transition.

There is no separate title card, logo, CTA asset, Phase 11 hero, thumbnail, or gallery insertion. However the route invents a four-second cloned last-frame outro plus one-second fade (13676-13712), and Phase 19 currently requires it (14201-14212). These must be removed from canonical certification unless represented as governed scenes/transitions. Phase 19 must stop making invented outro diagnostics mandatory.

Background music is read from options, then `audio/background.mp3` fallback. It may loop, change volume, duck, fade during outro, or—if enabled but missing—synthesize pink noise (13861-13885, 13978-13988). No source identity, copyright lineage, or frozen policy governs it. Exclude it from initial certification; never synthesize replacement ambience. Add it only through a future governed audio-bed authority.

## 13. Render configuration and codec audit

| Property | Current active embedded Phase 18 | Certification recommendation |
|---|---|---|
| Short canvas | `RenderingOptions.ShortVideoWidth/Height`, defaults **1080x1920** | Freeze 1080x1920, 9:16 in codec/render policy. |
| Long canvas | `RenderingOptions.VideoWidth/Height`, defaults/config **1280x720** | Preserve 1280x720 initially unless product authority explicitly upgrades to 1920x1080; do not report current as 1080p. |
| FPS | Hard-coded **30** in clips, filters, xfade | Freeze 30 fps and validate rational rate. |
| Video codec | `libx264` (H.264) | Preserve for upload compatibility. |
| Pixel format | `yuv420p` | Preserve. |
| Profile/level | Not specified | Record encoder-observed values; optionally freeze later. |
| Preset | `veryfast` | Version it; output checksum changes with policy/toolchain. |
| CRF/bitrate | Not specified, FFmpeg/libx264 defaults | Certification should explicitly freeze CRF/bitrate policy. |
| Final audio | AAC, bitrate/sample rate/channels unspecified | Freeze AAC-LC, 48 kHz, 2 channels, explicit bitrate (for example 192 kb/s) after product approval. |
| Combined audio | MP3 `libmp3lame -q:a 4` | Compatibility/optimization only; avoid extra generation when per-scene AAC composition is used. |

Defaults are defined at `Backend/src/Astronomy.MediaFactory.Contracts/Contracts.cs:406-410`; worker/API config confirms 1280x720/30 for landscape. Final upload compatibility is good at the codec/container level (MP4/H.264/yuv420p/AAC), but reproducibility is incomplete without explicit encoder/audio settings and an FFmpeg/toolchain identity.

## 14. Scene clip, concatenation, and intermediates

Current implementation creates one silent H.264 MP4 per scene under `/tmp/astro-video-assembly-{GUID}`, then a monolithic xfade output, a combined narration MP3, a mixed M4A in the published format directory, and final MP4. It deletes the temp GUID tree in `finally` and swallows cleanup errors (13600-13602, 13737-13741). Per-scene filters and several diagnostics remain in the compatibility output directory.

Recommended governed approach:

1. one audio-bearing, duration-locked MP4 per scene under **transaction staging**;
2. direct concat for Cut/0, transition graph only for explicit authority;
3. final optional subtitle pass;
4. physical validation in staging;
5. atomic language-root commit.

Scene clips, concat lists, combined audio, unsubtitled copies, and command/log files are candidate/debug intermediates, not canonical authority. Keep useful diagnostics if manifest-declared; never include bulky scene clips in canonical authority by default. Cleanup failure must be recorded, retried, and observable rather than swallowed. No GUID directory should remain after success.

## 15. Current outputs and target canonical outputs

### Current

* `video-assembly/{language}/short/final.mp4`
* `video-assembly/{language}/long/final.mp4`
* format narration MP3, mixed M4A, filter/timeline/audio diagnostics, subtitle intermediate copies
* copies at `video/short/final-short.mp4` and `video/long/final-long.mp4`
* validation-root ad-hoc Phase 18 JSON files

There is no numbered authority root, manifest, publication report, authority checksum, candidate validation/readback, atomic commit, or reuse identity. Existing files can be overwritten in place, so a later failure can leave mixed old/new/partial state.

### Target

```text
18-video-assembly/{language}/
  short/
    final.mp4
    final.srt
  long/
    final.mp4
    final.srt
  phase18-manifest.json
  phase18-authority-diagnostics.json
  phase18-publication-report.json
  validation/
    phase-18-validation.json
```

Each video manifest row records relative path, byte length, SHA-256, container duration, video duration, dimensions, rational FPS, frame count, video codec/profile/level/pixel format, and audio codec/sample rate/channels/bitrate/duration. It also records source SRT identity and subtitle mode. A distinct Phase 18 authority checksum must canonicalize upstream Phase 15/16/17 checksums, ordered output identities/hashes, language/requested formats, render/codec/audio/subtitle policy versions, schema/serializer version, and (where reproducibility requires it) toolchain identity. Never copy an upstream checksum.

English owns only `.../en`; Hindi owns only `.../hi`. Replacing one language must not delete another. When both formats are requested, one language transaction commits only after both pass. Short-only/Long-only requests are valid identities and must not require or delete the unrequested format; requested-format set is part of reuse identity.

## 16. Transaction, reuse, invalidation, and standalone execution

Phase 18 currently has no transactional or real reuse behavior. Target semantics mirror Phase 17:

* `overwriteExisting=false` + identical requested identity + fully valid committed readback -> reuse;
* `overwriteExisting=true` + existing authority -> render/validate a new candidate and atomically replace after success;
* identity mismatch -> regenerate transactionally;
* failure after any scene preserves the prior committed root and removes staging;
* never reuse solely because `final.mp4` exists.

Invalidate on any upstream authority/hash/SRT/timing/motion/visual change; language or requested-format change; resolution/FPS/render/codec/audio/subtitle/schema policy change; or invalid physical readback.

`startPhaseNo=18,endPhaseNo=18` must load frozen Phase 15/16/17 packages without rerunning them. Today the route can start at 18 only if all legacy compatibility projections also exist, so it is not standalone against numbered authorities.

## 17. Physical final-video validation

For every requested candidate MP4, fail unless:

* file exists, is regular, has a policy minimum byte length, and hash/length can be read;
* ffprobe succeeds and container has a nonzero-frame video stream;
* required narration yields an audio stream;
* resolution, FPS, H.264 codec, yuv420p, AAC policy, sample rate/channels/bitrate match;
* video duration equals sum of Phase 17 durations within explicit frame tolerance;
* scene boundaries (using packet/frame evidence where practical) are within one frame;
* audio contains every required ordered scene unit and is not truncated;
* container/video retains the calibrated silent tail even if narration ends earlier;
* subtitle sidecar hash/count/times match Phase 16; burn-in/stream checks match selected policy;
* checksum/readback validation passes after commit.

The current validation checks existence, audio stream, portrait/landscape orientation, approximate narration+outro duration, and selected diagnostics, but not hash, minimum length, exact FPS, codec/profile, pixel format, frame count, sample metadata, authoritative total, or committed readback.

## 18. Result projection and requested-output completion

Current diagnostics expose final paths, while aggregate projection sets `shortVideoGenerated`/`longVideoGenerated` when Phase 18 succeeded and paths are nonblank. These values are based on compatibility paths/existence, not committed authority. Phase 18 does not expose the requested governance fields as one typed publication result.

Add a single `Phase18PublicationResult` analogous to Phases 15-17 with: `reasonCode`, `reason`, loaded artifacts/output files, `generated`, `reused`, `regenerated`, candidate validation/readback, `publicationCommitted`, `committedReadbackPassed`, committed-state/semantic/checksum/manifest validation, `authorityChecksum`, manifest/validation status, and `downstreamReady`.

Only an accepted committed format row may set `shortVideoGenerated`/`longVideoGenerated` and `finalShortVideoPath`/`finalLongVideoPath`. `ShortVideo` completion should require its Phase 18 requested format. Current pipeline mapping makes both video outputs Phase 18-dependent; Phase 19 is QA/review and currently throws on either-video or cinematic-policy failure. Decide explicitly whether Long completion means assembled (Phase 18) or publication-approved (Phase 19); do not ambiguously mix the two.

## 19. Phase 19 current and target contract

Current Phase 19 searches legacy paths: `video/{short,long}`, shared sync/TTS/timing/motion, `scene-assets-v3`, motion debug, and `phase-18-video-diagnostics.json` (14176-14196). It probes MP4 duration/audio/silence/black/freeze, reconstructs scene/story/audio/visual checks from upstream legacy files, and mandates the invented four-second outro/fade (14201-14220, 14257-14270). It consumes no Phase 18 manifest/checksums/subtitle identity or full codec metadata.

Target Phase 19 loads only the committed `18-video-assembly/{language}/phase18-manifest.json`, publication report, validation, and manifest-declared final media/sidecars. It verifies Phase 18 checksum/readback and performs review on those deterministic files. It must not discover arbitrary videos or rebuild Phase 18 input decisions. Any deeper scientific/story review may follow manifest lineage to frozen authorities deliberately, not search compatibility folders.

## 20. Failure codes

Current failures are free-form `InvalidOperationException` messages such as missing inputs, unresolved frames, missing audio, clip render, narration/video mismatch, mix/mux, burn-in, count/orientation, and diagnostics errors. There is no stable Phase 18 reason-code enum.

Minimum closed vocabulary:

* `P18_UPSTREAM_PHASE15_INVALID`
* `P18_UPSTREAM_PHASE16_INVALID`
* `P18_UPSTREAM_PHASE17_INVALID`
* `P18_LINEAGE_MISMATCH`
* `P18_VISUAL_PHYSICAL_EVIDENCE_INVALID`
* `P18_AUDIO_PHYSICAL_EVIDENCE_INVALID`
* `P18_SUBTITLE_PHYSICAL_EVIDENCE_INVALID`
* `P18_RENDER_FAILED`
* `P18_VIDEO_VALIDATION_FAILED`
* `P18_CANDIDATE_VALIDATION_FAILED`
* `P18_COMMIT_FAILED`
* `P18_COMMITTED_READBACK_FAILED`
* `P18_VIDEO_ASSEMBLY_AUTHORITY_ACCEPTED`

Add specific unsupported-motion/easing/transition and cleanup diagnostics as details or stable subcodes without converting them into silent fallback.

## 21. Current test inventory and classification

### Direct active-route tests

| File / test (line) | Behavior | Target disposition |
|---|---|---|
| `ProductionPipelineExecutionServiceTests.cs:1510` `Phase18VisualDurations_GroupCueLevelTtsTimelineDurationsByScene` | Groups cue audio by parent scene. | **Obsolete authority behavior**; retain only as legacy adapter test. |
| same `:1551` `...KeepsSceneStructureWhileExpandingSceneDurations` | Audio expands scene duration. | **Obsolete**. Target must fail mismatch, not expand. |
| same `:1605` `Phase18SubtitleValidation_UsesParentSceneDurations...` | Validates legacy SRT against scene-grouped TTS. | **Obsolete**; replace with exact Phase 16 SRT checks. |
| same `:1652` `...UsesRequestedLanguageTtsTimelineForDurationExpansion` | Language-scoped audio override. | **Compatibility only**; language scoping remains, override does not. |
| same `:1713` `...MatchesNumericPrefixTtsSceneDurations` | Numeric-prefix matching fallback. | **Obsolete**; bind `SceneAudioUnitId`. |
| same `:1869-1894` four MotionV2Strength tests | Request/legacy-plan strength precedence/warnings. | **Compatibility only/obsolete active path**. |
| `StoryFrameV4ComparisonTests.cs:189` semantic Phase 18 visual mapping | Independent Story Frame selection. | **Compatibility renderer/input test**, remove from authority path. |
| `MotionRenderingTests.cs:8` `SmoothMotionRenderer_UsesMotionLayerV2SineEasingInterpolation` | Frame-driven endpoint/sine filter. | **Renderer unit test; preserve and extend**. |
| `ProductionPipelineExecutionServiceTests.cs:934,949` Phase 19 cinematic diagnostics tests | Trust/reject Phase 18 outro/fade diagnostics. | **Obsolete governance expectation** unless an outro becomes authoritative. |

### Adjacent renderer suite

`FfmpegVideoRenderServiceTests.cs` covers the separate renderer's segmented clips, concat, encoders, diagnostics, failures/timeouts, motion/effects, audio, and outputs. `VideoAssemblyIntelligenceServiceTests.cs` covers a separate planning/dry-run assembly service, missing visuals/plans, subtitle block construction, and duration validation. These are **renderer/service unit tests**, not proof that the active Phase 18 route consumes frozen authorities. Preserve mature mechanical assertions, but do not mistake their legacy plans for canonical inputs.

No current direct active-route test certifies: numbered Phase 15/16/17 loading; lineage failure; visual hash/dimensions; exact Static; exact keyframes/easing rejection; Cut/0; 4 Short/12 Long; 120/600 seconds; silence tails; exact Phase 16 SRT bytes; codecs/FPS/pixel/audio metadata; language/format transaction; cleanup failure; reuse/overwrite; checksums/readback; or Phase 19 manifest handoff.

Legacy `motion/motion-plan.json`, `timing/scene-duration-plan.json`, preview selection, numeric/position matching, fixed 5/9 counts, audio-driven override, invented transitions, and `scene-assets-v3` expectations are obsolete for authority. Tests of pure FFmpeg/filter mechanics remain renderer unit tests; compatibility projection tests should be explicitly named/scoped as such.

## 22. Current-vs-target matrix

| Concern | Current Phase 18 | Target Phase 18 |
|---|---|---|
| Visual source | Story Frames/legacy motion/manifest/assets fallback | Exact Phase 17 path/hash/dimensions |
| Motion source | Preview/legacy plan or generated default | Committed Phase 17 typed entry |
| Duration source | Grouped actual TTS/audio fallbacks | Exact Phase 17/Phase 16 ms |
| Audio source | Dynamic timeline matching + combined MP3 | Phase 15 unit by `SceneAudioUnitId`; combined derived only |
| Subtitle source | Legacy/Phase 15-looking SRT | Phase 16 `final.srt` |
| Scene order | JSON/position, capped 5/9 | Phase 17 `Sequence`; current 4/12 |
| Transitions | Invented xfade at every boundary | Exact governed transition; current Cut/0 |
| FFmpeg | Embedded mature subprocess implementation | Reused mechanics behind strict jobs |
| Short resolution | 1080x1920 default | Frozen 1080x1920 |
| Long resolution | 1280x720 default/config | Freeze repository 1280x720 unless governed change |
| FPS | 30 hard-coded | Versioned/validated 30 |
| Video codec | H.264/libx264, veryfast, implicit quality | Explicit versioned H.264 policy |
| Audio codec | AAC final, implicit metadata | Explicit AAC-LC/rate/channels/bitrate |
| Pixel format | yuv420p | yuv420p validated |
| Physical validation | Partial probes/orientation/duration | Full file/hash/streams/frames/codec/timeline |
| Artifacts | Compatibility MP4s + loose diagnostics | Numbered manifest/report/validation/videos/SRT |
| Transaction | In-place writes/copies | Atomic language requested-format set |
| Cleanup | `/tmp` finally; errors swallowed | Transaction staging, observable guaranteed cleanup |
| Reuse | None/unsafe existence effects | Full identity + valid committed readback |
| Result projection | Existence + phase success | Typed accepted publication result |
| Phase 19 | Searches legacy folders/upstream files | Reads deterministic Phase 18 authority |

## 23. Code reuse classification

| Component | Classification | Reason/action |
|---|---|---|
| `PhaseVideoAssemblyV1Async` | **REUSE WITH AUTHORITY ADAPTER / substantial orchestration replacement** | Keep route/phase integration; replace input, policy, publication, projection. |
| `SmoothMotionRenderer` | **REUSE RENDERER ONLY** | Good frame interpolation primitive; extend typed easing/keyframes/Static or wrap strictly. |
| `BuildPhase18MotionFilter` | **REUSE RENDERER ONLY, then replace/reshape** | Useful scale/crop/zoompan syntax; current semantic invention and fallback are forbidden. |
| Scene clip loop/process invocation | **REUSE WITH AUTHORITY ADAPTER** | Feed strict duration/visual/audio/motion jobs and staging paths. |
| FFmpeg argument-list/process runner | **REUSE AS-IS** | Avoids shell interpolation; retain logging/cancellation. |
| `ConcatenateSceneClipsAsync` | **REUSE AS-IS for Cut/0 after uniform validation** | Concat demuxer/stream copy matches current governed transitions. |
| `CrossfadeSceneClipsAsync` | **REUSE RENDERER ONLY** | Invoke only for explicit supported transitions; adapt audio and exact duration. |
| Combined narration logic | **RETAIN COMPATIBILITY ONLY** | Derived optimization, never authority/timing. |
| Final audio padding/mux primitives | **REUSE WITH AUTHORITY ADAPTER** | Remove speech trim/invented outro/music; enforce governed total. |
| Subtitle burn primitive | **REUSE WITH AUTHORITY ADAPTER** | Use immutable Phase 16 SRT; policy/version/font validation required. |
| Subtitle retiming helper | **REMOVE FROM ACTIVE AUTHORITY PATH / OBSOLETE** | Violates Phase 16 ownership. |
| Legacy visual resolvers/enumeration | **RETAIN COMPATIBILITY ONLY** | Never callable from canonical publication. |
| Legacy duration resolver/TTS override | **REMOVE FROM ACTIVE AUTHORITY PATH / OBSOLETE** | Violates Phase 16/17. |
| Default motion plan/profile fallback | **REMOVE FROM ACTIVE AUTHORITY PATH / OBSOLETE** | Violates Phase 17 and Static. |
| Invented timeline polish/outro/music | **REMOVE FROM ACTIVE AUTHORITY PATH** | No governed intent/source. |
| Existing probes | **REUSE WITH AUTHORITY ADAPTER** | Expand to full structured media evidence. |

## 24. Minimal implementation plan (not implemented)

1. Add typed Phase 18 authority/publication/media-evidence contracts and versioned render/codec/audio/subtitle policies.
2. At Phase 18 entry, resolve requested language/formats and load manifest-declared committed Phase 15, 16, and 17 artifacts only.
3. Validate all upstream publication states, manifests/checksums, and cross-phase lineage; fail closed.
4. Build strict jobs sorted by Phase 17 `Sequence`; cross-check Phase 15/16 identity and exact Phase 16/17 windows.
5. Validate each Phase 17 visual path/hash/dimensions, Phase 15 audio path/hash/probe, and Phase 16 SRT hash/parse/timeline.
6. Create language-transaction staging and render each scene using exact visual, duration, motion/keyframes/easing, and audio bound by `SceneAudioUnitId`; pad silence, never trim speech.
7. Compose scenes using exact transitions. For current `Cut/0`, use direct concat; reject unsupported semantics.
8. Apply the immutable Phase 16 SRT under the selected versioned sidecar/burn policy.
9. Probe and validate every final candidate against resolution/FPS/codec/pixel/audio/frame/timeline policy and record hashes/evidence.
10. Build Phase 18 manifest, diagnostics, validation, publication result, and a distinct authority checksum; candidate-readback validate them.
11. Atomically commit all requested formats as one language authority; committed-readback validate; clean staging while preserving prior authority on any failure.
12. Project accepted flags/paths/result; produce `video-assembly`/`video` compatibility copies only after commit and only while a declared downstream needs them.
13. Adapt Phase 19 to consume Phase 18 manifest/publication/validation and declared MP4/SRT files.
14. Add certification tests, then remove legacy decisions from the active route; verify frozen Phase 15/16/17 byte hashes before/after integration tests.

## 25. Files/classes expected to change in implementation

No files below were changed by this audit. Expected future changes are:

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` — strict entry adapter, render jobs, transaction, validation, result projection, Phase 19 handoff; disable legacy helpers from authority path.
* `Backend/src/Astronomy.MediaFactory.Core/Phase18VideoAssemblyAuthority.cs` (new) — manifest, media evidence, policies, publication result, reason codes.
* `Backend/src/Astronomy.MediaFactory.Rendering/SmoothMotionRenderer.cs` — typed Static/keyframe/easing interpretation or reusable interpolation functions.
* potentially a focused new rendering component under `Backend/src/Astronomy.MediaFactory.Rendering/` to move embedded Phase 18 FFmpeg mechanics out of the giant service without creating a second pipeline.
* `Backend/src/Astronomy.MediaFactory.Contracts/Contracts.cs` — explicit Phase 18 codec/render options if contracts are configured there.
* API/worker composition/config schema files only to register explicit versioned policy defaults (not to alter Phases 15-17).
* `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs` — replace obsolete duration/visual/motion tests with authority integration tests.
* `Backend/tests/Astronomy.MediaFactory.Tests/MotionRenderingTests.cs` — Static, all motion types, exact endpoints/keyframes/easing, unknown rejection.
* new `Phase18VideoAssemblyAuthorityTests.cs` and transactional/physical renderer tests.
* Phase 19 tests and governing architecture/pipeline/output-contract documents that currently require legacy paths/outro behavior.

Do not modify Phase 15/16/17 production semantics. Only shared DTO reading may be reused; upstream files must remain byte-identical.

## 26. Governing-document changes needed

Update phase registry/output-contract, media-pipeline, validation, folder-structure, localization, and Phase 19 QA documents to state: numbered authority paths; exact upstream lineage; language/requested-format transaction; subtitle policy; codec/render identity; silent-tail semantics; stable failure codes; and compatibility projections. Remove requirements for five/nine scenes, narration-plus-outro duration, universal motion/crossfade, and arbitrary legacy folder discovery.

## 27. Risks and controls

* **`-shortest`/`-t` truncation:** prohibit `-shortest`; never apply `atrim/-t` to speech below probed authoritative audio; validate last narration and full video duration.
* **Legacy override:** make strict typed job construction the only canonical path; no loose JSON fallback.
* **Transition overlap:** current xfade changes totals. Derive overlap only from authority and validate timeline equation.
* **A/V drift/frame rounding:** use integer milliseconds/rational FPS, deterministic frame allocation, cumulative-boundary checks, one-frame tolerance.
* **Subtitle Unicode/fonts:** package a Unicode Devanagari-capable font, validate libass/font discovery, test Hindi shaping and safe area.
* **Quoting/paths:** retain `ProcessStartInfo.ArgumentList`; separately test subtitle filter escaping, apostrophes, colons, spaces, Unicode, Windows paths.
* **600-second Long cost:** deterministic per-scene cache may optimize only after authority validation; transaction and cancellation remain mandatory.
* **Partial failure/cleanup:** candidate staging and atomic swap; never write canonical outputs during rendering; observable cleanup.
* **Codec compatibility/reproducibility:** explicit H.264/AAC/yuv420p policy and encoder evidence; policy/toolchain changes invalidate reuse.
* **Re-render expense:** identity-based reuse with complete committed readback, never output existence alone.
* **Audio transcoding:** avoid unnecessary MP3 concatenation; declare allowed AAC transcode and validate timing/content identity lineage.
* **Language overwrite:** language-root transaction only.
* **Phase 19 regression:** migrate Phase 19 in the same delivery or retain a post-commit compatibility projection temporarily.

## 28. Certification Definition of Done

Phase 18 is certified only when all are true:

1. standalone Phase 18 consumes valid frozen numbered Phase 15/16/17 and never invokes upstream generation;
2. upstream byte hashes are identical before and after execution;
3. lineage and row identity are checked and mismatch tests fail closed;
4. exact Phase 17 visual path/hash/dimensions are used, with no resolver fallback;
5. exact Phase 17 motion, transforms, keyframes, easing, safety decision, and transitions are interpreted; Static is truly static;
6. exact Phase 17/Phase 16 duration/windows/order are used; no audio/SRT/fixed/count re-derivation occurs;
7. Phase 15 audio is bound by `SceneAudioUnitId`, untrimmed, and any calibrated tail is silence;
8. Phase 16 final SRT is hash-validated and used unchanged; sidecar is published and selected burn/mux policy is verified;
9. current 4 Short/12 Long scenes render in `Sequence` order;
10. Short is 120,000 ms and Long is 600,000 ms within explicit one-frame/container tolerance, not narration length;
11. final MP4s pass existence/size/ffprobe/streams/frame count/resolution/FPS/H.264/yuv420p/AAC/audio metadata/duration checks;
12. narration ending early cannot truncate calibrated video; no `-shortest` failure is possible;
13. no ungoverned crossfade, zoom/pan, intro/outro, music, noise, hero, thumbnail, or gallery content is added;
14. requested formats publish atomically under the language-scoped numbered authority; prior authority survives every injected failure;
15. overwrite/reuse/invalidation and partial-format semantics pass tests;
16. manifest, distinct authority checksum, candidate/readback/committed validation, stable reason code, and `downstreamReady=true` are coherent;
17. generated flags and final paths derive only from accepted committed outputs;
18. staging is empty after success/failure and compatibility copies occur only post-commit;
19. Phase 19 consumes deterministic manifest-declared Phase 18 files and no arbitrary video folder;
20. English/Hindi subtitle/font/path fixtures and both Short/Long physical integration fixtures pass.

## 29. Remaining uncertainties requiring explicit product decisions

1. Whether canonical Long remains repository-native 1280x720 or becomes 1920x1080. Current code/config is unequivocally 1280x720; changing it is policy, not audit cleanup.
2. Whether delivery policy is `SidecarOnly` or `BurnInAndSidecar` per platform/language.
3. Exact explicit H.264 CRF/profile/level/preset and AAC bitrate/rate/channel values.
4. Whether future non-cut transitions are authored in Phase 17 or another frozen editorial authority; current Cut/0 is unambiguous.
5. Whether Phase 19 gates requested-output completion or is a separate publication approval.
6. Whether compatibility combined narration/video projections are still required after Phase 19 migrates.
7. The exact location convention for per-language upstream validation files should be read from each frozen manifest rather than guessed.

None of these uncertainties blocks initial certification of current Orion under Static, Cut/0, no ungoverned music/outro, 1080x1920 Short, and repository-native 1280x720 Long.

## 30. Final recommendation

Do **not** build another video pipeline. Introduce one strict Phase 18 authority adapter and transaction around the existing physical FFmpeg mechanics. Preserve process execution, image-to-clip filtering primitives, concat, audio padding/mux, subtitle burn, probes, diagnostics concepts, and cleanup structure. Remove legacy visual/duration/audio/subtitle/motion/transition/editorial decisions from the active authority path. For the frozen Orion package, the first certified renderer can be deliberately narrow—Static + Cut/0 + per-scene narration/silence + exact Phase 16 SRT—while retaining typed fail-closed extension points for governed future motion and transitions.
