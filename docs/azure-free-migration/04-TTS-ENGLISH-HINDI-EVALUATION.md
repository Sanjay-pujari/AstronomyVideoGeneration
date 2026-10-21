# English/Hindi TTS and alignment evaluation

Audit date: 2026-10-21. Branch: `RecoveryBrach-18-05-26`. Source commit: `0e5ba735191036fdfac8c35c789805b83f03c785`. Audit only; no migration, model installation, generation, publication, or certification run was performed.

## Status: no TTS replacement is approved

**English pilot:** Kokoro-82M with the upstream-documented `af_heart` voice, 24 kHz output, subject to benchmark and voice-weight review. **Hindi pilot:** Kokoro Hindi (`lang_code=h`), with an exact reviewed Hindi voice selected after listening tests; `hf_alpha` is a provisional name to verify against the pinned voice catalogue, not an approved voice. Evaluate a licensed Hindi Piper voice and an Indic multilingual model only if they outperform Kokoro on the same corpus. Do not replace `hi-IN-MadhurNeural` or the existing English voice defaults by name alone.

The required comparison against existing Azure output **was not performed**. No tracked `.wav`, `.mp3` or `.srt` baseline was found by git file enumeration, no external Azure archive was supplied, and no usable Speech binding exists here. No audio was synthesized, regenerated or certified during this audit. Existing certified material must remain untouched. Obtain authorized, read-only English/Hindi Azure samples and their source scripts/voice/rate metadata before approving any replacement. Human bilingual listening review is mandatory.

## Existing contract and the most important coupling

The RC2 Phase15 path consumes committed Phase14 `SceneAudioUnit`s, not arbitrary subtitle cues. SceneLevel TTS is mandatory. A unit over 10,000 characters is rejected. It resolves language/voice/rate, creates request identity from upstream checksum/text/voice/style/codec/policy, synthesizes one file per scene unit, probes and hashes it, and commits a new authority transactionally. Phase16 owns duration reconciliation and final subtitle timing.

- Required Phase15 physical output: **MP3, 24,000 Hz, mono**, with declared 160 kbps codec policy and valid physical duration/hash. Do not substitute raw WAV bytes with an .mp3 extension.
- Shared Azure WAV path: RIFF PCM, 24 kHz, signed 16-bit mono. Bulk TTS validates matching PCM formats before concatenation; pilot quality requires >10KB and >1s. Documentary adapter can request WAV or MP3 and performs its own inspection/normalization.
- Weekly forecast is a separate MP3-only 24 kHz / 96 kbps contract.
- Existing Azure voice IDs, SSML tags, prosody percentages and pitch are not portable local-engine settings. Map logical voice profile/language to a reviewed engine voice and preserve the original references in authority.
- Phase15 currently rejects Hindi voices without `hi-` prefix. A local Hindi voice ID will require a provider-aware compatibility validation change. Hardcoded `azure-scene-voice/1.0` and provider identity in diagnostics must become truthful, versioned local identities for **new** requests; do not rewrite historical manifests or checksums.
- `GenerateAndValidateTtsAudioAsync` passes text to SynthesizeMp3Async. Scene pause values enter request identity, but the audit did not demonstrate that every Phase14 pause is physically realized. Diagnostics derive SSML success flags from configuration rather than an SDK event. Verify silence/pause realization by measuring audio; do not treat these flags as timing ground truth.

## Candidate comparison

| Candidate | English / Hindi / astronomy pronunciation | Timing, pauses, speed, SSML | Formats / resources | License and commercial gate |
|---|---|---|---|---|
| Kokoro-82M | Upstream English example and Hindi language code confirmed. Naturalness, Hindi mixed script, Sanskrit/astronomical vocabulary and long-form consistency unmeasured here. | Segment output is not proof of word timestamps. Do not assume Azure SSML support. Translate supported plain-text/phoneme/prosody controls explicitly; synthesize scene units and insert certified silence where necessary. Benchmark speed controls and consistency. | Upstream example writes 24 kHz WAV, a good normalization starting point. CPU plausible for 82M model, but real-time factor/peak RSS UNKNOWN. Encode validated MP3 through FFmpeg. | Runtime Apache-2.0 verified; README asserts Apache weights. Exact pinned model/voice files and notices unavailable for verification: provisional. |
| Piper | Voice-dependent English quality; Hindi availability/quality must be verified for a specific voice. Review terms such as ecliptic, azimuth, perihelion, Betelgeuse, चन्द्रग्रहण, नक्षत्र, बृहस्पति and mixed numerals. | Length-scale/synthesis controls vary by selected release. Sentence chunking can yield segment boundaries; no assumption of word-level timing or native Azure SSML. Pause insertion and pronunciation lexicon need tests. | Generally suitable for CPU/ONNX offline speech; exact voice sample rate can differ. Resample and convert PCM/MP3 explicitly. Resource use unbenchmarked. | Current engine GPLv3 verified; each voice model/data license unknown here. Do not approve commercial usage from engine license alone. |
| Indic-Parler-TTS or a reviewed Hindi-specialist engine | Appropriate comparison candidate for Hindi prosody, code-switching and domain terms; quality unmeasured. | No verified Azure-compatible SSML or word timestamp contract. Need wrapper plus optional alignment. | Likely heavier than Kokoro/Piper; exact model size, acceleration and RAM/VRAM must come from chosen model card and benchmark. | Attempted upstream GitHub README was 404; model-card/license not retrieved. Evaluation candidate only, not an approved dependency. |
| Existing Azure samples | Required reference for current voice behavior, language pronunciation, short/long pacing and pauses | Preserve actual sample duration and source script. Existing provider does not expose word boundaries in this repository. | No regeneration of certified media; inspect authorized copies only. | Azure subscription unavailable; baseline access is an outstanding evidence requirement. |

## Compatibility design to evaluate, not implement now

Reuse `ISpeechSynthesisService`, `ITtsAudioGenerationService`, `IWeeklySkyForecastTtsSynthesizer`, and `IDocumentaryNarrationProviderBinding`. Add a small neutral low-level synthesis contract only where the direct `IAzureSpeechClient`/AzureSpeechOptions gate prevents existing interfaces from covering RC215. Input must explicitly carry approved text, language, scene/unit ID, voice/style profile, rate and pauses. Output must include actual format, sample rate, channel count, measured duration, model/voice/runtime digests and realized pause diagnostics.

Never feed arbitrary SSML to an engine expecting text. Parse the supported subset (`speak`, voice/lang, prosody, break) safely, map rates to bounded engine controls, reject unsupported required semantics, and avoid XML external-entity processing. Exact Azure voices will not exist locally. Require an explicit reviewed voice mapping per language; fail if missing. No silent English fallback for Hindi.

Normalize PCM before combining; encode once to required MP3; inspect with ffprobe; use measured duration for downstream timing. Ensure chunk joining does not clip phonemes or introduce duplicated words. FFmpeg atempo compression is constrained by current policy (`AllowAudioTempoCompression=false`, MaxAudioTempo=1.0 in inspected files); do not override pacing policy merely to fit video length.

## STT, alignment, language detection and subtitles

**No Azure STT, SpeechRecognizer, cloud language-detection call or word-boundary subscription was found.** AzureSpeechClient detects Hindi by the presence of Devanagari characters. Current Phase16 timing method is `Phase14EstimatedReadingDurationWeightedV1`: it distributes measured scene audio duration across the approved subtitle segments using text/estimated-reading weights. It is not phonetic forced alignment. Preserve this behavior as the compatibility baseline.

| Candidate | Appropriate role | Risks / decision |
|---|---|---|
| faster-whisper, multilingual base/small int8 CPU pilot | Independent transcription QA and omitted/duplicated-word detection | Transcription can hallucinate on silence and normalize Hindi/numerals differently. Never overwrite approved script/SRT with recognized text. Runtime/Whisper MIT verified; converted weights still require provenance review. Accuracy and real-time factor unmeasured. |
| Whisper-compatible local inference | Optional measured baseline for English/Hindi QA | Pin tokenizer/model/language/decoding; timestamps are estimates, not guaranteed forced alignment. No evidence this is needed to remove an Azure transcription dependency. |
| WhisperX / language-specific forced aligner | Optional alignment of the **known approved transcript** to actual audio if weighted timing fails quality gates | WhisperX BSD-2-Clause verified; exact English/Hindi aligner model, coverage, license, access terms and resource use unverified. Reject models with noncommercial restrictions for a commercial workflow. Diarization is unnecessary for single-narrator generated audio and can introduce gated dependencies. |

If an aligner is later certified, Phase16 must remain the single publisher of `final.srt`, `subtitle-timeline.json` and calibrated scene authority. Preserve SceneAudioUnitId, SubtitleSegmentId, sentence ordering, exact normalized text, cue segmentation/line limits, timestamp format, monotonicity and source checksums. Do not emit a competing SRT from the TTS sidecar or introduce duplicate subtitle burn-in in Phase18. Keep weighted timing as an explicitly selected, recorded baseline where accepted, not a hidden fallback after failed required alignment.

## Approval gates

Use the corpus and thresholds in report 09. Minimum evidence is paired English/Hindi Azure-versus-candidate recordings, source scripts/voice/rate/sample format, term list, bilingual human scores, measured cue alignment, codec inspection, pause/scene-boundary checks, long-form consistency, resource metrics and licensing snapshots. Candidate comparison and listening are **PENDING**, not passed.

## Repository evidence

- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs): Phase15RealTtsV2Async (9790+) and GenerateAndValidateTtsAudioAsync (10500+): direct provider/config coupling, voice gate, policy identity, codec and transaction.
- [`Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechClient.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechClient.cs): MP3/WAV formats, SSML/plain-text behavior, language heuristic; no word-boundary hook.
- [`Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechSynthesisService.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechSynthesisService.cs): Legacy narration wrapper and error behavior.
- [`Backend/src/Astronomy.MediaFactory.Rendering/SsmlBuilder.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/SsmlBuilder.cs): Existing SSML construction and prosody safety.
- [`Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs): Independent MP3 96kbps contract.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AzureTtsAudioGenerationService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AzureTtsAudioGenerationService.cs): Bulk/pilot provider-name gates and WAV validation.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase14AudioSyncPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase14AudioSyncPublisher.cs): Scene audio unit and subtitle segment authority.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase16DurationCalibrationPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase16DurationCalibrationPublisher.cs): Weighted timing, measured audio hashes, final.srt publication.
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/NarrationAdapter.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/NarrationAdapter.cs): Existing provider binding, voice resolver, normalizer and audio inspector.
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/SubtitleAdapter.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/SubtitleAdapter.cs): Existing subtitle provider binding and exact text/format inspection.
- [`Backend/src/Astronomy.MediaFactory.Contracts/AzureSpeechOptions.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/AzureSpeechOptions.cs): Voice/rate configuration and language-specific preferences.
