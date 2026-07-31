# O2.ORCH.4 Task 6B Runtime Certification

## Runtime correction

The previous production handoff cross-deserialized the canonical aggregate and its
variant projections into the unrelated legacy `DocumentaryBlueprintArtifact` type.
The new `DocumentaryBlueprintPhase5CompatibilityAdapter` is a deterministic,
in-memory bridge from the validated `DocumentaryBlueprintAggregate`. It preserves
aggregate identity, embedded Long and Short scene ordering, durations, knowledge
lineage, and source checksums. Its retained `Master` value is documented as a
non-authoritative compatibility container and is neither read nor published by the
adapter.

Phase 5 now requires `PublishedDocumentaryBlueprintAggregate` on the execution
context and invokes the typed adapter. No Phase 4 authority file is parsed as a
legacy artifact.

## Committed authority and reuse

`Phase4CommittedAuthorityEvaluator` reads the canonical aggregate once and delegates
physical artifacts, manifest, validation marker, inventory, and checksum validation
to the certified `IPhase4CommittedStateValidator`. Phase 4 resume installs the
returned physical aggregate in the execution context without planning, projection,
or republication. Downstream recovery uses the same evaluation.

The injected RC2 status reader consumes that evaluation rather than inferring commit
state from file existence. Its inventory contains manifest evidence and the actual
`validation/phase-04-validation.json` path. An invalid evaluator result reports an
invalid, uncommitted publication and retains its reason code.

## Authority semantics

`LegacyCompatibilityArtifactExists` reports a downstream Story Graph independently
from `LegacyPhase4AuthorityUsed`. Certified executions report the published
aggregate authority and never convert compatibility-file existence into authority
usage. The backward-compatible `LegacyAuthorityProduced` value remains false for
the certified integration.

## Verification evidence

Static architecture checks forbid legacy aggregate cross-deserialization, require
the typed adapter and committed evaluator, and prove the adapter has no file I/O,
Short-from-Long derivation, or publication operation. The retained controller test
is contract serialization coverage; a fully pipeline-backed API fixture remains
non-blocking follow-up in this container.

The requested .NET focused, documentary, and solution regression commands could not
be executed because the container has no `dotnet` executable. `git diff --check`
completed successfully. Therefore runtime compilation and the real Phase 1–4 API
evidence are not certified by this audit.

## Remaining non-blocking technical debt

Run the complete .NET suite and the real database-backed RC2 API fixture in the CI
image that contains the SDK and test database infrastructure.

NOT_READY_FOR_PHASE_5
