# O2.ORCH.ALIGN.1B — Phase 1 Final Certification and Freeze

## 1. Decision

**O2.ORCH.ALIGN.1B — PHASE 1 FINAL ARCHITECTURE CONFORMANCE: BLOCKED.** The implementation hardening is present, but this environment has no .NET SDK; consequently build, focused tests, regression tests, and the complete suite could not be certified. Phase 1 is **not frozen**.

## 2. Governing documents

The following frozen Word documents were extracted and read before production edits:

1. `docs/implementation/RC2_Pipeline_Implementation_Guide_RPIG_v1.1.docx`.
2. `docs/implementation/Drashyam_RC2_Pipeline_Development_Guide_v1.1_Final.docx`.
3. `docs/implementation/Drashyam_RC2_Orchestration_Code_Integration_Guide_v1.0.docx`.

The detailed v1.1 complete-set requirements govern Phase 1; the v1.0 instruction to retain plan-input is interpreted as compatibility preservation, never competing authority.

## 3. Repository audit findings

The endpoint-to-runner call graph remains intact. Before 1B, Phase 1 used generic `ExecutePhaseAsync`; `retryFailedOnly` could skip it from centralized validation alone; overwrite cleanup ran before replacement; persistence used static filesystem calls, permanent semaphore dictionary entries, deleted backup before committed validation, and had no stale-directory recovery. The centralized runner was and remains the only writer of `validation/phase-01-validation.json` and `phase-manifest.json`. `PhaseLoadPlanAsync` was the `01-plan` and `plan-input` writer. Phase 2 remains unchanged. Phase 6 supplied the repository model for cancellable keyed execution, filesystem injection, recovery, commit, warning, and resume concepts; its behavior was not changed.

## 4. Final call graph

`POST /api/content-planning/rc2/batch-generate-from-plans` → `ContentPlanningRc2Controller` → `Rc2ContentPlanningBatchOrchestrator` → `ContentPlanBatchGenerationService` → `ContentPlanProductionExecutionService` → `ProductionPipelineExecutionService.RunAsync` → `ExecutePhase1Async` → projector → keyed persistence lifecycle → compatibility publication → centralized validation/manifest publication. Phase 1 is excluded from generic retry skipping and generic execution.

## 5. Artifact tree and roles

```text
<workspace>/
├── 01-plan/
│   ├── execution-context.json       # Authoritative
│   ├── selected-plan.json           # Supporting
│   ├── production-request.json      # Supporting
│   └── pipeline-state.json          # Supporting
├── plan-input/
│   ├── content-plan-production-request.json # Compatibility
│   └── production-event-intelligence.json   # Compatibility
├── validation/phase-01-validation.json
└── phase-manifest.json
```

There is exactly one Phase 1 `Authoritative` manifest role. Compatibility artifacts never acquire authority.

## 6. Contract identities

Supported identities are `drashyam.phase1.v1`, `drashyam.phase1-selected-plan/1.0`, `drashyam.phase1-production-request/1.0`, `drashyam.phase1-pipeline-state/1.0`, `CanonicalExecutionContext/1.0`, `CG1`, `rc2.1.1`, `drashyam.phase1-projector/1.0`, and `drashyam.canonical-json.sha256/1.0`. Independent failures use `P1_AUTHORITY_CONTRACT_UNSUPPORTED`, `P1_SELECTED_PLAN_CONTRACT_UNSUPPORTED`, `P1_PRODUCTION_REQUEST_CONTRACT_UNSUPPORTED`, and `P1_PIPELINE_STATE_CONTRACT_UNSUPPORTED`.

## 7. Checksum rules

Canonical SHA-256 recursively orders object keys. Projected collections are normalized, de-duplicated, and ordinal-sorted. `GeneratedUtc`, `InitializedUtc`, and self-checksum properties are excluded as appropriate. The authority binds the exact three supporting checksums; request identity binds selected-plan and production-request checksums. Unknown or missing supporting references fail validation.

## 8. Complete-set validation

`Phase1AuthorityValidator` reads all four files through `IPhase1FileSystem`. It independently calculates structural validity, runtime/contract compatibility, reusability, and downstream readiness. It validates identity, lineage, requested/effective ranges, planned phases, state, references, checksums, safe workspace identity, and secret-bearing content. A complete valid set alone is insufficient when runtime compatibility or downstream readiness fails.

## 9. Path security model

`Phase1PathSecurity` requires a normalized direct child of the normalized workspace, separator-boundary containment, exact active name, and approved GUID-suffixed staging/backup names only when temporary validation is explicitly enabled. It rejects root-prefix collision, traversal resolution, UNC/device roots, alternate data streams, unexpected parents/names, staging or backup as active, and workspace/candidate reparse points.

## 10. Lock lifecycle

`InProcessPhase1ExecutionLock` normalizes the workspace key, asynchronously serializes equal roots, permits independent roots, propagates waiter cancellation, reference-counts owners and waiters, removes the entry after the final release, and releases on exceptions. Scope covers recovery through canonical commit. This lock is **in-process only**; multi-instance/distributed safety is not claimed.

## 11. Recovery decision table

| Active | Backups | Result |
|---|---|---|
| valid/downstream-ready | any | remove approved stale staging and obsolete backups; warnings retained |
| missing/invalid | newest valid compatible backup | isolate invalid active, restore deterministically, revalidate |
| missing/invalid | invalid newest, older valid | skip invalid evidence and restore older valid candidate |
| missing/invalid | none valid | continue to generation |

Foreign temporary names are ignored. Cancellation is checked before destructive recovery work.

## 12. Resume decision table

| Complete-set state | Request identity | Decision/code |
|---|---|---|
| valid, compatible, downstream-ready | equal | reuse / `P1_RESUME_REUSABLE` |
| missing | any | regenerate / `P1_RESUME_NO_AUTHORITY` |
| corrupt or checksum-invalid | any | regenerate / structured `P1_*` diagnostic |
| runtime/contract incompatible | any | regenerate / incompatibility diagnostic |
| valid | changed | regenerate / request identity mismatch |

Previous centralized success and file existence alone never authorize reuse.

## 13. Atomic commit and rollback

The sequence is staging write → staged complete-set validation → cancellation check → active-to-backup → staging-to-active → committed complete-set validation → backup deletion. Directory rename transaction and rollback validation are intentionally non-interruptible so cancellation cannot strand a valid backup without an active authority. Failed committed validation isolates the new active, restores the backup, revalidates it, and returns structured failure. Backup is never deleted before committed validation.

## 14. Compatibility publication policy

Both legacy projections are produced from the same in-memory normalized request after canonical commit and before Phase 1 success publication. The centralized outcome fails if publication throws; it does not publish success. A future certification pass must add injectable compatibility failure tests and verify the selected repair/rollback policy end-to-end.

## 15. RetryFailedOnly

Phase 1 is explicitly excluded from the generic shortcut. Every retry enters `ExecutePhase1Async` and complete-set validation. Reuse is published as `Skipped` with stable `P1_RESUME_REUSABLE`, and pipeline success recognizes that structured prefix rather than generic validation text.

## 16. Overwrite and downstream invalidation

When the effective range starts at Phase 1, initial cleanup is deferred. Only after validated canonical commit and successful compatibility publication does Phase 1 invoke the existing `ClearPhaseRangeOutputsForOverwrite` mechanism with boundary 2. Reuse and dry-run never invalidate downstream output. Non-Phase-1 range behavior is unchanged.

## 17. Dry-run behavior

The existing early return precedes Phase 1 execution and recovery: it does not project, create/replace canonical or compatibility artifacts, inspect/delete temporary directories, invalidate downstream output, or publish Phase 1 artifact roles. Only centralized skipped validation and manifest conventions execute.

## 18. Cancellation boundary

Cancellation propagates while waiting, before recovery mutation, during staged writes, before swap, before compatibility publication, and before invalidation. Once active-to-backup begins, rename, committed validation, and required rollback are non-interruptible; cancellation is observed again only after a valid active authority exists.

## 19. Validation and manifest ownership

`ProductionPipelineExecutionService.WritePhaseValidationAsync` remains the sole Phase 1 validation publisher. `WritePhaseManifestAsync` remains the sole manifest writer. Projector, filesystem, path policy, validator, lock, recovery, and persistence return diagnostics only.

## 20. Test classes and evidence

Existing applicable classes are `Phase1AuthorityTests`, `ProductionPipelineExecutionServiceTests`, `ServiceCollectionExtensionsTests`, and the Phase 6 authority lifecycle tests used as design evidence. Runtime-focused 1B test expansion and execution remain a certification blocker because no SDK is installed.

## 21. Commands and exact results

| Command | Exit | Tests |
|---|---:|---|
| `git status --short` | 0 | n/a |
| `git rev-parse HEAD` | 0 | n/a; `73f5e90291b2cb6281a9699acd58522471e228bb` |
| `git log -10 --oneline` | 0 | n/a |
| `dotnet --info` | 127 | unavailable |
| `dotnet restore Backend/Astronomy.MediaFactory.slnx` | 127 | unavailable |
| `dotnet build Backend/Astronomy.MediaFactory.slnx --no-restore` | 127 | unavailable |
| focused `dotnet test` commands | not run | total 0, passed 0, failed 0, skipped 0 |
| `dotnet test Backend/Astronomy.MediaFactory.slnx --no-build` | not run | total 0, passed 0, failed 0, skipped 0 |

No test pass is claimed.

## 22. Known deployment limitations

The lock is process-local, not distributed. Compatibility publication is atomic per existing file writer rather than a cross-directory filesystem transaction. The unavailable SDK prevents compile/runtime evidence and exact suite counts. Phase 6's older unavailable-SDK audit is historical and unrelated, and is not Phase 1 evidence.

## 23. Final conformance matrix

| Gate | State |
|---|---|
| governing documents, CG1, endpoint, 20 phases, one authority, complete set, contracts/checksums | implemented; runtime certification blocked |
| dedicated lifecycle, complete-set retry/reuse, structured codes | implemented; runtime certification blocked |
| filesystem, path policy, keyed lock cleanup, staging/backup recovery | implemented; runtime certification blocked |
| staged commit, committed validation, retained backup, rollback validation | implemented; runtime certification blocked |
| compatibility bridge, centralized validation/manifest ownership | preserved; failure-injection certification blocked |
| deferred downstream invalidation, dry run, cancellation boundary | implemented; runtime certification blocked |
| focused tests, regression tests, full build and suite | **BLOCKED: .NET SDK unavailable** |
| architecture document | current |

## 24. Final declaration

**O2.ORCH.ALIGN.1B**
**PHASE 1 FINAL ARCHITECTURE CONFORMANCE: BLOCKED**

Remaining blockers: compile/build evidence; exhaustive focused runtime tests; related regressions; complete suite with exact totals; compatibility publication failure/repair proof. **PHASE 1 STATUS: NOT FROZEN.**
