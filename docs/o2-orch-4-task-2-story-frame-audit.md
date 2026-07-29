# O2.ORCH.4 Task 2C — Phase 6 final certification audit

This audit records production behavior inspected in `ProductionPipelineExecutionService`, the Story Frame
integration/validation contracts, execution lock, DI registration, and the existing Phase 3–6 and Story Frame tests.
A helper's existence is not counted as executable certification evidence.

| Concern | Confirmed production behavior | Confirmed gap | Production change | Tests added | Executed result |
|---|---|---|---|---|---|
| Locked resume decision | `RunAsync` checked Phase 6 reuse before calling the locked phase action | Concurrent callers could both make a reuse/generate decision outside the lock; `RetryFailedOnly` could bypass it | Removed both generic Phase 6 bypasses. Recovery, Phase 5 validation, resume reads, runtime validation, generation, staged validation, and commit now occur under one injected plan/output-keyed lease | Existing keyed-lock tests remain; production concurrency matrix is still required | Source inspection complete; execution unavailable |
| Runtime compatibility | The structured validator accepted an optional compatibility context, but resume called the legacy projection without one | Builder and integration runtime changes could be reused | `StoryFrameIntegrationService` now provides its injected builder/integration identity. Locked resume and staged validation call `ValidateDetailed` with that context | Existing contract compatibility tests remain; requested runtime-resume matrix is still required | Source inspection complete; execution unavailable |
| Explicit reuse outcome | Phase 6 returned paths only and the outer generic executor inferred success | A locked reuse could not preserve the public skipped outcome/reason | Added internal generated/reused outcome and the exact frozen reuse reason; a Phase 6-specific executor writes public validation without changing response contracts | No new executable matrix in this environment | Source inspection complete |
| Atomic commit | Directory moves and rollback were inline and used static `Directory` calls | Deterministic move/delete failure injection was impossible; backup cleanup failure failed the phase | Added narrow filesystem and committer services. The critical two-move swap is uncancellable, precommit swap failures restore the backup, and postcommit backup cleanup failure returns a warning while retaining the new authority | Requested atomic failure matrix remains required | Source inspection complete; execution unavailable |
| Temporary recovery | No stale staging/backup scan occurred | Crashes left temporary directories; a missing active authority could not recover a compatible backup | Added clock/filesystem-injected recovery. It deletes only stale temporaries, validates backups as complete compatible sets, and restores the newest valid backup when active is absent | Requested recovery matrix remains required | Source inspection complete; execution unavailable |
| Manifest containment | Phase 6 used `StartsWith(workspace)` plus partial parent checks | Containment was not a reusable canonical policy and had insufficient attack evidence | Added canonical full-path/parent/exact-file helper with separator-delimited root containment, temporary-path and ADS rejection; malformed manifests return non-reusable | Requested manifest security matrix remains required | Source inspection complete; execution unavailable |
| Cancellation | Generic executor already excluded `OperationCanceledException`, but boundaries and the swap were implicit | No executable cancellation-boundary proof | Added checks before/across lock, recovery, resume, builder, staging validation, and commit. No cancellation is observed between the critical moves | Requested cancellation matrix remains required | Source inspection complete; execution unavailable |
| Dependency wiring | The execution lock was a static service field | Lock/filesystem/committer/recovery/runtime identity were not production-injected | Registered the keyed lock singleton; registered filesystem/clock, committer, recovery, and the integration runtime identity through repository DI conventions | DI registration tests still required | Source inspection complete; execution unavailable |
| Overwrite and dry-run | Existing range cleanup and early dry-run paths remain | Full Phase 7–20 overwrite and no-side-effect evidence was absent | Phase 6 overwrite bypasses locked reuse while retaining the old active directory through staged validation; dry run still never reaches Phase 6 | Requested overwrite and dry-run matrices remain required | Not executed |
| API/final suite | Production endpoint route and contracts remain unchanged | Required API/concurrency/failure/final certification files are absent | No endpoint contract change | Still required | Not executed |

## Frozen architecture confirmation

`ContentPlanningRc2Controller` → `Rc2ContentPlanningBatchOrchestrator` →
`ProductionPipelineExecutionService.RunAsync` is unchanged. Phase numbers, names, artifact names, manifest root
schema, Phase 3–5 authority formats, the certified production builder adapter, and Phase 7 implementation were not
changed. No second Story Frame engine was introduced.

## Runtime compatibility identity

The production provider reports builder type/version directly from the registered `ICertifiedStoryFrameBuilder`.
It reports integration type `StoryFrameIntegrationService`, integration version
`RC2-Phase6-Integration-v1`, and current authority/index/diagnostics contract version `1.1`.
Persisted explicit contract versions `1.0` and `1.1` remain the supported compatibility set.

## Certification conclusion

The production gaps above were corrected in source, but the requested focused test-file matrix was not created and
the container has no `dotnet` executable, so build, focused suites, regressions, and the complete suite could not be
executed. These are open certification blockers; this audit therefore does **not** claim final certification.
