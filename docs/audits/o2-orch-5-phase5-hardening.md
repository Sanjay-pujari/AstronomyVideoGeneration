# O2.ORCH.5 Phase 5 hardening audit

## Current Phase 4 lineage validation
`Phase5ExpectedPhase4Authority` carries the committed aggregate ID and aggregate, Long, and Short checksums. The evaluator compares certification, all five projections, the editorial contract, every manifest entry, and the returned published authority against it.

## Stale Phase 5 reuse prevention
Reuse supplies lineage derived from `PublishedDocumentaryBlueprintAggregate`; aggregate ID or checksum drift returns `P5REUSE_SOURCE_PHASE4_MISMATCH` and does not install the stale authority.

## Manifest role validation
All seven entries require their exact canonical roles.

## Manifest semantic checksum validation
Every manifest semantic checksum is required, lowercase SHA-256, and matched to the checksum validated from its deserialized artifact.

## Manifest physical-size validation
Positive integer sizes are required and compared to physical file lengths.

## Manifest source-lineage validation
Every entry must name the current committed Phase 4 aggregate checksum.

## Unknown manifest property preservation
Manifest writes merge generated owned values into the parsed existing `JsonObject`, preserving unknown top-level and nested values, then replace via a temporary file.

## Transactional backup retention
The previous editorial authority plus manifest and validation snapshots remain until committed readback. Failed readback restores prior state; successful readback removes transaction files.

## Post-publication committed readback
Success and authority installation occur only after the evaluator validates the physically published files, validation record, and manifest.

## Phase 6 resume lineage enforcement
Resume first rehydrates Phase 4 through its committed evaluator, then supplies that typed lineage while rehydrating Phase 5. Diagnostics remain optional.

## Tests and actual totals
The container does not provide the .NET SDK, so executable test totals could not be produced in this environment. `git diff --check` passed.

## Remaining technical debt
Run the complete requested test matrix in the repository's .NET build environment and add deterministic injected rollback-fault coverage.

## RC2 readiness verdict
Build and behavioral verification are mandatory and remain outstanding due to the missing SDK.

NOT_READY_FOR_RC2_PHASE_1_TO_5
