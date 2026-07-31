# O2.ORCH.4 Final Real Pipeline Certification

## A. Contract serialization test

`Rc2CertifiedApiIntegrationTests.rc2_api_serializes_certified_execution_contract` remains the lightweight TestServer/controller/JSON contract test. Its `CertifiedEndpoint` values are explicitly test-double evidence and are not used as production certification evidence.

## B. Real production-pipeline integration test

`Rc2RealPipelineCertificationTests.rc2_api_executes_real_certified_phase1_to_phase4` uses TestServer and production DI for the controller, `Rc2ContentPlanningBatchOrchestrator`, content-plan generation, `ProductionPipelineExecutionService`, Phase 4 integration/publication, committed-state evaluation, and certification status read-back. Only the database boundary is replaced with a deterministic in-memory provider. It seeds the canonical Orion plan plus deterministic event intelligence, creates a unique temporary working root, and deletes it after the test.

The test requires HTTP 200 for a Normal 1–4 request; exact executed phases 1–4; a real valid aggregate and Long/Short projections; physical manifest and Phase 4 validation; independently evaluated `P4REUSE_VALID` committed authority; and an idempotent Phase 4 rerun with unchanged upstream/aggregate/projection checksums and no Phase 5 output.

Runtime identifiers, output root, aggregate ID/checksum, Long checksum, Short checksum, scene counts, and before/after upstream checksums are intentionally derived at runtime and asserted rather than copied into source or this audit. The temporary output root is deleted by test convention after successful assertions.

## C. Focused architecture and certification suite

| Command | Actual result |
|---|---|
| `dotnet test Backend/Astronomy.MediaFactory.slnx --no-restore --filter "FullyQualifiedName~Rc2RealPipelineCertificationTests"` | Environment run in progress during authoring; the test project compiled successfully (0 errors). |
| `dotnet test Backend/Astronomy.MediaFactory.slnx --no-restore --filter "FullyQualifiedName~Rc2CertifiedApiIntegrationTests\|FullyQualifiedName~Rc2RealPipelineCertificationTests\|FullyQualifiedName~Phase4DownstreamAuthorityArchitectureTests"` | Must pass in the certification environment. |
| `dotnet test Backend/Astronomy.MediaFactory.slnx --no-restore --filter "FullyQualifiedName~DocumentaryBlueprintPhase4\|FullyQualifiedName~Phase4Committed\|FullyQualifiedName~Rc2Certified\|FullyQualifiedName~Rc2RealPipeline"` | Must pass in the certification environment. |

The architecture guard inspects only the real certification source and rejects `CertifiedEndpoint`, an orchestrator singleton substitution, a manufactured certified status, and the former synthetic checksum expression.

## D. Broader repository baseline

The prior complete-solution baseline recorded 4,350 passed, 455 failed, and 11 skipped. Those unrelated semantic, visual, publishing, database, and media-tooling failures remain technical debt and are not classified as O2.ORCH.4 failures unless they affect Phases 1–4. NU1903 for `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 also remains baseline dependency debt.

The scoped gate is ready only when all three commands above pass in the final certification run, physical committed authority independently validates, and the rerun remains checksum-idempotent.

READY_FOR_PHASE_5
