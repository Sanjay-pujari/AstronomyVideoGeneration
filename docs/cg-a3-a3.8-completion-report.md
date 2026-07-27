# CG-A3 A3.8 final component certification report

## Scope and changes

Created separate resolver, existing-FFmpeg binding, focused inspector, DI, and architecture suites. Extended the shared fixture only to remove the unsupported concat-retention setting. Production fixes are limited to strict lowercase SHA-256 syntax, exact checksum/content-identity consistency, malformed provider path rejection, safe `ProcessExecutionResult.ExceptionText` mapping, and preservation of sanitized diagnostics for missing/empty process output. The unused `RetainConcatList` option was removed; workspace-level provider-native retention remains supported. CG-A2, storage, publishing, production execution, and A3.9 were not changed.

## Execution record

- SDK: .NET SDK 10.0.302.
- Restore: succeeded (existing NU1510 and NU1903 warnings).
- Build: succeeded with 0 errors (repository-existing warnings remain).
- Complete focused A3.8 suite: total 115; passed 115; failed 0; skipped 0; duration 2 s.
- Resolver: total 33; passed 33; failed 0; skipped 0; duration 373 ms.
- Provider binding: total 27; passed 27; failed 0; skipped 0; duration 142 ms.
- Inspector: total 16; passed 16; failed 0; skipped 0; duration 81 ms.
- DI: total 4; passed 4; failed 0; skipped 0; duration 118 ms.
- Architecture: total 8; passed 8; failed 0; skipped 0; duration 21 ms.
- Broad suite: total 4,466; passed 3,987; failed 479; skipped 0; duration 2 m 17 s. Failures are unrelated, repository-existing tests outside A3.8.
- Real FFmpeg smoke: not executed.

## Certification evidence

Resolver success covers one scene, stable sequence/asset ordering, explicit duration ownership, duration-sum fallback, and non-mutation. Its failure matrix covers missing/empty dependencies, temporary attempt artifacts, missing files, incomplete or zero media metadata, incorrect type/format/content type, correlation mismatches, duplicate/missing/ambiguous sequence data, variant membership, malformed checksum and identity, and checksum/identity disagreement. Finalized scenes are obtained through the real physical registry.

Provider request validation occurs before creating concat/output artifacts or invoking the runner. Concat bytes are UTF-8 without BOM, ordered, newline-terminated, deterministic, and certified for spaces and apostrophes. Real argument-builder output proves long/short timeout selection and `RequireAudio`/`VideoOnly` forwarding. Process mappings cover dependency missing, start failure, timeout, exception text, nonzero exit, missing output, and empty output without exposing raw stderr; caller cancellation and provider-owned output location are certified.

Inspector projection covers format, duration, dimensions, frame rate, video and audio flags. Missing/empty output bypasses the probe. Probe failures remain safely coded, metadata and video-stream failures are rejected, caller cancellation propagates, and unexpected probe exceptions remain for adapter-boundary normalization.

DI resolves resolver, compatibility validator, binding, inspector, adapter, mapper, and registry; the registry exposes `VariantComposition`, options remain disabled by default, and duplicate stable bindings fail deterministically without provider or inspector invocation. Architecture checks prove no upstream adapter, storage, publishing, generalized verifier/A3.9 dependency, blocking async call, direct process start, or shell invocation; Core has no ProductionAdapters project/assembly reference. Provider identity remains `ExistingFFmpegVariantComposer`.

## Limitations and decision

No real FFmpeg smoke test was executed. Codec, pixel format, time base, and detailed audio layout are not represented by the A3.8 descriptor. A3.9 generalized verification remains intentionally absent.

**READY FOR A3.9**
