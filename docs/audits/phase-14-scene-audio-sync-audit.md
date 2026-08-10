# Phase 14 Scene Audio Sync audit

**Audit date:** 2026-08-10  
**Scope:** static repository audit only; no pipeline execution, speech synthesis, provider call, or production/config/test change.  
**Verdict:** **adapt the mature scene-level algorithm behind an authority adapter; do not replace it and do not add another audio engine.** The registry name is materially misleading: current Phase 14 is also an unfrozen narration writer, translator, subtitle generator, and draft-duration writer. It does not call TTS. Phase 15 is the physical-TTS owner, but it still derives request units from narration files/SRT rather than consuming a governed Phase 14 cue-plan contract.

## 1. Current Phase 14 call graph

The active RC2 registry maps 14 to `ProductionPipelineExecutionService.PhaseSceneAudioSyncAsync` and 15 to `PhaseGenerateTtsTimelineV1Async` (`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs:409-431`). There is **no Phase 14 service/interface boundary**: all active work is private methods in `ProductionPipelineExecutionService`.

| Stage | Exact route |
|---|---|
| Entry | `ProductionPipelineExecutionService.PhaseSceneAudioSyncAsync` (`.../ProductionPipelineExecutionService.cs:4400`) |
| Scene input resolution | `BuildSceneAudioSyncItemsAsync` (`:9311-9372`) reads `scene-assets-v3/{short,long}/visual-timeline-v3.json`, `scene-manifest-v3.json`, `scene-review-v3.json`, and `scene-timeline-metadata.json` (`:9313-9327`), then selects scene ID/image/visual intent/render mode/estimated duration (`:9333-9366`). |
| Narration loading/generation | Entry calls `BuildV31ProductionNarrationAsync` (`:4435-4455`, implementation `:6961`), applies it with `ApplyDocumentaryNarrationToSyncItems` (`:4457-4458`, `:6932`), and therefore does not load Phase 7 accepted candidates. |
| Output-layer planning | `WriteNarrationOutputLayerAsync` (`:4459`, `:4713`) deletes/recreates language narration folders, writes scene text and manifests, obtains/generates a draft duration plan, and creates SRT through `BuildNarrationSrtFromCleanFiles` (`:4752-4754`, `:7734`). |
| Sync calculation | `BuildSceneAudioSyncItemsAsync` makes a positional 5/9 mapping and copies estimated scene duration; `SplitSubtitleChunks` (`:8414`) plus `AllocateSubtitleCueDurations` (`:8514`) allocate subtitle time within each draft scene duration. There is no audio-aware sync calculation. |
| Artifact writing | `WriteNarrationTextFilesAsync` (`:7428`); `WriteNarrationOutputLayerAsync`; direct `scene-audio-sync.json` write (`:4513-4538`); direct validation/diagnostic writes (`:4542` onward). |
| Validation | Input existence/count checks (`:9313-9323`); mapping/image/text/duplicate/count checks (`:4497-4511`); narration/SRT checks in `ValidateV31NarrationBeforeSrt` (`:4678`), `ValidatePhase14LocalizedNarrationArtifacts` (`:4966`), and `BuildNarrationSrtFromCleanFiles` (including duplicate failure at `:7886`). |
| Result projection | The method returns file paths. The generic phase executor projects the ordinary `PhaseExecutionResult`; Phase 14 has no dedicated publisher/result mapper or P14 result contract. |

## 2. Current responsibilities (code, not name)

| Capability | Actual Phase 14 behavior |
|---|---|
| Scene-to-narration mapping | Yes: fixed ordered 5 short/9 long scene maps, marked `Matched` before narration exists (`:9333-9366`). |
| Narration beat sync | Only nominal scene/beat association; no word/audio alignment. |
| Audio segment planning | No provider-ready audio cue package. Scene narration files incidentally become Phase 15 request units. |
| Subtitle/SRT sync | Yes, authoritative-looking SRT is created from draft durations (`:4752-4754`, `:7734`). |
| Timeline generation | Writes `sync/scene-audio-sync.json`; may create fallback `timing/scene-duration-plan.json`. |
| Speech duration estimation | Coarse word estimate (`EstimateNarrationDurationSeconds`, `:5060`) and fallback scene duration policy; no measured audio. |
| TTS/audio generation | **No** `.mp3`/`.wav`, Azure call, voice selection, or codec work in Phase 14. |
| Scene duration planning | Yes, improperly blurred: draft/fallback plan and SRT timing. Final calibration remains Phase 16. |
| Short/long | Both, rigidly 5 and 9 scenes. |
| Voice selection | No. |
| Pause/break planning | Subtitle-only `SentenceBreakPauseMs`/`CueGapMs`; no durable speech pause model. |
| Narration semantics | **Yes, violation:** Phase 14 generates, adapts, translates, sanitizes, de-duplicates, and rewrites narration (`:4437-4459`; extensive writer/translation/cleanup methods `:5236-6931`). |

## 3. Historical and mature implementation inventory

1. **RC2 monolith Phase 14/15/16 (current):** `ProductionPipelineExecutionService` methods at `:4400`, `:9718`, `:10578`. Mature active scene-level synthesis is the `TtsMode=SceneLevel` branch at `:9735-9737`, `:9771-9827`; legacy per-cue synthesis remains at `:9828-9843`.
2. **Question-driven narration/subtitle V3.1 compatibility:** Phase 14 private DTOs and `narration-v31` compatibility outputs (`:4486-4488`, records `:9646-9703`). Active compatibility, not authority-grade.
3. **VideoAssembly subtitle timing implementation:** `VideoAssemblyIntelligenceService.BuildSubtitleBlocks`, `ResolveSubtitleCueWordCount`, `WrapSubtitle`, `NormalizeSubtitleTtsOptions` (`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/VideoAssemblyIntelligenceService.cs:1627-1778`). Mature subtitle algorithm but a parallel historical owner, not a Phase 14 TTS planner.
4. **Documentary production adapters:** whole narration block Azure adapter (`Backend/src/Astronomy.MediaFactory.ProductionAdapters/NarrationAdapter.cs:51-88,128-135`) and separate subtitle adapter (`SubtitleAdapter.cs:13-67`). This is mature architectural evidence that speech requests should not be subtitle requests.
5. **Documentary media orchestration:** production coordinator synthesizes a narration block, resolves the physical narration, then generates subtitle separately (see `docs/cg-a3-existing-azure-speech-narration-adapter.md:1-8` and `docs/cg-a3-production-adapter-mapping-specification.md:380-390`). Mature but not wired to RC2 Phase 14/15.
6. **Weekly forecast narration sync:** `WeeklySkyForecastV2TimelineComposition.BuildNarrationSync` (`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/WeeklySkyForecastV2TimelineComposition.cs:75,171-183`) and `NarrationSyncResult` (`Backend/src/Astronomy.MediaFactory.Core/Interfaces.cs:2469-2481`). Long-form planning only; separate product/dead for RC2.
7. **Old V1 timeline compatibility:** `BuildTtsTimelineItemsAsync` and `TtsTimelineItem` (`ProductionPipelineExecutionService.cs:10405-10421,10575`) are retained compatibility helpers; active Real TTS V2 writes a richer anonymous cue/scene structure.

## 4. Audio pipeline history matrix

| Version/path | Short/long | en/hi | Provider/audio | SRT | Sync/grouping | Status |
|---|---|---|---|---|---|---|
| Whole-block documentary adapter | variants/block based | Both language enums | Azure; one narration artifact per block | separate adapter | sentence continuity retained; internal SSML | Mature, separate architecture |
| Weekly timeline sync | Long | language-neutral | none; `AudioRendered=false` | none | estimated segments | Mature for weekly product; dead for RC2 |
| RC2 legacy cue branch | both | requested language | Azure/configured TTS; one MP3 per parsed SRT block | consumes Phase 14 SRT | per-SRT cue | Active only if config opts out of `SceneLevel`; compatibility/regression risk |
| RC2 SceneLevel branch | 5/9 | same branch for en/hi | configured provider; one MP3 per visual scene + concatenated track | consumes Phase 14 SRT only for subtitle lineage | longer, scene narration request | **Current mature default** |
| Phase 16 reconciliation | both | selected language; English-only SRT rewrite | ffprobe/actual MP3 | regenerates English SRT | groups cue durations by scene | Current |

## 5. Mature longer-cue architecture state

`SubtitleTtsOptions.TtsMode` defaults to `SceneLevel` (`Backend/src/Astronomy.MediaFactory.Contracts/Contracts.cs:365-377`) and API/Worker settings explicitly select it (`Backend/src/Astronomy.MediaFactory.Api/appsettings.json:21-29`; Worker equivalent `:21-29`). Phase 15 reads one scene narration file and invokes TTS once per visual scene (`ProductionPipelineExecutionService.cs:9771-9818`), while subtitle chunks are only counted/linked. This fixes the intended English migration under normal configuration and applies identically to Hindi. Sentence text is not split for synthesis inside that branch.

However, the opt-out `LegacyCueLevel` branch still performs one TTS call per SRT block (`:9828-9843`). Thus the regression is fixed by default, **not structurally impossible**. Also, “longer cue” currently means an entire scene, not a typed sentence-group cue with explicit pause boundaries.

## 6. Phase 14 versus Phase 15 boundary

| Responsibility | Current owner |
|---|---|
| Plans scene association | Phase 14, positional/fixed IDs |
| Creates TTS request units | Phase 15 re-derives them: scene narration files in default mode, SRT blocks in legacy mode |
| Physical audio/provider call | Phase 15 (`GenerateAndValidateTtsAudioAsync`, called `:9791`/`:9835`) |
| Measures audio | Phase 15 validation/probe and track probe (`:9849`); Phase 16 re-probes cue files (`:10624-10648`). |
| Final TTS timeline | Phase 15, `tts/{language}/tts-timeline.json` (`:9927-9938`). |
| Final duration calibration | Phase 16 (`:10578-10740`). |

The proposed split fits repository evidence. Phase 14 should publish provider-neutral scene/cue intent; Phase 15 should synthesize that intent. Phase 16 should remain the actual-duration reconciliation owner, as Phase 15 itself declares (`:9931-9937`).

## 7. Phase 14 inputs and frozen authorities

### Phase 7 narration contract

Canonical Phase 7 files are `07-narration/{short,long}/accepted-release-candidate.json`, `07-narration/narration-manifest.json`, and `07-narration/narration-certification.json` (pipeline readiness at `ProductionPipelineExecutionService.cs:594-597`; catalog at `:17151-17154`). `Phase8AuthorityLoader` validates candidate physical checksum against manifest and certification (`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase8AuthorityLoader.cs:208-249`). Those documents, rather than Phase 14’s regenerated `narration/*.txt`, are the frozen narration publication state/checksum authority.

The accepted-candidate contract is JSON and includes the accepted long/short narration payload plus its sentence/scene planning lineage; exact optional word counts, pauses, language variants, and estimated durations must be read from the accepted candidate version, not assumed by Phase 14. Current Phase 14 reads **none** of these canonical paths. Therefore its fidelity to Phase 7 checksum/publication cannot be established.

**Recommended consumption:** load and committed-state validate manifest/certification plus requested-language short/long accepted candidates; preserve ordered sentence IDs/text and any beat/pause/duration metadata exactly; record candidate and authority checksums.

### Phase 6

Current Phase 14 does not directly load Phase 6 story-frame authority. Its narrative writer may obtain story data through the preview/generator context, but that is generation, not a declared authority dependency. If Phase 7 candidate lineage supplies scene/beat IDs, Phase 6 must not be reloaded. Only load Phase 6 if a typed Phase 7 field explicitly references it and Phase 7 lacks required mapping.

### Phase 8/9/10

Actual dependencies are Phase 8/9-style `scene-assets-v3/{short,long}` files: timeline, manifest, review, and metadata (`:9313-9327`). Phase 10 certification is not read. Phase 14 needs scene IDs, ordering, image existence/lineage, visual metadata, and draft expected duration. It does not need Hero, Thumbnail, or Gallery (11-13). Pre-audio duration originates in scene timeline metadata/visual timeline with fallback 5 seconds (`:9342-9363`); Phase 16 owns final duration.

## 8. Short and long pipelines

| | Short | Long |
|---|---|---|
| Count | exactly 5 | exactly 9 |
| Scene source | `scene-assets-v3/short` | `scene-assets-v3/long` |
| Narration source now | Phase 14-generated per-scene text | Phase 14-generated per-scene text |
| Cue strategy | subtitle chunks; TTS unit later is whole scene by default | same; long scene item normalization helper exists (`:4995`) |
| Duration | metadata/beat/default 5; draft plan | same |
| Sync | positional canonical scene ID association | positional canonical scene ID association |
| Output | same shared sync JSON plus language-scoped narration/SRT | same |

There is no adaptive cross-scene narration or distinct long-form speech algorithm. Long-form consists of nine longer scene files concatenated later.

## 9. English and Hindi pipelines

| | English | Hindi |
|---|---|---|
| Current source | Phase 14 writer output | translated/rewritten Phase 14 writer output |
| Segmentation | punctuation/whitespace subtitle splitter | same splitter with more conservative normalized options (`NormalizePhase14SubtitleTtsOptions`, `:8635`) and Hindi rewrite paths |
| Synthesis grouping | scene-level default | scene-level default |
| Pause | subtitle weights from sentence break; no speech pause contract | same model |
| Scene break | file boundary/one synthesis call per scene | same |
| SRT relation | SRT chunks do not define default request units | same; language validation checks Devanagari (`:9855-9857`) |
| Remaining difference | Phase 16 regenerates SRT only for English (`:10965-10978`) | Hindi retains Phase 14 draft timing; asymmetric and risky |

The splitter recognizes sentence-ending punctuation through its regex/character logic, then whitespace fallback. Hindi danda handling exists in the segmentation/translation paths, but there is no abbreviation lexer: periods in abbreviations, decimals/numbers, or astronomy names receive no domain-aware protection. Commas are soft boundaries; whitespace splitting prevents mid-word cuts. Tests at `ProductionPipelineExecutionServiceTests.cs:1743-1842` cover readability, conservative Hindi limits, and no mid-word split.

## 10. SRT ownership

* **Current:** Phase 14 writes `narration/subtitles/{language}/{short,long}.srt` from draft durations (`:4752-4754`). Phase 16 then rewrites only English from actual TTS timeline (`:10965-10985`). Phase 15 consumes rather than initially creates it.
* **Historical:** `VideoAssemblyIntelligenceService` and documentary subtitle adapter are independent owners; the RC2 legacy path treats SRT as TTS input.
* **Recommended:** Phase 14 owns subtitle **text segmentation and references**, not final timing. Phase 15 publishes measured cue timing alongside TTS timeline; Phase 16 publishes the only final SRT after calibration, for both languages. If product requires SRT before paid TTS, label it explicitly `estimated/draft`, never canonical.

## 11. Cue and sync models

Current `SceneAudioSyncItem` fields are `Format, BeatNo, SceneId, SceneImagePath, NarrationText, NarrationBeat, VisualIntent, RenderMode, EstimatedDurationSec, RecommendedTransition, RecommendedMotion, SyncStatus, SourceNarrationStrategy` (`:9701`). Subtitle blocks carry number, start/end, lines, scene/source text, hashes/origins/generator/time (`:9664-9667`). Phase 15’s anonymous timeline item includes format, scene/parent/visual IDs, cue index, audio/narration paths, narration/cue text, actual durations, subtitle count/path, and provider flags (`:9801-9818`); legacy cue projections additionally include SRT start/end/duration (`:9865-9886`).

There is no durable DTO containing sentence indexes, Phase 7 sentence IDs, pause-before/after, policy version, voice-profile reference, or input checksum.

## 12. Grouping policy and punctuation

Subtitle grouping uses `SubtitleMaxWordsPerCue=8`, `SubtitleMaxLines=2`, `SubtitleMaxCharsPerLine=42`, min/max cue duration 1200/4200 ms, reading speed 14 chars/sec (contract default), sentence pause 80 ms, and gap 0 (`Contracts.cs:365-377`). `SplitSubtitleChunks` (`ProductionPipelineExecutionService.cs:8414-8499`) prefers punctuation/boundaries and then whitespace; `AllocateSubtitleCueDurations` (`:8514-8569`) weights characters/punctuation and clamps. Hindi normalization tightens layout/timing; tests document that intent (`ProductionPipelineExecutionServiceTests.cs:1767-1787`).

This policy controls **subtitles**, not mature TTS scene grouping. TTS grouping is simply one per visual-scene narration file. No max TTS characters/words, multiple-sentence selection rule, paragraph pause, title pause, or explicit scene-pause duration exists.

## 13. Configuration

| Section/property | Default/current | Consumer |
|---|---|---|
| `SubtitleTtsOptions:TtsMode` | `SceneLevel` | Phase 15 branch (`:9735-9737`) |
| `SubtitleMaxWordsPerCue` | 8 | subtitle splitter |
| `SubtitleMaxLines` | 2 | wrap/validation |
| `SubtitleMaxCharsPerLine` | 42 | wrap/split |
| `SubtitleMin/MaxCueDurationMs` | 1200/4200 | allocation |
| `ReadingSpeedCharsPerSecond` | 14 contract default (not explicitly in shown appsettings) | allocation |
| `SentenceBreakPauseMs` | 80 | allocation weight/pause |
| `CueGapMs` | 0 | timing |
| `Speech`/`AzureSpeech` voice | en Jenny/Aria depending consumer; hi Madhur; medium rates; MP3 format | Phase 15 provider only (`Backend/src/Astronomy.MediaFactory.Api/appsettings.json:146-184`) |

DI binds and validates subtitle options at `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs:195-201`. Provider keys/endpoints/codecs/rates do not enter Phase 14, which is correct.

## 14. Per-SRT TTS audit

* **Default active:** no. `SceneLevel` invokes synthesis once per expected visual scene (`:9771-9818`). This is shared by English/Hindi.
* **Config-reachable compatibility:** yes. Any non-`SceneLevel` `TtsMode` selects `LegacyCueLevel` and calls synthesis for every parsed SRT block (`:9828-9843`). It is not dead/test-only.
* **Documentary adapter:** whole block, explicitly not per SRT (`docs/cg-a3-existing-azure-speech-narration-adapter.md:1-8`).
* **Tests:** existing tests validate scene/cue duration compatibility but no test appears to forbid configuration from activating per-SRT synthesis.

## 15. Synchronization and duration mismatch strategy

Phase 14 does not react to narration longer/shorter than a visual scene: it allocates draft subtitle cues inside the selected draft duration. It never trims/extends media, changes speech rate, redistributes speech pauses, reassigns cues, or allows deliberate cross-scene narration. Phase 15 measures mismatch but marks `audioSrtDurationMismatchIsBlocking=false` (`:9931-9937`). Phase 16 sets each scene duration from actual cue audio, requires `sceneDurationSec >= audioDurationSec`, compares narration-track totals within 0.1 seconds, and writes an audio-driven duration plan (`:10624-10669`). No automatic speech-rate adjustment occurs; visual duration extends to audio. Motion/assembly then consumes the calibrated plan.

## 16. Phase 16 dependency

Phase 16 requires Phase 14 `sync/scene-audio-sync.json`, Phase 15 language-scoped TTS timeline, and short/long `scene-timeline-metadata.json` (`:10589-10606`). It consumes actual/probed cue durations, groups them into five/nine scene duration items (`:10624-10635`), writes `timing/scene-duration-plan.json`, and currently regenerates English SRT (`:10652-10671`). Therefore Phase 14 must not attempt final calibration.

## 17. Phase 17/18 expectations

Phase 17 reads `timing/scene-duration-plan.json`, scene assets, referenced audio paths, and records Phase 15 TTS/SRT locations (`:11030-11138`); it does not need a Phase 14 cue artifact directly. Phase 18 reads language-scoped TTS timeline, calibrated scene durations/SRT, and groups cue durations by parent scene (tests at `ProductionPipelineExecutionServiceTests.cs:1510-1743`). Today `scene-audio-sync.json` is chiefly a Phase 16 contract.

## 18. Current Phase 14 artifacts

Direct/current outputs include:

| Path/pattern | Format/version | Purpose/status |
|---|---|---|
| `sync/scene-audio-sync.json` | JSON `v1` | shared 5/9 scene mapping; current compatibility input to Phase 16, not governed authority (`:4513-4538`) |
| `narration/{language}/{short,long}/*.txt` | UTF-8 text | scene-level Phase 15 request source; improperly regenerated semantics |
| narration manifest/diagnostics/comparison/creative review | JSON | writer/output diagnostics; current compatibility |
| `narration/subtitles/{language}/{short,long}.srt` | SRT | draft subtitle text/timing; overwritten for English by Phase 16 |
| `narration-v31/subtitles/...` and V3.1 diagnostics | SRT/JSON | explicit compatibility (`:4486-4488`) |
| `timing/scene-duration-plan.json` when absent | JSON, `phase-14-fallback` lineage | draft/fallback compatibility; later replaced by Phase 16 |
| `validation/phase-14-validation.json` and multiple Phase 14/SRT diagnostics | JSON | ad-hoc validation, not transactional authority |

No physical audio is written by Phase 14. Historical `narration-v31`, old narration roots, fallback timing, and old scene-asset path diagnostics should be compatibility-only; the old scene roots are checked/ignored, not selected (`:4408-4413`).

## 19. Recommended canonical package

Do not introduce an unrelated engine, but a Phase 14 authority root is necessary because existing `sync/` and `narration/` names have mixed ownership. Follow the established numbered authority convention already used by Phases 7 and 11-13:

```text
14-audio-sync/
  scene-audio-sync.json       # both short and long, versioned mapping
  narration-cue-plan.json     # both short and long, requested language
  phase14-authority-diagnostics.json
  phase14-publication-report.json
validation/phase-14-validation.json
```

Prefer one package with short/long sections (matching established `scene-audio-sync.json`) rather than inventing duplicated per-format filenames. Continue writing `sync/scene-audio-sync.json` and current narration/SRT paths only as explicitly labeled compatibility projections until Phases 15-18 migrate.

## 20. Transaction/governance and result projection

Current Phase 14 creates final directories and writes directly. It has no staging workspace, candidate validation boundary, atomic directory/file commit, rollback, committed readback, deterministic authority checksum, publication report, or `downstreamReady`. Its validation JSON has ordinary `status` plus many diagnostics but not the frozen Phase 11-13 governance fields. The generic result cannot correctly project `generated/reused/regenerated`, `publicationCommitted`, manifest/semantic/checksum/committed-state validations, `authorityChecksum`, or `downstreamReady` because Phase 14 produces none of them. This is the largest certification gap.

## 21. Cleanup ownership

Phase 14 creates/deletes within mixed `narration`, `narration-v31`, `sync`, `timing`, and `validation` roots. `DeleteTargetNarrationFolders` (`:4669`) and output-layer cleanup make ownership unsafe and can replace narration compatibility outputs; direct writes are not rollback-safe. It does not intentionally delete Phase 1-13 numbered roots, but its narration rewriting violates Phase 7 semantic ownership. Target cleanup must be restricted to staging and committed `14-audio-sync`; compatibility projections should be file-scoped, never recursive across upstream roots.

## 22. Partial execution

`startPhaseNo=14,endPhaseNo=14` can only succeed if the unnumbered `scene-assets-v3` short/long files and sufficient in-memory production context/narration generator dependencies exist. It does **not** prove or consume frozen Phase 7 publication, and it rebuilds narration. Consequently it is operationally possible in a fully hydrated run but not authority-correct or reliably standalone from frozen disk artifacts.

## 23. Reuse/idempotency

There is no reuse decision: outputs are rewritten. No identity includes Phase 7 checksum, scene order/checksum, language, policy version, subtitle/grouping config, or rate assumption. Recommended deterministic identity is SHA-256 over: Phase 7 manifest/certification and selected candidate checksums; Phase 8/9/10 scene authority/checksum and ordered IDs; requested language; cue/sync contract version; normalized grouping/pause configuration; voice-profile **reference** (not provider secret); and serializer/schema version. Reuse only after committed readback validation and exact identity match.

## 24. Validation rules and failures

Current rules: all four scene input files exist per format; 5/9 item and matched counts; scene image exists; narration text nonempty; no duplicate narration within a format; no unmatched sections/scenes; narration files correspond to expected scene IDs; SRT is generated; cue layout/timing limits; no duplicate subtitle blocks; language script checks; and assorted narrative event-family/quality checks. Missing are: Phase 7 committed authority/checksum, exact sentence coverage once, deterministic normalized full-text equality, unique stable cue IDs, no orphan/duplicate scene assignments, explicit boundary legality, positive cue estimates, totals, schema validation, checksum/readback, and downstream readiness.

There are no dedicated `P14_*` reason-code constants. Failures are `InvalidOperationException` messages (missing scene input, writer failure, SRT duplicate, etc.), usually retryability-unknown. Add structured nonretryable codes for invalid upstream authority/schema/text fidelity/policy and retryable codes only for I/O/commit contention. Suggested minimum: `P14_UPSTREAM_AUTHORITY_MISSING`, `P14_UPSTREAM_AUTHORITY_INVALID`, `P14_SCENE_LINEAGE_INVALID`, `P14_NARRATION_FIDELITY_FAILED`, `P14_CUE_PLAN_INVALID`, `P14_CANDIDATE_VALIDATION_FAILED`, `P14_COMMIT_FAILED`, `P14_COMMITTED_READBACK_FAILED`.

## 25. Test inventory

Primary test file is `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs`:

* `Phase14SubtitleSegmentation_*` (`:1743-1842`) — cue readability, Hindi limits, whitespace/no mid-word behavior; useful subtitle algorithm tests, not authority tests.
* `Phase14NarrationExtraction_ReadsSectionsFromRootScenesArray` (`:1946`) — historical extraction compatibility.
* `Phase14DocumentaryNarration_*` and score tests (`:1977-2062`) — encode the now-invalid Phase 14 narration-authoring responsibility.
* `Phase14V31Adapter_*`, event guard, Hindi rewrite/translation family (`:2088-2546`) — compatibility algorithms; conflict with frozen Phase 7 immutability if kept active.
* `PhaseGating_ThumbnailOnly_RunsSceneAudioSyncButSkipsVideoPhasesNotRequested` (`:1928`) — reveals Phase 14 runs even for thumbnail-only output; architectural mis-gating.
* Phase 15/16/18 cue/scene lineage and duration tests (`:1284-1743`) — validate current default scene-level and compatibility cue-level downstream behavior.
* `OverwriteCleanup_Phase15Only_PreservesNarrationSubtitlesAndDeletesOnlyTts` (`:3134`) — relevant boundary/cleanup.

No test directly certifies Phase 14 against frozen Phase 7 bytes/checksum, transactionality, reuse, deterministic cue plan, result projection, or a hard ban on per-SRT synthesis. Cue-level tests support reading old timelines; they do not necessarily require per-SRT synthesis and should be retained as compatibility-reader tests.

## 26. English regression analysis

English and Hindi now traverse the same default `SceneLevel` branch and each scene narration file becomes one request (`:9747-9818`). English no longer synthesizes every SRT cue under shipped settings. Hindi is no longer uniquely advantaged. Risk remains because `TtsMode` accepts arbitrary non-`SceneLevel` values and silently activates `LegacyCueLevel` (`:9735-9737,9828-9843`), and because Phase 15 still reconstructs scene lineage from SRT/narration (`:10020-10089`). Certification should reject legacy mode in production rather than silently select it.

## 27. Narration immutability and text fidelity

Current Phase 14 explicitly modifies narration: it calls a documentary writer, applies replacements, translates English to Hindi, sanitizes, removes duplicates, and even rewrites subtitle duplicates. This is incompatible with frozen Phase 7. Current checks compare internal generated files/SRT, not Phase 7 accepted-candidate text.

Target validation must, for each `(format,language)`, preserve every Phase 7 sentence ID exactly once and assert:

```text
Normalize(concatenate(cues ordered by sentence span))
  == Normalize(authoritative Phase 7 narration text)
```

Normalization should be narrowly specified (Unicode normalization, CRLF and whitespace only); it must not erase punctuation or language semantics. Also validate per-sentence hashes before whole-stream hash, so dropped/duplicated/reordered text is diagnosable.

## 28. Pause model

Only `SentenceBreakPauseMs=80` and `CueGapMs=0` exist, and they affect subtitle timing allocation. There is no paragraph, scene, intro/title, minimum/maximum speech pause contract. Azure speech rate/SSML settings belong to Phase 15. Target Phase 14 cue plan should carry provider-neutral `pauseBeforeMs`, `pauseAfterMs`, `breakReason` (`sentence|paragraph|scene|none`) sourced from Phase 7/policy; Phase 15 translates this intent to SSML/audio without changing boundaries.

## 29. Authority lineage

Record plan/execution IDs, requested format/language, Phase 7 manifest/certification/candidate paths and physical checksums, Phase 7 authority checksum, Phase 8/9/10 selected scene manifest/timeline paths and authority checksums, ordered scene-ID checksum, sync/grouping/pause policy version/config checksum, cue-plan deterministic checksum, schema/serializer version, transaction ID, publication state/time, validation checksum, and committed authority checksum. Do not record secrets, Azure endpoint/key, codec, or actual duration in Phase 14.

## 30. Exact recommended Phase 14 to Phase 15 contract

Adapt the existing scene-level behavior into a typed `narration-cue-plan` rather than adding a V2/V3 engine. Package fields:

* Header: `schemaVersion`, `planId`, `executionId`, `language`, `phase7AuthorityChecksum`, candidate checksums, `sceneAuthorityChecksum`, `syncPolicyVersion`, `groupingPolicyChecksum`, `cuePlanChecksum`, `publicationState`.
* Per stream: `format`, ordered `sceneIds`, `estimatedTotalDurationMs`, `cues`.
* Per cue: `cueId` (deterministic), `sequence`, `format`, `language`, `sceneId`, `sentenceIds`, `sentenceStartIndex`, `sentenceEndIndex`, exact `text`, `textChecksum`, `estimatedSpeechDurationMs`, `pauseBeforeMs`, `pauseAfterMs`, `breakReason`, `mayCrossSceneBoundary=false` (unless explicitly authorized), `subtitleSegmentRefs`, and provider-neutral `voiceProfileRef`/`speechStyleRef`.

Phase 15 must not re-derive boundaries, sequence, language, scene mapping, pauses, sentence spans, subtitle association, or voice-profile selection. It may resolve the profile to provider voice/rate/SSML, choose codec, synthesize, retry, inspect files, measure actual duration, and publish physical paths/checksums plus actual timeline.

## 31. What Phase 14 should not know

Current Phase 14 correctly does not use subscription key, Azure endpoint, codec, provider response, actual audio duration, ffmpeg, or voice name. Preserve this. Remove its narration-generation/translation knowledge and do not move provider settings upstream.

## 32. Current versus target matrix

| Dimension | Current Phase 14 | Mature/certifiable target |
|---|---|---|
| Responsibility | writer + mapper + SRT + draft timing | frozen narration-to-scene/cue intent only |
| Inputs | scene-assets V3 + runtime writer context | committed Phase 7 + certified ordered scene authority |
| Short/long | fixed 5/9 | explicit authority-driven streams |
| English/Hindi | divergent translation/rewrite; common subtitle/TTS default | same grouping contract; already-authoritative language text |
| Cue grouping | subtitle chunks; incidental whole scene files | typed sentence-preserving scene cues |
| SRT | draft/canonical-looking | text refs/draft only; final after actual audio |
| TTS | none | none |
| Physical audio | none | none |
| Artifacts | mixed unnumbered roots | transactional `14-audio-sync` + compatibility projections |
| Validation | ad hoc counts/content/layout | lineage, exact coverage/fidelity, schema/checksum/readback |
| Transaction | direct writes | stage, validate, atomic commit, rollback, readback |
| Reuse | none | deterministic input/policy identity |
| Downstream | Phase 15 re-derives; Phase 16 reads sync | Phase 15 consumes cue plan directly |

## 33. Code reuse classification

| Code | Classification |
|---|---|
| `BuildSceneAudioSyncItemsAsync` scene loading/order | **REUSE ALGORITHM ONLY**; replace positional/fallback authority assumptions |
| `SceneAudioSyncItem` | **REUSE WITH AUTHORITY ADAPTER**; introduce public typed governed contract rather than parallel engine |
| `SplitSubtitleChunks`/wrap/allocation | **REUSE ALGORITHM ONLY** for subtitle references; not TTS request grouping |
| Phase 15 `SceneLevel` branch | **REUSE WITH AUTHORITY ADAPTER** to consume cue plan |
| Phase 15 `LegacyCueLevel` | **RETAIN COMPATIBILITY ONLY**, reject in production authority path |
| Phase 14 writer/translation/sanitize/duplicate rewrite | **REMOVE FROM ACTIVE AUTHORITY PATH**; retain only where another legacy endpoint requires it |
| `narration-v31` outputs | **RETAIN COMPATIBILITY ONLY** |
| old V1 `BuildTtsTimelineItemsAsync` | **OBSOLETE/compatibility reader only** |
| Phase 16 actual-duration grouping | **REUSE AS-IS** except bilingual final-SRT ownership correction |
| Documentary whole-block adapter | **REUSE AS-IS in its architecture**; Phase 15 may adapt it, do not clone it |

## 34. Minimal implementation plan (not implemented)

1. Add a Phase 14 authority loader for committed Phase 7 candidates and certified scene manifests; fail closed on checksum/publication mismatch.
2. Extract/adapt existing scene-level mapping and sentence-preserving grouping into typed provider-neutral contracts; remove writer/translation calls from active Phase 14.
3. Add exact sentence/text lineage validation and deterministic identity/checksums.
4. Wrap publication in the same staging/candidate-validation/atomic-commit/readback/report pattern used by frozen 11-13; scope cleanup to `14-audio-sync`.
5. Project governed result fields/reason codes and implement validated reuse.
6. Adapt Phase 15 SceneLevel branch to consume the cue plan; disable `LegacyCueLevel` for production while retaining compatibility parsing.
7. Make Phase 16 the single bilingual final-SRT/timing publisher; migrate 17/18 reads, then label/remove draft compatibility outputs.

## 35. Files expected to change in implementation phase

No files were changed now except this audit. Expected later: `ProductionPipelineExecutionService.cs`; new or existing Phase 14 authority contracts/loader/publisher under Core/Infrastructure (prefer established authority namespaces); DI registration; Phase 15 consumer adapter; Phase 16 bilingual subtitle publication; relevant appsettings validation (not necessarily values); `ProductionPipelineExecutionServiceTests.cs` plus focused authority loader/publisher/contract tests; architecture/output-contract documentation. Avoid changes to Phase 1-13 production implementations and their frozen tests.

## 36. Risks and compatibility

Major risks are unknown variation in Phase 7 candidate schema, downstream reliance on unnumbered narration/SRT paths, Hindi rewrite behavior currently masking bad upstream text, legacy plans without authority checksums, 5/9 hard-coded assumptions, and atomic replacement across platforms. Preserve read-only compatibility projections during migration. Do not accept a “fallback generate narration” path: it silently breaks frozen authority.

## 37. Certification criteria

1. Phase 14 standalone execution validates and consumes committed Phase 7 and certified scene authority without rerunning 1-13.
2. Every authoritative sentence appears exactly once, ordered, hash/text faithful, for requested short/long/language streams.
3. Every required scene is mapped exactly once or an explicitly supported zero-narration scene is declared; no orphan/duplicate/cross-boundary cue.
4. English/Hindi use the same approved sentence-preserving grouping contract; language punctuation tests pass.
5. Phase 14 performs no narration writing/translation, Azure/provider call, `.wav`/`.mp3`, actual-duration measurement, or final calibration.
6. Production Phase 15 cannot synthesize per SRT segment and consumes Phase 14 cue IDs/boundaries directly.
7. Canonical artifacts validate schema, deterministic checksums, lineage, and totals.
8. Stage/validate/atomic commit/rollback/readback publication succeeds; report says committed and `downstreamReady=true`.
9. Re-run with identical inputs reuses byte-identical authority; changed narration/scene/config identity regenerates.
10. Governed Phase result correctly projects status, reason, generated/reused/regenerated, publication and all validation flags/checksum/readiness.
11. Cleanup tests prove no Phase 1-13 byte is modified/deleted.
12. Phase 15, Phase 16, Motion, and Assembly integration tests pass for short/long and en/hi.

## 38. Remaining uncertainties

* Accepted Phase 7 candidate instances are runtime artifacts, not checked-in fixtures; optional sentence/pause/word-count field presence must be verified against its actual contract/serializer before implementation.
* The generic phase executor’s exact Phase 14 failure projection is dispersed in the large orchestration method; no dedicated Phase 14 mapper exists, which itself is the actionable finding.
* Some narrative V3.1 methods may support external endpoints beyond RC2; remove only from the active authority route, not blindly from the repository.
* “One scene = one cue” may exceed provider limits for unusually long Phase 7 scenes. The cue planner should group complete sentences up to an explicit maximum without splitting a sentence, using the mature whole-block principle.

## 39. Final recommendation

**Preserve and adapt, do not replace.** Keep the proven default scene-level TTS behavior and subtitle algorithms, but put the mapping/grouping portion behind a Phase 14 transactional authority adapter that consumes frozen Phase 7 and certified scene lineage. Remove all narrative generation/translation from the Phase 14 active path. Make its typed, deterministic cue plan the sole Phase 15 request contract. Phase 15 remains the only physical speech/TTS-timeline authority; Phase 16 remains final actual-duration and SRT calibration authority. Retain the per-SRT branch and V3.1 artifacts only as clearly isolated compatibility, never selectable in production authority execution.

This answers the audit gates: Phase 14 should own sync intent/cues, Phase 15 owns real speech, the existing mature architecture is scene-level/whole-block synthesis, English and Hindi share it by default, a config-reachable per-SRT path remains, the canonical output should be a governed `14-audio-sync` package, and certification can be achieved by adapting existing algorithms rather than creating another audio system.
