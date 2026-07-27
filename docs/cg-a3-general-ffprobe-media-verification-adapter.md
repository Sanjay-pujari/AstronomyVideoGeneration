# CG-A3 A3.9 — General FFprobe media verification adapter

## Architecture
A3.9 is a disabled-by-default, asynchronous, read-only verifier for finalized narration, scene, and variant artifacts. It resolves a registered artifact, validates its descriptor and physical SHA-256 identity, resolves a V1 policy, acquires structured metadata through the existing process runner/FFprobe boundary, evaluates pure rules, persists sanitized evidence when enabled, and maps the result. It never generates, renders, repairs, transcodes, finalizes, registers, publishes, uploads, or replaces media.

## Certified boundaries implemented
- The full-flow fixture uses the real registry, checksum and content-identity services, descriptor validator, policy resolver/evaluator, diagnostics serializer, and safe filename generator. Only the external provider binding is fake.
- `ExistingFfprobeDocumentaryMediaProbe` has a timeout-aware A3.9 boundary. The adapter chooses `min(configured probe timeout, attempt timeout)`, the binding owns the request timeout, and the same `IProcessRunner` receives it. No second process runner exists.
- Missing executable (`Win32Exception`) maps to `DependencyMissing`; other start/I/O failures, timeouts, exception text, nonzero exit, empty stdout, and malformed JSON have stable sanitized mappings.
- Provider and diagnostics exceptions are normalized by `IDocumentaryProductionFailureNormalizer`; caller cancellation propagates.
- The registry exposes `GetRegisteredAsync`, returning descriptor and stored kind. A3.9 requires the stored kind to equal the request kind while retaining `GetAsync` compatibility.
- Descriptor checks require exactly 64 lowercase hexadecimal SHA-256 characters, matching physical bytes and `sha256:<checksum>` content identity.
- Policy mismatches produce `Succeeded == true` and `Verified == false`; acquisition/contract failures produce `Succeeded == false`.
- Diagnostics contain structured expected/measured evidence and omit commands, stderr, secrets, and physical paths.
- Production DI binds both `IDocumentaryMediaProbe` and timeout-aware `IDocumentaryFfprobeProbe` to the same real `ExistingFfprobeDocumentaryMediaProbe` singleton by default.
- Architecture specifications scan A3.9 for direct process start, shell invocation, blocking async, finalization, and registration calls, and check that Core does not reference ProductionAdapters.

## Verification status
The certification suites were created, but this container has no .NET SDK (`dotnet: command not found`), so restore, build, focused tests, targeted groups, broad tests, and the optional real FFprobe smoke could not be executed. The smoke also requires both `FFPROBE_PATH` and `A39_SMOKE_MEDIA_PATH`.

## Known limitations
FFprobe parsing intentionally treats unusable numeric values as null; the evaluator rejects required null measurements deterministically. The normal suite records deterministic JSON and does not launch FFprobe. Real codec/container correctness remains covered only by the opt-in smoke. Production execution remains disabled.

**NOT READY FOR A3.10**
