# O2.ORCH.4 Task 5B — Final Atomic Publication Hardening

## Corrections applied

Recovery now owns the same execution-scoped lock as publication. The publication service invokes recovery before taking its publication lock, preventing recursive lock acquisition while ensuring stale staging or backup state cannot be deleted or restored concurrently with an active publication for the same execution.

The physical validator now performs complete typed reconciliation for all seven Phase 4 artifacts. Knowledge-selection validation compares every lineage and authority field, recomputes unique-reference and reuse summaries, verifies evidence-status totals, rejects compatibility-only sources, and enforces the editorial-only no-knowledge rule.

Scene-index validation reconstructs every index row from the embedded blueprint plus scene traceability and compares the complete deterministic projection, including opportunity, slot, question, objective, evidence, duration range, knowledge, editorial safety, scene checksum, and source-opportunity checksum fields.

Build-report validation now reconciles all publication identity, source intent, aggregate and variant identity/checksum, scene and duration totals, reconciliation flags, artifact inventory, compatibility decision, and deterministic `Prepared` publication status.

Authority and manifest validation occurs before the success record is created. The validation record is moved as the final mutation, after which it is re-read and checksum/commit-marker validated without any further successful-state mutation.

## Remaining repository-dependent certification items

The uploaded subset does not include the canonical Phase 1–3 shared-manifest contract or the authoritative list of mandatory frozen upstream paths. Therefore this patch intentionally does not claim that the current `phase4Artifacts` manifest member is the repository-owned canonical schema, and it cannot prove that non-empty caller snapshots contain every mandatory Phase 1–3 authority and validation file.

Before Task 6 certification, wire the Phase 4 updater to the existing repository manifest abstraction and replace caller-defined completeness with the repository-owned frozen-upstream snapshot builder/validator.

## Test evidence

No .NET test execution was performed in this environment. Run the complete Task 5/5A/5B fault-injection, recovery, concurrency, idempotency, Orion 12/4, manifest, frozen-upstream, and full regression suites in the repository SDK environment.

## Verdict

NOT_READY_FOR_PHASE_4_TASK_6
