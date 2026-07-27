# CG-A3 A3.10 — Production Execution Host

## Scope and boundary

A3.10 coordinates existing certified production adapters. A3.10 does not implement provider logic. It is located in `Astronomy.MediaFactory.ProductionAdapters`; the certified Core has no dependency on it. A3.10 does not publish or upload media. A3.10 does not itself execute real paid-provider smoke certification.

## Contracts

The certified nullable `IDocumentaryProductionExecutionHost` contract remains as a compatibility facade. New application callers use `IDocumentaryProductionExecutionCoordinator`, which returns the immutable `DocumentaryProductionExecutionResult`. Results preserve execution/correlation identity, ordered variant and scene evidence, partial artifacts, normalized failures, diagnostics and manifest references, and publishing eligibility.

## State machine and sequence

The externally visible states are NotStarted, Preparing, Executing, PartiallySucceeded, Succeeded, VerificationFailed, CertificationRejected, Cancelled, and Failed. Execution is deliberately sequential: each variant, then each scene by its certified `Sequence`; visual generation, narration, subtitles, scene composition, optional scene verification, variant composition, and optional final verification. Composition cannot run after a required predecessor fails. Verification cannot run before the adapter registers its artifact.

## Preparation and availability

The coordinator validates through `DocumentaryMediaPipelineValidator`, creates the certified execution context and workspace, writes `execution-started.json`, and checks all six adapter operations before provider work. Missing adapters produce `AdapterUnavailable` evidence and a failed completion record.

## Request building and identity

`IDocumentaryProductionExecutionRequestBuilder` is deterministic, provider-agnostic and side-effect free. It creates requests from the Core planner's asset plans and immutable project objects, preserving correlation, asset, variant, scene, sequence, language, dimensions, media profile, duration, and subtitle policy. The attempt factory derives every attempt from one execution context and the production clock.

## Timeouts, retries, cancellation, and cleanup

The host supplies operation-specific timeouts, falling back to `DocumentaryProductionAdapters.DefaultOperationTimeoutSeconds`. Adapters continue to own their internal process/provider timeout. Orchestration defaults to one attempt; only retryable failures are retried and deterministic/cancellation failures are excluded. Caller cancellation is never normalized: it propagates through every awaited call and prevents downstream work. Existing adapters/workspace management retain ownership of successful cleanup, quarantine, and finalized artifacts.

## Adapter stages

Visual routing, Azure Speech/SSML, subtitle construction, FFmpeg argument/process behavior, probing, inspection, checksums and `ContentIdentity` remain adapter-owned. Scene composition consumes mapped registered dependency identities. Scene verification gates variant composition; final verification gates success.

## Subtitle strategy

Verification requests explicitly represent BurnedIn, Embedded, Sidecar, or None. Only Embedded requires an MP4 subtitle stream. Sidecar therefore does not incorrectly require an embedded stream; its registered subtitle artifact remains scene evidence.

## Dependency resolution, partial results, and failures

`IDocumentaryProductionExecutionDependencyResolver` reads the existing registry, validates kind and correlation, orders by certified plan sequence, and never mutates registry state. Scene and variant result records retain every completed descriptor if a later operation fails. Stable safe adapter failures are retained without provider exceptions, stderr, credentials, prompts, or command lines.

## Persistence and diagnostics

Configured completion persists the existing `documentary-artifacts.json` registry manifest, safe `documentary-production-execution.json` evidence, and `execution-completed.json`. No finalized registered output is deleted.

## Optional O2.19 and publishing prohibition

The result reserves optional certification evidence and the state model reserves rejection. Concrete O2.19 invocation remains a narrow future integration because no application port is currently available. No storage, upload, publishing, or scheduler service is referenced. `EligibleForPublishing` is evidence only and never performs a handoff.

## Configuration and DI

`DocumentaryProductionAdapters:ExecutionHost` is disabled by default. It configures maximum attempts, verification gates, continuation, retention, persistence, certification intent, and per-operation host timeouts. DI registers the attempt factory, request builder, dependency resolver, coordinator, and compatibility facade once per appropriate lifetime.

## Tests, limitations, and A3.11 readiness

The implementation is designed for fake-adapter orchestration tests without paid calls. This execution environment did not contain the .NET SDK, so compilation and the mandated focused/targeted/broad suites could not be executed. Concrete O2.19 invocation and execution-record mapping back into the certified Core record remain compatibility-boundary limitations. A3.11 real-provider smoke certification must not start until the suites pass in an SDK-equipped environment.
