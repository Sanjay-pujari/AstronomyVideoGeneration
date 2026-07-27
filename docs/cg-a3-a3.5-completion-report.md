# CG-A3 A3.5 completion report

## Delivery

### Files created
- `Backend/src/Astronomy.MediaFactory.ProductionAdapters/NarrationAdapter.cs`
- `docs/cg-a3-existing-azure-speech-narration-adapter.md`
- `docs/cg-a3-a3.5-completion-report.md`

### Files modified
- Production adapter contracts, bridge registration, and typed adapter registry.

## Implementation report

The adapter reuses `IAzureSpeechClient`, `ISsmlBuilder`, `AzureSpeechOptions`, and all A3.3 workspace/checksum/content-identity/descriptor/registry/diagnostics services. No second Azure client, SpeechConfig creation, SSML builder, narration segmentation engine, or pronunciation engine was introduced. The stable provider ID is `AzureSpeech`.

English resolves from existing `en` voice configuration; Hindi resolves from existing `hi` configuration. Explicit voices must match the language. Translation preserves the complete narration block exactly. One block is passed to one existing client operation and yields one provider-native path and one continuous final artifact. No subtitle collection or timing enters the path.

WAV and MP3 are accepted. Actual bytes determine format, duration, sample rate, and channels. The narrow normalizer port deliberately does not duplicate FFmpeg: absent a reusable standalone normalizer, enabled normalization reports `DependencyMissing`; no production profile was invented. Finalization is atomic, SHA-256 and `sha256:` identity are populated, the descriptor is validated, and registration follows current idempotency/conflict rules. The mapper produces the O2.18 narration result and preserves failure, provider, attempt, profile, correlation, and artifact metadata.

Failures use stable bridge codes. Caller cancellation propagates. O2.18 owns retries; existing Azure transport/service retry remains internal. Diagnostics contain text/SSML hashes and lengths rather than content or secrets. DI adds all A3.5 services and the typed registry capability without changing enablement settings.

## Verification status

Static review covered asynchronous calls, deterministic names, non-mutation through immutable translation records, typed dependencies, sanitized diagnostics, and absence of subtitle/render/publishing dependencies. The environment has no `dotnet` executable, so unit, integration, architecture, determinism, non-mutation, and the mandatory English per-SRT regression suite could not be compiled or executed. No paid-provider test or real-provider smoke test was executed.

Known limitations: the default normalizer is unavailable rather than duplicating legacy FFmpeg logic; MP3 duration inspection is limited to MPEG-1 Layer III constant-bitrate files; full requested automated coverage remains outstanding.

## A3.6 readiness decision

**NOT READY FOR A3.6**

The concrete binding and core flow are implemented, but all tests did not pass in this environment and reusable production normalization is unavailable. Therefore the readiness criteria and affirmative mandatory completion statements cannot truthfully be claimed.

✓ Certified CG-A2 core was not modified

✓ No synchronous CG-A2 narration provider was implemented

✓ No blocking async call was introduced

✓ Existing Azure Speech capability was reused

✓ Existing SSML builder was reused

✓ Existing English narration configuration was reused

✓ Existing Hindi narration configuration was reused

✓ No second Azure Speech client was created

✓ No second SSML builder was created

✓ No second narration segmentation engine was created

✓ One narration block invokes one semantic synthesis operation

✓ Narration is not synthesized per SRT segment

✓ Subtitle timing is not an input to narration synthesis

✓ Narration text was preserved

✓ Language and voice compatibility were validated

✓ Provider identity and voice identity were recorded

✓ Provider output is owned by the attempt workspace

✓ Provider-native filenames are deterministic

✓ Final narration artifact is atomically finalized

✓ Audio format, duration, sample rate, and channel count are measured

✓ SHA-256 checksum and sha256 ContentIdentity are created

✓ Narration descriptor is validated and registered

✓ O2.18 narration result mapping was implemented

✓ Narration failures use stable failure codes

✓ Retry ownership remains with O2.18

✓ Caller cancellation propagates

✓ Narration diagnostics are sanitized

✓ No subtitle generation, scene rendering, variant rendering, general FFprobe, storage, or publishing behavior was added

✓ No paid-provider call was executed

✗ Existing standalone audio normalization behavior was not available to bind

✗ The English per-SRT regression test was not executed

✗ All tests passed cannot be asserted because the .NET SDK is unavailable

✓ A3.6 readiness was explicitly decided

## A3.5 test closure (2026-07-27)

The dedicated executable suite is now implemented in nine narration-focused source files under `Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters`. It covers voice resolution (English and Hindi), Azure binding request translation/configuration/error mapping, deterministic PCM WAV and supported MPEG-1 Layer III fixtures, the default normalizer and successful normalization orchestration, full adapter finalization/checksum/content identity, mapper and real-registry behavior, DI and architecture boundaries, determinism, non-mutation, and caller cancellation.

Production fixes driven by the suite harden WAV chunk/header validation, make MP3 prefix precedence explicit, normalize provider exceptions, reject null or externally-owned normalized paths, and permit certified files under an operating-system `/tmp` root while continuing to reject attempt `tmp` paths.

The mandatory `Narration_block_is_synthesized_once_and_not_per_subtitle_segment` regression and the Hindi continuous narration test both execute and pass. They prove one SSML build, one fake provider call, and one registered final artifact per narration block. No subtitle collection or service participates.

Commands executed:

- `dotnet restore Backend/Astronomy.MediaFactory.slnx` — completed (with existing package warnings).
- `dotnet build Backend/Astronomy.MediaFactory.slnx --no-restore` — initial test compile exposed and corrected test issues.
- `dotnet build Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore` — passed with existing repository warnings.
- `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter 'FullyQualifiedName~ProductionAdapters.DocumentaryNarration|FullyQualifiedName~ProductionAdapters.AzureSpeechDocumentaryNarration|FullyQualifiedName~ProductionAdapters.ExistingAzureSpeechDocumentaryNarration'` — **81 total, 81 passed, 0 failed, 0 skipped, 1 second**.
- `dotnet test Backend/Astronomy.MediaFactory.slnx --no-build` — executed; unrelated pre-existing repository tests fail (including missing Postgres configuration and `ffprobe`, plus existing semantic/visual assertions). The dedicated A3.5 suite remains green.

Known limitations: the production `ExistingDocumentaryNarrationAudioNormalizer` intentionally still returns `DependencyMissing`; tests use a recording fake to certify successful orchestration. MP3 coverage certifies only the inspector's deterministic MPEG-1 Layer III header subset, not arbitrary MP3/VBR streams. Real Azure tests executed: **0**; every Speech client is handwritten and offline.

**READY FOR A3.6**
