# O2.ORCH.4 Final Certification

## Scope and authority

RC2 now treats an explicit `StartPhaseNo=1`, `EndPhaseNo=4` request as an exact
range. It does not expand the production request into Phase 5 or Phase 6. Phase 5
continues to receive a compatibility projection only when Phase 5 is explicitly
requested.

The published `DocumentaryBlueprintAggregate` remains the sole Phase 4 authority.
Certification is based on its deterministic checksum and the embedded Long and
Short projection checksums. `Master` is only an in-memory compatibility view; its
checksum is compatibility metadata, is not published, is not entered in the Phase 4
manifest, and cannot compete with the published aggregate.

The compatibility adapter maps each scene's certified Phase 4 knowledge selections
into its legacy section knowledge map and retains scene-to-learning-objective
coverage from the published variant traceability.

`Rc2CertifiedExecutionStatusReader` obtains committed-state validity, publication
commit state, validation status, artifact inventory, and reason code from
`IPhase4CommittedAuthorityEvaluator`. Its only direct file-existence observation is
the separately labelled optional legacy Story Graph compatibility artifact; that
observation is never used as certification evidence.

## Executed commands and totals

| Scope | Command | Result | Total |
|---|---|---|---:|
| SDK discovery | `dotnet --info` | Could not execute: `dotnet` is not installed in this container | Not available |
| Focused certification | `dotnet test Backend/Astronomy.MediaFactory.slnx --filter "FullyQualifiedName~Rc2CertifiedApiIntegrationTests|FullyQualifiedName~Phase4DownstreamAuthorityArchitectureTests"` | Not executed: SDK unavailable | Not available |
| Regression | `dotnet test Backend/Astronomy.MediaFactory.slnx --filter "FullyQualifiedName~DocumentaryBlueprint|FullyQualifiedName~RC2"` | Not executed: SDK unavailable | Not available |
| Solution | `dotnet test Backend/Astronomy.MediaFactory.slnx` | Not executed: SDK unavailable | Not available |

## Runtime evidence

The required real pipeline-backed API execution could not be run in this container,
so production values must not be fabricated.

| Evidence | Certified value |
|---|---|
| Physical checksum | Not produced |
| Aggregate checksum | Not produced |
| Manifest checksum | Not produced |
| Committed-state result | Not executed |
| Long scene count | Not observed |
| Short scene count | Not observed |
| Actual API request | Not sent |
| Actual API response | Not received |
| Idempotent rerun | Not executed |

## Remaining technical debt

The repository still lacks the requested non-mocked
`rc2_api_executes_real_certified_phase1_to_phase4` runtime test, and this environment
lacks the .NET SDK needed to produce the required execution evidence and test totals.
Consequently the production gate cannot be truthfully certified by this audit.

NOT_READY_FOR_PHASE_5
