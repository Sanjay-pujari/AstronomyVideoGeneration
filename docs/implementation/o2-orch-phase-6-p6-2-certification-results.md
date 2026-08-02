# O2.ORCH.6.2.H1 certification results

## Execution

- **UTC execution time:** 2026-08-02
- **Branch:** `work`
- **Commit:** recorded by the repository commit containing this document.
- **Build result:** not executable in this container because `dotnet` is not installed (`exit 127`).

## Change inventory

### Files added

- `Backend/tests/Astronomy.MediaFactory.Tests/Phase6InputAuthorityEvaluatorTests.cs`
- `Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelinePhase6RoutingTests.cs`
- `docs/implementation/o2-orch-phase-6-p6-2-certification-results.md`

### Files modified

- `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/Phase5BlueprintCertificationContracts.cs`
- `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/StoryFrameAuthorityContracts.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/DocumentaryBlueprint/Phase4CommittedAuthorityEvaluator.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/DocumentaryBlueprint/Phase5CommittedAuthorityEvaluator.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/DocumentaryBlueprint/Phase6InputAuthorityEvaluator.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/StoryFrameIntegrationService.cs`
- `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`

## Production boundary and gaps fixed

The undefined Phase 6 logging locals were replaced with typed `StoryFrameIntegrationRequest` properties. The Phase 5 committed evaluator now exposes the transaction ID and both committed-state gates read from its validated publication evidence. Phase 4 exposes explicit committed-validation and manifest evidence. Phase 6 rejects missing/unsafe evidence, missing publication identity, false committed gates, malformed allowed/requested variants, unsupported intent variants, ordinal scene mismatches, and incomplete certified scene evidence. Expected evaluator exceptions are normalized while cancellation propagates.

The final input remains one immutable `Phase6CommittedInputAuthority`: committed Phase 4 aggregate and projections, explicit Phase 4 validation/manifest evidence, the committed Phase 5 complete set and inventory, distinct certification/editorial/publication identities, validated gates, canonical requested variants, and independent Long/Short certified scene snapshots.

- **Phase 4 evidence source:** dedicated `CommittedValidationEvidence` and `ManifestEvidence` on `Phase4CommittedAuthorityEvaluation`, populated from the inventory already owned by the committed evaluator.
- **Phase 5 evidence source:** the seven physically and semantically validated `Phase5ArtifactInventoryEntry` values plus explicit validation and manifest evidence on `Phase5CommittedStateEvaluation`.
- **Publication identity source:** `transactionId` in the committed Phase 5 validation record, parsed and validated by `Phase5CommittedAuthorityEvaluator`, then exposed as `PublicationTransactionId`; it is not the editorial contract ID.
- **Committed-state gates:** typed `PublicationCommitted` and `CommittedStateValidationPassed`, set only after committed validation and manifest validation succeed, and explicitly gated again at the Phase 6 join.
- **Requested variants:** requested duplicates are deduplicated case-insensitively and values are normalized and ordered exactly `Long`, then `Short`. Allowed variants must be nonempty, supported, nonblank, and unique ignoring case.
- **Scene IDs:** `StringComparer.Ordinal` for source duplicates, traceability, intent grouping, reconciliation, and lookup.
- **Diagnostics paths:** one `StoryFrameCommittedInputDiagnostics.ArtifactPaths` projection supplies integration and validator expectations. It contains requested Phase 4 projections and typed Phase 4/5 committed evidence, rejects unsafe/staging/backup paths, and excludes certification diagnostics and legacy story graph authority.
- **Diagnostic counts:** `InputSceneCount` counts requested variant-scene inputs; `GeneratedSceneCount` counts distinct source IDs; `GeneratedVariantSceneCount` counts distinct variant/source-ID pairs.
- **DI:** source inspection shows one scoped `IPhase6InputAuthorityEvaluator` registration. Executable verification was blocked by the missing SDK.

## Command results

The process could not start for any .NET command, so test totals are **N/A**, not zero. No test is reported as passed or failed without execution.

| Suite | Total | Passed | Failed | Skipped | Duration | Result |
|---|---:|---:|---:|---:|---:|---|
| Build (`dotnet build Backend/Astronomy.MediaFactory.slnx --no-restore --logger "console;verbosity=normal"`) | N/A | N/A | N/A | N/A | N/A | not started; `dotnet: command not found`, exit 127 |
| `Phase6InputAuthorityEvaluatorTests` filter | N/A | N/A | N/A | N/A | N/A | not started; exit 127 |
| `ProductionPipelinePhase6RoutingTests` filter | N/A | N/A | N/A | N/A | N/A | not started; exit 127 |
| `StoryFrameIntegrationCommittedInputTests` filter | N/A | N/A | N/A | N/A | N/A | not started; exit 127 |
| `Phase6|StoryFrame` filter | N/A | N/A | N/A | N/A | N/A | not started; exit 127 |
| `Phase4|DocumentaryBlueprint` regression filter | N/A | N/A | N/A | N/A | N/A | not started; exit 127 |
| `Phase5|PublicationTransaction` regression filter | N/A | N/A | N/A | N/A | N/A | not started; exit 127 |
| Complete test project | N/A | N/A | N/A | N/A | N/A | not started; exit 127 |

## Exact failures and remaining work

- **Exact failed tests:** none can be classified because the test host never started. The environment failure is `/bin/bash: dotnet: command not found` for every requested command.
- **Uncertified P6.2 evidence:** compilation and the mandatory full focused/regression matrices remain unverified; the requested exhaustive named test matrix is not complete in this change.
- **P6.3 (not implemented):** redesign Story Frame mapping to a certified per-variant bijection/one-frame-per-scene model and remove shared editorial scene-order assumptions. No P6.3 mapping change is included here.

## Final verdict

PHASE6_COMMITTED_INPUT_BOUNDARY_STILL_FAILING
