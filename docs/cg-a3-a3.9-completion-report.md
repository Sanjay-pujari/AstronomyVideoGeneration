# CG-A3 A3.9 completion report

## Delivery
Created the verification contracts, immutable evidence, policy resolver, evaluator, FFprobe JSON parser, real probe/provider binding, asynchronous adapter, diagnostics, and O2-style asset result mapper. Modified bridge DI and the typed adapter registry. Added policy/parser/evaluator/architecture/non-mutation tests and the architecture document.

The implementation reuses `IProcessRunner`, `RenderingOptions.FfprobePath`, the physical artifact registry, checksum service, content-identity factory, descriptor validator, diagnostics writer, and safe-name service. No second process runner, registry, checksum service, or identity system was created. Provider ID: `ExistingFFprobeMediaVerifier`.

## Verification behavior
Supported policies are narration audio, scene video, and variant video V1. Artifact resolution is registry-only and validates correlation, finalized location, file presence/length, checksum, and content identity. Verification is read-only and result-only: media and registry state are unchanged. Container families, duration tolerance, exact dimensions, decimal frame-rate tolerance, stream policy, sample rate, and channel count are evaluated deterministically.

Process start, timeout, exit, invalid JSON/response, source, and media-policy failures map to stable sanitized codes. Caller cancellation propagates. Diagnostics contain safe structured evidence and no stderr or command. The mapper preserves identity, provider, checksum, measurements, attempt, correlation, and failure.

## Test status
The environment does not contain the .NET SDK (`dotnet: command not found`), so restore, build, focused tests, broad tests, and optional real-FFprobe smoke were not executed. Consequently no unexecuted certification claim is made.

## Known limitations and readiness
The current registry lookup does not return its stored kind, so kind ownership is enforced through the request/type policy plus descriptor ownership rather than a kind-bearing lookup. No approved local smoke sample was configured.

Because the required build and test suites could not be executed in this environment, the readiness criteria are not proven.

**NOT READY FOR A3.10**
