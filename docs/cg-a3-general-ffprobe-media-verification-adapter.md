# CG-A3 A3.9 — General FFprobe media verification adapter

## Scope and invariant
A3.9 verifies finalized media. A3.9 does not generate, render, repair, normalize, transcode, concatenate, publish, or upload media. Verification is read-only with respect to media bytes. Supported registered artifact kinds are narration audio, scene video, and variant video; image and subtitle documents remain outside this adapter.

## Existing capability reused
`ExistingFfprobeDocumentaryMediaProbe` uses the Rendering project's `IProcessRunner`, `RenderingOptions.FfprobePath`, timeout/cancellation and process-tree termination. It requests JSON with `-v error -show_format -show_streams -of json`. The isolated typed parser reads format, duration, stream flags, dimensions, rational frame rate, sample rate, and channels. The stable binding ID is `ExistingFFprobeMediaVerifier`.

## Policies and evaluation
The deterministic policies are `NarrationAudioVerificationV1`, `SceneVideoVerificationV1`, and `VariantVideoVerificationV1`. Acquisition and pure evaluation are separate. Checks have fixed order: container, streams, duration, dimensions, frame rate, sample rate, channels. MP4 accepts the FFprobe MOV/MP4 family; WAV, MP3, and raw AAC require their respective names. Durations use integer milliseconds and a default 500 ms tolerance; frame rates preserve decimal rational values and use a default 0.01 tolerance.

Narration requires valid duration/audio metadata and rejects video. Video policies validate required streams, positive duration/dimensions/frame rate, explicit expectations, and audio metadata when audio exists. Subtitle presence/absence is enforced only through explicit request flags.

## Artifact identity and persistence
The adapter resolves by asset ID exclusively through the physical artifact registry. It checks request/correlation ownership, descriptor validity, a nonempty existing final path outside the attempts tree, actual length, lowercase SHA-256, `sha256:<checksum>`, and expected content type. It uses result-only verification: the original registry descriptor is never replaced and no duplicate media descriptor is registered.

## Failure, timeout, cancellation, and diagnostics
Unsupported policies are rejected. Registry, identity, process start, timeout, nonzero exit, malformed response, and policy violations use stable production failure codes. Caller cancellation propagates. Raw stderr, commands, and paths are not emitted. One deterministic `verification-<safe-id>-<attempt>.json` records expectations, safe measurements, evidence, hashes, and outcome.

## Result mapping and DI
The immutable adapter evidence maps to the existing `DocumentaryMediaAssetResult` with Verified/Failed status. Bridge DI binds options (disabled by default), parser, the real probe, provider binding, resolver, evaluator, adapter, mapper, and typed registry availability. A pre-registered fake probe remains replaceable for tests.

## Tests and limitations
Focused tests cover deterministic policies, parsing, pure evaluation, stream/container/measurement checks, architecture independence, and physical-byte non-mutation. Normal tests do not require FFprobe. No real-FFprobe smoke sample is configured, so smoke execution is not part of the focused suite. AAC certification is limited to FFprobe's raw `aac` container name; M4A is accepted through the MP4 family when requested as MP4.
