# Phase 19 Final Video QA / Review Authority Audit

**Audit date:** 2026-08-11  
**Scope:** the current production path in `ProductionPipelineExecutionService`, the frozen Phase 18 authority publisher, Phase 20's publishing gate, API/result projection, and all repository tests that explicitly exercise Phase 19.  
**Change boundary:** audit documentation only. No production code, tests, configuration, media, subtitles, audio, or Phase 18 artifacts were changed or generated.

## Executive finding

Phase 19 is **not certifiable as the final-video QA authority** in its current form.

It does select the fixed language-scoped Phase 18 `short/final.mp4` and `long/final.mp4` paths rather than discovering an arbitrary MP4. However, it does not deserialize or validate the Phase 18 manifest/publication authority, requires both formats regardless of request identity, checks the wrong caption paths, does not verify byte lengths or hashes, and does not probe the complete physical stream contract. Its motion and transition decisions trust legacy metadata rather than encoded frames. Its music check trusts an intermediate-file path and unconditionally requires music and ducking. Most critically, despite a comment saying no outro is required, the active success gate still requires Phase 18 fields describing a four-second cinematic outro and one-second fade-to-black.

The current model is a hybrid: an automated heuristic score creates `recommendation = Approved`; Phase 20 treats that as `phase19ReviewApproved`; a separate manual approval file/request flag supplies `publishApproved`. Technical QA, automatic review recommendation, and human publication consent are therefore distinguishable in the Phase 20 gate, but the names and Phase 19 scoring conflate technical and editorial review.

## 1. Current production entry and active call graph

### Exact entry

| Item | Current value |
|---|---|
| File | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` |
| Class | `Astronomy.MediaFactory.Infrastructure.Persistence.ProductionPipelineExecutionService` |
| Method | `PhaseVideoQaProductionReviewAsync(ProductionPhaseContext, CancellationToken)` |
| Definition line | 14189 |
| Dispatch | `PhaseDefinitions()` entry 19, line 437 |
| Pipeline public entry | `ExecuteAsync(ProductionPipelineRequest, CancellationToken)`, line 160 |

### Active call graph

```text
ExecuteAsync
  -> PhaseDefinitions()[19]
  -> PhaseVideoQaProductionReviewAsync                           (14189)
     -> ResolvePipelineLanguage
     -> fixed paths under 18-video-assembly/{language}           (14197-14215)
     -> File.Exists / Directory.Exists for an unparsed input list
     -> BuildPhase19VideoChecksAsync(short canonical path)       (14220)
        -> HasAudioStreamAsync -> configured ffprobe
        -> ProbeAudioDurationSecondsAsync -> configured ffprobe
        -> DetectPhase19MediaIssue(silencedetect) -> FFmpeg null output
        -> Phase19MotionIsActive(motion-plan metadata)
        -> DetectPhase19MediaIssues(blackdetect/freezedetect) -> FFmpeg null output
        -> Phase19IsTransitionWindowEvidence
     -> BuildPhase19VideoChecksAsync(long canonical path)        (14221)
     -> BuildPhase19SceneChecks                                  (14222)
        -> ReadPhase19SceneFingerprints from legacy scene/sync/timing/motion JSON
        -> Directory.EnumerateFiles on scene-assets-v3 images
        -> ReadPhase19AudioPaths from legacy TTS JSON
     -> BuildPhase19StoryChecks                                  (14223)
        -> ReadPhase19TextValues from legacy sync/TTS/scene metadata
        -> lexical hook/science/guidance/ending/continuity heuristics
        -> normalized-text duplicate detection
     -> BuildPhase19AudioChecksAsync                             (14224)
        -> ProbeAudioLevelsAsync(short and long) -> FFmpeg audio analysis
        -> ResolvePhase18FinalMixedAudioPath (intermediate existence)
        -> ResolvePhase18BackgroundMusicConfig (configuration, not authority)
     -> BuildPhase19VisualChecks                                 (14225)
        -> read legacy motion-plan/motion-debug/scene metadata
        -> metadata-token checks only
     -> JsonNode.Parse(validation/phase-18-validation.json)      (14226)
     -> score six heuristic categories; derive recommendation
     -> write review/video-review.json                           (14271-14272)
     -> write review/qa-report.json                              (14274-14275)
     -> write validation/phase-19-review-diagnostics.json        (14277-14286)
     -> re-read legacy motion metadata and Phase 18 validation fields
     -> IsPhase18CinematicOutroValidated                         (14306)
     -> IsPhase18FadeToBlackValidated                            (14310)
     -> write validation/phase-19-validation.json                (14300-14301)
     -> throw on failure, otherwise return the four output paths

PhaseFinalValidationAsync (Phase 20)                             (14621)
  -> WriteScenesManifestsAsync
  -> MaterializePlanFolderAsync
  -> qualityValidator.ValidateFinalOutputAsync
  -> WriteAndValidatePublishGateAsync                            (14632)
     -> read Phase 19 validation and QA report
     -> discover manual approval marker files / request flag
     -> write validation/phase-20-publish-gate-diagnostics.json
     -> throw unless technical flag + Phase 19 recommendation + manual approval pass

ContentPlanProductionExecutionService.BuildResult               (589)
  -> read Phase 20 publish-gate diagnostics
  -> project PublishGateChecked, PublishApproved,
     Phase19ReviewApproved into ContentPlanProductionExecutionResult
```

There is no active Phase 19 manifest loader, Phase 19 authority reader, caption parser, ASS parser, frame sampler, checksum builder, transactional publisher, or reuse reader.

## 2. Current responsibilities

| Capability | Current behavior from code |
|---|---|
| Video file discovery | Fixed canonical-looking Phase 18 paths are constructed; no manifest declaration is read. |
| Directory search | Not for MP4 selection. It does enumerate `scene-assets-v3/{short,long}` to find any image. |
| Legacy final-video lookup | Not in the Phase 19 method. Legacy projections exist elsewhere, including Phase 18 compatibility publication and top-level result paths. |
| Duration validation | Only `duration > 0` locally; Phase 18 booleans are copied. No comparison to governed total. |
| Resolution validation | None. |
| Codec/pixel-format/FPS validation | None. |
| Audio stream validation | Presence via ffprobe and positive duration; whole-track silence/loudness heuristics via FFmpeg. No codec/rate/channel validation. |
| Subtitle validation | None. Merely requires incorrectly located `short/final.srt` and `long/final.srt`. |
| Motion validation | Metadata-token/debug-array check; no encoded-frame evidence. |
| Transition validation | `!motionText.Contains("advanced")`; no boundary sampling and no governed type comparison. |
| Fade validation | No physical validation. A Phase 18 boolean/duration assertion is trusted. |
| Background music validation | Intermediate mixed-audio file existence plus current config ducking flag; no final-media evidence and music is mandatory even when disabled. |
| Review scoring | Six equally averaged heuristic category scores; approval threshold is score and confidence >= 80. |
| Automatic publish approval | No. Automatic `recommendation` is treated as Phase 19 review approval, not `publishApproved`. |
| Manual approval | Owned by Phase 20 gate through marker-file discovery or request `PublishApproved`. Database `manual_validation` is not read by Phase 19. |
| Intro/outro | Active hard requirement for >=4.0 s cinematic outro and >=1.0 s fade despite contrary comment. |
| Hero/thumbnail/gallery | Not directly validated in Phase 19; legacy story/scene semantic categories substitute unrelated editorial expectations. |
| Social/publishing preparation | None in Phase 19; Phase 20 materializes and validates the publishing package. |

The story-scoring requirements (hook, educational explanation, sky guide, emotional ending, scene diversity) and raw scene/TTS existence checks are legacy editorial/upstream validation, not final-video physical QA.

## 3. Current Phase 18 input handling

Phase 19 constructs these direct candidates:

* `18-video-assembly/{language}/phase18-manifest.json`
* `18-video-assembly/{language}/phase18-publication-report.json`
* `validation/phase-18-validation.json`
* `18-video-assembly/{language}/short/final.mp4`
* `18-video-assembly/{language}/long/final.mp4`
* **incorrect:** `18-video-assembly/{language}/{short,long}/final.srt`

It omits `phase18-authority-diagnostics.json`; canonical captions are actually `{format}/captions/{language}.srt` and `.ass`. It checks that the manifest and report files exist but never opens either. Only the validation JSON is parsed, and its gate checks merely `status == Succeeded`, `publicationCommitted`, and `downstreamReady`.

The frozen Phase 18 publisher already supplies the required lineage checksums, requested formats, policy versions, media evidence (relative MP4/SRT paths, governed and physical durations, dimensions, codecs, sample format, hashes, lengths, and source scene hashes), diagnostics, and complete publication governance. Phase 19 should adapt this authority rather than reload legacy Phase 15/16/17 locations for primary discovery.

**Governance gap:** Phase 19 does not require `committedReadbackPassed`, `committedStateValidationPassed`, `semanticValidationPassed`, `checksumValidationPassed`, `manifestValidationPassed`, `validationStatus == Valid`, a non-empty/checksum-consistent `authorityChecksum`, or agreement among manifest/report/validation/diagnostics.

**Requested-output gap:** current Phase 18 itself currently declares and renders `Short` and `Long` unconditionally. Phase 19 independently requires both. Neither supports short-only or long-only on this authority path yet. Phase 19's target adapter must follow the manifest set; requested-format enablement may first require a Phase 18 contract evolution, without changing frozen Phase 18 semantics as part of Phase 19 work.

## 4. Legacy video lookup audit

The active Phase 19 video lookup does **not** enumerate MP4s, choose the first/latest MP4, or use `video/{short,long}`. It hardcodes the new authority root, which is directionally correct but is still not manifest-driven.

Legacy media locations remain elsewhere:

* the pipeline's top-level compatibility/result initialization references `video/short/final-short.mp4`;
* the legacy Phase 18 compatibility path uses `video/{short,long}/final-{short,long}.mp4`;
* frozen Phase 18 deliberately copies canonical files to `video-assembly/{language}/{format}/final.mp4` and `video/{format}/final-{format}.mp4` as compatibility projections;
* generic final-output validation/materialization may therefore still consume compatibility products independently of Phase 19 authority.

Classification: those locations may remain explicit migration/compatibility projections, but must be removed from the Phase 19 and Phase 20 authoritative selection paths.

## 5. Physical media validation

Current Phase 19 checks file existence, audio-stream existence, positive probed duration, detected silence, black runs, and (only when metadata says motion is inactive) frozen runs. It uses configured FFprobe for stream/duration helpers and configured FFmpeg for analysis filters. The FFmpeg invocations decode to a null sink and do not render or mutate the MP4, but they scan entire videos and are not targeted per scene.

Missing checks are: regular-file identity, manifest byte length/SHA-256, readable container failure semantics, video stream presence, exact width/height, rational FPS, H.264, `yuv420p`, AAC, 48 kHz, stereo, audio duration, and checksum readback of every declared caption. Probe command failures are not translated to precise Phase 19 codes.

Target physical contracts should come from Phase 18 policy/evidence: Short 1080x1920 and Long 1280x720; both 30 fps, H.264, `yuv420p`, AAC, 48 kHz, two channels. Governed format duration, not TTS length, is authoritative. Use one-frame-plus-narrow-container-rounding tolerance (Phase 18 currently uses 35 ms at 30 fps), not seconds.

## 6. Duration QA

Current local validation is only positive duration. It copies `shortDurationValidationPassed` and `longDurationValidationPassed` out of Phase 18 validation but does not include either in the final `validationPassed` expression except indirectly through the obsolete `productionQaPassed` fields. The emitted label `durationValidationMode = NarrationPlusCinematicOutro` is stale.

Target: probe the final MP4 duration and compare it to each manifest output's governed total/physical duration with the policy tolerance. Do not compare to narration duration and do not create an outro allowance.

## 7. Codec, resolution, FPS, and container QA

None currently exists in Phase 19. `HasAudioStreamAsync` selects only an audio stream; duration probing is format-level. Target a single structured ffprobe JSON read per format (or a small bounded set) and validate container readback, video/audio streams, dimensions, average/real frame rate, codecs, pixel format, sample rate, channels, and durations against the Phase 18 manifest/policy.

## 8. Audio and narration QA

Current checks combine Short and Long by taking the louder peak/RMS, so one good format can mask the other. `narrationAudible = max RMS > -35 dB`; silence detection rejects **any** one-second region below -45 dB, including intentional visual tails. There is no per-scene narration-window evidence. Clipping/distortion are coarse global thresholds.

Target: retain stream probing and a lightweight energy primitive, but evaluate each requested format and each Phase 15/18-declared narration interval independently. Require non-silence only where narration is expected. Do not speech-recognize, regenerate, or require continuous audio in intentional silent tails.

## 9. Background music QA

Current `BackgroundMusicAudible` means only that a resolved Phase 18 intermediate mixed-audio path exists for either format. It does not prove the final MP4 contains a bed. Current configuration, not committed authority, supplies `DuckUnderNarration`; both music and ducking are required unconditionally. No narration/music relative-level test exists.

Target: read `backgroundMusicUsed` and mix policy from the Phase 18 authority. If disabled, intentional silence is valid and music/ducking must not be required. If enabled, sample deterministic narration-free portions derived from scene narration versus governed scene duration and require safe non-silence evidence per requested format. Optionally compare narration-window and bed-window levels to reject only obvious masking; document that energy analysis cannot identify semantic sources.

## 10. Subtitle QA

No subtitle content or presentation QA exists. Current required paths conflict with Phase 18 canonical `captions/{language}.srt`. ASS is ignored. Burn-in mode, burn count, SRT/ASS enablement, cue lineage, style, hashes, and duplicate-presentation risk are not evaluated.

Target checks:

1. Read committed subtitle policy/diagnostics, not universal assumptions.
2. For each enabled sidecar, resolve only its manifest relative path, require a regular file, and verify length/SHA-256.
3. If SRT is declared copied from Phase 16, verify the declared/source checksum lineage and parse ordered cue number, time range, and text without rewriting it.
4. If ASS is generated, verify manifest identity, `[Script Info]`, styles/events, cue count/timing lineage, font, bottom alignment, safe margin, and maximum two-line policy.
5. If burn-in is enabled, require exactly one reported burn pass and `duplicateSubtitleRisk == false`; physically require that `final.srt` does not share the MP4 basename. Do not require burn-in when disabled.
6. Prefer deterministic authority plus ASS style evidence; OCR is unnecessary.

## 11. Motion QA

Current motion approval is wholly metadata-based. It requires `motion-debug.json`, legacy profile words, debug arrays, and the **absence** of tokens including `slowZoomIn`, `slowZoomOut`, and `panRight`. Thus current Orion Phase 17 motions can be rejected as “RC2 or legacy,” while no encoded motion is measured. Frozen-frame detection is disabled whenever the motion-plan text merely contains a motion token.

Target: for every non-Static Phase 17/18 scene, extract a small deterministic set of interior frames (normally start/middle/end after excluding transition/fade margins), mask the caption region when burn-in is enabled, and calculate an existing/lightweight pixel-difference or SSIM-style metric. Record numeric evidence, threshold/policy version, timestamps, and pass/fail per scene. Directional inference may remain limited: require material change and corroborate declared transform direction unless a robust crop/feature metric proves scale/translation direction. Static/Hold scenes should tolerate compression and subtitle changes but otherwise remain stable.

No reusable repository-wide visual-difference utility was found in the Phase 19 path. FFmpeg `freezedetect` is a useful coarse read-only primitive, not sufficient scene motion evidence. Avoid heavyweight CV.

## 12. Fade QA

Current Phase 19 does not sample a fade. It trusts Phase 18 validation fields `fadeToBlackEnabled` and duration >=1.0. Black detection merely ignores evidence near the first/last 2.5 seconds. Target luminance sampling immediately inside governed fade windows, with the expected direction and endpoint comparison. A fade must be required only where Phase 17/18 declares one.

## 13. Transition QA

Current transition approval is the absence of the word `advanced` in a legacy motion plan. Target boundary samples must distinguish governed `Cut`, `CrossFade`, and `FadeThroughBlack` behavior with narrow windows. Do not assume every boundary is xfade. Exclude these windows from interior motion metrics.

## 14. Outro requirement

**Yes, an obsolete hardcoded outro remains in the active gate.** `IsPhase18CinematicOutroValidated` requires enabled and >=4.0 seconds; fade-to-black requires enabled and >=1.0 second. `productionQaPassed` and then `motionRc1ValidationPassed` require both. This contradicts the immediately preceding “not required” comment and target authority rules.

Target: no outro, hero, thumbnail, or gallery requirement unless the Phase 18 manifest explicitly declares it as timeline content. Remove both helpers and the `NarrationPlusCinematicOutro` projection from the active path; preserve only if an explicitly versioned migration adapter truly needs them.

## 15. Review and approval model

Current architecture is **hybrid but ambiguously named**:

* Phase 19 automated heuristics produce `recommendation = Approved` and Phase 20 maps that to `phase19ReviewApproved`.
* Phase 20 separately obtains `publishApproved` from a marker file or request flag.
* The gate needs Phase 19 validation, the automated recommendation, and manual/request approval.

Target: `technicalQaApproved` must be the deterministic result of governed technical checks. Optional human/editorial approval must be a separate explicit state. `publishApproved` must never follow merely from technical success. Whether human approval is mandatory should be an explicit review-policy version rather than implicit file discovery.

## 16. Manual review ownership

Phase 19 does not inspect `manual_validation` or approval files. Phase 20 discovers four possible markers (`review/manual-review-approval.json`, `validation/manual-review-approval.json`, root or validation `publish-approved.json`) or trusts the request flag. The database-facing result only reads Phase 20 diagnostics. This is Phase 20 gate ownership today, not Phase 19 authority ownership.

Recommendation: Phase 19 may record `manualReviewRequired`, `manualReviewStatus`, and an independently authenticated approval reference, but technical approval must remain immutable. Phase 20 owns the final publish gate and projects `publishApproved`.

## 17. Publish-gate interaction and Phase 20 handoff

Phase 20 currently consumes only `phase-19-validation.json` and `review/qa-report.json` for Phase 19 decisions; it does not consume a committed Phase 19 authority checksum. Before that gate it writes scene manifests, materializes the plan folder, and invokes a generic final-output validator, allowing independent media rediscovery/compatibility coupling.

Target handoff:

* committed `19-video-qa/{language}` authority, with its validation and checksum;
* the exact Phase 18 authority checksum and manifest-declared media identities referenced by that review;
* an independent manual/editorial approval state according to review policy.

Phase 20 must verify the Phase 19 publication/readback/governance gate and follow its Phase 18 media references; it must not independently choose another MP4.

## 18. Current artifacts

Phase 19 writes non-transactionally, directly into final locations:

* `review/video-review.json`
* `review/qa-report.json`
* `validation/phase-19-review-diagnostics.json`
* `validation/phase-19-validation.json`

The files can be partially written if a later check fails. No manifest, authority checksum, publication report, staging area, commit/readback, cleanup, or reuse semantics exist.

## 19. Target owned artifacts

Use the repository's numbered, language-scoped convention:

```text
19-video-qa/{language}/
  phase19-review.json
  phase19-authority-diagnostics.json
  phase19-publication-report.json
validation/
  phase-19-validation.json
```

`phase19-review.json` should contain request identity, policies, Phase 18 checksum, ordered per-format results, and compact per-scene evidence. Per-format fields: requested, video path/hash, duration, resolution, FPS, video codec, pixel format, audio codec/rate/channels, each QA category, semantic/physical pass, technical approval, and rejection reasons. Per-scene fields: audio-unit/scene identity, format/sequence/times, expected motion and evidence, fade expectations/evidence, narration expectation/evidence, and results. Do not embed frames or binary data.

## 20. Transaction model

Build all candidate JSON under `19-video-qa/.staging/{transaction}/{language}`, validate and read it back, atomically replace the language authority using Phase 17/18 backup semantics, re-read committed state, then write the validation projection. Always clean staging/backup directories. Never touch Phase 18 files; pre/post hashes in tests should prove byte identity. Failure before commit leaves the previous valid Phase 19 authority intact and reports `P19_CANDIDATE_VALIDATION_FAILED`; publication/readback failures use their precise codes.

## 21. Reuse and overwrite

No current Phase 19 reuse exists; every run repeats analysis and overwrites four files. Target authority identity is a distinct checksum over Phase 18 authority checksum, ordered requested formats and QA results, QA policy version, review policy version, and schema/serializer version. It must not copy the Phase 18 checksum.

* `overwriteExisting=false` + identical identity + fully valid committed Phase 19 authority => reuse without media analysis.
* `overwriteExisting=true` => rerun QA and republish Phase 19 JSON; never rerender video.
* Different Phase 18 checksum/policy/format set => regenerate review authority.

## 22. Result projection

Current Phase 19's own JSON lacks the standard frozen-authority projection: `reasonCode`, generated/reused/regenerated, publication committed/readback, authority checksum, validation status, semantic/checksum/manifest validations, and downstream readiness. The generic orchestrator supplies only phase status/generated files around the method's success or exception. `Phase19ReviewApproved`, `PublishGateChecked`, and `PublishApproved` are later read from Phase 20 diagnostics.

Target `phase-19-validation.json` must project all standard fields consistently. Governed technical failure means `status=Failed`, `downstreamReady=false`, `technicalQaApproved=false`, and no inferred publication approval. Success uses `P19_VIDEO_QA_AUTHORITY_ACCEPTED`. `phase19ReviewApproved` should mean technical review approval only if the API contract is explicitly renamed/documented; otherwise introduce `technicalQaApproved` and reserve review approval for the appropriate human/automatic policy.

## 23. Failure codes

Adopt the requested closed set, with one primary reason and structured per-format/per-scene diagnostics:

| Code | Use |
|---|---|
| `P19_UPSTREAM_PHASE18_INVALID` | Missing/inconsistent/uncommitted/non-ready Phase 18 authority. |
| `P19_VIDEO_MISSING` | Manifest-declared requested regular MP4 absent. |
| `P19_VIDEO_HASH_MISMATCH` | Length or SHA-256 differs. |
| `P19_MEDIA_PROBE_FAILED` | Container/stream metadata cannot be read. |
| `P19_DURATION_INVALID` | Governed duration outside frame/container tolerance. |
| `P19_VIDEO_STREAM_INVALID` | Missing/wrong dimensions, FPS, codec, or pixel format. |
| `P19_AUDIO_STREAM_INVALID` | Missing/undecodable/wrong codec, rate, channels, or narration evidence. |
| `P19_MOTION_QA_FAILED` | Governed non-Static scene lacks encoded evidence. |
| `P19_FADE_QA_FAILED` | Declared fade lacks matching evidence. |
| `P19_TRANSITION_QA_FAILED` | Declared boundary behavior lacks matching evidence. |
| `P19_BACKGROUND_MUSIC_QA_FAILED` | Enabled bed absent or clearly masks narration. |
| `P19_SUBTITLE_QA_FAILED` | Sidecar/burn/style/lineage/duplicate-risk failure. |
| `P19_CANDIDATE_VALIDATION_FAILED` | Candidate authority is internally invalid. |
| `P19_COMMIT_FAILED` | Transaction publication failed. |
| `P19_COMMITTED_READBACK_FAILED` | Committed authority differs or cannot validate. |
| `P19_VIDEO_QA_AUTHORITY_ACCEPTED` | Fully committed technical authority accepted. |

## 24. Current test inventory

Repository search found only two explicit Phase 19 tests, both reflection tests in `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs`:

| Test (line) | Behavior | Classification |
|---|---|---|
| `Phase19CinematicDiagnostics_TrustsPhase18VideoDiagnostics` (934) | Accepts enabled 4.0-second outro and enabled 1.0-second fade. | **Obsolete**: enshrines ungoverned fixed outro/fade. |
| `Phase19CinematicDiagnostics_RejectsInsufficientPhase18Durations` (949) | Rejects 3.99-second outro and 0.99-second fade. | **Obsolete**: enshrines fixed thresholds. |

There are no explicit Phase 19 tests for manifest governance, exact requested formats, path traversal, MP4/SRT/ASS hashes, duration tolerance, resolution, FPS, codecs, audio format, per-scene narration, policy-conditioned music, subtitles, duplicate burn risk, encoded motion, fade/transition sampling, static scenes, transactional publication, reuse/overwrite, cleanup, failure codes, standard result projection, Phase 18 immutability, or authoritative Phase 20 handoff.

Phase 18 tests cover useful upstream primitives and publication semantics, but do not certify Phase 19. `Phase18PublicationFilesystemTests.cs` exercises transactional filesystem behavior; `Phase18VideoAssemblyAuthorityTests.cs` covers canonical caption naming, no same-basename collision, media evidence, policy, and upstream authority behavior. Reuse their patterns rather than mislabeling them Phase 19 coverage.

## 25. Obsolete and compatibility test assessment

| Requirement searched | Finding | Classification |
|---|---|---|
| Fixed 4-second outro / 1-second fade | Two direct tests found. | Obsolete. |
| `final-short.mp4` / `final-long.mp4` in Phase 19 tests | None. | Future compatibility-only tests may assert they are ignored. |
| Video directory/first/latest MP4 search | None for Phase 19. | Add still-valid negative tests. |
| Fixed old motion assumptions | No direct test; active production logic requires RC1 profile/debug tokens and rejects new motion words. | Production behavior obsolete; add replacement tests. |
| Mandatory burn-in | No Phase 19 tests. | Must be policy-dependent. |
| Accept missing music when required | No Phase 19 tests. | Add enabled/disabled policy cases. |
| Manual approval / publish gate | No explicit Phase 19-named tests found. | Existing behavior needs focused still-valid separation tests. |

## 26. Current-versus-target matrix

| Concern | Current Phase 19 | Target Phase 19 |
|---|---|---|
| Video source | Hardcoded canonical-looking Short + Long paths | Manifest-declared requested media only |
| Manifest source | Existence check only | Deserialize and validate all committed Phase 18 authority files |
| Duration QA | Positive duration + copied booleans | Probe vs governed total within ~one frame/container tolerance |
| Codec/resolution/FPS | None | Exact policy comparison |
| Audio QA | Stream, global duration/silence/RMS | Per-format stream contract + per-narration-window energy |
| Music QA | Intermediate existence; always required | Policy-conditioned final-media bed evidence and conservative dominance check |
| Subtitle QA | Wrong-path existence only | Hash, parse, lineage, ASS style, burn count, duplicate risk |
| Motion QA | Metadata/debug tokens | Encoded interior frame evidence per non-Static scene |
| Fade QA | Trust fixed Phase 18 flags | Governed boundary luminance evidence |
| Transition QA | “advanced” word absent | Encoded behavior by declared type |
| Outro QA | Mandatory >=4 s + fade >=1 s | None unless authority declares it |
| Manual review | Not Phase 19; implicit Phase 20 markers | Separate explicit review policy/state |
| Publish approval | Separate Phase 20 flag/marker | Remains separate; never inferred from technical success |
| Artifacts | Four ad-hoc review/validation files | Numbered language authority + validation |
| Transaction | None | Stage, validate, atomic commit, readback, cleanup |
| Reuse | None | Identity/policy-based reuse; overwrite forces re-QA only |
| Phase 20 handoff | Validation + heuristic QA report; generic media validation | Committed Phase 19 authority + referenced Phase 18 media |

## 27. Code reuse classification

| Code | Classification | Rationale |
|---|---|---|
| `Phase18MediaToolchainResolver` | **REUSE AS-IS** or extract shared resolver | Correct configured/PATH resolution and version evidence. |
| Phase 18 manifest/media evidence contracts and hash validation patterns | **REUSE WITH PHASE18 AUTHORITY ADAPTER** | They are the canonical input vocabulary. |
| `HasAudioStreamAsync`, structured probe/process runner, hashing | **REUSE QA PRIMITIVE ONLY** | Extend to complete structured media metadata and explicit errors. |
| `ProbeAudioLevelsAsync`, FFmpeg analysis execution | **REUSE QA PRIMITIVE ONLY** | Apply deterministic windows per format; do not aggregate. |
| `blackdetect` / `freezedetect` | **REUSE QA PRIMITIVE ONLY** | Supplemental evidence, not motion/fade/transition proof. |
| `Phase19IsTransitionWindowEvidence` | **REUSE QA PRIMITIVE ONLY** after authority-driven windows | Current global 2.5-second rule is too coarse. |
| Phase 17/18 transactional stage/backup/commit patterns | **REUSE AS-IS** structurally | Proven authority semantics and cleanup model. |
| Frozen Phase 18 compatibility projections | **RETAIN COMPATIBILITY ONLY** | Never authoritative Phase 19/20 selection. |
| Direct `sync`, `tts`, `timing`, `motion`, `scene-assets-v3` discovery | **REMOVE FROM ACTIVE PATH** | Bypasses Phase 18 authority and duplicates upstream QA. |
| `BuildPhase19StoryChecks` and editorial keyword scoring | **REMOVE FROM ACTIVE PATH** | Not final media authority QA. |
| `BuildPhase19VisualChecks` metadata approval | **OBSOLETE** | Rejects governed motion tokens and proves no encoded motion. |
| fixed outro/fade validators | **OBSOLETE** | Ungoverned hardcoded contract. |
| intermediate-file music evidence/current config requirement | **OBSOLETE** | Does not prove final media and mishandles disabled music. |
| `review/video-review.json` and `qa-report.json` readers | **RETAIN COMPATIBILITY ONLY** | Migrate Phase 20 to committed Phase 19 authority. |

## 28. Minimal implementation plan (ordered; not implemented)

1. Define Phase 19 policy/contracts/result/reason codes and a read-only Phase 18 authority adapter.
2. Load manifest, authority diagnostics, publication report, and validation; require full checksum/governance agreement or fail `P19_UPSTREAM_PHASE18_INVALID` before probing.
3. Obtain the exact requested format set and output paths from the validated manifest; resolve paths under the Phase 18 authority root with traversal protection and no fallback.
4. Verify every requested MP4/caption is a regular file with exact manifest byte length and SHA-256.
5. Run configured FFprobe and validate container, streams, governed duration/tolerance, dimensions, FPS, codecs, pixel/sample formats, rate, and channels per format.
6. Validate policy-conditioned SRT/ASS/burn-in evidence, cue lineage, style, and duplicate-presentation prevention without altering captions.
7. Load only the Phase 18-declared ordered scene/timeline evidence needed for strict comparisons; use Phase 15/16/17 authorities only if Phase 18 lacks sufficient declared evidence, never for media discovery or plan re-derivation.
8. Sample a bounded deterministic set of masked interior/boundary frames and evaluate non-Static motion, Static stability, governed fades, and transition types.
9. Sample deterministic narration and optional narration-free tail audio windows; evaluate narration, enabled music-bed evidence, and conservative relative-level limits.
10. Build ordered per-format/per-scene review diagnostics and fail closed with precise reasons.
11. Compute a distinct Phase 19 checksum over Phase 18 checksum, requested formats, ordered results, and policy/schema/serializer identities.
12. Implement reuse/overwrite and transactional staging, candidate validation, commit, committed readback, and cleanup without writing Phase 18.
13. Project standard validation/result fields and separate `technicalQaApproved`, editorial/manual state, and `publishApproved`.
14. Change Phase 20 to consume committed Phase 19 authority and referenced Phase 18 media only; retain legacy report/path readers solely behind explicit migration mode.
15. Replace obsolete tests and add the missing authority, physical, semantic, transactional, performance-bounded, projection, and Phase 18 immutability suites.

## 29. Files/classes expected to change in implementation

No files below were changed by this audit. The minimal future change set is expected to be:

* **Modify** `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`: replace `PhaseVideoQaProductionReviewAsync` active implementation with the dedicated publisher invocation; update Phase 20 gate/handoff and result integration; remove obsolete active helpers while retaining explicitly versioned compatibility readers only.
* **Add** `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase19VideoQaAuthorityPublisher.cs`: Phase 18 adapter, governance gate, probe/hash/subtitle/audio/frame QA, review construction, authority identity, reuse, and transaction lifecycle (split into focused files if repository maintainability warrants it).
* **Add** `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase19VideoQaContracts.cs`: typed manifest/review/per-format/per-scene evidence, policies, results, and failure codes (or colocate internal records with the publisher consistent with Phase 17/18 convention).
* **Modify** `Backend/src/Astronomy.MediaFactory.Core/ContentPlanBatchGeneration.cs` only if the public result contract must add `TechnicalQaApproved`/manual review fields; preserve compatibility for existing `Phase19ReviewApproved`, `PublishGateChecked`, and `PublishApproved`.
* **Modify** `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ContentPlanProductionExecutionService.cs`: project the new explicit technical/manual/publish states from committed Phase 19/20 diagnostics.
* **Replace/extend** `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs`: remove the two obsolete fixed-outro tests and cover dispatch/gate/result compatibility.
* **Add** `Backend/tests/Astronomy.MediaFactory.Tests/Phase19VideoQaAuthorityTests.cs`: governance, manifest media, requested sets, physical/subtitle/audio/motion/transition rules, reuse, failure, and projection.
* **Add** `Backend/tests/Astronomy.MediaFactory.Tests/Phase19PublicationFilesystemTests.cs`: atomic publication, rollback/readback/cleanup, and Phase 18 byte-identity tests.

Do not change Phase 15-18 production semantics. If requested-output support is absent from the frozen Phase 18 manifest, record that as an upstream contract/version prerequisite rather than silently changing Phase 18 during Phase 19 implementation.

## 30. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Subtitle changes create false motion positives | Mask lower caption/style-declared region; sample corresponding cue-stable timestamps where possible. |
| Compression creates false positives/negatives | Downscale/luma-normalize, use calibrated thresholds plus multiple samples, record raw metric; fixture-test codecs. |
| Fade/transition frames pollute motion | Derive exclusion margins from declared transition durations and sample scene interiors. |
| Motion direction cannot be inferred robustly | Require material change and authority corroboration; document direction limitation until a crop/feature metric is proven. |
| Static scenes rejected due to captions/noise | Mask captions, allow bounded codec noise, exclude boundaries. |
| Music thresholds fail intentional silence | Run only when authority says enabled and only in derived eligible windows; use conservative configurable policy. |
| Music masks narration | Compare levels only as a broad sanity check; reject obvious violations, not creative borderline mixes. |
| Long (~600 s) QA is slow | Seek to 3 small samples per governed scene; avoid full-frame/full-audio decode and cache one ffprobe result. |
| Container timestamp rounding | Policy-bound one-frame plus documented mux rounding (35 ms for current 30 fps policy). |
| Manual vs automatic approval confusion | Separate typed `technicalQaApproved`, `manualReviewStatus`, and `publishApproved`; version the review policy. |
| Legacy fallback certifies wrong MP4 | No fallback in authority adapter; path containment and hashes; compatibility only behind explicit version mode. |
| Phase 20 generic validation chooses another file | Make committed Phase 19 references the sole publishing-media input. |
| Partial review publication | Stage/validate/atomic commit/readback/cleanup; preserve prior valid authority on failure. |
| QA mutates Phase 18 | Open read-only, write temp samples outside Phase 18, delete samples, assert all Phase 18 hashes unchanged. |

## 31. Certification criteria / Definition of Done

Phase 19 is certified only when all of the following are demonstrated by automated tests and an authority fixture run:

1. The complete committed Phase 18 manifest/diagnostics/publication/validation set is the canonical direct input and passes all stated governance flags/checksum agreement.
2. Only manifest-requested formats are required (Short, Long, or both), and only manifest-declared contained paths are opened.
3. No first/latest/directory/legacy MP4 fallback occurs; decoy legacy videos cannot influence results.
4. Each final MP4 and enabled SRT/ASS sidecar has exact declared length/SHA-256 and committed readback.
5. FFprobe proves readable container, required video/audio streams, policy dimensions/FPS/H.264/`yuv420p`/AAC/48 kHz/stereo, and governed duration within explicit frame/container tolerance.
6. Narration energy exists in every required scene audio window; intentional tails are not falsely rejected.
7. When background music is enabled, eligible bed evidence and conservative narration-dominance QA pass; when disabled, music/silent tails are not required/rejected.
8. Subtitle enablement/burn mode, exactly-one burn evidence, SRT/ASS identity/lineage/basic structure/style/max-lines/safe margin, and no same-basename duplicate risk pass.
9. Every non-Static scene has masked, interior, encoded-frame motion evidence; Static/Hold scenes use appropriate stability semantics.
10. Every declared fade and transition has targeted physical evidence of the governed behavior.
11. No ungoverned four-second outro, fade, hero, thumbnail, gallery, or editorial-keyword requirement remains.
12. Technical QA failure yields a precise `P19_*` reason, `status=Failed`, `technicalQaApproved=false`, `downstreamReady=false`, and no inferred publish approval.
13. Phase 19 checksum identity and reuse/overwrite semantics match frozen Phase 17/18 conventions; overwrite re-QAs but never rerenders.
14. Authority publication is transactional, candidate and committed readback pass, staging/backup are clean, and failure cannot leave partial authority.
15. Every Phase 18 input remains byte-identical before/after success, failure, reuse, and overwrite.
16. Validation and API projections consistently report status, reason, generated/reused/regenerated, publication/readback, checksum, validation flags, downstream readiness, and separate review/publish states.
17. Phase 20 accepts only committed downstream-ready Phase 19 authority plus its referenced committed Phase 18 media; no independent video discovery occurs.
18. Successful authority reports `P19_VIDEO_QA_AUTHORITY_ACCEPTED`, `technicalQaApproved=true`, and `downstreamReady=true`; manual publication remains governed separately.

## 32. Remaining uncertainties

1. Frozen Phase 18 currently hardcodes both requested formats; the versioned mechanism that will express short-only/long-only must be agreed before Phase 19 can certify those cases.
2. Phase 18 manifest evidence includes SRT but the diagnostics advertise ASS paths; the target contract must decide whether ASS receives first-class manifest length/hash fields in a future Phase 18 schema or Phase 19 validates it through diagnostics plus a governed checksum extension.
3. The authoritative location of per-scene transition/fade/audio-window details exposed to Phase 19 needs confirmation. Phase 18 carries lineage and output evidence, but Phase 19 may need strict read-only Phase 17/16 comparison until Phase 18 exposes a complete scene evidence projection.
4. Product policy must explicitly decide whether `phase19ReviewApproved` denotes technical automation or human editorial review; current behavior uses it for automated recommendation.
5. Authentication and provenance for manual approval marker files are outside the current Phase 19 code and need a product/security contract.
6. Motion/fade/music thresholds require representative encoded fixtures and calibration; they must be versioned, deterministic, and conservative.

## 33. Direct answers to audit completion questions

* **Does Phase 19 consume committed Phase 18 authority directly?** Partially in path choice, **no** in authority semantics: it does not load the manifest/report/diagnostics or validate full governance/checksums.
* **Does it still search legacy video folders?** The Phase 19 method does not search for MP4s, but broader pipeline/Phase 20 compatibility paths remain coupled to legacy projections.
* **Does it require an obsolete four-second outro?** **Yes**, in the active final success expression.
* **Does it physically verify motion?** **No**; it trusts metadata and conditionally suppresses freeze detection.
* **Does it physically verify fades/transitions?** **No**; it trusts fixed flags and text heuristics.
* **Does it validate configured background music?** **No**; it checks an intermediate file/config flag, requires music even when disabled, and supplies no final encoded evidence.
* **Does it validate subtitle policy without duplicate presentation?** **No**; it checks wrong paths and does not parse policy/sidecars/style or duplicate risk.
* **Does it distinguish technical QA from manual publish approval?** Phase 20 separates the manual publish flag, but Phase 19/Phase 20 naming conflates automatic heuristic review approval with technical QA.
* **What should Phase 19 own?** Only the language-scoped committed review authority, diagnostics, publication report, and validation projection described above—never media rendering or upstream plan generation.
* **What should Phase 20 consume?** The committed downstream-ready Phase 19 authority and the exact committed Phase 18 media identities it references, plus separately governed manual/editorial approval.
* **Can Phase 19 certify videos without altering Phase 18?** **Yes, by design**: hash/probe/sample read-only, use temporary diagnostics, publish only Phase 19 JSON, and prove Phase 18 byte identity. The current implementation does not yet meet that certification standard.

## 34. Final recommendation

Do not patch the existing score incrementally. Introduce a dedicated, typed `Phase19VideoQaAuthorityPublisher` modeled on frozen Phase 17/18 authority lifecycle semantics, with a strict Phase 18 adapter and reusable read-only QA primitives. Keep any legacy reports and video projections behind explicit migration compatibility only. Replace the two obsolete outro tests first, then implement governance/hash/probe/subtitle/audio/frame evidence and transaction tests in that order. Finally move Phase 20 from heuristic report/path discovery to committed Phase 19 authority consumption while keeping technical, editorial, and publication decisions separate.
