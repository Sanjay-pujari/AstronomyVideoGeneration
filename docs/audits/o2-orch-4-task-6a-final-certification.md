# O2.ORCH.4 Task 6A — Final Phase 4 Certification

## Legacy authority removed

The production Phase 4 path invokes `DocumentaryBlueprintPhase4IntegrationService` and publishes through
`Phase4DocumentaryBlueprintPublicationService`. It does not inject either `DocumentaryBlueprintBuilder` or
`StoryGraphBuilder`. The RC2 status explicitly reports whether the historical Story Graph file was produced.

## Downstream migration

The certified downstream authority type is `PublishedDocumentaryBlueprintAggregate`. Legacy-shaped values may
exist only as compatibility views; they are not an independent publication or a second authority. Phase 4 Long
and Short variants remain the variants embedded in the published aggregate and are not re-derived downstream.

## Execution context changes

`ProductionPipelineExecutionContext.PublishedDocumentaryBlueprintAggregate` carries the exact aggregate returned
by successful committed-state read-back. The pipeline installs that instance immediately after Phase 4. Disk is
the recovery boundary, rather than the normal in-process phase boundary.

## Static architecture proof

`Phase4DownstreamAuthorityArchitectureTests` verifies the context contract, rejects legacy Phase 4 builder
constructor dependencies, and identifies the publication service as the Phase 4 publication boundary.

## RC2 API verification

`rc2_api_executes_certified_phase1_to_phase4` posts to the RC2 batch endpoint and verifies successful Phase 1–4
status, aggregate checksum, 12 Long scenes, 4 Short scenes, committed physical authority, committed-state
validation, and absence of legacy authority.

## Idempotent verification

The same API test reruns Phase 4 and requires `AlreadyPublished` while proving that the frozen upstream checksums
did not change. Publication inventory and manifest reconciliation remain owned by the atomic publication service,
which prevents duplicate authority and duplicate manifest entries.

## Remaining technical debt

Historical RC2 overlay and narration compatibility components still understand Story Graph-shaped diagnostic
input. They are not the certified production Phase 4 publisher. Their eventual deletion can occur after all
non-production replay fixtures have moved to aggregate-native contracts.
