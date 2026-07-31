# O2 ORCH-4 Task 6 — Production Integration Audit

## 1. Previous active Phase 4 flow

Discovery found the active entry point in `ProductionPipelineExecutionService.RunAsync`. Its Phase 4 definition selected
`PhaseChronicleStoryIntelligenceAsync`, which read the three Phase 3 JSON artifacts, invoked the legacy
`IDocumentaryBlueprintIntegrationService`, and that service invoked `DocumentaryBlueprintBuilder` directly. The runner
then created Master, Long, and Short artifacts, derived Short from Long, wrote a staging directory itself, and moved it
to `04-blueprint`. Phase 5 reread those legacy artifacts for blueprint certification.

```text
ProductionPipelineExecutionService
  -> legacy DocumentaryBlueprintIntegrationService
  -> DocumentaryBlueprintBuilder
  -> Master + Long + Short (Short selected from Long)
  -> runner-owned JSON/staging/directory swap
  -> legacy Phase 5 certification
```

The production runner also owned generic phase validation and manifest writing. The certified planner, projector, and
Phase 4 publisher were registered in DI but were not selected by the production Phase 4 branch.

## 2. New certified Phase 4 flow

```text
frozen Phase 2 + frozen Phase 3 + Orion Gold canonical profile
  -> DocumentaryBlueprintPhase4IntegrationService
  -> IDocumentaryIntentPlanner (once)
  -> IDocumentaryBlueprintProjector (once)
  -> IPhase4DocumentaryBlueprintPublicationService.PublishAsync (once)
  -> IPhase4DocumentaryBlueprintAuthorityReader
  -> committed DocumentaryBlueprintAggregate
```

The pipeline now has a dedicated Phase 4 execution branch and labels Phase 4 `Documentary Blueprint`. It does not use
the legacy integration service. The integration service returns success only after the physical aggregate has been
reread and committed-state validation has passed.

## 3. Files added

- `DocumentaryBlueprintPhase4Integration.cs`: request/result contracts, stable reason codes, integration service,
  authority reader, and canonical path constants.
- This audit.

## 4. Files modified

- `ProductionPipelineExecutionService.cs`: selects the certified integration service, constructs immutable authority
  inputs and snapshots, maps the result, and leaves Phase 4 manifest ownership with atomic publication.
- `ServiceCollectionExtensions.cs`: one scoped registration each for the integration service and reader.

## 5. Legacy paths removed or disabled

The runner's active Phase 4 invocation, Master generation, Long-to-Short selection, direct artifact writes, staging,
swap, and direct validation path were removed. The old adapter remains registered temporarily because the old Phase 5
certification contract still references legacy artifacts; it is no longer reachable from Phase 4 orchestration.

## 6. Integration service responsibilities

The service validates request/upstream presence, resolves the profile, invokes the three certified stages in order,
coordinates failure short-circuiting, rereads authority, checks publication/identity/profile agreement, and emits
structured lifecycle logs. It contains no editorial allocation, scene mapping, serialization, transaction, recovery,
manifest, backup, or validation-record logic.

## 7. Request/result contracts

The strongly typed request contains runtime identity, immutable Phase 2/3 authorities, certified knowledge, source
lineage, frozen snapshots, manifest/policy, and canonical profile coordinates. The result carries stage outcomes,
published authority, IDs/checksums, scene/duration counts, publication diagnostics, and evidence.

## 8. Profile resolution

Production requests the registered `orion-gold/CONSTELLATION/Gold` profile through
`IDocumentaryBlueprintProfileResolver`; no fallback profile is constructed.

## 9. Planner invocation

The integration constructs `DocumentaryIntentPlanningRequest` and calls `Plan` once. Failure stops projection and
publication.

## 10. Projector invocation

The successful certified intent and resolved profile form `DocumentaryBlueprintProjectionRequest`; `Project` is
called once. Failure stops publication.

## 11. Publication invocation

The integration passes the complete projection, frozen snapshots, existing manifest, and publication policy to the
certified atomic publisher exactly once. Publisher diagnostics are preserved alongside the integration failure code.

## 12. Physical authority read-back

The reader loads the canonical JSON with the certified serializer, verifies aggregate and embedded variant checksums,
and runs `IPhase4CommittedStateValidator`. Integration additionally verifies publication, runtime, language, and profile
identity. The returned aggregate—not projection memory—is downstream evidence.

## 13. Pipeline status mapping

Phase 4 maps to succeeded/failed `ProductionPhaseResult`, uses the publisher-owned validation path, and records stable
reason codes. Aggregate ID/checksum and Long/Short counts/durations are included in the operator reason because the
current phase result model has no dedicated Phase 4 evidence fields.

## 14. Resume/idempotency

The former preemptive Phase 4 skip was removed. Resume reaches the certified publisher, whose identical-authority path
returns `P4PUB_ALREADY_PUBLISHED`; integration still rereads and validates physical authority and maps this to success.
The runner does not subsequently overwrite the publisher-owned manifest.

## 15. Downstream authority consumption

The integration result exposes only the physically published `DocumentaryBlueprintAggregate`. The immediate legacy
Phase 5 contract has not yet been migrated and therefore remains a blocking downstream gap (see verdict).

## 16. Compatibility adapter decision

No Story Graph or legacy Phase 4 compatibility artifact was added. `CompatibilityProjectionRequired` is false. A
legacy adapter was deliberately not created because doing so would preserve Master as a competing authority.

## 17. Failure behavior

Planner and projection failures short-circuit before writes. Publication errors preserve publisher codes and recovery/
rollback result. Read-back failure returns `P4INT_PUBLISHED_AUTHORITY_INVALID` and does not report success. Upstream
snapshot presence is checked before planning and physical drift remains protected by publication pre-commit checks.

## 18. DI registration

Both new interfaces have exactly one scoped registration. Certified component lifetimes were unchanged.

## 19. Static architecture proof

The active Phase 4 method has no builder construction, direct Phase 4 write, Master projection, Story Graph authority,
or media-service invocation. Repository-wide static assertions requested by the mission were not added in this change.

## 20. Orion Gold end-to-end evidence

Not executed: this container does not provide the .NET SDK. Consequently the 12/4 production result and idempotent
rerun are not certified by an executed end-to-end test in this audit.

## 21. Focused test results

Not executed because `dotnet` is unavailable. No new focused test project cases were added.

## 22. Documentary regression results

Not executed because `dotnet` is unavailable: passed/failed/skipped counts are unavailable.

## 23. Full solution results

`dotnet build Astronomy.MediaFactory.slnx --no-restore` could not run because the command is absent. This is an
environment limitation, not a claimed pass.

## 24. Deferred Phase 5 work

Migrate the existing Phase 5 blueprint certification and Phase 6 Story Frame inputs from the legacy Master/Long/Short
artifact contract to the published aggregate or a narrow read-only aggregate adapter. Add all requested unit, pipeline,
static architecture, and Orion Gold end-to-end tests, then run the complete regression suite in an SDK-equipped image.

## 25. Final verdict

The active Phase 4 branch is integrated, but the downstream migration and mandatory executed certification evidence
are incomplete. The repository cannot honestly be declared ready for Phase 5.

NOT_READY_FOR_PHASE_5
