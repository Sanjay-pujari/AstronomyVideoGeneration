# O2.ORCH.5 Phase 5 hardening audit

## Authority and lineage

`Phase5ExpectedPhase4Authority` carries the current committed aggregate ID and aggregate, Long, and Short checksums. Every generation readback, recovery readback, reuse decision, certification, projection, editorial contract, manifest entry, and Phase 6 resume boundary is evaluated against that current authority. A differing aggregate ID or any differing checksum returns `P5REUSE_SOURCE_PHASE4_MISMATCH`; stale Phase 5 authority is never installed.

The committed evaluator requires the exact seven governing artifacts and their exact roles. It verifies each semantic checksum, physical SHA-256, positive physical size, and `sourcePhase4Checksum`. `certification-diagnostics.json` remains optional and is not authority.

## Transaction architecture

`Phase5PublicationTransactionCoordinator` is the sole publication owner. The pipeline creates the candidate, submits a typed `Phase5PublicationTransactionRequest`, maps the typed result, and installs only the physical `PublishedBlueprintCertification` returned after successful committed readback. Reuse remains outside the transaction because `AlreadyPublished` is an execution decision, rather than a property of physical committed state.

One GUID is used to derive exact staging, backup, manifest snapshot, validation snapshot, marker, failed-authority, and diagnostic paths. Marker states are `Preparing`, `StagedValidated`, `PreviousStateBackedUp`, `EditorialSwapped`, `MetadataPublished`, `Committed`, `RollingBack`, and `RollbackFailed`.

The coordinator validates the in-memory candidate, writes and rereads staging, performs semantic validation, snapshots previous state, swaps authority, atomically merges only `phase5Artifacts` into the existing `JsonObject`, atomically publishes validation, and invokes committed readback before cleanup. The merge preserves unknown top-level/nested properties and the complete Phase 1–4 inventory.

## Rollback, diagnostics, and recovery

Rollback uses only typed exact transaction paths. It preserves the original readback error, removes the new authority, restores the exact prior editorial/manifest/validation snapshots (or restores their prior absence), verifies restored existence, and cleans evidence only after success. A rollback error produces controlled `P5PUB_ROLLBACK_FAILED`, retains evidence, and atomically writes a payload-free transaction diagnostic containing original and rollback errors.

Recovery orders marker files deterministically, rejects marker/path disagreement as `P5PUB_RECOVERY_AMBIGUOUS_STATE`, cleans abandoned pre-swap staging, restores interrupted swaps, evaluates metadata-published/committed authority before finalization, continues rollback, and blocks on `RollbackFailed`. Unmarked wildcard directories are never guessed as transactions.

## Files added

- `Phase5PublicationTransactionContracts.cs`
- `Phase5PublicationTransactionCoordinator.cs`
- `Phase5PublicationRecoveryService.cs`

## Files modified

- `ProductionPipelineExecutionService.cs`
- `ServiceCollectionExtensions.cs`
- `Phase5CommittedAuthorityEvaluator.cs`
- this audit

## Dependency injection

The coordinator and recovery service are registered once as scoped services. The committed evaluator retains exactly one scoped production registration.

## Actual verification (2026-08-01 UTC)

- `/tmp/dotnet/dotnet restore Astronomy.MediaFactory.slnx`: succeeded; 13 projects restored. Warnings: `NU1510` and the pre-existing `NU1903` SQLite advisory.
- `/tmp/dotnet/dotnet build Astronomy.MediaFactory.slnx --no-restore -v:minimal`: succeeded; 0 errors, 200 warnings, 1 minute 56.67 seconds.
- `/tmp/dotnet/dotnet test tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --filter "FullyQualifiedName~Phase5" --logger "console;verbosity=minimal"`: 24 total, 24 passed, 0 failed, 0 skipped, 1 second.

The RC2 endpoint was not executed.

## Remaining technical debt

The complete requested deterministic fault-injection, rollback-failure, recovery-state, pipeline/API, Phase 4 regression, Phase 6, documentary namespace, and RC2 in-memory test matrix has not been implemented or executed in this pass. A testable file-system fault boundary is also still required to prove rollback-operation failures rather than merely handling them. Consequently rollback correctness and RC2 readiness are not claimed.

## RC2 readiness verdict

NOT_READY_FOR_RC2_PHASE_1_TO_5
