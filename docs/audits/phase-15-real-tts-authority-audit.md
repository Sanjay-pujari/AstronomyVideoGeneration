# Phase 15 Real TTS Authority Audit

**Audit date:** 2026-08-10  
**Scope:** current `HEAD`; static inspection only. No provider was called and no media was generated.  
**Decision:** **Phase 15 is not yet a governed Phase 14 consumer.** Its mature scene-level synthesis algorithm is reusable, and production per-SRT synthesis is now fail-closed, but the active path still requires SRT and loose narration files and re-derives scene lineage. It has no numbered, transactional Phase 15 authority.

## 1. Current call graph

| Concern | Exact active location and call |
|---|---|
| Registry/entry | `ProductionPipelineExecutionService.PhaseDefinitions`, `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs:412-433`, maps phase 15 to `PhaseGenerateTtsTimelineV1Async`; that method delegates to `Phase15RealTtsV2Async` at `:9732-9733`. |
| Mode selection | `Phase15RealTtsV2Async`, `:9748-9753`, reads `SubtitleTtsOptions.TtsMode`; every value other than case-insensitive `SceneLevel` throws `P15_LEGACY_CUE_LEVEL_FORBIDDEN`. |
| Phase 14 input | **None.** Neither `14-audio-sync/narration-cue-plan.json` nor `scene-audio-sync.json` is loaded. The ready-made `Phase15SceneAudioUnitAdapter.LoadAsync` is at `Phase14AudioSyncPublisher.cs:206-222` but is unused. |
| SRT loading | `ResolvePhase15SrtPath` (definition around `ProductionPipelineExecutionService.cs:8693`) and `ParseSrtBlocks` (`:10016-10031`), called at `:9756-9771`. |
| Scene mapping | `ResolvePhase15VisualSceneIdLineage` (`:10033-10118`) plus duration-range and narration-file fallbacks (`:10120-10332`), called at `:9771-9778`. |
| Narration loading | `ResolvePhase15NarrationRoot` (`:10338-10344`), then one `{sceneId}.txt` read per expected scene at `:9792-9807`. |
| SceneLevel branch | `Phase15RealTtsV2Async`, `:9787-9843`: one loop iteration and one provider wrapper call per expected visual scene. |
| LegacyCueLevel body | Retained unreachable `else`, `:9844-9859`: one call per parsed SRT block, named from `block.SceneId`. |
| Request/provider/write | `GenerateAndValidateTtsAudioAsync`, `:10441-10489`; resolves diagnostics, calls `IAzureSpeechClient.SynthesizeMp3Async`, writes final MP3 and duplicate raw-debug bytes. |
| SDK synthesis/SSML/retry | `AzureSpeechClient.SynthesizeMp3Async`, `Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechClient.cs:43-76`; `SynthesizeWithVoiceAsync` and retry at `:78-150`. |
| Audio validation/duration | `ValidateGeneratedTtsMp3Async`, `ProductionPipelineExecutionService.cs:10538-10582`; `ProbeAudioContentMetricsAsync`, `:12326-12360`; duration is from ffprobe `:12319-12324`. |
| Concatenation | `ConcatenatePhase15AudioAsync`, `:10004-10014`, called `:9861-9865`. |
| Timeline/write | scene and subtitle anonymous DTOs at `:9817-9834` and `:9881-9913`; language timeline write at `:9943-9955`. |
| Phase validation | inline checks `:9812-9873`, lineage validation `:10243-10267`, and validation/diagnostic writes `:9957-9977`. |
| Result projection | generic phase projection `:15420-15471`; it has no Phase 15 publication result specialization. |

There is also an older compatibility builder, `BuildTtsTimelineItemsAsync` (`:10421-10438`) and `TtsTimelineItem` (`:10591`), which is not called by the active Real TTS V2 route.

## 2. Current responsibilities

| Responsibility | Actual behavior |
|---|---|
| Request-unit planning | Yes: visual-scene IDs, not Phase 14 units, define SceneLevel requests. |
| Scene mapping | Yes: reconstructed from SRT, narration filenames, scene metadata, timing plan, or existing timelines. |
| SRT parsing | Yes, mandatory for both Short and Long. |
| Narration loading | Yes, loose per-scene `.txt` files. |
| Voice/rate | Indirectly: Azure client detects Devanagari, resolves configured language voice/rate, and tries fallback voices. |
| SSML/provider call | Yes, via `IAzureSpeechClient`; SDK, not HTTP and not `NarrationAdapter`. |
| Retry | Azure client retries only service timeouts; unsupported voices advance to a fallback voice. |
| Audio generation | Yes: per-scene MP3, duplicate `.raw`, and combined narration track. |
| Measurement/validation | Yes: ffprobe/ffmpeg metadata, duration, size, peak/RMS/silence. |
| Timeline | Yes: scene entries plus `subtitleItems`, aggregate audio/SRT duration and delta. |
| Subtitle timing/final SRT | No write. It reads pre-existing SRT timing. Phase 16 recalibrates subtitles. |
| Duration calibration | No; timeline labels `durationReconciliationOwner = Phase16` (`:9947-9949`). |

Phase 15 does not translate in its active method. Nearby Hindi adaptation helpers at `:9990-10002` are not part of the active route and must not be revived.

## 3. Current inputs

For the single requested language and both formats, current inputs are:

* `narration/subtitles/{language}/{short|long}.srt` (with resolution compatibility rules), mandatory.
* `narration/{language}/{short|long}/{sceneId}.txt`, with fallback to `narration/{short|long}`.
* `scene-assets-v3/{short|long}/scene-timeline-metadata.json` for expected visual IDs.
* Optional lineage fallbacks: `timing/scene-duration-plan.json`, language-scoped/legacy TTS timelines, and narration filenames.
* Azure Speech options and ffmpeg/ffprobe paths.

It does **not** read either canonical Phase 14 JSON file or Phase 14 validation/publication evidence. Consequently `startPhaseNo=15,endPhaseNo=15` can run only if the legacy loose inputs exist; it cannot meet the target of operating solely from committed Phase 14 authority.

## 4. Phase 14 contract consumption

The canonical DTOs are in `Backend/src/Astronomy.MediaFactory.Core/Phase14AudioSyncAuthority.cs:18-48`:

* `SceneAudioUnit`: `SceneAudioUnitId`, `Sequence`, `Format`, `Language`, `SceneId`, `NarrationBeatId`, `SentenceIds`, sentence index range, `Text`, `TextChecksum`, estimated duration, before/after pauses, `BreakReason`, `SubtitleSegments`, `VoiceProfileRef`, `SpeechStyleRef`, `MayCrossSceneBoundary`, and Phase 7/scene authority references.
* `SubtitleSegment`: stable ID, sequence, parent unit/scene, sentence lineage, text/checksum, reading estimate, two display lines, character span, break reason.
* Authority envelope: schema/plan/execution/event/language, Phase 7 and scene checksums, policy identities, Short/Long streams, authority checksum and publication state.

Phase 14 writes identical semantic authority to `14-audio-sync/scene-audio-sync.json` and `14-audio-sync/narration-cue-plan.json`, plus manifest, diagnostics and publication report transactionally (`Phase14AudioSyncPublisher.cs:57-89`). The adapter already validates committed state and cross-scene prohibition and returns ordered units (`:206-222`), but does not validate the complete validation/report gate requested for Phase 15.

**Compliance:** current production requests equal visual scenes, usually the same cardinality as Phase 14 units, but identity and text are independently reconstructed. Therefore the required invariant is accidental, not contractually guaranteed.

## 5. SceneLevel implementation and one-scene/one-audio

For each requested-language stream:

| Stream | Visual scenes | Requests | physical scene MP3s | subtitle segments/cues |
|---|---:|---:|---:|---:|
| Short | metadata-derived (current downstream contract expects 5) | one per expected visual scene | one per successful request | parsed SRT count; independent and may exceed scenes |
| Long | metadata-derived (current downstream contract expects 9) | one per expected visual scene | one per successful request | parsed SRT count; independent and may exceed scenes |

The same loop handles `en` and `hi`; one execution handles only `context.Request.Language` (`:9763-9765`). Current default behavior is indeed 1 visual scene -> 1 wrapper call -> 1 `{sceneId}.mp3`, regardless of SRT cue count. However, it is not 1 **SceneAudioUnit** -> 1 because Phase 14 units are never loaded. `generatedAudioFileCount` also increments even when validation/file creation fails, so it is not an authoritative physical-success count.

## 6. LegacyCueLevel

* **Configuration trigger:** any `SubtitleTtsOptions:TtsMode` other than `SceneLevel`, including an environment override such as `SubtitleTtsOptions__TtsMode`, missing/invalid bound text, or explicit `LegacyCueLevel`.
* **Current branch condition:** none can reach the body. The guard at `:9750-9751` throws first; `sceneLevelTtsRequested` is constant `true`, making `:9844-9859` dead in this governed method.
* **Retained body:** parses SRT; one request for every `Phase15SrtBlock.Text`; outputs `tts/{language}/{format}/{cue-id}.mp3`; the cue timeline would expose cue IDs/times and one audio path/duration per cue.
* **Tests:** Phase 16/18 tests at `ProductionPipelineExecutionServiceTests.cs:1355-1599` deliberately construct cue-level timelines to verify downstream backward compatibility. They do not execute this branch. No active Phase 15 test certifies the legacy synthesis body.

Classification: **dead in governed RC2 Phase 15, retained compatibility code only**. The older Phase 14 audit statement that arbitrary values activate it is stale relative to current `HEAD`. Invalid values now fail closed. Safest target is to delete the dead body from the governed method and place any truly required compatibility projection behind an explicitly non-production adapter—not a normal option.

## 7. Production reachability and policy

API and Worker base/development/production settings all select `SceneLevel`; the default is also `SceneLevel` (`Contracts.cs:365-377`). Options/environment binding can change the value, but can only cause `P15_LEGACY_CUE_LEVEL_FORBIDDEN`, never cue synthesis. Production per-SRT synthesis is therefore **not reachable at current HEAD** through configuration, environment, unknown values, or ordinary option binding.

Retain the fail-closed check, but replace the string mode as production authority with an invariant (`SceneAudioUnit` input is the only request enumerable). Compatibility callers should use a separately named service/API that is impossible for RC2 phase dispatch to resolve.

## 8. English behavior

* Phase 14 profile is `voice-profile:en`, style `documentary-neutral` (`Phase14AudioSyncPublisher.cs:117-120`), currently ignored by Phase 15.
* Azure defaults prefer `en-US-JennyNeural`, then primary/fallback voices; rate resolves from `ProsodyRate[en]`, `EnglishProsodyRate`, defaults, then legacy rate.
* Text script detection resolves English unless Devanagari is present. SSML is built with voice/rate/pitch; failed SSML falls back to plain text.
* Requests are scene-level from loose narration; output is Azure `Audio24Khz160KBitRateMonoMp3` despite the options label defaulting to `audio-24khz-96kbitrate-mono-mp3`.
* ffprobe supplies actual duration/sample rate/channels/codec; scene files are stream-copied in order into a narration track.
* Timeline fields are those listed in section 19.

## 9. Hindi behavior

* Phase 14 profile is `voice-profile:hi`, same neutral style, currently ignored.
* Default preferred voice is `hi-IN-SwaraNeural`; `DefaultVoiceName` is `hi-IN-MadhurNeural` but is only a diagnostic fallback in Phase 15 resolution, not normally first in `GetPreferredVoices`. English fallback voices remain appended after the Hindi configured voice.
* Rate resolves from Hindi-specific options; Devanagari detection selects Hindi. SSML/punctuation receives no special Phase 15 logic; the general builder and Azure SDK process `।/॥` as text.
* Boundary, MP3, probe, concatenation, and timeline algorithms are identical to English. Subtitle count may differ without changing request count.

## 10. Voice profile ownership

Current voice authority lives in `AzureSpeechOptions` and script detection, not Phase 14. Provider names are not hard-coded in Phase 14, which is correct. Target split:

1. Phase 14 owns provider-neutral `VoiceProfileRef` and `SpeechStyleRef` only.
2. Phase 15 uses a versioned resolver to map those plus language/event policy to Azure voice, rate, style and fallback order.
3. The resolved policy/version is recorded in request identity/timeline. Do not put Azure voice names in Phase 14.

## 11. Provider configuration and abstraction

`AzureSpeechOptions` (`Backend/src/Astronomy.MediaFactory.Contracts/AzureSpeechOptions.cs:3-99`, section `AzureSpeech`) supports subscription key + region/endpoint or managed identity + region/resource ID/client ID. Secrets must never enter diagnostics or identity. It configures SSML, language voices, prosody, pitch, output-format label and timeout retry (default two retries, 750 ms fixed delay). No explicit overall timeout exists beyond cancellation.

The mature abstraction is `IAzureSpeechClient`, registered in `ServiceCollectionExtensions.cs:560`, implemented with Microsoft Cognitive Services Speech SDK. Reuse it; do not create HTTP or duplicate clients. `NarrationAdapter` is a separate documentary provider binding and is not in this call graph.

Actual codec is 24 kHz, 160-kbit/s, mono MP3 (`AzureSpeechClient.cs:43-50`). Options/diagnostics may incorrectly report 96-kbit/s (`AzureSpeechOptions.cs:15`), a certification risk. No canonical channel/codec assertion currently enforces those expected values.

## 12. SSML and pause behavior

`AzureSpeechClient` asks `ISsmlBuilder.BuildSsml(text, voice, rate, pitch)`, rejects unsafe fast prosody by rebuilding at medium, then falls back to plain text if SSML synthesis fails (`AzureSpeechClient.cs:124-150`). It has no style argument, sentence/paragraph/scene pause model, or Phase 14 break translation. Phase 14 `PauseBeforeMs`, `PauseAfterMs`, and `BreakReason` are therefore unconsumed.

Required adapter: construct one SSML document for exactly one unit, escape `SceneAudioUnit.Text`, apply resolved voice/rate/style, and translate only its provider-neutral before/after/break intent into `<break>`/provider style markup. It must not split the unit. If policy cannot represent an intent, fail or document a deterministic fallback; never silently manufacture request boundaries.

## 13. Synthesis request model and identity

Current wrapper arguments are only context, format, scene ID, narration text and output path. The provider receives text plus the whole `AzureSpeechOptions`; voice/rate/SSML are internally derived. There is no request DTO, provider request ID, Phase 14 checksum, unit ID, text checksum, profile/style, policy version, codec identity, attempt diagnostics from the SDK, or Azure result ID.

Introduce an internal Phase 15 request record with unit lineage and resolved policy. Deterministic request identity should hash: source Phase 14 authority checksum; unit ID/text checksum/language; voice profile and resolver version; resolved voice/rate/style; non-secret provider deployment identity; actual codec; SSML/pause policy version; and request schema. Never include credentials or timestamps.

## 14. Physical audio naming and chunking

Current names are `tts/{language}/{format}/{visualSceneId}.mp3`; legacy names would be cue IDs. Debug response bytes are duplicated at `tts/{language}/debug/{format}/{sceneId}.raw`, although they are MP3 bytes on Azure success. Sanitization can generate a random GUID only when an identifier contains no accepted characters, which is unsuitable for authority.

Target naming should use validated stable unit IDs under `15-tts/{language}/{short|long}/{sceneAudioUnitId}.mp3`. No current request-size chunking exists. Phase 14 rejects text over 10,000 characters, but that is a repository safety limit, not a verified Azure service limit. Prefer a structured Phase 15 size failure until a tested internal chunk/merge implementation exists. If chunking is later necessary, chunks stay staging/debug-only and exactly one merged file is authoritative.

## 15. Audio format and validation

Canonical recommendation: retain the already consumed 24-kHz mono MP3, explicitly declare the **actual** 160-kbit/s codec contract (or deliberately change SDK format and downstream tests together). Current validation checks:

* provider response bytes > 0; real provider called/succeeded;
* file size > 1000 bytes;
* ffprobe duration > 0 and first audio stream metadata can be read;
* peak amplitude > 0.001, RMS > 0.0005, and not silent;
* provider/fallback diagnostics and file existence in the calling loop.

It records codec/sample rate/channels but does not reject the wrong values, validate an MP3 container explicitly, hash bytes, detect orphans/duplicates, or compare timeline and filesystem as a package. The target must add SHA-256, byte length, declared format/language/unit ID, decoding/stream checks, duration, expected codec/rate/channel checks, no orphan files, and exact timeline coverage.

## 16. Actual duration ownership

Phase 15 correctly measures physical scene and combined-track duration with ffprobe. Phase 16 reads those values/files and owns scene-duration calibration. That ownership boundary should remain. Phase 15 currently also calculates SRT end and audio/SRT delta; keep this only as non-authoritative diagnostics, not timing authority.

## 17. Track concatenation and authority hierarchy

Phase 15 orders files by the expected-scene loop, writes an ffmpeg concat list, and performs `-c copy`; it adds no explicit silence/gaps (`ProductionPipelineExecutionService.cs:10004-10014`). Phase 14 pause intent is not inserted. Outputs are:

* `video-assembly/{language}/short/narration-track.mp3`
* `video-assembly/{language}/long/narration-track.mp3`

Phase 16 probes them and Phase 18 assembles them, so they are operational compatibility outputs. They should be secondary projections derived from canonical scene files; canonical authority is ordered individual unit audio + timeline. Concatenation belongs in Phase 15 only while Phase 18 requires it, but its checksum/derivation must be manifest-recorded and it must never replace unit authority.

## 18. TTS timeline

Current `tts/{language}/tts-timeline.json` is scene-level at `short.items`/`long.items` plus cue-level `subtitleItems`: version, generated timestamp, language, duration owner, nonblocking audio/SRT mismatch; per scene: format, scene/parent/visual IDs, ordinal `cueIndex`, audio/narration paths and text, duration, subtitle count/source and provider booleans; per subtitle: cue IDs/source/mapping, audio path chosen by positional index, cue text, audio duration and SRT start/end/duration. The positional audio mapping in `subtitleItems` is incorrect when multiple subtitle cues map to fewer scene files.

Target canonical timeline: one ordered entry per SceneAudioUnit containing unit/scene/format/sequence/language, relative audio path, byte length/SHA-256, text checksum, provider request ID if available, profile and resolved non-secret voice policy, actual duration, ordered subtitle segment IDs, and source Phase 14 checksum. Subtitle references carry lineage, never their own production audio identity.

## 19. Subtitle ownership

Phase 15 does not write final SRT, so there is no direct ownership violation. It wrongly requires and parses an already timed SRT to run and republishes its timing. Target Phase 15 should only copy unit-owned `SubtitleSegment` references and actual audio durations. Phase 16 remains the only final subtitle timing/SRT owner for English and Hindi.

## 20. Phase 16 contract

`PhaseDurationCalibrationV1Async` (`ProductionPipelineExecutionService.cs:10594-10750`) reads:

* language-scoped timeline (English legacy fallback allowed);
* legacy `sync/scene-audio-sync.json`, not canonical `14-audio-sync/...`;
* short/long scene metadata;
* timeline `version`, duration owner, mismatch flag/deltas;
* each format's `items`: scene/parent/visual IDs, `audioPath`, `audioDurationSec`/`durationSec`, cue index;
* combined narration tracks to compare summed item duration within 0.1 seconds.

It groups cue-level compatibility items by scene, probes files when necessary, writes `timing/scene-duration-plan.json`, recalculates subtitle timing and emits final subtitle outputs/validation. Migration must teach Phase 16 the pure unit-entry schema and unit subtitle references while preserving a temporary projection with the current fields/paths.

## 21. Phase 17/18 dependencies

Phase 17 reads the language timeline and timing plan for motion duration diagnostics (`:10985`, `:11034-11230`). Phase 18 reads the timeline (`:12519`), combined tracks (`:12542-12543`), final SRT and grouped scene durations; it validates narration-track/SRT duration equality (`:12804-12805`). Neither should consume Phase 15 subtitle timing as final authority. Do not redesign them in Phase 15; preserve compatibility until Phase 16/18 adapters migrate.

## 22. Current artifacts

| Path | Scope/format | Current status | Consumer |
|---|---|---|---|
| `tts/{lang}/{format}/{sceneId}.mp3` | requested language, Short/Long | de facto scene audio; non-transactional | Phase 16/timeline; indirect assembly |
| `tts/{lang}/debug/{format}/{sceneId}.raw` | duplicate provider bytes | debug artifact; not returned in outputs | diagnostics only |
| `tts/{lang}/tts-timeline.json` | requested language, mixed JSON | de facto timeline | Phases 16, 17, 18 |
| `video-assembly/{lang}/{format}/narration-track.mp3` | requested language, combined MP3 | compatibility/assembly projection | Phases 16/18 |
| `validation/phase-15-tts-mode-diagnostics.json` | execution | diagnostics | operators/tests |
| `validation/phase-15-real-tts-v2-diagnostics.json` | execution | diagnostics | operators |
| `validation/phase-15-validation.json` | execution | pass/fail | generic phase execution |

One execution publishes only the requested language. Language scoping prevents normal English/Hindi overwrite, but overwrite cleanup currently deletes the entire TTS root (both languages), as certified by `ProductionPipelineExecutionServiceTests.cs:3133-3175`; the combined track lives outside that root and may remain stale.

## 23. Recommended canonical artifacts

Use one language-scoped numbered authority without redundant copies:

```text
15-tts/{language}/
  short/<sceneAudioUnitId>.mp3
  long/<sceneAudioUnitId>.mp3
  tts-timeline.json
  phase15-manifest.json
  phase15-authority-diagnostics.json
  phase15-publication-report.json
validation/phase-15-validation.json
```

Keep `tts/{language}/...` and `video-assembly/{language}/.../narration-track.mp3` temporarily as explicit compatibility projections. A language execution must transact only `15-tts/{language}`, not replace another language's authority.

## 24. Transaction, failure, cleanup and standalone execution

Current Phase 15 creates directories and writes directly to live paths. It has no staging, atomic swap, rollback, candidate/committed readback, manifest, publication report, semantic authority checksum, or reuse. If scene N fails after earlier scenes, prior files remain and the method continues; it writes timeline/diagnostics before finally throwing when errors exist (`:9957-9977`). Previous authority is not preserved as a coherent package.

Target: load/validate Phase 14 first; synthesize all units in a same-filesystem language staging root; validate candidate plus audio; write manifest/report; atomically swap with backup; committed readback; then update compatibility projections. On any failure, clean staging and preserve previous committed language authority. Phase 15 cleanup owns only its requested-language numbered root, its validation file and explicitly listed projections—never Phase 14 or Phases 1-13.

Standalone Phase 15 becomes valid once it consumes committed on-disk Phase 14 and configuration, without requiring Phase 14 in the same request.

## 25. Provider failure, retry and diagnostics

The Azure client retries only `Canceled + ServiceTimeout`, at most `TimeoutRetryAttempts + 1` (default 3 total), fixed 750-ms delay. Bad/unsupported voice errors try the next voice. All other failures are permanent; there is no exponential backoff/jitter, throttling classification, HTTP status taxonomy, circuit breaker or Phase 15 multi-attempt state. The wrapper catches all non-cancellation exceptions: production returns failed diagnostics; non-production generates a synthetic tone. Synthetic fallback is forbidden for production but still writes debug/media outside governed transaction.

Target bounded policy should retry Azure-documented transient timeout/throttle/service-unavailable categories with capped exponential backoff and jitter, never retry invalid text/config/auth/voice after policy fallbacks are exhausted, and retain cancellation. Record per unit: attempt count, elapsed duration, non-secret provider request/result ID, resolved voice, final status/error category, byte count/SHA-256 and measured duration. Current logs contain voice attempts but no unit ID/provider request ID and current `RetryAttempt` is always `1` at the wrapper level.

## 26. Input validation, fidelity, no translation and checksum binding

Before any provider call, require Phase 14 validation status `Succeeded`, reason `P14_AUDIO_SYNC_AUTHORITY_ACCEPTED`, publication committed, committed validation passed, downstream ready, valid authority checksum and matching plan/event/language. Recompute every `SceneAudioUnit.TextChecksum`; reject missing language units, cross-scene units, duplicate IDs, noncontiguous sequence or changed text. Phase 15 must pass exact `Text` (or an SSML-escaped representation that does not alter spoken words), must not clean/rewrite/translate, and must record `sourcePhase14AuthorityChecksum`.

## 27. No boundary re-derivation matrix

| Current operation | Location | Current use | Target classification |
|---|---|---|---|
| Parse SRT blocks | `:9770`, `:10016-10031` | lineage, subtitle timing; dead branch request boundaries | **REMOVE FROM ACTIVE AUTHORITY PATH**; retain compatibility reader only |
| Load per-scene narration text | `:9792-9807`, `:10338-10344` | production request text/boundary | **REMOVE FROM ACTIVE AUTHORITY PATH** |
| Enumerate/order narration filenames | `:10346-10367` | scene mapping fallback | **RETAIN COMPATIBILITY ONLY** |
| Resolve scenes from metadata/SRT/timing/timeline | `:9771`, `:10033-10332` | production scene boundary/lineage | **REMOVE FROM ACTIVE AUTHORITY PATH**; Phase 14 supplies IDs/order |
| Split text | none in active SceneLevel synthesis | no request split | keep absent |
| Phase 14 SubtitleSegments | currently unused | — | **REUSE VALIDATION/LINEAGE ONLY** |

Required diagnostics: `ttsBoundaryModel=SceneLevel`, Phase 14 unit count, production request count, successful physical file count, subtitle segment count, `perSrtSynthesisRequestCount=0`, `legacyCueLevelReachable=false`; enforce request count == unit count and physical file count == successful unit count.

## 29. Reuse, checksums and authority identity

Current behavior always calls the provider and overwrites files; it has no reuse. Target request/package identity includes source Phase 14 checksum, language, ordered unit IDs/text checksums/order, profile resolver/policy version and resolved voices/rate/style, provider deployment identity, codec, SSML/pause policy and serializer/schema version. Any change invalidates reuse. Reuse only a fully validated committed package.

Every audio manifest entry records relative path, byte length, SHA-256, measured duration, actual codec/container/sample rate/channels, language and unit ID. Compute Phase 15 semantic authority checksum over canonical manifest/timeline semantics, source Phase 14 checksum, ordered physical identities/checksums and policy versions; exclude timestamps and machine-specific absolute paths.

## 30. Validation and reason codes

Existing Phase 15 has only the thrown `P15_LEGACY_CUE_LEVEL_FORBIDDEN`; other failures are free-form strings followed by `InvalidOperationException("Phase 15 Real TTS V2 validation failed: ...")`. Add:

* `P15_UPSTREAM_AUTHORITY_MISSING`, `P15_UPSTREAM_AUTHORITY_INVALID`
* `P15_TTS_POLICY_INVALID`, `P15_PROVIDER_NOT_CONFIGURED`, `P15_PROVIDER_FAILURE`
* `P15_AUDIO_VALIDATION_FAILED`, `P15_TIMELINE_INVALID`
* `P15_CANDIDATE_VALIDATION_FAILED`, `P15_COMMIT_FAILED`, `P15_COMMITTED_READBACK_FAILED`
* `P15_TTS_AUTHORITY_ACCEPTED`

Certification gates: valid Phase 14; complete one-to-one unit coverage; zero SRT requests; correct language/text checksums; all expected files exist, decode, have audio, positive duration and valid checksums; correct codec; no orphan/duplicate audio; timeline/files match; candidate/readback/commit/readback pass; and `downstreamReady=true`.

## 31. Result projection

The validation JSON exposes only phase/name/language/paths, SRT-before flag, version/input source, status, `validationPassed`, diagnostics and errors (`:9971-9972`). Generic result projection only specializes authority fields through Phase 13 (`:15446-15471`). For Phase 15, generated/reused/regenerated, publication committed, committed validation, authority checksum, manifest/semantic/checksum validation and downstream ready are null/false rather than authoritative. Status/reason is generic, not a stable accepted reason code.

Introduce a `Phase15PublicationResult` analogous to Phase 14 and explicitly project every requested field from that single result. Do not infer success merely from files existing.

## 32. Test inventory

### Directly relevant current tests

* `Phase14SceneAudioUnitContractTests.cs:45` — multiple SubtitleSegments still produce one adapter-counted request; `:67` — English/Hindi share boundary count. These are contract tests, not provider/Phase 15 execution tests.
* `ProductionPipelineExecutionServiceTests.cs:876,901` — language-scoped SRT resolution; `:966,1022,1108,1163,1203,1284` — SRT/narration/timing scene-lineage reconstruction, including Hindi; these lock legacy input derivation.
* Same file `:1356,1405,1462` — Phase 16 cue-level duration/group/count compatibility; `:1510,1551,1605,1652,1713` — Phase 18 cue/scene/language duration compatibility.
* Same file `:3134` — Phase 15 overwrite preserves narration/SRT but deletes the entire TTS root.
* `AzureSpeechSynthesisServiceTests.cs:12-86` — success writes, provider/config/write failures and managed identity for another synthesis service.
* `AzureSpeechClient` behavior is indirectly covered by production-adapter tests, but there is no direct current Phase 15 retry/duration/concat/provider-error end-to-end certification.
* `TtsAudioGenerationServiceTests.cs:16-132`, `TtsPackageValidationServiceTests.cs:16-85`, and `TtsAlignmentRepairServiceTests.cs:16-218` test other TTS/package workflows, not RC2 Phase 15.
* `VideoAssemblyIntelligenceServiceTests.cs:1206` verifies one scene narration splits into display cues under SceneLevel; it does not synthesize production audio.

Missing required tests: exact one provider call/file per Phase 14 unit; one unit with four subtitle segments -> one call/file + four refs; equivalent en/hi unit counts; invalid Phase 14 gate; text/checksum mismatch; provider transient/permanent failures; positive probe and wrong-codec rejection; deterministic filename/checksum/reuse; concatenation projection; partial-failure rollback; candidate/committed readback; result projection; no upstream byte changes.

## 33. Obsolete/compatibility tests

Cue-level Phase 16/18 fixtures should remain explicitly labeled **legacy compatibility projection** until consumers migrate. SRT/narration lineage tests (`:966-1284`) must not be governed Phase 15 authority tests; relocate them to a compatibility adapter suite. No test should force per-SRT production synthesis.

## 34. Governing documents

`docs/architecture/PipelineArchitecture.md:39` still says Phase 15 generates cue-level TTS from Phase 14 SRT/sync and must be updated. `docs/FolderStructure.md:352-360` already states the desired one-scene/unit/audio policy but must name numbered Phase 15 artifacts and compatibility paths. Add/adjust an ADR/output contract stating: Phase 14 owns boundaries and neutral pause/profile intent; Phase 15 owns real physical TTS authority and actual duration; Phase 16 owns final timing/SRT; no governed per-SRT synthesis.

## 35. Current-versus-target matrix

| Dimension | Current Phase 15 | Target Phase 15 |
|---|---|---|
| Input | SRT, loose narration, scene metadata/fallbacks | validated committed Phase 14 authority |
| Boundary/scene mapping | re-derived visual scene IDs | exact SceneAudioUnits |
| SRT use | mandatory lineage/timing; dead cue branch | unit SubtitleSegment lineage only |
| English/Hindi | one requested language; same scene loop | same unit model; language-scoped transaction |
| Provider | mature Azure SDK client | reuse client behind typed request/policy |
| Voice | script detection/config | profile -> versioned resolved policy |
| SSML | generic rate/pitch; plain fallback | one unit SSML; pause/style adapter; no split |
| Physical audio/count | `{sceneId}.mp3`, one/visual scene | `{unitId}.mp3`, exactly one/unit |
| Duration | ffprobe actual | retain; manifest/checksum it |
| Timeline | scene items + cue subtitle items | one canonical entry/unit + subtitle refs |
| Combined track | always live assembly output | secondary deterministic projection |
| Final SRT | not written, but prerequisite | never written; Phase 16 owns it |
| Transaction | direct partial writes | stage/validate/atomic commit/readback/rollback |
| Reuse | none | deterministic committed reuse |
| Cleanup | deletes all-language `tts`; leaves track risk | requested Phase 15 language package/projections only |
| Result | generic, authority fields absent | typed publication result fully projected |
| Downstream | Phase 16/17/18 read old timeline/track | canonical unit timeline plus temporary adapter |

## 36. Code reuse classification

| Code | Classification |
|---|---|
| `PhaseGenerateTtsTimelineV1Async` | **REUSE WITH PHASE14 ADAPTER** as dispatch shell |
| `Phase15RealTtsV2Async` SceneLevel loop | **REUSE PROVIDER ALGORITHM**, replace inputs/output governance |
| dead LegacyCueLevel body | **RETAIN COMPATIBILITY ONLY**, remove from governed method |
| `Phase15SceneAudioUnitAdapter` | **REUSE WITH PHASE14 ADAPTER**, strengthen full publication validation |
| `GenerateAndValidateTtsAudioAsync` | **REUSE PROVIDER ALGORITHM**, accept typed request and staging path |
| `IAzureSpeechClient`/options | **REUSE AS-IS** initially; expose resolved/request diagnostics carefully |
| duration/audio probes | **REUSE AS-IS** plus strict codec/checksum/package gates |
| concatenation | **REUSE AS-IS** as compatibility projection, then validate/hash |
| SRT parsing/scene mapping | **REMOVE FROM ACTIVE AUTHORITY PATH**; compatibility only |
| narration-file loading | **REMOVE FROM ACTIVE AUTHORITY PATH** |
| current anonymous timeline builder | **REUSE WITH PHASE14 ADAPTER** as a temporary projection; typed canonical DTO needed |
| old `BuildTtsTimelineItemsAsync` | **OBSOLETE** for governed authority |

## 37. Minimal implementation plan

1. Add typed Phase 15 authority/result/request/timeline contracts and stable reason codes without changing Phase 14 semantics.
2. Strengthen/use `Phase15SceneAudioUnitAdapter` to validate committed Phase 14 validation/report/checksum/identity and text checksums from disk.
3. Make the only governed enumerable the ordered requested-language Short/Long `SceneAudioUnits`; reject legacy mode and delete the dead branch from this route.
4. Add a versioned profile/pause/SSML adapter; pass one exact unit per wrapper call. SRT and narration files disappear from active inputs.
5. Stage deterministic `{unitId}.mp3` outputs, validate/decode/probe/hash every file, build typed one-entry-per-unit timeline/manifest/diagnostics.
6. Enforce counts, no orphans, candidate readback, atomic language commit, rollback and committed readback.
7. Add deterministic reuse identity and correct `PhaseExecutionResult` projection.
8. Derive current language-scoped timeline and combined tracks as explicit compatibility projections while Phase 16/18 migrate.
9. Update Phase 16 to consume unit entries/subtitle refs and remain final SRT/calibration owner; then retire cue compatibility.
10. Add certification tests before enabling authority acceptance.

## 38. Expected files/classes to change

Exact likely implementation surface (not changed by this audit):

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` — dispatch integration, remove active legacy derivation, projection/compatibility wiring.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase14AudioSyncPublisher.cs` — narrowly strengthen/extract the existing read-only adapter only; do not alter Phase 14 publisher semantics.
* New Phase 15 authority publisher/validator in `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/`.
* New Phase 15 DTO/reason-code file in `Backend/src/Astronomy.MediaFactory.Core/`.
* `Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechClient.cs` and/or a new resolver/SSML adapter only where request result ID/style/pause support requires it.
* `Backend/src/Astronomy.MediaFactory.Contracts/AzureSpeechOptions.cs` for versioned non-secret voice policy or codec-label correction.
* `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs`, new Phase 15 authority tests, and focused Azure client tests.
* `docs/architecture/PipelineArchitecture.md`, `docs/FolderStructure.md`, and the appropriate output-contract/ADR documentation.

No Phase 1-14 production authority semantics or artifacts should change.

## 39. Risks

* Phase 16/17/18 assume current timeline fields and combined-track locations.
* Azure request limits are not documented/enforced in code; Phase 14's 10,000-character bound may not match provider limits.
* Hindi fallback ordering can fall through to English voices; resolved voice must be validated for language.
* Actual SDK codec (160 kb/s) disagrees with configured diagnostic label (96 kb/s).
* Stream-copy concatenation represents neither Phase 14 pauses nor explicit gaps and can expose MP3 boundary artifacts.
* Existing cue-level downstream tests may obscure production-boundary guarantees.
* Direct writes and broad cleanup risk partial/stale cross-language output.
* ffmpeg/ffprobe availability is mandatory but not a governed prerequisite record.
* Legacy plans with only SRT/narration cannot run governed Phase 15; compatibility must be explicit rather than silently authoritative.
* Provider request/result IDs may not be available through the current byte-only interface.

## 40. Certification criteria

Phase 15 is done only when:

* standalone 15 reads and validates committed Phase 14 from disk;
* each requested unit causes exactly one production synthesis request and one authoritative file;
* one unit/four subtitle segments yields one request/file and four ordered refs;
* per-SRT request count is zero and LegacyCueLevel is unreachable;
* English/Hindi use identical boundaries, with valid language-specific resolved voices;
* exact text/checksums are preserved; no rewrite or translation occurs;
* actual audio is decoded, measured, format-checked, hashed and fully covered by timeline;
* canonical unit timeline/manifest/checksum validate with no duplicate/orphan audio;
* Phase 15 publishes no final SRT;
* candidate validation, atomic publication, rollback behavior and committed readback pass;
* deterministic reuse works and all identity-changing inputs invalidate it;
* result projection exposes the complete typed publication state;
* Phase 1-14 bytes remain unchanged; language peers are not overwritten;
* required compatibility projections work, and `downstreamReady=true` only after all gates.

## 41. Remaining uncertainties

* The exact Azure maximum accepted text/SSML size is not encoded or tested; confirm against the deployed Speech resource before choosing a limit.
* Whether combined narration tracks remain a long-term Phase 15 output or move to assembly should be decided with Phase 18 owners; current consumers require them.
* `ISsmlBuilder` support for Azure expressive styles and deterministic external break injection needs focused inspection/design during implementation.
* Decide whether the canonical authority is one package per language (recommended to match executions) or a multi-language super-manifest; never let one language transaction replace another.

## 42. Final recommendation

**Adapt; do not rebuild.** Reuse `IAzureSpeechClient`, its voice fallback/timeout algorithm, the mature one-scene loop concept, ffmpeg concatenation and the audio probes. Replace every active boundary/input decision with the already available committed `SceneAudioUnit` contract, add a typed request/profile/pause adapter, and wrap outputs in a language-scoped transactional `15-tts` authority.

Answers to the audit's decisive questions:

1. **Does governed Phase 15 consume Phase 14 SceneAudioUnits?** No.
2. **Can production synthesize per SRT cue at current HEAD?** No; non-SceneLevel configuration fails before the retained dead body.
3. **Is one SceneAudioUnit guaranteed to one authoritative audio?** No; current equality is only visual-scene based and publication is non-transactional.
4. **Do English/Hindi share boundaries?** Yes in the current scene loop, but both derive them outside Phase 14.
5. **What provider code should be reused?** `GenerateAndValidateTtsAudioAsync`, `IAzureSpeechClient/AzureSpeechClient`, `ISsmlBuilder`, timeout/voice fallback, probes and concatenation—with typed lineage/policy and governance adapters.
6. **What should Phase 15 own?** Language-scoped individual unit MP3s, canonical unit timeline, manifest/diagnostics/publication report, physical measurements/checksums, and temporary derived combined-track/legacy projections.
7. **What should Phase 16 receive?** Ordered unit/scene IDs, audio paths/checksums and actual durations plus ordered SubtitleSegment references and source Phase 14 checksum; Phase 16 owns final timing/SRT.
8. **Can certification adapt mature SceneLevel code?** Yes. The missing work is authority consumption, policy adaptation, transactional publication, validation/reuse and downstream projection—not another TTS engine.
