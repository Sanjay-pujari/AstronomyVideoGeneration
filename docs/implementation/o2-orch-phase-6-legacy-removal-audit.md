# O2.ORCH.6.5 Phase 6 legacy-removal audit

## Governing implementation note

The governing owner is `ProductionPipelineExecutionService.ExecutePhase6Async`, through `ExecuteLockedPhase6Async`, `PhaseChronicleDocumentaryArchitectCoreAsync`, the typed Phase 4/5 input evaluator, `IStoryFrameIntegrationService`, `StoryFrameArtifactValidator`, and `IStoryFrameAuthorityCommitter`. The RC2 API orchestrator is a response observer and is not a Phase 6 writer.

The documents reviewed were the RC2 implementation and pipeline guides, the artifact contract reference, `o2-orch-phase-6-story-frame-audit.md`, `o2-orch-phase-6-p6-2-certification-results.md`, the O2.ORCH.6.4 closure report, and the committed-authority material referenced by the Phase 4/5 sections of those reports. Phases 1–5 remain frozen.

## Exact current/legacy inventory

| Source file | Class | Method | Classification and current behavior | Current consumer | Disposition | Replacement | Test impact |
|---|---|---|---|---|---|---|---|
| `Infrastructure/Persistence/ProductionPipelineExecutionService.cs` | `ProductionPipelineExecutionService` | `ExecutePhase6Async` / `ExecuteLockedPhase6Async` / `PhaseChronicleDocumentaryArchitectCoreAsync` | Canonical Phase 6 execution and successful validation publication | Production RC2 pipeline | Retain | N/A | Covered by Phase 6 routing tests |
| same | same | `WriteCanonicalPhase6ValidationAsync` | Canonical successful/reuse validation writer | manifest/API readers | Retain and complete canonical fields | N/A | Validation contract coverage |
| same | same | `RemoveLegacyPhase6Artifacts` | Exact allow-list cleanup; deletes only ten retired files and empty owned parents on overwrite | canonical Phase 6 execution | Add | No legacy compatibility generation | Cleanup behavior coverage |
| `Infrastructure/Orchestration/RC2/Rc2ContentPlanningBatchOrchestrator.cs` | `Rc2ContentPlanningBatchOrchestrator` | former Phase 6 branch in `GenerateFromPlansAsync` | Obsolete legacy execution, validation writer, manifest writer, and response aggregator | RC2 batch endpoint | Removed | Observe production result and certified status | Legacy constructor assertions updated |
| same | same | former `ExecuteRc2OverlayPhaseAsync`, `WriteRc2PhaseValidationAsync`, `BuildPhase6ValidationPayload`, `ApplyRc2Phase6Response`, `UpsertPhaseManifestAsync` and supporting legacy manifest/diagnostic helpers | Read `editorial/*`, executed `CreativeStoryboardBuilder`, wrote legacy validation/history | Former Phase 6 overlay | Removed | canonical production owner | Superseded overlay assertions no longer drive API behavior |
| `Infrastructure/Orchestration/RC2/CreativeStoryboardBuilder.cs` | `CreativeStoryboardBuilder` | `BuildAndWriteDiagnosticsAsync` | Legacy compatibility producer for `creative/*` and `story-frames/{short,long}/*`; no longer reachable from current RC2 Phase 6 routing | isolated adapter/tests | Isolated outside current route | `IStoryFrameIntegrationService` | Standalone legacy coverage remains non-governing |
| `Infrastructure/Orchestration/RC2/CertifiedStoryFrameBuilderAdapter.cs` | `CertifiedStoryFrameBuilderAdapter` | `BuildAsync` | Compatibility adapter around the builder's in-memory canonical build API | story-frame integration | Retain as builder adapter; it does not invoke legacy file writer | typed integration request/result | Existing committed-scene tests |
| `Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | `NarrationGeneratorV5` | legacy input reads | Obsolete Phase 7 consumer of creative contracts/storyboard | Phase 7 execution | Known blocked boundary; Phase 7 not executed or redesigned by this change | future `PublishedStoryFrameAuthority` evaluator boundary | Phase 7 readiness remains incomplete |
| `Infrastructure/Persistence/ProductionPipelineExecutionService.cs` | `ProductionPipelineExecutionService` | `ValidatePhase7ChronicleCoreInputs` | Obsolete Phase 7 precondition names legacy Phase 6 files | Phase 7 | Known blocked boundary, deliberately not executed | future typed committed authority | no Phase 7 implementation in this task |
| `Core/Certification/CertificationServices.cs` | `Phase6Certifier` / registry | certification declarations | Legacy family-certification contract and phase name | non-RC2 certification subsystem | Isolated from current RC2 route; pending removal with certification migration | canonical three-artifact inventory | legacy certifier tests remain separate |
| `tests/.../Rc2StoryIntelligenceTests.cs` | `Rc2StoryIntelligenceTests` | direct builder tests | Test-only assertions for compatibility producer | tests only | Keep isolated; remove builder injection from orchestrator construction | canonical routing architecture test | constructor calls updated |

## Retired artifact/path inventory

The obsolete Phase 6 authority paths found are `editorial/story-graph.json`, legacy `editorial/editorial-contract.json` and `editorial/scene-intents.json`, all six named `creative/*` outputs, and the short/long story-frame manifest and diagnostics paths. The explicit overwrite cleanup owns only:

- `creative/creative-storyboard.json`
- `creative/creative-diagnostics.json`
- `creative/documentary-contract.long.json`
- `creative/documentary-contract.short.json`
- `creative/documentary-architecture-diagnostics.json`
- `creative/documentary-decision-log.json`
- `story-frames/short/story-frame-manifest.json`
- `story-frames/short/story-frame-diagnostics.json`
- `story-frames/long/story-frame-manifest.json`
- `story-frames/long/story-frame-diagnostics.json`

The governing replacement paths are exactly `06-story-frames/story-frames.json`, `06-story-frames/story-frame-index.json`, and `06-story-frames/story-frame-diagnostics.json`. Cleanup does not recurse and legacy residue is ignored when reuse is evaluated.

## Residual scan and verdict

Current RC2 Phase 6 routing contains no call to `CreativeStoryboardBuilder`, no Creative Intelligence phase label, no legacy editorial input, and no legacy validation/manifest writer. Residual matches are the isolated compatibility implementation, legacy certification subsystem, tests documenting compatibility, and the not-yet-migrated Phase 7 consumer. Therefore current Phase 6 routing cleanup is complete, while end-to-end Phase 7 typed-boundary and committed-evaluator certification remain explicit blockers.
