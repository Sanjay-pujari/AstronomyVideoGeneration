# O2.ORCH.4 Task 5A — Hardened Phase 4 Atomic Publication Audit

## Scope and frozen behavior

The change is confined to Phase 4 publication contracts, infrastructure, coordinator registration, and this audit. Documentary intent, blueprint projection, `DocumentaryBlueprintBuilder`, and Phase 1–3 production behavior were not edited.

## Transaction and locking model

Every staging and backup directory now contains a typed `Phase4TransactionRecord` with execution ID, transaction ID, aggregate checksum, UTC creation time, state, and deterministic checksum. Recovery ignores unmarked directories and transactions younger than the stale threshold. Execution locking combines the repository's execution-keyed in-process serialization with an exclusive lock file so independent processes cannot publish the same execution concurrently.

Backup is treated as a mutation from its first operation. A failure after moving authority or copying manifest/validation enters the same rollback path as a commit failure. Rollback restores authority, manifest, and success validation and removes candidate transaction debris.

## Physical and committed certification

`Phase4PublishedAuthorityValidator` now enforces the exact seven-file inventory, typed deserialization of all files, deterministic checksums, canonical and embedded variant checksums, byte-exact Long/Short projection equivalence, knowledge-selection/traceability identity, editorial-only knowledge exclusion, scene identity/order/count/duration/transition correspondence, and build-report correspondence.

`Phase4CommittedStateValidator` composes physical authority validation with the shared manifest's seven Phase 4 entries, physical SHA-256 checks, and typed success-validation checksum and commit-marker checks. It is used for idempotency and post-commit certification, so `P4PUB_ALREADY_PUBLISHED` cannot be returned for a damaged manifest or success validation.

## Validation marker and manifest stability

Success validation is a typed `Phase4ValidationRecord`; its flags are populated only after staged evidence succeeds, and its deterministic checksum follows the serializer's semantic checksum convention. It remains the last commit marker. The manifest updater parses and preserves the existing shared JSON root byte-semantically, replacing only the already-canonical `phase4Artifacts` projection; Phase 1–3 members are not reconstructed or modified.

## Policy, failures, and injection

`ReplaceExisting` and `RemoveStaleTransactions` now affect execution. Public fault injection exposes all eleven required checkpoints. Failures are separated across recovery, staging, serialization, staged validation, manifest preparation, backup, authority commit, manifest commit, validation commit, post-commit validation, and rollback reason codes rather than being uniformly reported as lock failures.

## Test evidence

Focused tests executed: **0**. Full tests executed: **0**. The container does not provide the .NET SDK (`dotnet: command not found`), so compilation and executable fault/concurrency certification could not be performed here. Static checks and repository diff checks were performed, but they are not substitutes for the mandated test matrix.

## Verdict

The implementation has been materially hardened, but without compilation and the required executable fault/recovery/concurrency suite it cannot honestly be certified for Task 6.

NOT_READY_FOR_PHASE_4_TASK_6
