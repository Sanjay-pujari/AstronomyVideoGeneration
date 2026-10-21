# Regression and certification proposal

Audit date: 2026-10-21. Branch: `RecoveryBrach-18-05-26`. Source commit: `0e5ba735191036fdfac8c35c789805b83f03c785`. Audit only; no migration, model installation, generation, publication, or certification run was performed.

## Audit validation versus future migration validation

Only read-only source/configuration/hardware/upstream-license inspection and report checks were executed. No inference benchmark, listening session, Phase1–20 generation, backend test suite, API smoke run, database migration or social API call was run in this audit. Earlier onboarding test results were from main and do not validate this branch. The absence of a current full-run result is not recorded as a passing test.

A successful provider adapter test is necessary but insufficient for preserving production behavior. The benchmark below is a **proposed acceptance suite**, not a report of measured results. Existing repository validators are authoritative; where they are stricter than these proposed numerical thresholds, keep the stricter requirement.

## Corpus design

Prepare a versioned, non-canonical fixture set with at least five supported astronomy families (for example lunar event, meteor shower, constellation, planet grouping/conjunction, eclipse), English and Hindi, short and long narration. Reuse certified source facts with source IDs, not model-invented observations. Include:

- English: ecliptic, azimuth, perihelion, lunar eclipse, Betelgeuse, Orion, right ascension, UTC/local-time conversion and decimal/degree values.
- Hindi: चन्द्रग्रहण, नक्षत्र, बृहस्पति, शुक्र, क्षितिज, mixed-script object names, dates/times, negative declination, Hindi/Arabic numerals, punctuation, abbreviations and code-switching.
- Scene units with pauses, multiple sentences, short clauses, numerals, uncommon names and near the 10,000-character safety gate. Include both very short and long-form inputs.
- Scene imagery spanning factual sky geometry, infographic, cinematic atmosphere and hybrid backgrounds; all Hero/Thumbnail aspects and six distinct gallery slots.

Freeze fixture hashes, source/claim references and expected identity/role contracts; do not regenerate certified production media or copy secrets/OAuth into fixtures. Authorized baseline audio must match exact scripts/voice/rate settings; unmatched historic audio is not a fair comparison.

## Capability benchmark and gates

| Capability | Baseline → candidate | Quality / pass threshold (proposal) | Runtime / hardware metrics and gate | Rollback |
|---|---|---|---|---|
| Text narration / structured scripts | Current schemas/accepted Azure script samples → Qwen local adapter | 100% schema/required-scene/identity checks; 100% required fact coverage; **0 invented scientific claims**, 0 internal-ID/prompt leaks; exact en/hi output language; existing Long/Short acceptance policy passes | Input/output tokens, prefill/decode tokens/sec, first-token/p50/p95 latency, retries, queue wait, RSS/cgroup and threads. p95 must fit explicitly configured job deadline; no truncation/OOM. SLA must be agreed from measured daily workload, not assumed here. | Disable new local generation; retain old accepted artifacts; use only another certified local model |
| English TTS | Paired existing Azure samples → Kokoro af_heart / reviewed Piper voice | Blind human MOS ≥4/5 and no >0.3-point degradation against paired baseline; 0 critical astronomy/name/number errors in reviewed corpus; no dropped/duplicated sentences; stable speaker character across long run | Real-time factor, p95 per-scene latency, peak RSS, actual sample format/bitrate/duration, silence/clipping. Proposed pilot RTF ≤1 on target hardware; no OOM or mismatched codec. | Keep candidate disabled; preserve reference files and historical authority |
| Hindi TTS | Paired Hindi Azure samples → reviewed Kokoro Hindi / Hindi-specialist alternative | Native/bilingual listening mandatory; same MOS/degradation gate, 0 critical word/numeral errors; review mixed-script pronunciation and consistent Hindi voice | Same metrics; separately report Hindi RTF/latency, Unicode handling and voice-map failures | Do not fall back to an English voice or approve without baseline |
| Subtitle/forced alignment | Current Phase16 weighted timing + manually annotated reference → same method or reviewed optional aligner | 100% exact normalized approved text reconstruction; monotonic nonnegative cues, no forbidden overlap; p95 annotated onset/offset error ≤200ms; max endpoint drift ≤100ms or existing stricter policy; line/cue limits unchanged | Aligner RTF/RSS, annotation count, per-language boundary-error distribution; no duplicate subtitle files/tracks/burn-in | Retain explicitly certified weighted method; stop if required quality cannot be met |
| Optional transcription QA | Known source transcript → multilingual faster-whisper int8 candidate | Initial diagnostic target WER ≤5% en / ≤10% hi after documented normalization; inspect Hindi CER and critical terms separately. Never treat STT transcription as narration authority | RTF, RAM, language/code-switch detection errors, hallucinations on silence. Targets are proposed and unmeasured. | Disable optional QA; do not replace approved script |
| Image/backgrounds | Current certified role/geometry/render constraints → reviewed local diffusion/factual sources | All required roles/aspects/dimensions present; 0 misleading scientific geometry/labels; 100% deterministic factual overlays correct; no text/logo/watermark in required clean background; existing certification passes | Seconds/image, p95 six-page/three-aspect completion, peak RAM/VRAM, queue/retry/failure rate and seed/model digest. Completion must fit chosen production window on measured GPU/CPU. | Stop new generation; do not fake success with prohibited fallback/reused wrong-purpose raster |
| End-to-end rendering | Existing phase contracts/FFmpeg validation → fresh local en/hi Short/Long | Existing Phase19 technical checks pass; Phase20 required roles/hash/authority verify; audio/video and SRT bounds valid; no regression in scientific/caption handling | End-to-end p50/p95, disk/intermediate peak, CPU/GPU contention, exit statuses and retained current-run reports. No zero-test or stale-report success. | Disable new outputs; keep old media/packages immutable |
| Public-media staging | Current IPublicMediaStorageService result/renewal contract → signed filesystem HTTPS | External GET/HEAD/range/TLS, correct byte/hash/MIME, token expiry/renewal and path-traversal/access rejection all pass; same object key/content on renewal | Upload/fetch/poll duration, expiry margin, egress, retention under pending/unknown outcome and backup restore | Preserve object until reconciliation; renew access without republishing |

Human review should use at least two listeners competent in the evaluated language, randomized paired clips, the same playback conditions and recorded per-item scores. Report disagreements and pronunciation errors, not just average MOS. Baseline, candidate, codec-normalized copies and annotations must be hashed and isolated from canonical production roots. Required comparison is still pending.

## Existing test suites to reuse (not run here)

- [`Backend/tests/Astronomy.MediaFactory.Tests/AzureOpenAiAICinematicImageGeneratorTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AzureOpenAiAICinematicImageGeneratorTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AzureOpenAiContentGenerationServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AzureOpenAiContentGenerationServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AzureSpeechSynthesisServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AzureSpeechSynthesisServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/SsmlBuilderTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/SsmlBuilderTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/PublicMediaStorageServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/PublicMediaStorageServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase20PublishingAuthorityContractTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase20PublishingAuthorityContractTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase14SceneAudioUnitContractTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase14SceneAudioUnitContractTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2RealPipelineCertificationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2RealPipelineCertificationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2PublishingExecutionTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2PublishingExecutionTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2PublishingApprovalPersistenceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2PublishingApprovalPersistenceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2PublishingPublicationPersistenceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2PublishingPublicationPersistenceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2YouTubeLongCheckpointTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2YouTubeLongCheckpointTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2YouTubeShortCheckpointTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2YouTubeShortCheckpointTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2InstagramPostVerificationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2InstagramPostVerificationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2FacebookPostVerificationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2FacebookPostVerificationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase7NarrationHandoffTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase7NarrationHandoffTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase8AuthorityArchitectureTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase8AuthorityArchitectureTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationNonMutationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationNonMutationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationDeterminismTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationDeterminismTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentarySubtitleInspectorTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentarySubtitleInspectorTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/AzureSpeechDocumentaryNarrationProviderBindingTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/AzureSpeechDocumentaryNarrationProviderBindingTests.cs)

These suite names do not prove coverage of a new local provider. Inspect their assertions and extend targeted contracts in a separate authorized implementation task. Retain tests that ensure no mutation and semantic/authority failure behavior; never replace them with trivial provider mocks that always succeed.

## Future regression order and side-effect controls

1. Compile and run focused pure/unit tests, then selected-provider startup validation with no Azure bindings. Use separate test output and database; no production connection string or canonical output root.
2. Run each adapter with checked request/response and failure fixtures. Include malformed/truncated JSON, rate limits/timeouts/cancellation, missing weights, checksum mismatch, wrong Hindi voice, wrong codec/sample rate and unsupported SSML/pause semantics. Verify no paid-provider or Azure fallback.
3. For a **fresh** test PlanId run phases 1–20 in en and hi, first with mocked providers to check contracts and then with licensed real local models to certify quality. Verify executed phase ranges, report timestamps/run IDs and underlying command exit status.
4. Check resume/reuse/overwrite/crash during authority publication. Assert unchanged old PlanId/package/media SHA256, phase roots, approval checksum and publication metadata. New model/voice must create truthful new request/content identities, not invalidate history.
5. Mock YouTube/Meta HTTP and OAuth refresh first. Exercise upload/container/create/publish/verify uncertainty, checkpoint resume, signed URL renewal, lease expiry and owner/account validation. No duplicate live posts, no replacement of remote IDs or deletion of existing publication records.
6. If real API behavior cannot be established with sandbox/read-only operations, list the exact missing test and seek separate explicit authorization for controlled non-public test publication. This audit authorizes none. Never treat a GET or local URL probe as proof of a successful remote publish.

## Current evidence checklist

| Evidence | Status |
|---|---|
| Repository dependency/call-site and Phase1–20 mapping | Completed statically for inspected commit; see 01/02 |
| Runtime hardware/credential-name inspection | Completed; no app secret bindings observed |
| Engine license retrieval | Completed for listed sources in 03 |
| Exact selected model/voice/aligner/diffusion licenses and hashes | Incomplete; model-card network fetches failed, some candidates not selected |
| Paired Azure/local English and Hindi audio + human review | Not performed; baseline unavailable |
| Current-branch backend tests/startup / real local inference | Not run by this audit |
| Full fresh RC2 generation/certification and public HTTPS Meta fetch | Not run; later implementation validation |
| Production files/artifacts/publication changes | None authorized or made |

## Repository evidence

- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase14AudioSyncPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase14AudioSyncPublisher.cs): Scene and subtitle identity source.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs): SceneLevel enforcement, provider/policy IDs and physical audio gating.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase16DurationCalibrationPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase16DurationCalibrationPublisher.cs): Current timing and SRT owner.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase18VideoAssemblyAuthorityPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase18VideoAssemblyAuthorityPublisher.cs): Assembly, captions and physical evidence.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase19VideoQaAuthorityPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase19VideoQaAuthorityPublisher.cs): Technical certification.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase20PublishingAuthorityPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase20PublishingAuthorityPublisher.cs): Requested role/hash authority.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs): Remote side effects/checkpoints and approval safety.
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/SubtitleAdapter.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/SubtitleAdapter.cs): Exact reconstruction, file ownership and cue inspection.
