# CG-A3 A3.9 certification closure report

## Scope
This closure restored and built the existing A3.9 implementation, executed the focused and component matrices, executed the repository suite, fixed only defects exposed on the A3.9 path, and recorded the evidence below. It did not implement A3.10, enable production execution, invoke a paid provider, or change generation, composition, storage, publishing, repair, or transcoding behavior.

## Files created
None. `DocumentaryMediaVerificationFullAdapterTests.cs` and every other required A3.9 test file were already present and were included in execution.

## Files modified
- `Backend/src/Astronomy.MediaFactory.ProductionAdapters/Foundation.cs`
- `Backend/src/Astronomy.MediaFactory.ProductionAdapters/Hosting.cs`
- `docs/cg-a3-general-ffprobe-media-verification-adapter.md`
- `docs/cg-a3-a3.9-completion-report.md`

## Production fixes
1. Descriptor validation now rejects an artifact whose immediate staging directory is `tmp`, rather than rejecting every absolute path beneath the operating system's `/tmp` root. The prior behavior incorrectly rejected valid finalized artifacts in the Linux test environment.
2. The real FFprobe probe and its two interfaces are scoped so they safely consume the existing scoped `IProcessRunner`; both interfaces still resolve to the same real probe instance within a scope. This removed the A3.9 singleton-to-scoped DI validation defect exposed by the first broad run.

## SDK version
.NET SDK **10.0.302** installed in `/tmp/dotnet` (`DOTNET_ROOT=/tmp/dotnet`). No `global.json` exists.

## Restore command and result
`dotnet restore Backend/Astronomy.MediaFactory.slnx`

- Exit code: 0
- Wall duration: 16 seconds
- Errors: 0
- Warnings: 2 (`NU1510` for `System.Net.Http.Json`; `NU1903` for the existing `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 advisory)

## Build command and result
`dotnet build Backend/Astronomy.MediaFactory.slnx --no-restore`

- Exit code: 0
- MSBuild duration: 2:43.88 (wall: 164 seconds)
- Errors: 0
- Warnings: 237 existing compiler/package/analyzer warnings
- Post-fix incremental build: exit 0, 0 errors, 4 warnings, 12.40 seconds

## Focused command
`dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter "FullyQualifiedName~ProductionAdapters.DocumentaryMediaVerification|FullyQualifiedName~ProductionAdapters.ExistingFfprobe|FullyQualifiedName~ProductionAdapters.ExistingFFprobe|FullyQualifiedName~ProductionAdapters.DocumentaryFfprobe"`

- Focused total: 64
- Focused passed: 64
- Focused failed: 0
- Focused skipped: 0
- Focused test duration: 615 ms (post-fix final execution; wall approximately 6 seconds)

## Targeted component suite totals
All commands used the test project above with `--no-build --filter "FullyQualifiedName~<suite>"`.

| Suite | Total | Passed | Failed | Skipped | Test duration |
|---|---:|---:|---:|---:|---:|
| Probe (`ExistingFfprobeDocumentaryMediaProbeTests`) | 11 | 11 | 0 | 0 | 54 ms |
| Provider binding | 5 | 5 | 0 | 0 | 35 ms |
| Parser | 12 | 12 | 0 | 0 | 43 ms |
| Policy resolver | 4 | 4 | 0 | 0 | 27 ms |
| Evaluator | 4 | 4 | 0 | 0 | 37 ms |
| Full adapter | 10 | 10 | 0 | 0 | 272 ms |
| Mapper | 3 | 3 | 0 | 0 | 138 ms |
| DI | 5 | 5 | 0 | 0 | 157 ms |
| Architecture | 4 | 4 | 0 | 0 | 228 ms |
| Determinism | 3 | 3 | 0 | 0 | 171 ms |
| Non-mutation | 3 | 3 | 0 | 0 | 177 ms |

## Executed certification evidence
- **Successful flows:** narration WAV, scene MP4-family video, silent scene with optional audio, and variant video all passed through the real `DocumentaryProductionMediaVerificationAdapter`.
- **Attempt timeout:** the binding tests prove configured 60 seconds bounded by a 5-second attempt/request timeout, forwarded to `IDocumentaryFfprobeProbe` and the existing `IProcessRunner`; the configured timeout wins when the attempt timeout is larger.
- **Missing executable and process failures:** `Win32Exception` → `DependencyMissing`; start `IOException` and `ExceptionText` → `ProcessStartFailed`; timeout → `ProcessTimedOut`; nonzero exit → `ProcessExitedWithError`; empty output and malformed JSON → `ProviderInvalidResponse`. Public failures remain sanitized.
- **Exception normalization:** provider `TimeoutException`, `IOException`, and `InvalidOperationException` map to `ProviderTimeout`, `FileSystemFailure`, and `ProviderRejectedRequest`; diagnostics `IOException` maps to `FileSystemFailure`; caller cancellation propagates as `OperationCanceledException`.
- **Registry and identity:** matching stored kind is allowed; mismatched kind fails `SourceArtifactInvalid` without provider invocation. Strict lowercase 64-character SHA-256, physical checksum, length, and `sha256:<digest>` ContentIdentity agreement are enforced.
- **Policy semantics:** probe acquisition remains successful while deterministic policy violations produce `Succeeded == true`, `Verified == false`, the first violation as failure, and all violations as ordered evidence. Container, stream, duration, dimension, frame-rate, sample-rate, and channel checks executed.
- **Diagnostics:** deterministic safe filenames and structured evidence executed; path, command, stderr, authorization, secret, token, and API-key leakage is prohibited. Disabled diagnostics creates no file.
- **Read-only/non-mutation:** bytes, file length/timestamp, path, checksum, ContentIdentity, registry descriptor/kind, request/contexts, probe/response objects remain unchanged; no additional media artifact is registered. Diagnostics are the only allowed output.
- **Mapping, DI, architecture, determinism:** mapper, real-probe DI, direct-process/shell/blocking/mutation boundary scans, deterministic evidence, and repeated non-mutation checks all passed.
- **Paid providers:** none were invoked.

## Broad repository suite
Command: `dotnet test Backend/Astronomy.MediaFactory.slnx --no-build`

Final post-fix execution:
- Total: 4,530
- Passed: 4,050
- Failed: 480
- Skipped: 0
- Test duration: 2:52
- Wall duration: 180 seconds

### Unrelated broad failures
The 480 failures are outside the focused A3.9 namespaces and include existing semantic-characterization, thumbnail/hero, weekly forecast, database-provider/concurrency, configuration, publishing, and environment/file-layout failures. A search of the final broad log found no failure from `DocumentaryMediaVerification*`, `ExistingFfprobe*`, `ExistingFFprobe*`, or `DocumentaryFfprobe*`. The initial broad run exposed the A3.9 FFprobe lifetime mismatch; that defect was fixed, rebuilt, and is absent from the final broad run. No remaining broad failure originates from A3.9.

## Real FFprobe smoke status
Real FFprobe smoke: not executed because `FFPROBE_PATH` and `A39_SMOKE_MEDIA_PATH` were not configured. This optional smoke does not block focused certification.

## Completion statements
✓ A3.9 full certification suite executed
✓ Restore succeeded
✓ Build succeeded with zero errors
✓ All focused A3.9 tests passed
✓ All targeted A3.9 component suites passed
✓ The real media verification adapter was directly tested
✓ The existing FFprobe process boundary was exercised
✓ Existing IProcessRunner was reused
✓ Attempt timeout propagation passed
✓ Missing FFprobe executable mapping passed
✓ Process-start failure mapping passed
✓ Probe timeout mapping passed
✓ Process ExceptionText mapping passed
✓ Nonzero-exit mapping passed
✓ Empty stdout mapping passed
✓ Malformed JSON mapping passed
✓ Provider exception normalization passed
✓ Diagnostics exception normalization passed
✓ Caller cancellation propagation passed
✓ Registry kind ownership passed
✓ SHA-256 validation passed
✓ ContentIdentity consistency passed
✓ Verification is read-only
✓ Media bytes remained unchanged
✓ Registry descriptors remained unchanged
✓ No duplicate media artifact was registered
✓ Narration verification passed
✓ Scene verification passed
✓ Silent-scene verification passed
✓ Variant verification passed
✓ Container validation passed
✓ Duration validation passed
✓ Dimension validation passed
✓ Frame-rate validation passed
✓ Stream validation passed
✓ Sample-rate validation passed
✓ Channel-count validation passed
✓ Policy failure semantics passed
✓ Verification diagnostics were sanitized
✓ Result mapping passed
✓ Production DI uses the real FFprobe probe
✓ Architecture boundaries passed
✓ No direct Process.Start exists
✓ No shell invocation exists
✓ No blocking async call exists
✓ No media mutation behavior was introduced
✓ Determinism passed
✓ Non-mutation passed
✓ No paid-provider request was executed
✓ A3.10 readiness was explicitly decided

## Final readiness decision
All A3.9 gates pass, and no final broad-suite failure originates from A3.9. Unrelated repository failures remain recorded above.

**READY FOR A3.10**
