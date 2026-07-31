# O2.ORCH.4 Final Certification

## Scope and authority

This cleanup removed the uncalled legacy RC2 Phase 4 response mapper without
changing Phase 4 execution. The published `DocumentaryBlueprintAggregate` remains
the sole Phase 4 authority. Phase 5 compatibility diagnostics now name the
in-memory published aggregate, its Long and Short variants, and their checksums;
they do not describe legacy files as adapter inputs.

When the batch response contains a `ProductionPipelineRequest` with a non-empty
`AstronomyEventIntelligenceId`, `Rc2CertifiedExecutionStatusReader` now supplies
that EventId to `IPhase4CommittedAuthorityEvaluator`. Otherwise it preserves the
previous empty fallback.

## Executed commands and totals

All commands below were executed on 2026-07-31 with .NET SDK 10.0.302.

| Scope | Command | Result | Total |
|---|---|---|---:|
| Focused certification | `dotnet test Backend/Astronomy.MediaFactory.slnx --no-restore --filter "FullyQualifiedName~Rc2CertifiedApiIntegrationTests\|FullyQualifiedName~Phase4DownstreamAuthorityArchitectureTests"` | Passed | 12 passed, 0 failed, 0 skipped |
| Architecture | `dotnet test Backend/Astronomy.MediaFactory.slnx --no-restore --no-build --filter "FullyQualifiedName~Phase4DownstreamAuthorityArchitectureTests"` | Passed | 11 passed, 0 failed, 0 skipped |
| RC2 API certification | `dotnet test Backend/Astronomy.MediaFactory.slnx --no-restore --no-build --filter "FullyQualifiedName~Rc2CertifiedApiIntegrationTests"` | Passed | 1 passed, 0 failed, 0 skipped |
| Documentary Blueprint / RC2 regression | `dotnet test Backend/Astronomy.MediaFactory.slnx --no-restore --no-build --filter "FullyQualifiedName~DocumentaryBlueprint\|FullyQualifiedName~RC2"` | Failed in existing unrelated tests | 1,033 passed, 4 failed, 0 skipped; 1,037 total |
| Complete solution | `dotnet test Backend/Astronomy.MediaFactory.slnx --no-restore --no-build` | Failed in the repository baseline | 4,350 passed, 455 failed, 11 skipped; 4,816 total |

The build emitted existing repository and dependency warnings, including NU1510,
NU1903, and pre-existing compiler/analyzer warnings. No warning identified in the
changed production files was introduced by removing the dead method.

## Runtime and API evidence

The focused API certification used ASP.NET Core `TestServer` and made two real HTTP
POST requests to `/api/content-planning/rc2/batch-generate-from-plans`. Both
requests returned HTTP 200 and the serialized RC2 certification contract was
validated. However, that test registers a deterministic `CertifiedEndpoint` test
double rather than the production pipeline orchestrator. Its values are therefore
contract evidence, not proof of a real pipeline execution.

| Evidence | Observed value |
|---|---|
| API execution | Two in-process HTTP POST requests returned HTTP 200 |
| Aggregate checksum | `aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa` (synthetic API fixture value) |
| Committed-state validation | `true` (synthetic API fixture value) |
| Long scene count | 12 (synthetic API fixture value) |
| Short scene count | 4 (synthetic API fixture value) |
| Idempotent rerun | Contract test returned `AlreadyPublished=true` and preserved the three upstream fixture checksums |
| Real production pipeline execution | Not executed by the available RC2 API certification test |
| Real aggregate checksum and scene counts | Not established |

## Remaining technical debt

* The RC2 API certification test still substitutes `CertifiedEndpoint`; a
  non-mocked API-to-production-pipeline certification test is not present.
* The focused Documentary Blueprint / RC2 regression has four existing failures:
  `Phase8_RangeRequest_RunsNarrationV5AndAddsOutputsToResponseAndManifest`,
  `Phase4And5_BuildStoryGraphThenEditorialIntelligence`,
  `Authoritative_validation_is_preserved_and_not_overwritten_by_generic_validator`
  for Phase 3, and
  `Phase7_BuildsCreativeStoryboardAndDiagnosticsFromEditorialArtifacts`.
* The complete solution has 455 failures and 11 skipped tests. These failures span
  unrelated semantic, visual, publishing, database, and media-tooling areas and
  prevent full regression certification.
* NU1903 reports a known high-severity vulnerability in
  `SQLitePCLRaw.lib.e_sqlite3` 2.1.11.

Because the complete certification suite did not pass and a real production
pipeline execution was not demonstrated, the Phase 5 readiness gate is not met.

NOT_READY_FOR_PHASE_5
