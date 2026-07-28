# CG-A3 A3.11 — Real-Provider Smoke Certification Report

## Certification decision

**CG-A3 A3.11 — NOT CERTIFIED**  
**NOT READY FOR A3.12**

The certification harness is installed deny-by-default. This repository environment did not
have the .NET SDK or explicitly enabled provider configuration, so no external provider,
upload, storage, publishing, social-platform, or scheduling operation was attempted.

## Environment summary

| Evidence | Safe result |
|---|---|
| Environment | Linux container, x64 |
| .NET SDK | Unavailable (`dotnet` was not found) |
| Provider configuration | Not inspected beyond presence checks; smoke switch remained disabled |
| FFmpeg / FFprobe | Certification preflight not run because the .NET SDK was unavailable |
| Credential material | Never read, printed, persisted, or included in this report |

## Implemented certification controls

- Explicit `DocumentaryProductionAdapters:RealProviderSmoke` switch and per-adapter switches.
- Structured preflight results (name, pass state, safe diagnostic, blocking state, remediation).
- Credential-boundary availability check and diagnostic redaction.
- Dedicated workspace write/free-space checks and path-contained recursive cleanup.
- Separate Azure Speech, visual provider, FFmpeg scene, FFmpeg variant, FFprobe, coordinator,
  timeout, cleanup, and architecture test classes under `A3.11-RealProviderSmoke`.
- Hindi smoke has an explicit configuration-based skip.

## Execution evidence

| Command / test group | Passed | Failed | Skipped / not run | Reason |
|---|---:|---:|---:|---|
| `dotnet build Backend/src/Astronomy.MediaFactory.ProductionAdapters/Astronomy.MediaFactory.ProductionAdapters.csproj --no-restore` | 0 | 0 | 1 | `dotnet` executable unavailable |
| Normal regression suite | 0 | 0 | 1 | Cannot execute without .NET SDK |
| A3.11 preflight | 0 | 0 | 1 | Cannot execute without .NET SDK; explicit switch disabled |
| Provider and coordinator smoke groups | 0 | 0 | all | Correctly not attempted before successful preflight |

## Artifact, checksum, and duration evidence

No media artifact was produced. Consequently, there are no artifact paths, checksums,
provider IDs, or media durations to report. A future controlled certification run must append
these values only after every prerequisite gate succeeds.

## Timeout and cleanup evidence

The harness includes assertions for provider/process timeout normalization, caller-cancellation
separation, safe diagnostics, owned-workspace cleanup, and escape rejection. Runtime evidence
is pending execution in a correctly provisioned certification environment.

## Safety statement

**No upload or publishing occurred.** No YouTube, Meta, storage-upload, scheduler, publication,
or social-platform API was invoked. No API key, bearer token, connection string, raw environment
dump, full provider payload, or unsafe process stderr is included.
