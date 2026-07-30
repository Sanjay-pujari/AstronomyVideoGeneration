# O2.ORCH.ALIGN.1F — Phase 1 Final Coherence Certification and Freeze

## Current declaration

This is the sole current Phase 1 architecture declaration. The historical 1C–1E findings are retained only in the change log below; they do not describe the current implementation.

## Certified architecture

Phase 1 owns one lifecycle-locked decision sequence: validate input, project canonical and compatibility publications, perform pair-aware recovery, read canonical, compatibility, the six-entry manifest, and committed success metadata, then select reuse, manifest repair, compatibility repair, validation repair, or full combined publication. Reuse requires every component, matching authority/request checksums, and `publicationCommitted=true`; `Publishing`, failed, missing, corrupt, and stale success metadata cannot be reused. Independent canonical and compatibility commit APIs are blocked with `P1_TRANSACTION_COORDINATOR_REQUIRED`.

Downstream invalidation is reversible: Phase 2–20 paths move to a transaction-specific `.phase1-downstream-backup-{id}` quarantine, partial staging exposes its move ledger for reverse-order rollback, and quarantine deletion occurs only after coherent final metadata publication.

Provisional validation is explicitly `Publishing` and uncommitted; only the coordinator publishes the final `Succeeded` document after manifest validation and downstream staging. The coordinator supplies the current publication transaction ID to both validation staging callbacks; recovery IDs are never used as publication IDs. `PreviousPhaseSucceeded` rejects Phase 1 success documents without committed validation metadata.

The combined coordinator uses one transaction ID for canonical authority, compatibility projection, validation, and manifest staging/backups/failed evidence. Manifest staging reads its six checksums exclusively from the canonical and compatibility staging roots while recording only their final active workspace paths and expected authority lineage. The mutation boundary is non-interruptible and every ordinary exception after it enters rollback, which restores manifest and validation before compatibility and canonical authority.

Manifest rollback treats a previously absent manifest as a valid restored state; when one existed it must be restored and semantically validated. Compatibility-repair rollback validates canonical authority, canonical-owned compatibility lineage, and any restored manifest.

Manifest-only repair atomically owns both manifest replacement and final committed validation. Compatibility-only repair atomically owns compatibility, manifest, and final committed validation. Validation-only repair verifies the unchanged canonical, compatibility, and manifest, then replaces only success metadata. All three use their own coordinator transaction IDs, preserve canonical timestamps and downstream outputs, and restore validation plus every other repair target on failure.

Recovery treats canonical, compatibility, manifest, and validation as four rollback components. Metadata is eligible only under the authority pair's backup transaction identity, and recovered flags derive from semantic manifest and committed-success validators rather than successful moves. Missing, mismatched, `Publishing`, uncommitted, or stale metadata sets its repair-required flag without preventing recovery of a coherent authority pair.

Once all required active-state semantic checks establish coherence, backup, staging, failed-provisional-validation, and downstream-quarantine cleanup is warning-only; warnings retain the evidence path. Cleanup failure cannot turn a coherent publication into a failed result.

Downstream invalidation consumes the side-effect-free Phase 2–20 output target resolver. It deduplicates exact workspace-contained targets and preserves relative quarantine paths; it does not scan the workspace root, touch Phase 1 authority/metadata, archives, logs, or unrelated content. The invalidation transaction is a mandatory DI dependency and uses the registered Phase 1 filesystem and resolver.

The final execution outcome is assembled from the completed transaction result; provisional validation cannot claim downstream invalidation or a successful manifest. Rollback compatibility acceptance is semantic: restored file checksums must match the compatibility lineage recorded by the restored canonical execution context, rather than merely containing parseable JSON.

Manifest validation requires exactly six recognized Phase 1 artifacts, exact semantic role cardinality, checksum and lineage fields, and active workspace-contained paths. Malformed properties and unsafe staging, backup, failed, quarantine, or transaction paths produce structured validation diagnostics rather than escaping as property-access exceptions.

Recovery considers only canonical/compatibility backup pairs with the same transaction ID, newest-first, validates compatibility using the candidate canonical backup's own checksum lineage, and restores only matching manifest/validation backups when available. It never combines unrelated backups or restores canonical alone, and it restores isolated original active sets when a candidate restoration fails.

## Concise change log

- **1C:** introduced canonical authority, structured validation, and lifecycle ownership.
- **1D:** added structured outcomes, recovery flow, mandatory DI, and manifest validation.
- **1E:** combined canonical and compatibility staging under one coordinator and one non-interruptible transaction boundary.
- **1F:** broadened post-swap rollback coverage, transactionally protected validation/manifest metadata, made recovery pair-aware, blocked independent commits, hardened manifest path handling, and added manifest-only repair reuse semantics.

## Certification evidence (2026-07-30)

The .NET 10.0.302 SDK was installed for certification. Source compilation succeeds, but the freeze gate remains blocked because the required focused test names are absent and both Phase 1-filtered and full solution tests fail. No frozen claim is made.

| Command | Exit | Passed | Failed | Skipped | Duration | Result |
|---|---:|---:|---:|---:|---:|---|
| `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~Phase1Recovery` | 0 | 0 | 0 | 0 | 90s | No matching tests |
| `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~PhaseOutputTargetResolver` | 0 | 0 | 0 | 0 | 17s | No matching tests |
| `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~Phase1PublicationTransactionCoordinator` | 0 | 0 | 0 | 0 | 10s | No matching tests |
| `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~Phase1ManifestRepair` | 0 | 0 | 0 | 0 | 10s | No matching tests |
| `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~Phase1CompatibilityRepair` | 0 | 0 | 0 | 0 | 11s | No matching tests |
| `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~Phase1ValidationRepair` | 0 | 0 | 0 | 0 | 11s | No matching tests |
| `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~Phase1Downstream` | 0 | 0 | 0 | 0 | 11s | No matching tests |
| `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --filter FullyQualifiedName~Phase1` | 1 | 95 | 30 | 0 | 14s | Failed |
| `dotnet build Backend/Astronomy.MediaFactory.slnx` | 0 | n/a | 0 compiler errors | n/a | 12s | Passed with NU1510 and NU1903 warnings |
| `dotnet test Backend/Astronomy.MediaFactory.slnx` | 1 | 4263 | 459 | 11 | 227s | Failed |

The exact failing test names are retained in the certification logs generated during this pass (`/tmp/test-Phase1.log` and `/tmp/final-test.log`); these transient logs are not architecture source. The acceptance failures are the absent seven focused test suites, 30 failures under the broad `Phase1` substring filter, and 459 full-suite failures.

**O2.ORCH.ALIGN.1F**

**PHASE 1 FINAL ARCHITECTURE CONFORMANCE: BLOCKED**

**PHASE 1 STATUS: NOT FROZEN**
