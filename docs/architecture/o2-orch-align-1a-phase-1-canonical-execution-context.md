# O2.ORCH.ALIGN.1A — Phase 1 canonical execution context

## 1. Governing documents

The frozen inputs are `docs/implementation/RC2_Pipeline_Implementation_Guide_RPIG_v1.1.docx`, `docs/implementation/Drashyam_RC2_Pipeline_Development_Guide_v1.1_Final.docx`, and `docs/implementation/Drashyam_RC2_Orchestration_Code_Integration_Guide_v1.0.docx`. They were read before production edits. The development guide's detailed Phase 1 complete set controls while the integration guide's “keep unchanged” language controls the legacy bridge, not canonical authority.

## 2. Frozen Phase 1 requirements

Phase 1 remains **Load Plan**, owned by CG1, within the existing twenty-phase runner. `01-plan/execution-context.json` is its sole authority. The other three `01-plan` files support it and `plan-input` remains a compatibility bridge.

## 3. Previous production behavior

`PhaseLoadPlanAsync` wrote only two `plan-input` transport projections directly. Reuse was not content/checksum validated and there was no `01-plan` complete set. The centralized runner wrote `validation/phase-01-validation.json` and `phase-manifest.json`.

## 4. Final production call graph

`POST /api/content-planning/rc2/batch-generate-from-plans` → `ContentPlanningRc2Controller` → `Rc2ContentPlanningBatchOrchestrator` → `ContentPlanBatchGenerationService` → `ContentPlanProductionExecutionService` → `ProductionPipelineExecutionService.RunAsync` → `PhaseLoadPlanAsync` → projector/persistence/validator. No endpoint, controller, runner, phase number, or request DTO changed.

## 5. Canonical artifact tree

```text
<plan-workspace>/
├── 01-plan/
│   ├── execution-context.json
│   ├── selected-plan.json
│   ├── production-request.json
│   └── pipeline-state.json
└── plan-input/
    ├── content-plan-production-request.json
    └── production-event-intelligence.json
```

## 6. Authority and artifact roles

| Artifact | Role |
|---|---|
| `01-plan/execution-context.json` | Authoritative |
| `01-plan/selected-plan.json` | Supporting |
| `01-plan/production-request.json` | Supporting |
| `01-plan/pipeline-state.json` | Supporting |
| both `plan-input/*.json` files | Compatibility |

The single existing phase manifest now publishes these roles.

## 7. Contract definitions

Explicit safe projections are `Phase1ExecutionContext`, `Phase1SelectedPlan`, `Phase1ProductionRequest`, and `Phase1PipelineState`. The selected plan is an allow-listed production projection rather than a database/transport entity dump. The request separates requested and effective phase ranges and requested/resolved language. Pipeline state initializes downstream phases to `Pending`; it is not an execution-status authority.

## 8. Runtime identity

Stable identities are `drashyam.phase1.v1`, `CanonicalExecutionContext/1.0`, `drashyam.phase1-projector/1.0`, `drashyam.canonical-json.sha256/1.0`, `rc2.1.1`, and explicit supporting-projection versions. Unknown authority versions produce `P1_CONTRACT_UNSUPPORTED`.

## 9. Checksum canonicalization rules

UTF-8 SHA-256 operates on recursively property-sorted JSON. Projection collections are trimmed, lower-cased, deduplicated, and sorted. Dictionary keys are sorted. `generatedUtc`, `initializedUtc`, and self-checksum fields are excluded from stable checksums. Authority checksum covers stable context and all supporting checksum references. Request identity covers selected-plan and effective-request checksums.

## 10. Complete-set validation rules

One validator reads all four files and emits structured `P1_*` diagnostics for missing/corrupt artifacts, unsupported identities, CG/runtime incompatibility, execution/plan/language/variant/output/range mismatch, supporting and authority checksum mismatch, request identity mismatch, false downstream success, and secret-bearing property names. Reuse requires the complete result to be valid, compatible, reusable, downstream-ready, and request-identical.

## 11. Persistence and recovery design

All four files are written into a uniquely named sibling staging directory, the staged complete set is validated, and a directory rename commits it. Existing active authority is renamed to backup immediately before commit and restored if staging-to-active fails. Cancellation is checked before commit. Active authority is therefore never a mixed set.

## 12. Lock scope

A normalized-workspace keyed, cancellable semaphore covers reuse evaluation, generation writes, staged validation, active/backup rename, rollback, committed validation, and cleanup. Different roots use different keys. A later hardening should add reference-counted key eviction; current dictionary keys live for process lifetime.

## 13. Resume decision table

| Existing set | Identity | Overwrite | Decision |
|---|---|---:|---|
| absent | n/a | false | generate |
| complete and valid | equal | false | reuse |
| missing/corrupt/incompatible | any | false | replace atomically |
| any | any | true | replace atomically |

## 14. Overwrite/invalidation behavior

Phase 1 replacement itself is rollback-safe. Existing runner phase-range cleanup remains the sole downstream invalidation system. **Known blocker:** cleanup currently occurs before Phase 1 staged commit, so the stricter “invalidate only after commit” acceptance requirement is not certified.

## 15. Dry-run behavior

The existing dry-run branch returns before any phase action. It writes no `01-plan`, no compatibility projections, and no Phase 1 artifact roles; centralized skipped validation/manifest behavior remains unchanged.

## 16. Compatibility-output policy

Both legacy schemas and writers remain unchanged. They are generated after canonical commit from the same normalized in-memory request/intelligence inputs and are manifest-classified only as Compatibility.

## 17. Phase 2 boundary

`IPhase1AuthorityReader` exposes the complete validated canonical set and downstream-readiness result. Phase 2 behavior is intentionally unchanged in this milestone.

## 18. Security/path validation

Validation uses full normalized path containment with a separator boundary (preventing root-prefix collision), exact active/staging directory naming, and rejects foreign, backup, or active-staging paths. Canonical contracts contain only a safe plan-ID workspace identity. Symlink/reparse-point resolution, UNC/ADS rejection, and stale staging recovery require further hardening.

## 19. Test matrix

Static repository auditing and build invocation were performed. Runtime tests could not be compiled or executed because the container has no `dotnet` executable. Full contract, validator, rollback, concurrency, manifest, end-to-end, and regression certification remains blocked.

## 20. Commands and results

- `git status --short`: passed; initially clean.
- `git rev-parse HEAD`: `b703b41039842f4056518da505465dab7f655ceb`.
- `git log -10 --oneline`: passed.
- `dotnet --info`: failed, `/bin/bash: dotnet: command not found`.
- `dotnet restore Backend/Astronomy.MediaFactory.slnx`: not executable without SDK.
- `dotnet build Backend/Astronomy.MediaFactory.slnx --no-restore`: failed, `/bin/bash: dotnet: command not found`.
- Focused and complete `dotnet test` commands: not executable without SDK.

## 21. Remaining known limitations

No .NET build/test certification; no post-commit downstream invalidation transaction; lock-key eviction absent; filesystem abstraction, symlink/reparse, UNC/ADS, stale staging/backup recovery and warning propagation need hardening; validation/manifest do not yet expose every requested reason/checksum/lineage field; required exhaustive runtime tests remain to be added and run.

## 22. Final conformance matrix

| Requirement | Governing document | Previous state | Final state | Evidence |
|---|---|---|---|---|
| CG1 / production path | all three | single runner | preserved | `ProductionPipelineExecutionService` integration |
| Canonical complete set | Development Guide §4.1 | absent | implemented | `Phase1Authority.cs` |
| Exactly one authority | RPIG; Development Guide | legacy inputs only | execution context only | manifest role table |
| Compatibility bridge | Integration Guide Phase 1 | authoritative by default | retained/classified Compatibility | manifest registration |
| Stable checksum/runtime | Development Guide verification | absent | implemented | canonical JSON and constants |
| Complete-set validation | frozen alignment requirement | absent | implemented, not runtime-certified | structured validator |
| Atomicity and locking | frozen alignment requirement | absent | staging/rename/rollback/keyed lock | persistence service |
| Phase 2 reader | dependency map | legacy-only | canonical reader exposed | DI registration |
| Complete build/tests | acceptance gate | n/a | **BLOCKED** | .NET SDK unavailable |

**O2.ORCH.ALIGN.1A PHASE 1 FINAL ARCHITECTURE CONFORMANCE: BLOCKED.**
