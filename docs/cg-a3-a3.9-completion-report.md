# CG-A3 A3.9 certification closure report

## Delivery
The closure adds separate process-boundary, provider-binding, full-adapter, mapper, DI, determinism, architecture, and full non-mutation suites, together with shared deterministic fixtures. Existing parser, resolver, evaluator, and read-only suites were expanded.

Production fixes driven by certification cover: `Win32Exception` → `DependencyMissing`; timeout-aware FFprobe probing through the existing `IProcessRunner`; provider and diagnostics exception normalization; malformed and empty provider output; strict lowercase SHA-256/content-identity agreement; numeric FFprobe duration parsing; case-safe container families; kind-aware registry reads; and real-probe DI.

## Certification execution (2026-07-27)
- SDK: unavailable (`dotnet: command not found`).
- Restore: attempted; could not run because the SDK is unavailable.
- Build: attempted; could not run because the SDK is unavailable.
- Complete focused and broad suites were attempted; targeted component totals are unavailable because no test host could be executed. Probe, provider-binding, parser, policy-resolver, evaluator, full-adapter, mapper, DI, architecture, determinism, and non-mutation totals are therefore unavailable.
- Optional real FFprobe smoke: not run; `FFPROBE_PATH` and `A39_SMOKE_MEDIA_PATH` were not configured.

## Results and ownership decisions
Attempt timeout ownership is explicit: the adapter computes the bounded timeout, the provider binding forwards it to `IDocumentaryFfprobeProbe`, and the existing probe forwards it to `IProcessRunner`. The registry now exposes a backward-compatible descriptor-and-kind read, and A3.9 rejects a registered kind mismatch. Verification remains registry-only and read-only; it has no generation, composition, finalization, registration, storage, publishing, or paid-provider behavior. Policy mismatches remain successful acquisitions with `Verified == false`; provider/acquisition failures remain `Succeeded == false`.

The added executable specifications cover narration, scene, silent scene, variant, identity, process failures, cancellation, sanitized diagnostics, mapping, real-probe DI, deterministic evidence/filenames, and non-mutation. These statements describe implemented tests, not executed results.

## Known limitations and readiness
No test totals can truthfully be reported and no pass claim is made while the required .NET SDK is absent. The optional real-media smoke remains environment-gated. No A3.10 behavior was implemented and production execution remains disabled by default.

**NOT READY FOR A3.10**
