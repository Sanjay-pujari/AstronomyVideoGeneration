# CG-A3 A3.5 — existing Azure Speech narration adapter

## Scope and invariant

A3.5 adds only the asynchronous O2.18 narration production binding. **Narration is not synthesized per SRT segment. Subtitle timing is not an input to narration synthesis. A3.5 does not generate subtitles. One narration block produces one final narration artifact.** Scene/variant rendering, publishing, and general FFprobe verification remain outside this increment.

The adapter preserves one `DocumentaryNarrationBlock` as one semantic call to the repository's `IAzureSpeechClient`. It uses the existing `ISsmlBuilder`; it does not segment text and does not create a Speech SDK client or SSML formatter. The existing client retains ownership of SDK transport retries and cancellation.

## Translation, voices, and provider binding

`DocumentaryNarrationVoiceResolver` deterministically maps `English` to configured `en` voices and an `en-IN` bridge locale, and `Hindi` to configured `hi` voices and `hi-IN`. An explicit compatible voice wins; configured language voice and the applicable existing default follow. Missing configuration and cross-language voices fail explicitly. Provider identity is the stable, credential-free `AzureSpeech` value.

The immutable provider request preserves asset, instruction, scene, variant, correlation, language, narration text, requested format, audio profile, attempt, and owned output directory. WAV follows the existing single-SSML operation. MP3 follows the existing client's plain-text entry point, which builds SSML internally, thereby avoiding double construction. Output is durably written under the attempt workspace using `provider-azure-speech.wav` or `.mp3`.

## Audio and artifact flow

The focused inspector reads actual RIFF/WAVE headers or MP3 frame metadata to measure encoding, duration, sample rate, channels, and byte length. It is intentionally not a general media/FFprobe adapter. A profile mismatch is rejected unless bridge normalization is enabled. The normalization port is narrow; the default bridge binding reports `DependencyMissing` because this repository has no standalone existing audio-normalization operation suitable for reuse. It does not duplicate FFmpeg command construction.

After inspection, the A3.3 workspace manager atomically finalizes the file. Existing inspection/checksum/identity services create SHA-256 and `sha256:` content identity, descriptor validation runs, and the existing registry enforces idempotent same-byte replay and conflicting-byte rejection. Visual descriptor fields remain absent.

## Failures, timeouts, retries, and cancellation

Disabled/missing bindings, configuration, incompatible voices, provider timeout/rate limit/authentication/unavailability, invalid/missing/empty output, duration failure, profile mismatch, and normalization dependency failures use stable CG-A3 codes. Raw provider diagnostics and credentials are not emitted. Caller cancellation is always rethrown. There is no adapter retry; O2.18 owns attempts. Existing Azure SDK/service sub-attempt behavior remains internal.

## Diagnostics and DI

One `narration-<safe-asset-id>-<attempt>.json` diagnostic records identities, voice/locale/provider, measured audio metadata, hashes and lengths (not full text/SSML), checksum, content identity, elapsed time, and safe request ID. `AddDocumentaryProductionBridge` registers the voice resolver, existing-client provider binding, focused inspector, normalization port, adapter, mapper, and typed registry capability. Configuration is disabled by default and no production appsettings were changed.

## Validation and limitations

Automated tests must fake `IAzureSpeechClient`; paid Azure calls are prohibited. The current environment does not contain the .NET SDK, so compilation and tests could not be executed here. MP3 inspection uses the first valid MPEG-1 Layer III frame and constant-bitrate byte duration; variable-bit-rate certification requires an already-approved metadata capability. No real-provider smoke test was run.
