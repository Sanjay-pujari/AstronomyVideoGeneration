# CG-A3 — A3.3 Production Execution Bridge Foundation

## Scope and placement

A3.3 is an isolated, disabled-by-default foundation. It adds no provider, renderer, probe process, storage handoff, publishing gate, or production configuration. The new `Astronomy.MediaFactory.ProductionAdapters` project references the certified Core contracts so that it can consume the exact O2.18 request and asset types; Core does not reference this project and was not changed. A separate project was chosen because existing Infrastructure contains provider, storage, and publishing dependencies that are deliberately outside this foundation.

## Boundary and execution behavior

`IDocumentaryProductionExecutionHost` is async and cancellation-aware. Disabled execution immediately returns without creating a context/workspace or resolving an adapter. Enabled execution validates the existing O2.18 request, creates one immutable context and owned workspace, writes start diagnostics, and asks the empty future-adapter registry about capability. A3.3 then records the stable `AdapterUnavailable` failure and returns no fabricated execution record or physical asset. Asset-result composition for future adapters is isolated in `IDocumentaryProductionExecutionRecordMapper`; it preserves the plan's asset type, format, logical ID and correlation while mapping physical measurements. Full variant/execution-record composition is deferred until adapters exist.

The boundary contains no synchronous CG-A2 provider implementation, blocking await bridge, service locator, or publishing reference. Caller cancellation is always propagated as `OperationCanceledException`.

## Modes and configuration

Modes are `Legacy` (existing pipeline authoritative), `Shadow` (non-publication-authoritative bridge output), and `Certified` (eligible only as a future publication candidate after existing validation and O2.19). Publishing is not implemented. Defaults are disabled, `Legacy`, existing-layout preference, and no legacy fallback. Validation requires a legal workspace path when enabled, positive bounded timeouts, and enabled state for Certified mode; validation never creates a directory and options contain no credentials.

## Context, workspace, naming, and finalization

The execution context copies request execution/correlation identity, uses an injected UTC clock and ID generator, canonicalizes the configured root, and exposes an ordinal, read-only metadata copy. Secrets are not accepted as dedicated context fields. Attempt context records operation, logical identities, provider, positive one-based attempt, UTC start, and finite timeout for future adapters.

The manager owns `{root}/{safe-correlation}/{safe-execution}` with `variants`, `attempts`, and `diagnostics`. Helpers produce four-digit scene and two-digit attempt layout, including attempt-only `tmp` directories and kind-specific final directories. Every path is canonicalized and checked below the root. Cleanup and quarantine likewise reject paths outside ownership.

Safe names use Unicode Form KC, collapse runs outside ASCII `[A-Za-z0-9._-]`, trim dots/underscores, recognize Windows reserved names on every OS, and cap names at 100. Unsafe, empty, reserved, overlong, and consequently collision-prone transformations receive a deterministic prefix plus the first 20 lowercase SHA-256 hex characters of the original UTF-8 ID. Random suffixes are never used.

Finalization accepts only a nonempty owned attempt-temp file. It streams an initial checksum, safely copies into a final-directory staging file, flushes it to durable storage, verifies it, then atomically renames it. Existing equal content is idempotently reused; unequal content conflicts unless replacement is explicit. Partial final files are never exposed.

## Physical artifacts, identity, registry, and probe

`DocumentaryChecksumService` streams SHA-256 asynchronously and returns 64 lowercase hex characters. `DocumentaryContentIdentityFactory` accepts only that strict representation and creates `sha256:<digest>`; paths and URIs never define content identity. The inspector checks regular-file existence, nonzero length, supplied content type, checksum and identity, then optionally copies only actual values returned by the injected probe. `NullDocumentaryMediaProbe` reports `AdapterUnavailable` and never guesses.

Descriptors are immutable and pure validation rejects blank logical/correlation identity, relative or temporary paths, empty length, invalid attempts, malformed checksums, malformed content identity, and checksum/identity disagreement. The scoped registry keys by logical `AssetId`, enforces a single correlation, makes equal-checksum replay idempotent, rejects conflicting content, returns ordered immutable snapshots, and atomically persists deterministically ordered `documentary-artifacts.json`.

## Failures and diagnostics

The stable taxonomy includes all A3.2 provider, source/output, media, process, filesystem, cancellation, and A3.3 adapter-unavailable codes. The normalizer maps known filesystem, timeout, cancellation, argument, and state exceptions to sanitized stable messages. Caller cancellation is rethrown; owned cancellation maps to provider or process timeout by operation kind. Exception text and stack traces are not result data.

Diagnostics use deterministic camel-case JSON, UTC values supplied by callers, redaction of credential-like names, same-directory temporary writes, and atomic replacement under `diagnostics`. Artifact manifest persistence is fatal. An attempt diagnostic failure is secondary and future adapter orchestration must preserve the primary failure while recording/logging the secondary failure.

## Dependency injection and lifetimes

`AddDocumentaryProductionBridge(configuration)` binds and validates its isolated section without changing any existing registration. Stateless naming, hashing, identity, validation, clock/ID, diagnostics, failure normalization, null probe, empty registry capability, and mapper services are singletons. Context factory, inspector, host and workspace manager are scoped to execution ownership. The mutable artifact registry is scoped. No six synchronous O2.18 ports are registered.

## Tests and determinism

Tests cover safe defaults and validation, Unicode/reserved/colliding/long names, the standard SHA-256 vector and cancellation, strict content identity, owned deterministic paths, traversal rejection, missing/empty/conflicting/idempotent/replaced final files, registry ordering/idempotency/conflict/manifest persistence, pure descriptor validation, failure normalization, and source inspection for forbidden blocking patterns. Immutable records, copied context metadata, immutable registry snapshots, stable sorted manifests, explicit clocks/IDs, and pure validators establish the non-mutation and determinism boundaries.

## Extension points and deferred work

A3.4–A3.9 can provide asynchronous capabilities behind `IDocumentaryProductionAdapterRegistry`, use attempt/workspace/inspection services, register physical descriptors, and map them through the focused O2.18 asset mapper without redesigning ownership.

Deferred: real visual adapter; Azure Speech adapter; subtitle extraction; FFmpeg scene adapter; FFmpeg variant adapter; FFprobe implementation; storage handoff; publishing gate implementation; full failed/partial O2.18 execution-record composition; retry orchestration and provider-specific diagnostic policies.

## Readiness

The foundation is isolated and ready for the visual-adapter program only after its architecture and unit test suite passes in a .NET 10 SDK environment. No A3.4 capability is included here.
