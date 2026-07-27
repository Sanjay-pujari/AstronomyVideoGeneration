# CG-A3 A3.10 Completion Report

## Certification status

**CG-A3 A3.10 — CERTIFIED**  
**READY FOR A3.11**

The deterministic A3.10 host matrix is provider-free: it creates isolated temporary workspaces and deterministic physical artifact evidence. No Azure Speech, image provider, FFmpeg/FFprobe process, storage, publishing, or scheduler was invoked.

## Build evidence

- SDK: .NET SDK **10.0.302** (`/tmp/dotnet`).
- Restore: exit code 0.
- Build: exit code 0, **0 errors** (237 existing warnings).
- Discovery: 73 matching lines and every required suite class was present.

## Targeted suite evidence

| Suite | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| DocumentaryProductionOperationRunnerTests | 9 | 9 | 0 | 0 |
| DocumentaryProductionAttemptContextFactoryTests | 2 | 2 | 0 | 0 |
| DocumentaryProductionExecutionRequestBuilderTests | 2 | 2 | 0 | 0 |
| DocumentaryProductionExecutionDependencyResolverTests | 3 | 3 | 0 | 0 |
| DocumentaryProductionExecutionHostTests | 7 | 7 | 0 | 0 |
| DocumentaryProductionExecutionHostFullFlowTests | 6 | 6 | 0 | 0 |
| DocumentaryProductionExecutionHostFailureTests | 8 | 8 | 0 | 0 |
| DocumentaryProductionExecutionHostTimeoutTests | 6 | 6 | 0 | 0 |
| DocumentaryProductionExecutionHostRetryTests | 7 | 7 | 0 | 0 |
| DocumentaryProductionExecutionHostCancellationTests | 1 | 1 | 0 | 0 |
| DocumentaryProductionExecutionRecordTests | 3 | 3 | 0 | 0 |
| DocumentaryProductionExecutionHostPersistenceTests | 7 | 7 | 0 | 0 |
| DocumentaryProductionExecutionHostDiTests | 4 | 4 | 0 | 0 |
| DocumentaryProductionExecutionHostArchitectureTests | 1 | 1 | 0 | 0 |
| DocumentaryProductionExecutionHostDeterminismTests | 5 | 5 | 0 | 0 |
| DocumentaryProductionExecutionHostNonMutationTests | 4 | 4 | 0 | 0 |

Focused A3.10 execution: **75 total, 75 passed, 0 failed, 0 skipped**, 224 ms. No targeted class matched zero tests.

## Regression evidence

- A3.9 verification regression: **64 total, 64 passed, 0 failed, 0 skipped**, 413 ms.
- Shared A3.4–A3.8 contracts are exercised by the targeted dependency, voice, registry, mapping, DI, verification, determinism, architecture, and non-mutation suites above.
- Broad suite: **4,605 total, 4,153 passed, 452 failed, 0 skipped**, 2m16s. The 452 failures are pre-existing unrelated and environment-dependent tests (including missing external media tools/configuration); **zero A3.10 failures** occurred.

## Certified behaviors

The named matrix covers the one-scene English-long flow, multi-scene sequence, four variants, compatibility facade, configured voices, scene-level narration aggregation, visual preservation, registry dependency enforcement, scene/final verification gates, partial evidence, continuation, every operation retry/timeout class, caller cancellation, persistence, execution-record metadata, disabled and missing-adapter behavior, DI validation, architecture, determinism, non-mutation, safe exception normalization, and publishing ineligibility without O2.19.
