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
- Production DI binds both `IDocumentaryMediaProbe` and timeout-aware `IDocumentaryFfprobeProbe` to the same real scoped `ExistingFfprobeDocumentaryMediaProbe` by default.
- Architecture specifications scan A3.9 for direct process start, shell invocation, blocking async, finalization, and registration calls, and check that Core does not reference ProductionAdapters.

## Executed certification closure (2026-07-27)

The closure used .NET SDK **10.0.302** from `/tmp/dotnet`. Restore completed in 16 seconds with zero errors and two existing package warnings. The full build completed with zero errors (237 existing warnings, 2:43.88); the post-fix incremental build also completed with zero errors.

The complete focused A3.9 filter passed **64/64** (0 failed, 0 skipped; 615 ms). Separately executed component totals were: probe 11/11, provider binding 5/5, parser 12/12, policy resolver 4/4, evaluator 4/4, full adapter 10/10, mapper 3/3, DI 5/5, architecture 4/4, determinism 3/3, and non-mutation 3/3.

The final broad command executed 4,530 tests: **4,050 passed, 480 failed, 0 skipped** in 2:52 (180 seconds wall). Failures are unrelated existing repository failures; none matched the A3.9 focused namespaces. The broad run initially exposed an A3.9 scoped-lifetime defect, which was corrected by making the real probe scoped with the existing scoped `IProcessRunner`; the final broad run contains no such failure.

Executed full-adapter evidence covers narration, scene, silent-scene, and variant success; provider and diagnostics normalization; caller cancellation; safe deterministic diagnostics; and the prohibition on generation/composition dependencies. Timeout evidence proves a 5-second attempt bounds the configured 60-second probe timeout through the provider, `IDocumentaryFfprobeProbe`, and `IProcessRunner`, while a longer attempt uses the configured timeout. Process evidence proves stable sanitized mapping for missing executables, start failures, timeouts, exception text, nonzero exits, empty output, and malformed JSON.

Kind-aware lookup, strict checksum/ContentIdentity agreement, policy-failure ordering, mapper behavior, read-only bytes/descriptors/contexts, no duplicate registration, real-probe DI, architecture scans, determinism, and non-mutation all passed. The descriptor fix preserves temporary-staging rejection while allowing finalized artifacts beneath the operating-system temporary root. Production execution remains disabled and no paid provider was invoked.

Real FFprobe smoke: not executed because `FFPROBE_PATH` and `A39_SMOKE_MEDIA_PATH` were not configured.

**READY FOR A3.10**
