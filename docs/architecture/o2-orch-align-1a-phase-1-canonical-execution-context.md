# O2.ORCH.ALIGN.1F — Phase 1 Final Coherence Certification and Freeze

## Current declaration

This is the sole current Phase 1 architecture declaration. The historical 1C–1E findings are retained only in the change log below; they do not describe the current implementation.

## Certified architecture

Phase 1 owns one lifecycle-locked decision sequence: validate input, project canonical and compatibility publications, perform pair-aware recovery, read and aggregate active validation, then select reuse, manifest repair, compatibility repair, or full combined publication. Independent canonical and compatibility commit APIs are blocked with `P1_TRANSACTION_COORDINATOR_REQUIRED`.

Downstream invalidation is reversible: Phase 2–20 paths move to a transaction-specific `.phase1-downstream-backup-{id}` quarantine, partial staging exposes its move ledger for reverse-order rollback, and quarantine deletion occurs only after coherent final metadata publication.

Provisional validation is explicitly `Publishing` and uncommitted; only the coordinator publishes the final `Succeeded` document after manifest validation and downstream staging. `PreviousPhaseSucceeded` rejects Phase 1 success documents without committed validation metadata.

The combined coordinator uses one transaction ID for canonical authority, compatibility projection, validation, and manifest staging/backups/failed evidence. Manifest staging reads its six checksums exclusively from the canonical and compatibility staging roots while recording only their final active workspace paths and expected authority lineage. The mutation boundary is non-interruptible and every ordinary exception after it enters rollback, which restores manifest and validation before compatibility and canonical authority.

Manifest rollback treats a previously absent manifest as a valid restored state; when one existed it must be restored and semantically validated. Compatibility-repair rollback validates canonical authority, canonical-owned compatibility lineage, and any restored manifest.

Manifest-only repair stages and validates a replacement, backs up the active manifest, atomically promotes the replacement, validates it against the unchanged active publication, and retains failed evidence while restoring the backup on failure. Compatibility-only repair similarly stages both projections and a manifest sourced from those staged projections, then promotes and validates the pair without rewriting canonical authority or invalidating downstream outputs.

The final execution outcome is assembled from the completed transaction result; provisional validation cannot claim downstream invalidation or a successful manifest. Rollback compatibility acceptance is semantic: restored file checksums must match the compatibility lineage recorded by the restored canonical execution context, rather than merely containing parseable JSON.

Manifest validation requires exactly six recognized Phase 1 artifacts, exact semantic role cardinality, checksum and lineage fields, and active workspace-contained paths. Malformed properties and unsafe staging, backup, failed, quarantine, or transaction paths produce structured validation diagnostics rather than escaping as property-access exceptions.

Recovery considers only canonical/compatibility backup pairs with the same transaction ID, newest-first, validates compatibility using the candidate canonical backup's own checksum lineage, and restores only matching manifest/validation backups when available. It never combines unrelated backups or restores canonical alone, and it restores isolated original active sets when a candidate restoration fails.

## Concise change log

- **1C:** introduced canonical authority, structured validation, and lifecycle ownership.
- **1D:** added structured outcomes, recovery flow, mandatory DI, and manifest validation.
- **1E:** combined canonical and compatibility staging under one coordinator and one non-interruptible transaction boundary.
- **1F:** broadened post-swap rollback coverage, transactionally protected validation/manifest metadata, made recovery pair-aware, blocked independent commits, hardened manifest path handling, and added manifest-only repair reuse semantics.

## Certification evidence (2026-07-29)

Actual failed checks (the .NET SDK is unavailable, so no test process or totals existed):

- Required focused tests, solution build, and full solution tests: `/bin/bash: dotnet: command not found` (exit 127).

**O2.ORCH.ALIGN.1F**

**PHASE 1 FINAL ARCHITECTURE CONFORMANCE: BLOCKED**

**PHASE 1 STATUS: NOT FROZEN**
