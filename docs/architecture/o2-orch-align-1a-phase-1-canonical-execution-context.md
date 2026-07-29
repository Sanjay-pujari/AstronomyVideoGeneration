# O2.ORCH.ALIGN.1C — Phase 1 Final Transactional Certification and Freeze

## 1. Decision

**O2.ORCH.ALIGN.1C — PHASE 1 FINAL ARCHITECTURE CONFORMANCE: BLOCKED.**

**PHASE 1 STATUS: NOT FROZEN.** The code now establishes the final lifecycle ownership model, but certification cannot be approved because this container has no `dotnet` executable. Build and runtime evidence therefore remains unavailable.

## 2. Governing documents

The audit read `RC2_Pipeline_Implementation_Guide_RPIG_v1.1.docx`, `Drashyam_RC2_Pipeline_Development_Guide_v1.1_Final.docx`, and `Drashyam_RC2_Orchestration_Code_Integration_Guide_v1.0.docx`. The implementation preserves the endpoint, twenty phases, one-language execution, CG1 ownership, `execution-context.json` authority, and the existing production runner.

## 3. Repository audit findings

Before editing, searches covered every `AcquireAsync`, `PersistAsync`, `WritePlanInputAsync`, `ClearPhaseRangeOutputsForOverwrite`, `WritePhaseValidationAsync`, and `WritePhaseManifestAsync` call and every literal reference to `01-plan`, `plan-input`, `phase-01-validation.json`, and `phase-manifest.json`.

The 1B lifecycle acquired its lock inside `Phase1AuthorityPersistence.PersistAsync`; compatibility projection, downstream invalidation, centralized validation, and manifest publication happened after release. Compatibility files were written separately by `WritePlanInputAsync`. External cancellation was observed after canonical commit. `Phase1ExecutionOutcome`, `Phase1ResumeEvaluation`, and `Phase1RecoveryResult` were declarations rather than the production decision/result path. `PhaseLoadPlanAsync` duplicated projection, persistence, and compatibility writing. Manifest entries were based on `File.Exists`, had only path/role/contract, and a dry run could claim an old authority. The production constructor used fallback `new` expressions for Phase 1 dependencies.

Readers outside the Phase 1 writer include `ContentPlanProductionExecutionService`, `Rc2ContentPlanningBatchOrchestrator`, `EventProductionIntelligence`, `SceneIntentBuilder`, `NarrationGeneratorV5`, blueprint integration, and certification services. The centralized runner remains the writer of `validation/phase-01-validation.json` and `phase-manifest.json`.

## 4. Final call graph

`RunAsync` → `ExecutePhase1Async` → `IPhase1ExecutionLock.AcquireAsync` → project canonical and compatibility publications → read/validate existing authority → validate compatibility → `IPhase1ResumeEvaluator.Evaluate` → reuse or lock-free `IPhase1AuthorityPersistence.PersistAsync` → `IPhase1CompatibilityPublisher.PublishAsync` → committed compatibility validation → deferred downstream invalidation → `WritePhaseValidationAsync` → `WritePhaseManifestAsync` → release lease.

Phase 1's generic action throws `P1_DEDICATED_LIFECYCLE_REQUIRED`; only the dedicated switch can execute it.

## 5. Full lifecycle lock scope

The normalized, keyed, asynchronous lock covers projection, validation, persistence recovery/staging/commit, compatibility staging/commit, invalidation, validation, manifest publication, and warning aggregation. Persistence is the selected lock-free internal component (design B), called only by the locked production lifecycle. Same roots serialize, different roots use distinct entries, waiting cancellation propagates, and reference-counted entries are removed.

## 6. Artifact trees and contracts

Canonical: `01-plan/{execution-context.json,selected-plan.json,production-request.json,pipeline-state.json}`. Compatibility: `plan-input/{content-plan-production-request.json,production-event-intelligence.json}`. Frozen canonical contract identities remain `drashyam.phase1.v1`, `drashyam.phase1-selected-plan/1.0`, `drashyam.phase1-production-request/1.0`, and `drashyam.phase1-pipeline-state/1.0`.

## 7. Compatibility checksum lineage

`Phase1ExecutionContext.CompatibilityArtifactChecksums` binds both exact UTF-8 serialized compatibility payloads. `CompatibilityInputChecksum` binds that exact two-entry map, and `AuthorityChecksum` binds the context including the map. Unknown, absent, or non-SHA-256 entries invalidate canonical validation.

## 8. Validation semantics

`IsValid` is canonical structural/checksum validity; `IsCompatible` is contract/runtime identity; `IsRequestCompatible` is exposed separately; `IsManifestCompatible` and `IsCompatibilityProjectionValid` are independent; `IsDownstreamReady` covers pipeline state; `IsReusable` requires valid, compatible, and downstream-ready canonical state. The expected request identity comparison is exclusively made by the resume evaluator before reuse.

## 9. Resume decision table

The evaluator emits `P1_RESUME_REUSABLE`, `P1_RESUME_NO_AUTHORITY`, `P1_RESUME_INCOMPLETE_SET`, `P1_RESUME_CORRUPT_JSON`, `P1_RESUME_CONTRACT_UNSUPPORTED`, `P1_RESUME_RUNTIME_INCOMPATIBLE`, `P1_RESUME_REQUEST_CHANGED`, `P1_RESUME_CHECKSUM_MISMATCH`, `P1_RESUME_PATH_INVALID`, `P1_RESUME_MANIFEST_INVALID`, `P1_RESUME_COMPATIBILITY_MISSING`, `P1_RESUME_COMPATIBILITY_MISMATCH`, `P1_RESUME_RECOVERED_AUTHORITY`, or `P1_RESUME_VALIDATION_REPAIR_REQUIRED`. Only reusable or recovered-authority decisions authorize reuse.

## 10. Structured execution outcome

Production consumes `Phase1ExecutionOutcome`, with `Phase1ExecutionKind`, reason code/reason, files, warnings/errors, authority/request checksums, reuse/replacement/invalidation flags, compatibility/recovery/manifest/validation status. Centralized validation serializes stable structured fields rather than requiring reason-string parsing.

## 11. Recovery decision table and retention

The existing canonical recovery removes approved staging, retains a valid active set, removes obsolete backups only after active validation, and selects valid backups newest-first. `Phase1RecoveryResult` now carries isolated paths, compatibility recovery, and manifest-repair state. Failed directories are never considered active. **Remaining blocker:** bounded failed-evidence retention and fully rollback-safe move-boundary recovery are not yet runtime-certified.

## 12. Canonical and compatibility transaction sequences

Canonical persistence writes four staged files, validates, renames active to backup and staging to active, validates committed state with the named non-interruptible boundary, and restores backup on canonical validation failure. Compatibility publication builds two in-memory payloads, hashes exact text, stages and re-reads both, backs up `plan-input`, atomically renames the set, revalidates both, and restores the prior set on failure. No compatibility file is published independently by Phase 1.

**Remaining blocker:** canonical persistence currently deletes its backup before compatibility commit; therefore a compatibility failure cannot yet roll canonical authority back as one combined transaction.

## 13. Cancellation boundary and downstream invalidation

Cancellation propagates through lock waiting, projection, reads, and staging. The final interruptible checkpoint occurs before compatibility publication. A named `nonInterruptiblePublicationToken` covers compatibility commit, committed validation, downstream invalidation, centralized validation, and manifest publication. Downstream invalidation occurs only after both committed sets validate.

## 14. Validation and manifest ownership

`ProductionPipelineExecutionService` remains the sole centralized validation and single-manifest writer. Phase 1 writes both while holding its lifecycle lease. Manifest diagnostic collections now include `phasesGenerated`, `phasesReused`, `phasesFailed`, `phasesDryRunSkipped`, and `phasesNotRequested`.

## 15. Manifest metadata and dry-run behavior

Roles remain exactly one Authoritative, three Supporting, and two Compatibility entries. A dry run emits no Phase 1 current-run entries, exposes pre-existing files only as `existingDependencies`, performs no recovery or invalidation, and lists Phase 1 in `phasesDryRunSkipped` when requested. **Remaining blocker:** complete checksum and lineage metadata on each manifest entry has not been implemented and certified.

## 16. DI construction and duplicate path removal

`IPhase1AuthorityProjector`, `IPhase1AuthorityPersistence`, `IPhase1AuthorityReader`, `IPhase1ExecutionLock`, `IPhase1ResumeEvaluator`, and `IPhase1CompatibilityPublisher` are mandatory constructor parameters. `AddMediaFactory` registers one singleton lock, filesystem, persistence, reader alias, evaluator, and publisher. There are no Phase 1 fallback constructions in the runner. `PhaseLoadPlanAsync` was removed.

## 17. Test classes

`Phase1AuthorityTests` covers canonical hashing, secret-safe contracts, and missing sets. `Phase1LifecycleLockTests` adds same-workspace serialization, workspace independence, cancellation propagation, and entry cleanup. Existing `ProductionPipelineExecutionServiceTests` retain production, retry, overwrite, manifest, cancellation, dry-run, and downstream regression coverage.

## 18. Exact commands and evidence

Repository baseline: `e36be0401d480a021937f32e5fabf55a8d90d511`.

| Command | Exit | Total / Passed / Failed / Skipped | Duration |
|---|---:|---|---:|
| `git status --short` | 0 | n/a | <1s |
| `git rev-parse HEAD` | 0 | n/a | <1s |
| `git log -10 --oneline` | 0 | n/a | <1s |
| `dotnet --info` | 127 | 0 / 0 / 0 / 0 | <1s |
| `dotnet restore Astronomy.MediaFactory.slnx` | not reached | 0 / 0 / 0 / 0 | n/a |
| `dotnet build Astronomy.MediaFactory.slnx --no-restore` | not reached | 0 / 0 / 0 / 0 | n/a |
| focused and full `dotnet test` commands | not reached | 0 / 0 / 0 / 0 | n/a |
| `git diff --check` | 0 | n/a | <1s |

Skipped-test reason: the image does not contain the .NET CLI (`bash: command not found: dotnet`).

## 19. Known deployment limitations

The lock is deliberately process-local, not distributed. Directory rename atomicity requires staging and active paths on the same volume. The absent SDK prevents compile and runtime certification in this environment.

## 20. Final conformance matrix

Passed by inspection: governing documents, CG1/public path, canonical/supporting/compatibility topology, one implementation path, dedicated lifecycle and lock ownership, lock cleanup design, structured outcome/evaluator contracts, request identity decision, canonical and compatibility checksums/staging/validation, deferred invalidation, failure-before-invalidation, dry-run classification, single writers, and mandatory DI.

Blocked: exact build/test evidence; combined canonical rollback after compatibility failure; fully rollback-safe recovery/failure injection; bounded evidence retention; complete per-entry manifest metadata; exhaustive focused/integration tests.

## 21. Final declaration

**O2.ORCH.ALIGN.1C**
**PHASE 1 FINAL ARCHITECTURE CONFORMANCE: BLOCKED**

**PHASE 1 STATUS: NOT FROZEN**
