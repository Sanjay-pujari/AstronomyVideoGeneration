# Phase 16 Duration Calibration V1 — authority audit

**Audit date:** 2026-08-10  
**Audited implementation:** current HEAD, `ProductionPipelineExecutionService.PhaseDurationCalibrationV1Async`  
**Scope:** evidence and recommended design only. This audit changes no production code, tests, configuration, speech, video, or authority output.

## Executive answer

No: current Phase 16 does **not** consume the governed Phase 14 and Phase 15 packages directly. It reads legacy `sync/scene-audio-sync.json`, legacy `tts/{language}/tts-timeline.json` (with an English unscoped fallback), and scene-assets metadata. It never opens `14-audio-sync/*`, `15-tts/{language}/*`, either upstream manifest/publication report, or either upstream validation file.

No: Phase 15's `ActualAudioDurationMs` is not the primary duration authority today. Phase 16 resolves each compatibility cue audio path and ffprobes it; only when probing fails does it use timeline duration. It also probes the assembled narration track as a consistency check.

Phase 16 never invokes TTS, changes voice/rate, trims audio, or assembles video. It sets every populated scene duration exactly equal to the sum of its resolved cue audio durations (zero padding); planned visual duration and the nominal `maximumSceneDurationSec` arguments are ignored. This can shorten or lengthen visuals downstream without touching audio.

Phase 16 writes an unnumbered, shared `timing/scene-duration-plan.json`, two validation files, and—**English only**—overwrites `narration/subtitles/en/{short,long}.srt`. Hindi returns before subtitle regeneration, so it retains earlier draft timing. The current SRT is rebuilt from timeline `CueText`, is resegmented, and allocates time by configured cue weights; it is not a timing-only projection of Phase 14 `SubtitleSegment`s. Thus Phase 16 claims final timing but is neither governed nor bilingual.

The mature algorithms worth adapting are cumulative, non-overlapping SRT time allocation and audio-driven scene extension. Certification requires adapters to the frozen Phase 14/15 authorities, authority-driven counts, language-scoped transactional publication, and timing the existing Phase 14 segments without rewriting them—not another synthesis or video engine.

---

## 1. Current call graph

All methods below are in class `ProductionPipelineExecutionService`, file `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`, unless stated otherwise.

| Concern | Active method and exact lines | Finding |
|---|---|---|
| Registry/entry | phase registry lines 431; `PhaseDurationCalibrationV1Async`, 10622-10787 | Registry delegates phase 16 directly to the private method. |
| Phase 14 input loading | entry 10633, 10643, 10648 | Checks only legacy `sync/scene-audio-sync.json`; it never parses it. No governed Phase 14 load exists. |
| Phase 15 input loading | `ResolveLanguageScopedTtsTimelinePath`, 9726-9733; entry 10634, 10647, 10660-10669 | Reads compatibility `tts/{lang}/tts-timeline.json`; English may fall back to `tts/tts-timeline.json`. No `15-tts` load exists. |
| Legacy visual timing load | entry 10635-10636, 10649-10650; `AssignCueTimelineItemsToVisualScenesAsync`, 10877-10897 | Reads `scene-assets-v3/{short,long}/scene-timeline-metadata.json`; expected durations are used only to distribute unmatched cues, not duration calculation. |
| Timeline normalization | `ReadCanonicalTtsTimelineItemsWithActualDurationsAsync`, 10795-10806; `ReadCanonicalTtsTimelineItems` family, 8848-8951 | Normalizes several old cue/scene timeline shapes. |
| Audio path/duration resolution | `ResolveCueAudioDurationAsync`, 10808-10817; `ResolveCueAudioPath`, 10820-10832 | Physical ffprobe wins; timeline seconds are fallback. Legacy `tts` paths are synthesized as fallback candidates. |
| Scene mapping | `AssignCueTimelineItemsToVisualScenesAsync`, 10877-10955 | Exact/normalized scene ID, then numeric prefix; unmatched cues are proportionally assigned using planned visual duration (or equal scene fractions). |
| Scene duration calculation/visual adjustment | `BuildSceneDurationPlanItemsAsync`, 10850-10871 | `audio = round(sum(max(0,cue duration)))`; `scene = audio > 0 ? audio : 0.5`. The 12/15 second maximum arguments are unused. |
| Combined-track verification | `ProbeNarrationTrackDurationAsync`, 10789-10793; caller 10687-10692 | Optional ffprobe of `video-assembly/{lang}/{format}/narration-track.mp3`; mismatch over 0.1 seconds fails. It is verification, not scene authority. |
| Plan write | entry 10696-10714 | Writes unnumbered `timing/scene-duration-plan.json` before validation. |
| Subtitle loading/calculation | `RegenerateNarrationSubtitlesFromTtsTimeline`, 11009-11030; `BuildNarrationSrtFromTtsTimeline`, 11032-11058 | English only. Reads compatibility timeline, re-splits `CueText`, weights chunks, accumulates time from zero. It does not parse SRT in this route. |
| SRT text splitting/weighting | `SplitSubtitleChunks`, 8432-8458; `AllocateSubtitleCueDurations`, 8532-8561; `EstimateSubtitleCueWeight`, 8564 onward | Punctuation/whitespace splitting and configured word/character/readability weighting, min/max and gap. This violates target no-resegmentation. |
| SRT formatting/write | `BuildSrt`, 10058-10059; regeneration 11015-11027 | Writes `narration/subtitles/en/{short,long}.srt` and misnamed `validation/phase-14-final-srt-write-validation.json`. |
| Validation | entry 10647-10650, 10672-10695, 10714, 10717-10785 | Ad-hoc error list; writes diagnostics and validation, then throws on errors. No staged candidate/readback/checksum/publication validation. |
| Result projection | entry return 10785-10786; generic `WritePhaseValidationAsync` projection 15320-15540 | Returns three paths. There is no Phase 16 publication result, reason code, authority checksum, generated/reused/regenerated state, or governed readiness projection. Generic flags do not recognize Phase 16. |
| Phase 17 consumer | `PhaseMotionLayerV1Async`, 11062 onward; inputs 11072-11107 | Reads shared `timing/scene-duration-plan.json`; uses `SceneDurationSec`, `AudioPath`, motion recommendation. |
| Phase 18 consumer | `PhaseVideoAssemblyV1Async`, 12551 onward; inputs 12560-12639; `ReadVideoAssemblyItems`, 13278 onward | Reads compatibility TTS, shared duration plan, motion plan, legacy SRT and narration tracks; can independently expand duration from TTS again. |

The nominal `syncPath` is existence-only in Phase 16: no `ReadAllText(syncPath)` occurs. Consequently current scene mapping actually comes from TTS timeline plus scene metadata, not the sync document it claims as input.

## 2. Current responsibilities

| Behavior | Actual current behavior |
|---|---|
| Scene duration calibration | **Yes.** Sum resolved audio per mapped visual scene. |
| Audio measurement | **Yes.** ffprobe each resolved MP3; probe combined track optionally. |
| Audio duration reuse | **Fallback only.** Timeline duration is used only if probe is unavailable/zero. |
| SRT parsing | **No in Phase 16 active route.** Parsing helpers exist for Phase 15/Hindi compatibility elsewhere. |
| SRT regeneration/cue retiming/final SRT | **English only.** Rebuilds and overwrites two language-scoped SRTs. |
| Subtitle text resegmentation | **Yes.** Splits timeline `CueText`; does not preserve Phase 14 segment identity. |
| Scene extension | **Indirectly yes.** Audio duration replaces visual duration; downstream renders use it. |
| Scene trimming | **Visual timing may shrink.** It ignores planned duration when audio is shorter. No media is physically trimmed here. |
| Audio trimming/clipping | **No.** No ffmpeg trim/cut invocation in Phase 16. |
| Speech-rate/voice/provider/TTS regeneration | **No.** No provider call or synthesis path. |
| Pause addition | **No explicit pause.** Actual encoded audio duration is used; transition padding is zero. |
| Motion timing/generation | **No.** It emits recommended motion strings only; Phase 17 creates motion. |
| Assembly/music/ducking/ambient | **No.** Phase 18 owns these. |
| Video assembly/combined-track creation | **No.** Combined track is only probed. |
| Hindi calibration | Scene duration plan **yes for the requested Hindi compatibility timeline**; SRT **no**. |
| Both languages per run | **No.** One requested language; shared timing plan means one language can overwrite the other's durations. |

## 3. Target responsibility boundary and compliance

* **Phase 14 (frozen):** owns `SceneAudioUnit` boundaries, ordered `SubtitleSegment` content and IDs, sentence/character lineage, estimated reading/speech duration, pause intent, and scene mapping. Current Phase 16 bypasses this authority and resegments text: non-compliant.
* **Phase 15 (frozen):** owns exactly one physical audio per unit, checksums and measured duration, synthesis policy, and TTS timeline. Current Phase 16 bypasses its numbered package and creates a competing physical measurement authority: non-compliant.
* **Phase 16:** must own deterministic reconciled scene windows, final timing of the unchanged Phase 14 segments, final bilingual SRT, checksum, transaction, and readiness. Current scene calibration and English SRT direction are useful, but governance, source lineage, identity preservation, and bilingual ownership are absent.
* It is already compliant with prohibitions on narration rewriting, TTS/provider/voice/rate work, audio trimming, motion generation, music, and final assembly.

## 4. Governed Phase 14 inputs

The actual typed contract is `Backend/src/Astronomy.MediaFactory.Core/Phase14AudioSyncAuthority.cs` lines 20-41:

* `SubtitleSegment`: ID, sequence within scene, unit/scene IDs, `SentenceIds`, exact `Text` and checksum, estimated reading milliseconds, rendered lines, optional source-character span, break reason.
* `SceneAudioUnit`: ID, sequence, format/language/scene, narration beat and sentence lineage, exact text/checksum, estimated speech duration, pause before/after, break reason, ordered subtitle segments, voice/style references, crossing policy, source references.
* Streams supply authority-driven narrated scene/unit counts and narration checksums. Root authority supplies language/policy/request lineage, `AuthorityChecksum`, and committed state.

Canonical files published transactionally by `Phase14AudioSyncPublisher` are:

1. `14-audio-sync/narration-cue-plan.json` — load this as the typed authority and source all unit/segment fields above.
2. `14-audio-sync/scene-audio-sync.json` — equivalent governed scene mapping projection; useful for diagnostics/readback, not a second semantic source.
3. `14-audio-sync/phase14-manifest.json` — artifact inventory, checksum, committed publication state.
4. `14-audio-sync/phase14-publication-report.json` — candidate/readback/commit/readiness flags.
5. `validation/phase-14-validation.json` — final semantic/checksum/manifest/committed-state gates.

`Phase14AudioSyncPublisher.cs` lines 77-87 write those governance artifacts and lines 220-239 validate their checksum and flags. The old `sync/scene-audio-sync.json` is compatibility/legacy and must leave the active authority path.

## 5. Governed Phase 15 inputs

Phase 15 constructs each entry at `ProductionPipelineExecutionService.cs` lines 9810-9813 and its timeline at 9836-9869. The actual canonical entry fields are: `SceneAudioUnitId`, `SceneId`, `Sequence`, `Format`, `Language`, `AudioRelativePath`, `AudioByteLength`, `AudioSha256`, `TextChecksum`, `ActualAudioDurationMs`, voice/style/resolved voice/rate/request identity, ordered `SubtitleSegmentIds`, and `SourcePhase14AuthorityChecksum` (record definition lines 12492-12502).

Consume only requested-language files:

1. `15-tts/{language}/tts-timeline.json` — ordered entries and root Phase 14/15 checksums.
2. `15-tts/{language}/phase15-manifest.json` — canonical inventory, validity, commit/readiness, checksums.
3. `15-tts/{language}/phase15-publication-report.json` — candidate/readback/commit/readiness and lineage.
4. `validation/phase-15-validation.json` — final projected certification (currently shared by language, so validate its language and checksum carefully).
5. Physical `15-tts/{language}/{short,long}/{SceneAudioUnitId}.mp3` only for existence/hash/readback or optional duration tolerance verification.

The manifest gate at lines 9967-9990 demonstrates a reusable partial loader but checks only manifest readiness plus audio existence; it is not sufficient by itself for Phase 16.

## 6. Cross-phase lineage validation

Current Phase 16 validates **none** of: Phase 14 publication/readiness/checksum, Phase 15 publication/readiness/checksum, or `Phase15.SourcePhase14AuthorityChecksum == Phase14.AuthorityChecksum`.

Target loader must fail closed before writing anything:

1. Load all canonical files, reject absent/malformed/schema-incompatible files.
2. Require Phase 14 manifest/report/validation committed, read back, semantically/checksum/manifest valid, and downstream-ready; recompute authority checksum.
3. Apply the equivalent checks to requested-language Phase 15 and recompute/verify its distinct checksum.
4. Require the Phase 14 checksum to agree in Phase 14 authority, manifest, report, and validation.
5. Require the Phase 15 checksum to agree in timeline, manifest, report, and validation.
6. Require every Phase 15 root and entry `SourcePhase14AuthorityChecksum` to equal the loaded Phase 14 `AuthorityChecksum` exactly.
7. Join one-to-one by `SceneAudioUnitId`, then verify scene, format, sequence, language, text checksum, ordered subtitle IDs, audio hash/size/path, and counts.

Any mismatch is `P16_LINEAGE_MISMATCH`, never a warning or compatibility fallback.

## 7. Audio-duration authority and legacy classification

| Path/source | Current use | Classification | Target use |
|---|---|---|---|
| `15-tts/{lang}/tts-timeline.json` / `ActualAudioDurationMs` | Not read | **Canonical** | Primary duration source. |
| `15-tts/{lang}/{format}/*.mp3` | Not resolved by current fallback | **Canonical physical evidence** | Existence/hash/readback; optional probe verification only. |
| `tts/{lang}/tts-timeline.json` | Primary Phase 16 input | **Compatibility** | Temporary downstream projection only; never authority. |
| `tts/tts-timeline.json` | English fallback | **Legacy** | Remove from authority path. |
| `tts/{lang}/{short,long}/*.mp3` and synthesized `tts/.../{cueIndex}.mp3` | Primary physical duration candidate | **Compatibility/legacy cue model** | Retain only while 17/18 adapter needs it. |
| `video-assembly/{lang}/{format}/narration-track.mp3` | Optional total-duration probe | **Compatibility combined track** | Optional verification, never per-scene authority. |
| `timing/scene-duration-plan.json` | Current Phase 16 output | **Compatibility** | Project from canonical Phase 16 until 17/18 migrate. |
| `duration-calibration/` | Cleanup catalog claims ownership, implementation never writes it | **Dead/legacy designation** | Replace with numbered root. |

Current primary equation is therefore based on re-probed MP3 seconds, not Phase 15 milliseconds. Recommended target: `ActualAudioDurationMs` is authoritative; an optional ffprobe may fail validation if outside a documented tolerance but must not silently replace the value.

## 8. Scene-duration formula and long/short visual behavior

Current exact equations per scene are:

```text
ActualAudioDurationSec = round_3(sum(max(0, resolvedCueDurationSec)))
FinalSceneDurationSec = ActualAudioDurationSec > 0
    ? ActualAudioDurationSec
    : 0.500
TransitionDurationSec = 0.000
```

`resolvedCueDurationSec = probed MP3 duration` when positive, else timeline duration, else zero. Missing/zero audio is subsequently an error, so the 0.5 fallback cannot yield successful certification. Arguments named `maximumSceneDurationSec` receive 12 (Short) and 15 (Long) but are never read. Planned metadata duration is not in the formula. There are no fixed caps, safe padding, transition overlap, or inter-scene gap.

* **Audio longer than visual:** the output scene duration becomes audio duration, so Phase 17/18 extend the visual render. Audio is untouched.
* **Audio shorter than visual:** the output scene duration also becomes audio duration, so downstream visual timing shrinks. It does not keep the original, add silence, or change motion/speech.
* **No narration trimming:** no current Phase 16 code modifies an MP3 or narration text.

A mature deterministic target should preserve the visual floor explicitly rather than accidentally shorten it:

```text
FinalSceneDurationMs = max(PlannedSceneDurationMs or MinimumVisualDurationMs,
                           ActualAudioDurationMs + RequiredPaddingMs)
```

Repository evidence supports zero narration padding today (`transitionPaddingSec = 0`) and a 500 ms fallback floor, but does **not** establish that 500 ms is the desired product minimum. Make `PlannedSceneDurationMs`, minimum, and padding explicit policy inputs/versioned values; do not reuse the ignored 12/15 arguments as caps. Never truncate audio. Phase 16 should not double-count Phase 14 pauses because Phase 15's measured audio already includes any successfully encoded pause intent.

## 9. Subtitle content and timing algorithm

### Current source and algorithm

Current Phase 16 does not load a legacy SRT as subtitle input. For English it loads compatibility timeline items and uses each item's `CueText`, then **splits it again** on sentence/clause punctuation, whitespace, line length, and max words. This is neither legacy-SRT timing reuse nor Phase 14 segment preservation.

For each timeline item in cue order:

1. `sceneStart = prior item sceneEnd`, initially zero.
2. `sceneEnd = sceneStart + item.AudioDurationSec` (timeline value, not Phase 16's newly probed value).
3. Split `CueText` into chunks.
4. Reserve configured `CueGapMs`; weight chunks using `EstimateSubtitleCueWeight` (not provider marks). Clamp weighted allocations to configured minimum/maximum. If minima cannot fit, share available time equally. Any residual is redistributed while bounds permit.
5. Each chunk starts at the previous chunk end; the last ends exactly at the item audio end. The implementation folds gaps into preceding duration, so emitted cues remain contiguous rather than exposing blank intervals.
6. Next timeline item starts exactly at prior end. Indexes are regenerated 1..N.

Thus current distribution is configured linguistic/reading weight (primarily word/readability estimation), not purely equal, not retained legacy timestamps, and not actual word/sentence boundary events. It may emit a zero-duration cue when input duration is zero and only clamps `end < start`, not `end == start`; Phase 16 does not validate positivity/overlap/text fidelity.

### Target algorithm

Do not split, merge, wrap into new semantic cues, or rewrite Phase 14 segments. For each ordered unit:

* define `SceneStartMs` by cumulative prior `SceneEndMs` and `SceneEndMs = SceneStartMs + FinalSceneDurationMs`;
* define the audible subtitle window as `[SceneStartMs, SceneStartMs + ActualAudioDurationMs]` (or an explicitly versioned bounded tail policy); keep visual padding subtitle-free;
* allocate the ordered Phase 14 `SubtitleSegment`s using their `EstimatedReadingDurationMs` as weights, falling back deterministically to spoken-word count then Unicode text-element count only when estimates are invalid;
* use integer-millisecond largest-remainder allocation so every segment gets at least the configured minimum and the final cue ends exactly at audio end; fail upstream-policy validation if the minimums cannot fit rather than creating zero-duration cues or changing segmentation;
* keep every cue within its parent unit/scene, preserve IDs/order/text/sentence/character lineage, and set `crossSceneSubtitleCueCount = 0`.

Without provider marks this is proportional estimated timing, not word-level synchronization. Record `TimingMethod`, e.g. `Phase14EstimatedReadingDurationWeightedV1`.

## 10. Provider timing metadata availability

Azure synthesis only awaits `SpeakTextAsync`/`SpeakSsmlAsync` and returns `AudioData` (`AzureSpeechClient.cs` lines 26-40, 43-65, 78-94, 124 onward). No `WordBoundary`, sentence-boundary, viseme, bookmark, speech-mark, or synthesis-event subscription is present, and Phase 15 retains none. Therefore Phase 16 cannot claim word-level/provider-event alignment. Capturing such metadata would be a future Phase 15 contract version, not something Phase 16 may invent.

## 11. English, Hindi, and multilingual ownership

| Item | English current | Hindi current | Symmetric target |
|---|---|---|---|
| Subtitle input | compatibility TTS `CueText` | no Phase 16 subtitle input | Phase 14 `SubtitleSegments` |
| Audio duration | requested compatibility timeline plus ffprobe for scene plan; SRT uses timeline seconds | same scene-plan resolution | Phase 15 `ActualAudioDurationMs` |
| Timing | re-split/weighted/cumulative | Phase 16 returns immediately; earlier draft SRT remains | same segment-preserving algorithm |
| Final path | `narration/subtitles/en/{short,long}.srt` | no Phase 16 final output | `16-duration-calibration/{lang}/{short,long}/final.srt` |
| Consumer | Phase 18 sidecar/burn-in; Phase 17 only reports path | Phase 18 consumes draft-looking path | 17/18 canonical adapters plus temporary compatibility projection |

One execution processes only `context.Request.Language`. This is correct for provider/language scoping, but `timing/scene-duration-plan.json` is shared and overwritten, so English and Hindi authorities collide. Canonical root and transaction/cleanup must be `16-duration-calibration/{language}`. An English overwrite must not touch Hindi or its validation identity. If the repository retains a single `validation/phase-16-validation.json`, it must be a language-indexed projection or move canonical validation beneath the language package; otherwise concurrent/sequential languages overwrite certification.

## 12. SRT writer audit

`BuildSrt` emits sequential numeric indexes, `HH:MM:SS,mmm` via `FormatSrtTimestamp`, LF-separated blocks, one trailing LF, and .NET `File.WriteAllText` UTF-8 output. Unicode text, including Devanagari, is supported by the writer. Current generated cues are monotonic and normally contiguous; the final cue ends at audio/timeline end and before any future scene padding. There is no explicit prohibition/validation of overlap, zero length, cross-scene crossing, malformed Unicode, or physical-file checksum.

Configured subtitle options govern max characters/line, max lines, max words/cue, minimum/maximum cue duration, gap, and reading weights (`SplitSubtitleChunks`/`AllocateSubtitleCueDurations`, lines 8432-8561). Phase 16 currently resegments to satisfy these. Target Phase 16 must not split long segments: readability failure belongs to Phase 14 and must fail closed. Minimum duration remains a Phase 16 allocation feasibility gate; maximum is a validation gate, not permission to split/merge.

Other writers still create final-looking SRT: Phase 14 compatibility/draft subtitle generation, Phase 15's legacy Hindi adaptation (`CreateHindiPhase15AdaptationAsync`, 10013-10026), Phase 18's retiming helper (`ProductionPipelineExecutionService.cs` around 13900-13934), and `VideoAssemblyIntelligenceService` around 1632/1781. Active Phase 18 also consumes `narration/subtitles/{lang}`. These are ownership conflicts until disabled from governed paths or explicitly labeled compatibility derived from Phase 16.

## 13. Recommended contracts

### Calibrated scene timeline (`phase16.calibrated-scene-timeline/1.0`)

Root: schema/policy/serializer versions, language, Phase 14 and Phase 15 source checksums, ordered Short/Long streams, totals, Phase 16 authority checksum. Per scene:

* `SceneAudioUnitId`, `SceneId`, `Format`, `Sequence`, `Language`;
* `SourcePhase14AuthorityChecksum`, `SourcePhase15AuthorityChecksum`;
* `PlannedSceneDurationMs`, `MinimumVisualDurationMs`, `ActualAudioDurationMs`, `RequiredPaddingMs`, `FinalSceneDurationMs`;
* `SceneStartMs`, `SceneEndMs`;
* ordered `SubtitleSegmentIds`;
* `CalibrationReason` (`AudioExtendedVisual`, `PlannedVisualRetained`, etc.);
* Phase 15 `AudioRelativePath`, `AudioSha256`, byte length;
* explicit transition/gap fields only if zero/non-owned, not assembly crossfade semantics.

### Subtitle timeline (`phase16.subtitle-timeline/1.0`)

Per cue: `SubtitleSegmentId`, `SceneAudioUnitId`, `SceneId`, `Format`, unit `Sequence`, `SequenceWithinScene`, exact `Text`, `TextChecksum`, `StartMs`, `EndMs`, `DurationMs`, ordered `SentenceIds`, optional `SourceCharacterStart/End`, `TimingMethod`, and both source authority checksums. Root records cue/SRT counts, cross-scene/overlap/invalid-duration counts and stream totals.

Scene starts are deterministic: first zero; every subsequent start equals previous end. No inter-scene gap, transition overlap, or crossfade is currently owned by Phase 16. Keep these in Phase 17/18. Music, ambience, ducking, outro, fade, and final mix remain Phase 18.

## 14. Short and Long streams

Current code hard-codes Short=5 and Long=9 at Phase 16 entry and validation, takes only those scene metadata rows, and passes unused 12/15-second maxima. This conflicts with the current Orion Phase 14 authority (Short=4, Long=12). Target counts/order come only from Phase 14 streams and must match one-to-one with Phase 15 entries. No historical 5/9 assumption may survive in authority, validation, Phase 17 adapter, or Phase 18 adapter.

## 15. Downstream contracts

### Phase 17 Motion

Phase 17 reads `timing/scene-duration-plan.json` and `scene-assets-v3`, then builds `motion/motion-plan.json`. Its item adapter requires scene ID, audio path, `SceneDurationSec`, transition/recommended motion and validates positive duration/audio existence. It hard-codes 5/9 and does not consume scene start/end. It should migrate to the canonical Phase 16 scene timeline and never re-derive durations. Temporarily project the exact old plan schema from Phase 16.

### Phase 18 Assembly

Phase 18 reads:

* `tts/{language}/tts-timeline.json` and groups/re-derives cue durations;
* `timing/scene-duration-plan.json` and motion plan for per-scene image/video duration;
* `narration/subtitles/{language}/{short,long}.srt` for burn-in/sidecar;
* `video-assembly/{language}/{short,long}/narration-track.mp3` for audio placement/mux;
* scene sync and scene assets.

At lines 12610-12639 and 13278 onward it can expand/choose scene duration again from grouped TTS, creating a competing calibration authority. Its diagnostics label SRT timing source Phase 16 at lines 13220-13247 even when Hindi was never retimed. Migrate it to canonical Phase 16 scene and subtitle timelines; preserve the shared plan and narration subtitle paths as explicit compatibility projections until then. Combined tracks may remain an assembly input but never replace per-unit timings.

Transitions/crossfades, outro/fade, background music and ducking are assembly concerns. Phase 16 should publish contiguous logical scene windows; assembly may separately report physical overlap/effective output length.

## 16. Current and recommended artifacts

### Current writes

| Path | Schema/scope | Status | Consumer |
|---|---|---|---|
| `timing/scene-duration-plan.json` | ad-hoc `v2`; Short+Long; requested language data in a shared file | de facto compatibility authority | Phase 17 and Phase 18 |
| `narration/subtitles/en/short.srt` | SRT; English Short | final-looking compatibility output | Phase 18 |
| `narration/subtitles/en/long.srt` | SRT; English Long | final-looking compatibility output | Phase 18 |
| `validation/phase-14-final-srt-write-validation.json` | ad-hoc counts; English | misowned compatibility validation | no governed consumer |
| `validation/phase-16-duration-diagnostics.json` | ad-hoc requested-language Short+Long diagnostics | diagnostics | operators/tests |
| `validation/phase-16-validation.json` | ad-hoc success/error object | result validation | pipeline/API generic machinery |

The method returns only plan, Phase 16 validation, and diagnostics; it omits its SRTs and the Phase-14-named validation from output registration.

### Recommended minimal canonical package

```text
16-duration-calibration/
  en/                         # independently replaceable
    calibrated-scene-timeline.json
    subtitle-timeline.json
    short/final.srt
    long/final.srt
    phase16-manifest.json
    phase16-authority-diagnostics.json
    phase16-publication-report.json
  hi/
    ...same files...
validation/
  phase-16-validation.json    # compatibility/API projection; language-aware
```

Do not add separate redundant Short/Long JSON timelines: one typed file with two streams matches Phase 14/15 practice. Manifest records SHA-256, byte length and semantic role for both physical SRTs and timeline files. Phase 16 authority checksum is distinct and computed from upstream checksums, language, ordered calibrated scenes/cues, policies/schema/serializer identity, and physical SRT checksums (excluding self-referential fields).

Temporary projections, written only after canonical commit/readback, are `timing/scene-duration-plan.json` and `narration/subtitles/{language}/{short,long}.srt`. They are never checksum inputs or certification prerequisites.

## 17. Transaction, cleanup, standalone, reuse

### Current

There is no staging, candidate validation/readback, atomic rename, backup/rollback, committed readback, manifest, publication report, authority checksum, reuse decision, or `downstreamReady`. Files are written directly; a late failure leaves partial output. Cleanup catalog incorrectly declares `duration-calibration/` as Phase 16-owned (`Phase1Authority.cs` line 171), although active output is in shared `timing`, `narration`, and `validation`. It does not declare the actual files, so overwrite behavior is incoherent. Phase 16 itself never deletes Phase 14/15, but outer cleanup is not yet correctly scoped for its real writes.

A standalone 16–16 run can execute only if legacy sync/TTS/metadata/track projections remain. It cannot run solely from committed numbered Phase 14+15 authority, and it cannot process Orion 4/12 because it demands 5/9.

### Target

* Publisher owns only `16-duration-calibration/{requestedLanguage}` and its language-aware validation projection; compatibility file ownership must be exact-file scoped.
* Clean stale staging and its own backup safely; stage under `16-duration-calibration/.staging/{transaction}/{language}`, validate/read back, atomically replace only that language, rollback on failure, validate committed bytes, then project compatibility.
* Never delete/write `14-audio-sync`, `15-tts`, or any Phase 1-15 root. Hash both upstream trees before/after standalone Phase 16 and require byte identity.
* Authority identity includes Phase 14/15 checksums, language, ordered unit IDs, audio duration/hash/size, ordered segment IDs/text checksums/lineage, calibration and subtitle policy versions, padding/floor values, schema/serializer version, and SRT serialization version.
* Reuse only if committed package, checksums, physical files, policies and all readback flags validate. Otherwise regenerate atomically. Changes to either upstream, audio/hash/duration, segments, language, policy or schema invalidate reuse.

## 18. Failure codes and validation

Current errors are free-form strings for missing timeline/sync/metadata, fixed count mismatch, missing duration, nonpositive audio, scene shorter than audio, narration-track delta >0.1 seconds, missing plan, old path (dead constant), or final SRT block-count mismatch. Success has no reason code.

Recommended codes:

* `P16_UPSTREAM_PHASE14_INVALID`
* `P16_UPSTREAM_PHASE15_INVALID`
* `P16_LINEAGE_MISMATCH`
* `P16_AUDIO_DURATION_INVALID`
* `P16_SCENE_MAPPING_INVALID`
* `P16_SUBTITLE_TIMING_INVALID`
* `P16_CANDIDATE_VALIDATION_FAILED`
* `P16_COMMIT_FAILED`
* `P16_COMMITTED_READBACK_FAILED`
* `P16_DURATION_AUTHORITY_ACCEPTED`

Final gates:

1. Both upstream packages are committed, readback/checksum/manifest/semantic valid and downstream-ready; lineage checksums match.
2. Counts and ordered IDs match; every Phase 14 unit maps exactly once to one Phase 15 entry and every entry maps back.
3. Every audio duration is positive; physical path stays under requested-language `15-tts`, byte/hash metadata agree; optional probe agrees within versioned tolerance.
4. Every calibrated duration is positive and at least actual audio plus required padding; starts/ends are contiguous and totals agree.
5. Every Phase 14 subtitle segment is timed exactly once, in order, with unchanged ID/text/checksum/lineage; no extras, gaps in identity, overlap, zero/negative duration, or cue outside parent scene; `crossSceneSubtitleCueCount=0`.
6. Short/Long counts are authority-driven; both en and hi pass the same architecture (in independent runs).
7. SRT indexes/timestamps/text/order parse back exactly; UTF-8 physical bytes, SHA-256 and manifest agree; final cue policy agrees with timeline.
8. Candidate readback passes before commit; publication commits atomically; committed readback passes; `downstreamReady=true` only when every gate passes.

No narration-track equality should be mandatory unless concatenation encoding tolerance is documented; MP3 concat/probe may differ slightly from summed units. Treat it as verification diagnostics.

## 19. Result projection and aggregate flags

Current Phase 16's ad-hoc validation exposes status, selected legacy paths, boolean calibration claims, `validationPassed`, and errors. It lacks `reasonCode`, generated/reused/regenerated, publication/readback, authority checksum, validation/manifest statuses, semantic/checksum/manifest flags, and `downstreamReady`. Generic `WritePhaseValidationAsync` only projects governed publication certification through Phase 15 (lines 15320-15540), so Phase 16 cannot be canonically certified through the API.

Add a typed `Phase16PublicationResult` analogous to Phase 14/15 and make one source project validation/API fields. `durationCalibrationGenerated`/`subtitleGenerated` may summarize output but must derive from accepted publication state. Existing `shortVideoGenerated`/`longVideoGenerated` are Phase 18 bookkeeping and must not influence Phase 16. No aggregate flag may turn a failed/reused/partial compatibility projection into canonical success.

## 20. Test inventory and gaps

All explicit Phase 16 tests are in `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs`:

| Test (line) | Behavior | Classification |
|---|---|---|
| `Phase16SubtitleRegeneration_UsesCueLevelTtsTimelineDurations` (1356) | invokes private regeneration; checks English scoped path/text/cumulative timestamps | **Obsolete authority assumption; retain only as compatibility adapter test** |
| `Phase16SceneDurationPlan_GroupsCueLevelTtsDurationsByScene` (1405) | invokes private planner; normalized scene grouping and sums cue durations | **Reuse algorithm only / compatibility fixture** |
| `Phase16CueTimelineDiagnostics_CountsAllCueLevelTtsItems` (1462) | counts/sums nested legacy cue timeline shapes | **Compatibility only** |

Adjacent tests at 876/901 test `ResolvePhase15SrtPath` language scoping, not Phase 16 certification. Subtitle splitter tests around 1747-1860 test the shared resegmentation utility, not preservation of Phase 14 segments.

There are no Phase 16 integration tests for the entry point, governed Phase 14/15 loading, checksum lineage, Phase 15 duration authority, audio longer/shorter than planned, deterministic padding/floor, Orion 4/12 counts, Hindi final SRT, bilingual isolation, scene/cue windows, no upstream mutation, cleanup, transaction/rollback/readback, reuse/invalidation, SRT checksum/Unicode, or result projection.

Required certification tests:

* parameterized en/hi and Short/Long authority fixtures; assert final SRT resides only in Phase 16 canonical root and draft Phase 14 timing is never consumed as final;
* different Phase 14 estimated vs Phase 15 actual durations to prove actual duration controls timing;
* longer and shorter audio policy tests, no trim/synthesis/provider calls;
* every segment identity/text/lineage preserved exactly and once, last cue ends at audio end, visual padding is cue-free, no crossings/overlaps/nonpositive durations;
* authority-driven 4/12 plus arbitrary counts; stable ordering;
* transactional failure injection, rollback, committed readback, checksum/SRT tamper detection;
* en overwrite preserves hi byte-for-byte and vice versa;
* hash every Phase 14/15 canonical file, execute standalone overwrite 16–16, rehash identical; also snapshot all Phase 1-15 roots and assert only Phase 16 roots/projections changed;
* reuse and invalidation matrix; typed API fields and aggregate flags.

Tests requiring legacy TTS paths/cue timelines and English-only recalibration are obsolete as authority tests. None currently asserts fixed 5/9 directly at entry because entry is not invoked, but fixtures and private calls encode the old model. Treat them as compatibility until 17/18 migration, then remove.

## 21. Governing-document changes (future implementation)

`Architecture/RC2-Phase-Output-Contract-v1.0.md` lines 661-690 still describes Phase 16 as “Manifest / Packaging,” conflicting with the active registry and `docs/architecture/PipelineArchitecture.md`. Update it to Duration Calibration and the numbered package. Update `docs/architecture/PipelineArchitecture.md` and `docs/FolderStructure.md` to state the bilingual authority boundary, numbered paths, compatibility projections, and Phase 17/18 migration. Record a short ADR for timing/padding/allocation and transition ownership. No governing document besides this audit is changed now.

## 22. Current-versus-target matrix

| Dimension | Current Phase 16 | Target Phase 16 |
|---|---|---|
| Input authority | legacy sync/TTS + scene metadata | validated committed Phase 14+15 packages |
| Audio duration | ffprobe legacy cue MP3; timeline fallback | Phase 15 `ActualAudioDurationMs`; probe verification only |
| Scene count | hard-coded 5/9 | Phase 14 authority-driven (currently 4/12) |
| Subtitle text | compatibility TTS `CueText`, re-split | unchanged Phase 14 segments |
| Subtitle timing | weighted regenerated chunks from timeline seconds | deterministic timestamps on existing segment IDs |
| English | scene calibration + SRT | full canonical authority |
| Hindi | scene calibration; no SRT rewrite | same architecture as English |
| Final SRT | English legacy path; Hindi draft remains | only canonical under language-scoped Phase 16 |
| Scene formula | `audio sum`, else 0.5; no planned floor/padding | versioned `max(planned/floor, actual + padding)` |
| Audio trimming | none | forbidden/tested |
| TTS regeneration/rate | none | forbidden/tested |
| Motion | recommendation string only | no generation; publish duration for 17 |
| Assembly/music/transitions | none; combined track verification | out of scope |
| Artifacts | shared unnumbered plan, ad-hoc validation, English SRT | numbered language package + temporary projections |
| Transaction | direct writes | stage/validate/atomic replace/rollback/readback |
| Cleanup | catalog points at unused root; real paths unclear | requested-language canonical root only; exact projections |
| Reuse | none | checksum/policy identity and validated reuse |
| Result | paths + ad-hoc flags | typed reason/state/checksum/gates/readiness |

## 23. Code reuse classification

| Method/component | Classification | Rationale |
|---|---|---|
| `PhaseDurationCalibrationV1Async` | **REUSE WITH PHASE14/15 ADAPTER** structurally | Keep orchestration role; replace inputs/counts/publication/projection. |
| `BuildSceneDurationPlanItemsAsync` | **REUSE ALGORITHM ONLY** | Audio grouping/non-trim direction useful; formula and types must change. |
| `AssignCueTimelineItemsToVisualScenesAsync` | **REMOVE FROM ACTIVE AUTHORITY PATH** | Frozen unit IDs make heuristic matching/proportional fallback unsafe. |
| `ResolveCueAudioDurationAsync` / `ResolveCueAudioPath` | **RETAIN COMPATIBILITY ONLY** | Legacy paths/probe precedence conflict with Phase 15 authority. |
| `ProbeNarrationTrackDurationAsync` | **REUSE AS-IS** only as optional diagnostics | Never gate canonical scene truth without tolerance policy. |
| `RegenerateNarrationSubtitlesFromTtsTimeline` | **OBSOLETE** | English-only, wrong source, wrong owner path/name. |
| `BuildNarrationSrtFromTtsTimeline` | **REUSE ALGORITHM ONLY** | Cumulative clock concept useful; timeline item/chunk model is not. |
| `SplitSubtitleChunks` in Phase 16 | **REMOVE FROM ACTIVE AUTHORITY PATH** | Phase 14 already owns segmentation. |
| `AllocateSubtitleCueDurations` | **REUSE ALGORITHM ONLY** | Weight/bounds ideas useful, but operate on Phase 14 segment estimates and integer ms. |
| `BuildSrt` / timestamp formatter | **REUSE AS-IS WITH STRICT READBACK** | Deterministic LF SRT formatting is suitable; add UTF-8/checksum/parser validation. |
| `ResolveLanguageScopedTtsTimelinePath` | **RETAIN COMPATIBILITY ONLY** | Points to unnumbered TTS and English fallback. |
| Phase 14 publisher transaction/readback patterns | **REUSE WITH PHASE16 TYPES** | Established governance model. |
| Phase 15 language-scoped stage/backup/commit patterns | **REUSE WITH PHASE16 TYPES** | Correct ownership shape; add robust rollback/readback tests. |

## 24. Minimal implementation plan

1. Define Phase 16 scene/subtitle/manifest/publication/result contracts and policy versions; decide visual floor/padding and integer allocation feasibility explicitly.
2. Implement a read-only loader validating all committed Phase 14 artifacts and requested-language Phase 15 artifacts, checksums/readiness and cross-phase lineage before any write.
3. Bind exact `SceneAudioUnitId` one-to-one; reject heuristics, unmatched/duplicate units, changed text/segment IDs, and counts.
4. Use `ActualAudioDurationMs` as primary; verify physical audio path/hash/size and optionally probe within tolerance without replacing authority.
5. Calculate deterministic contiguous scene windows using planned/floor/audio+padding policy; never trim or synthesize.
6. Allocate times to unchanged ordered Phase 14 segments, create subtitle timeline, and serialize/read back Short/Long SRT for requested language.
7. Compute distinct Phase 16 checksum and transactionally publish/rollback/read back `16-duration-calibration/{language}` with typed diagnostics/manifest/report and readiness.
8. Project old duration-plan and narration SRT paths after commit for Phase 17/18; label them compatibility and remove independent recalibration from downstream.
9. Add identity reuse/invalidation and correct typed validation/API/result projection.
10. Certify en and hi, arbitrary authority-driven counts, no Phase 1-15 mutation, overwrite isolation, tamper/failure paths, and downstream adapters.

## 25. Expected files to change in implementation (not changed by this audit)

Exact existing files expected:

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` — route, loaders/adapters, compatibility projections, downstream adapters/result projection.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase1Authority.cs` — correct Phase 16 language-scoped cleanup catalog.
* `Backend/src/Astronomy.MediaFactory.Core/Phase14AudioSyncAuthority.cs` — preferably **do not modify frozen Phase 14 types**; place `Phase16PublicationResult` in a new Phase 16 contract file instead.
* `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs` — migrate compatibility tests and add integration/result tests (focused new test files are preferable).
* `Architecture/RC2-Phase-Output-Contract-v1.0.md`, `docs/architecture/PipelineArchitecture.md`, and `docs/FolderStructure.md` — correct governing contract and paths.

Recommended new files (names may follow repository conventions discovered during implementation):

* `Backend/src/Astronomy.MediaFactory.Core/Phase16DurationCalibrationAuthority.cs`.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase16DurationCalibrationPublisher.cs` (or loader/publisher split).
* `Backend/tests/Astronomy.MediaFactory.Tests/Phase16DurationCalibrationAuthorityTests.cs`.

Do not change Phase 14/15 production semantics, authority bytes, speech generation, voice policy, or frozen tests except where a downstream test is explicitly reclassified as compatibility.

## 26. Risks

1. Phase 17/18 hard-code 5/9 and legacy paths; abrupt removal breaks render flows.
2. Shared plan/validation and English-only SRT create cross-language overwrite/certification hazards.
3. No provider boundaries exist; proportional segment timing is approximate and must not be marketed as word sync.
4. Combined MP3 duration can differ from summed units due to containers/encoder delay; do not elevate track probing.
5. Planned visual duration provenance is presently legacy scene metadata and needs an explicit authoritative field/policy.
6. Transition crossfade changes physical video length; mixing it into Phase 16 would make two timing authorities.
7. SRT must preserve Hindi Unicode, deterministic UTF-8/LF and integer rounding without zero cues.
8. Compatibility writers after Phase 16 can overwrite final-looking SRT unless ownership is enforced.
9. Reuse identity can miss policy/serializer/text changes unless all listed inputs are covered.
10. The phase output contract currently assigns a different responsibility to Phase 16; governance must be reconciled before certification.

## 27. Certification criteria / Definition of Done

Phase 16 is certified only when all are demonstrably true:

* standalone 16–16 consumes only committed, valid numbered Phase 14 and requested-language Phase 15 authority;
* every Phase 15 lineage reference names the exact loaded Phase 14 checksum;
* arbitrary authority-driven counts—including current Orion Short=4, Long=12—are preserved;
* Phase 15 measured duration is timing truth and optional probe cannot silently supersede it;
* no TTS/provider/rate/voice call, narration/audio mutation, trim, clip, split, merge, or text rewrite occurs;
* deterministic final scene duration is positive and at least physical/authoritative audio plus policy padding;
* each Phase 14 subtitle segment receives exactly one positive, ordered timestamp, unchanged text/lineage, within its parent scene; no overlap or cross-scene cue;
* English and Hindi produce the same canonical package architecture independently; neither treats Phase 14 draft SRT as final;
* Short/Long SRT parse-back, UTF-8 bytes and SHA-256 match manifest/timelines;
* language-scoped staging, validation, atomic commit, rollback and committed readback work; only then `downstreamReady=true`;
* Phase 14/15 and all other Phase 1-15 files remain byte-identical under standalone overwrite; the other language's Phase 16 package remains byte-identical;
* reuse/invalidation and generated/reused/regenerated state are correct;
* typed result exposes reason code, publication/readback/checksum and semantic/checksum/manifest/readiness flags correctly;
* Phase 17 consumes Phase 16 duration rather than deriving it, and Phase 18 consumes Phase 16 timing/SRT rather than rewriting it (or verified compatibility projections during migration).

## 28. Remaining uncertainties requiring an explicit policy decision

* What is the authoritative planned/minimum visual duration source after scene metadata migration, and is 500 ms intended or merely a failure fallback?
* Is required tail padding zero, or should a fixed subtitle/audio-safe tail exist? If nonzero, should the last cue stop at audio end (recommended) or extend into it?
* What ffprobe-versus-Phase-15 tolerance, if any, is blocking given MP3 encoder delay?
* Should canonical Phase 16 validation live inside each language package with a shared API projection, or should `validation/` contain one file per language? Language collision must be solved either way.
* When may legacy Phase 18 SRT retiming and shared path projections be removed?

## 29. Final recommendation

**Preserve and adapt; do not build another timing engine.** Reuse cumulative timing, bounded weighted allocation concepts, deterministic SRT serialization, and the non-trimming audio-driven visual extension behavior. Replace heuristic legacy inputs with strict Phase 14/15 adapters; make Phase 15 milliseconds authoritative; time the already-defined Phase 14 segments; publish a distinct transactional, language-scoped Phase 16 package; and maintain old paths only as derived adapters while Phase 17/18 migrate.

Direct answers to the audit completion questions:

1. **Consumes new authorities directly?** No.
2. **Phase 15 duration is current truth?** No; physical reprobe wins. It should become truth.
3. **Regenerates/trims TTS?** Neither; it only probes audio. Visual duration can shrink/extend.
4. **Owns final timed SRT?** It attempts to for English only, in a legacy path; not governed and not Hindi.
5. **English/Hindi symmetric?** No.
6. **Exact current timing?** Per timeline item, cumulative from zero; re-split text; configured weighted/clamped allocation; last chunk ends at timeline audio end.
7. **Exact numbered ownership?** `16-duration-calibration/{language}` containing one calibrated scene timeline, one subtitle timeline, Short/Long `final.srt`, manifest, diagnostics and publication report, plus a language-safe validation projection.
8. **Phase 17/18 consumption?** Phase 17 reads shared duration plan; Phase 18 reads that plan plus compatibility TTS, SRT and narration track and may independently expand durations.
9. **Can mature logic be adapted?** Yes—the clock/allocation/formatting concepts are adequate once governed inputs, segment identity, policy, transaction, bilingual symmetry and downstream ownership are corrected.
