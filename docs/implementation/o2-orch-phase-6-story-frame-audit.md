# O2.ORCH.6.1 — Phase 6 Story Frame authority audit

**Audit date:** 2026-08-02  
**Scope:** contract and implementation audit only; no RC2 execution and no Phase 1–5, narration, image, TTS, or video change.

## 1. Governing documents reviewed

The three current governing documents are (1) `Architecture/RC2-Phase-Output-Contract-v1.0.md` (phase responsibility, output and consumer baseline), (2) `docs/audits/o2-orch-phase-4-documentary-blueprint-audit.md` (the certified-scene, 12/4 Orion, lineage, and Phase 6 boundary decisions), and (3) `docs/audits/o2-orch-5-phase5-hardening.md` (committed Phase 5 and matching Phase 4 lineage gate). The older `Architecture/Pipeline-21-Phase-Specification-v1.0.md`, `Architecture/RC2-Story-Intelligence-v1.0.md`, and `Architecture/RC2-Creative-Intelligence-v1.0.md` describe superseded phase numbering or the legacy creative implementation. They are evidence for compatibility consumers, not authority for the current phase map.

Where the old RC2 output contract names `creative/*` and long/short directories, the later committed-authority architecture and current phase registry prevail: those paths cannot silently become canonical again. This conflict is resolvable by retaining proven active consumers as compatibility projections until Phase 7 migration.

## 2. Frozen Phase 6 responsibility

Phase 6 transforms each certified Phase 4/5 scene into exactly one deterministic, narration-free visual-planning frame. It neither composes/repairs upstream content nor calls narrative composition, image generation, TTS, rendering, or Phase 3. Counts come from committed variants/profile (Orion currently 12 Long and 4 Short), never generic literals.

## 3. Frozen Phase 6 inputs

The only admissible input is a physically validated `PublishedDocumentaryBlueprintAggregate` plus a physically validated committed Phase 5 complete set. The complete-set reader must prove the listed Phase 4 authority/variants, Phase 4 validation and manifest and all seven Phase 5 artifacts, Phase 5 validation and manifest. It must gate `Certification.Passed`, accepted certification status, `StoryFrameEligible`, allowed requested variants, coverage/transitions/pause validity, committed publication, committed-state validity, and current Phase 4 lineage.

Current code improves on loose JSON reads by invoking `IPhase5CommittedAuthorityEvaluator` with `ExpectedPhase4(context)`. However it still separately uses `PreviousPhaseSucceeded(5)`, reads optional `certification-diagnostics.json`, and constructs Phase 6 from only certification/editorial contract. P6.2 must make the committed evaluator result the single request source and carry the complete-set evidence explicitly.

## 4. Required Phase 6 outputs

One committed complete set is recommended:

* `06-story-frames/story-frames.json` — canonical typed authority.
* `06-story-frames/story-frame-index.json` — exact supporting/downstream index projection.
* `06-story-frames/story-frame-diagnostics.json` — supporting diagnostics, never authority.
* `validation/phase-06-validation.json` — committed-state certification.
* the Phase 6 slice of `phase-manifest.json` — physical inventory and checksums.

Per-variant and per-scene files are **not currently required canonical projections**: the active authority contains variants/frames and the index exposes both ordered projections. Do not invent them merely to satisfy stale catalogs. If a later consumer requires streaming scene files, add them only as deterministic `VariantProjection`/`SceneProjection` outputs of the one authority after consumer proof.

Each frame must add/project the required question/objective text and primary identities, editorial outcome, transition, primary knowledge designation, min/target/max duration, safe opportunity, subject/object, composition, safe area, density, treatment, profile/version and complete Phase 4/5 lineage. No narration-bearing property is permitted.

## 5. Existing implementation inventory

| Component | Current role | Decision |
|---|---|---|
| `StoryFrameAuthorityContracts.cs` | authority/index/diagnostics contracts, semantic checksum, validator, request/result | Reuse and extend; this remains the single contract/validator owner. |
| `StoryFrameIntegrationService` | invokes builder once, assembles authority/index/diagnostics | Reuse as current integration owner. |
| `CertifiedStoryFrameBuilderAdapter` | test seam over `CreativeStoryboardBuilder.BuildCertifiedFramesAsync` | Reuse; do not add another engine. |
| `CreativeStoryboardBuilder.BuildCertifiedFramesAsync` | current certified-frame mapping | Reuse/modify mapping only; legacy file-writing entry point is not authority. |
| `StoryFrameAuthorityCommitter` | active-directory swap with local rollback | Reuse behind a broader transaction coordinator. |
| `StoryFrameTemporaryDirectoryRecovery` | stale staging/backup cleanup and valid-backup restoration | Reuse/extend with transaction state. |
| `IStoryFrameFileSystem` | move/delete/read test seam | Extend with file write/copy/replace/metadata operations. |
| `IStoryFrameRuntimeIdentityProvider` | builder/integration and contract compatibility identity | Reuse; add explicit compatibility identity/version to authority lineage. |
| `StoryFrameExecutionLock` | same-authority in-process serialization | Reuse unchanged. |
| `EvaluateStoryFrameResume` | private complete-set resume check | Extract/harden into committed-state evaluator. |
| `ProductionPipelineExecutionService` | routing, request creation, publication invocation, validation and manifest | Current execution owner; narrow its responsibilities via existing services, do not add route. |
| `CreativeStoryboardBuilder.BuildAsync` | old `creative/*` and `story-frames/*` publisher | Legacy/compatibility owner; not called by current Phase 6 route. Isolate from phase number 6 before compatibility retirement. |
| Core `Phase6Certifier`/registry | certifies old creative and scene-folder layout | Obsolete authority catalog with possible external certification consumers; isolate, then migrate. |
| Long/Short planners | pre-existing format-specific visual planning/diagnostic foundations | Not production authority builders; optional field-level reuse only, never fixed-template orchestration. |

## 6. Existing tests inventory

Named source inventory: `StoryFrameAuthorityChecksumTests` (5 test methods), `StoryFrameContractCompatibilityTests` (3), `StoryFramePhase6ManifestSecurityTests` (7), `Rc2Phase6ValidationStateTests` (6), `ProductionPipelinePhase6ConcurrencyTests` (3), `LongStoryFramePlannerTests` (4), and `ShortStoryFramePlannerTests` (4). Story Frame integration behavior is also embedded in broader Phase 4/5, RC2 API, and pipeline tests. There is no dedicated file named `StoryFrameIntegrationServiceTests`; publication/recovery/resume coverage is distributed rather than a complete fault matrix.

The requested executable baseline could not be collected: this container has no `dotnet` executable. Therefore totals are **unavailable**, not zero: total N/A, passed N/A, failed N/A, skipped N/A. Static method counts are inventory only and are not represented as executed results.

## 7. Current runtime execution flow

`ProductionPipelineExecutionService.ExecuteAsync` enumerates `PhaseDefinitions`, dispatches phase 6 only through `ExecutePhase6Async`, then `ExecuteLockedPhase6Async`, then `PhaseChronicleDocumentaryArchitectCoreAsync`. The definition’s `PhaseChronicleDocumentaryArchitectAsync` delegate is a legacy-shaped placeholder but the switch prevents its invocation for phase 6. Core flow: acquire lock; validate/recover Phase 4 and Phase 5; construct request; recover stale Phase 6 directories; evaluate reuse; call `StoryFrameIntegrationService`; validate candidate; write three staging files; deserialize/read back and validate; swap directory; return outputs; generic validation writer writes Phase 6 validation; generic manifest writer runs afterward.

The RC2 overlay orchestrator still contains legacy creative validation/augmentation logic, but the production execution route is the current RC2 production owner. API phase state is derived from its `ProductionPhaseResult`; no evidence shows the overlay calling current Phase 6 a second time on this path.

## 8. Current artifact flow

| Path | Producer | Known consumer | Classification/status |
|---|---|---|---|
| `06-story-frames/story-frames.json` | integration + pipeline staging | resume/validator; intended Phase 7 | `CanonicalAuthority`, required; semantic `SemanticChecksum`; manifest physical SHA-256 must be added. |
| `06-story-frames/story-frame-index.json` | `StoryFrameIndexProjector` | resume/downstream | `SupportingIndex`, required; semantic `Checksum`; physical SHA-256 missing from Phase 6 manifest. |
| `06-story-frames/story-frame-diagnostics.json` | integration | resume/operations | `SupportingDiagnostics`, required; no semantic checksum today; physical SHA-256 missing. |
| `validation/phase-06-validation.json` | generic validation writer | resume/API/status | `CommittedValidation`, required; not transactionally coupled. |
| `phase-manifest.json#phase6Artifacts` | generic manifest writer | resume/security/status | `ManifestEntry`, required; path/role only today. |
| `creative/creative-storyboard.json` | legacy `CreativeStoryboardBuilder.BuildAsync` | `NarrationGeneratorV5`, tests/certifier | `CompatibilityProjection`, actively consumed, not canonical. |
| `creative/documentary-contract.{long,short}.json` | legacy builder | `NarrationGeneratorV5`, orchestrator, tests/certifier | `CompatibilityProjection`, actively consumed pending Phase 7 migration. |
| `creative/documentary-architecture-diagnostics.json`, `documentary-decision-log.json` | legacy builder | Phase 7 preflight/certifier/tests | compatibility diagnostics; non-authoritative. |
| `story-frames/{long,short}/manifest.json`, `scene-*.json` | legacy builder | old orchestrator/certifier/tests | obsolete legacy projections; do not create from new authority without proven retained consumer requirement. |
| `creative/creative-diagnostics.json` / legacy diagnostics | legacy builder | old output contract/tests | obsolete/supporting compatibility only. |

Proposed final inventory metadata: every required canonical/supporting artifact gets a lowercase physical SHA-256, byte size and safe relative path in the manifest. Authority, index, diagnostics and validation carry their canonical semantic checksum; diagnostics requires a new checksum. Authority lineage includes aggregate ID/checksum, Long/Short checksum, certification ID/checksum, editorial checksum, profile ID/version and runtime identity/version. Index/diagnostics reference authority checksum. Variant/scene projections, if later approved, reference authority, variant and frame semantic checksums and use roles `VariantProjection`/`SceneProjection`.

## 9. Canonical versus legacy artifacts

The canonical three are exactly the `06-story-frames` files above. `creative/*` is compatibility-only where a real Phase 7/certification consumer exists and otherwise obsolete diagnostics. The `story-frames/{long,short}` tree is a legacy format, not required by the current authority model. The registry’s old paths do not mandate generation. Compatibility removal is blocked until `NarrationGeneratorV5`, `Rc2ContentPlanningBatchOrchestrator`, `Phase6Certifier`, and related tests consume the typed committed authority.

## 10. Duplicate-owner/routing analysis

There is one current execution route: `ExecutePhase6Async`. Current integration is `StoryFrameIntegrationService`; publication owner is the core method plus `IStoryFrameAuthorityCommitter`; resume owner is private `EvaluateStoryFrameResume`; validation and manifest owners are the generic pipeline writers. `PhaseChronicleDocumentaryArchitectAsync` is unreachable for phase 6 through the dispatch switch but should be removed/renamed to eliminate ambiguity. `CreativeStoryboardBuilder.BuildAsync` remains a legacy phase-6 publisher if independently called, and the RC2 overlay/old certifier still owns old result semantics.

No observed production call executes both builders in one `ProductionPipelineExecutionService` request. Nevertheless ownership is not safely frozen: after the authority directory swap, generic validation/manifest writes can fail; the legacy builder can independently overwrite `phase-06-validation.json`; and the overlay can reinterpret the result. Thus “one current route” is true, while “one exclusive owner of all committed state” is false.

## 11. Long/Short independence analysis

The adapter passes requested variants to one builder and the authority/index keep variant tags. The index independently filters each variant, and new lists are materialized. The visual planners explicitly prescribe native 16:9 and 9:16 and prohibit cross-format composition reuse. However the current request is based on a single Phase 5 editorial `SceneOrder`, and the validator requires every requested variant to contain that same order. It does not consume separate certified Long/Short scene authorities. Independence, no truncation, and 12/4 therefore are **not proven** by the current authority path.

## 12. One-frame-per-certified-scene analysis

The current validator requires at least one frame for every editorial scene per requested variant, canonical ordering and contiguous per-scene frame numbers, but it permits multiple frames per scene. `GeneratedFrameCount` is not required to equal the certified variant scene count. Unknown-scene detection is against a shared scene order. Consequently exactly once, variant-specific membership, and Orion 12/4 are missing requirements. P6.3 must map each committed variant scene once and validate a bijection `(variant, sourceSceneId) -> frame`.

## 13. Lineage and checksum analysis

Existing strengths: semantic authority/index checksums are deterministic JSON projections; generated timestamps are excluded; ordered frames are retained; collections whose order is not semantic are sorted; Phase 5 certification/editorial and one Phase 4 checksum reconcile; checksum shape is checked; runtime builder/integration compatibility is checked on reuse. Physical SHA-256 is not confused with these semantic hashes inside the checksum class.

Gaps: no aggregate ID, separate aggregate/Long/Short checksums, profile version, source-scene checksum, frame semantic checksum, primary knowledge identity, certification-status checksum evidence, diagnostics semantic checksum, or explicit runtime identity fields in authority. Upstream Phase 4 identity is flattened through Phase 5’s `SourcePhase4Checksum`. The manifest omits semantic and physical checksums for Phase 6. There must be exactly one checksum function per artifact/frame and upstream values must be copied from committed authority, not recomputed from compatibility objects.

## 14. Validation-gap analysis

Already present wholly or partly: allowed/requested variants, missing scenes, unknown scenes, duplicate frame IDs, ordering, relationship coverage, nonempty production intent, timing overlap/positive duration, lineage reconciliation, checksum format/recomputation, runtime compatibility, index/diagnostic reconciliation, unsafe diagnostic values and canonical manifest path/role checks.

Missing/inadequate: variant-specific required scene sets/counts; exact one frame per scene; duplicate/extra/cross-variant source scenes; certified per-variant order; question/objective text and single primary IDs; editorial outcome and exact transition intent; exactly one primary knowledge reference; min/target/max duration ordering; named safe opportunity/subject-object/composition/safe-area/text-density/treatment fields; unsupported-claim proof; explicit forbidden narration-field/value leakage scan; full Phase 4 aggregate/variant and Phase 5 complete-set lineage; profile version; per-scene/frame checksum; deterministic diagnostics checksum; manifest physical/checksum/size contract; validation and manifest role/path checks as part of committed-state evaluation.

## 15. Transaction/publication/recovery analysis

Present: same-plan lock, unique staging/backup directories, candidate validation, staged physical readback, staged validation, directory swap, restoration if the swap fails, stale staging/backup enumeration, newest valid backup restoration when active is absent, cleanup warnings, and filesystem fault seams for directory/read operations.

Missing: transaction marker/state machine; snapshots of manifest and Phase 6 validation; atomic coupling of authority, validation, and manifest; committed readback after all three publish; exact transaction-path recovery; file write/copy/replace fault seam; deterministic rollback after validation/manifest failure; restoration verification; failure artifact outside authority; rollback-failure diagnostics; crash recovery for “directory committed, metadata old”; cancellation policy across the entire commit. Generic overwrite cleanup can also destroy Phase 6 before replacement. Directory atomicity alone is insufficient.

## 16. Resume/reuse analysis

Resume checks validation success, three files, manifest paths/roles, deserialization, semantic checksums, lineage, projections and runtime compatibility under the lock. It correctly regenerates rather than trusting existence. It does not validate validation/manifest physical checksums or publication transaction identity, does not use a public typed committed-state evaluator, and relies on shared editorial order. A skipped validation is rewritten after reuse, which mutates evidence rather than returning the existing committed certification. P6.5 should return a typed committed authority with an explicit reuse reason and leave valid committed evidence unchanged.

## 17. Phase 6 → Phase 7 typed boundary

Introduce `PublishedStoryFrameAuthorityAggregate` (name subject to existing namespace convention) returned only by `IPhase6CommittedAuthorityEvaluator`. It contains immutable/deserialized `StoryFramesAuthority`, `StoryFrameIndex`, diagnostics, committed validation evidence, manifest artifact records, physical checksums/sizes, aggregate semantic checksum, Long and Short projection checksums, source Phase 4 aggregate/variant checksums, source Phase 5 certification/editorial checksums, profile/runtime identities, and publication transaction ID/state. Collections must be immutable/read-only snapshots.

Phase 7 receives this type, selects Long and Short independently, and records both blueprint variant and Story Frame variant/frame-set checksums. It never receives `CreativeStoryboardBuilder` objects. Migration of Phase 7 consumption is Phase 7 work, not this audit; compatibility projections remain until then.

## 18. Files to reuse unchanged

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/StoryFrameExecutionLock.cs`.
* Long/Short planners as standalone advisory visual-planning foundations and their existing tests (not authority execution).
* Phase 4/5 contracts, schemas, evaluators, publishers and tests—all frozen.

## 19. Files requiring modification

* `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/StoryFrameAuthorityContracts.cs`.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/StoryFrameIntegrationService.cs`.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/CertifiedStoryFrameBuilderAdapter.cs` and the certified mapping portion only of `CreativeStoryboardBuilder.cs`.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/StoryFrameAuthorityPersistence.cs`.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs`.
* `Backend/src/Astronomy.MediaFactory.Core/Certification/CertificationServices.cs` only to redirect/isolate the obsolete catalog after consumers are migrated.

## 20. Files to add

Prefer focused additions in `Infrastructure/Orchestration/RC2`: `StoryFrameCommittedAuthorityEvaluator.cs` and `StoryFramePublicationTransactionCoordinator.cs`. Add corresponding test files: `StoryFrameCommittedAuthorityEvaluatorTests.cs`, `StoryFramePublicationTransactionTests.cs`, `StoryFrameArtifactValidatorTests.cs`, `StoryFrameIntegrationServiceTests.cs`, and `ProductionPipelinePhase6RoutingTests.cs`. Names may follow the exact established Phase 4/5 convention discovered at implementation time; no duplicate engine is allowed.

## 21. Files to retire or isolate

After active-consumer migration, isolate/remove the legacy publishing entry of `CreativeStoryboardBuilder.BuildAsync`, the obsolete Phase 6 artifact registry entries and `Phase6Certifier` assumptions, and the unreachable `PhaseChronicleDocumentaryArchitectAsync` delegate. Do not remove legacy files or projections during P6.2–P6.5. `NarrationGeneratorV5` legacy reads remain until a distinct Phase 7 task.

## 22. Ordered implementation tasks

### P6.2 — committed input authority and request construction

**Files:** `StoryFrameAuthorityContracts.cs`, new `StoryFrameCommittedAuthorityEvaluator.cs`, `ProductionPipelineExecutionService.cs`, DI registrations; tests `StoryFrameCommittedAuthorityEvaluatorTests.cs`, `ProductionPipelinePhase6RoutingTests.cs`, existing Phase 4/5 committed-authority suites. **Entry:** Phase 4/5 committed evaluators green and frozen. **Exit:** one typed request can only be created from validated Phase 4 aggregate + Phase 5 complete set and rejects every failed gate/stale lineage/variant mismatch. **Frozen rules:** never deserialize loose compatibility JSON or mutate Phase 1–5. **Rollback risk:** low code-publication risk, high accidental upstream coupling; preserve old runtime behind the same route until tests pass.

### P6.3 — canonical Story Frame authority and independent Long/Short mapping

**Files:** `StoryFrameAuthorityContracts.cs`, `StoryFrameIntegrationService.cs`, `CertifiedStoryFrameBuilderAdapter.cs`, certified mapping section of `CreativeStoryboardBuilder.cs`; tests `StoryFrameIntegrationServiceTests.cs`, `StoryFrameAuthorityChecksumTests.cs`, new mapping/bijection tests plus existing planner tests. **Entry:** P6.2 typed input. **Exit:** independent immutable variant maps, deterministic IDs/checksums, exactly one frame per certified variant scene, source ordering/counts (Orion fixture 12/4) without global literals. **Frozen rules:** no narrative composition, no fixed 9/5, no Phase 4/5 repair. **Rollback risk:** medium; keep contract-version incompatibility forcing regeneration and do not publish until P6.4.

### P6.4 — artifact validator, lineage, checksums, and narration-leakage rules

**Files:** `StoryFrameAuthorityContracts.cs` (or a single extracted validator in the same Core domain); tests `StoryFrameArtifactValidatorTests.cs`, `StoryFrameAuthorityChecksumTests.cs`, `StoryFrameContractCompatibilityTests.cs`, `StoryFramePhase6ManifestSecurityTests.cs`. **Entry:** P6.3 contract frozen. **Exit:** every rejection in the governing validation list has a positive and negative test; authority/index/diagnostics/frame checksums have one canonical definition; explicit property-name and content narration-leakage rules pass. **Frozen rules:** semantic checksums do not use file hashes; copied upstream identities are not recomputed. **Rollback risk:** medium/high compatibility rejection; bump contract version and regenerate rather than accepting ambiguous old authority.

### P6.5 — atomic publication, committed readback, rollback, recovery, and reuse

**Files:** `StoryFrameAuthorityPersistence.cs`, new transaction coordinator/evaluator, `ProductionPipelineExecutionService.cs`; tests `StoryFramePublicationTransactionTests.cs`, `StoryFrameCommittedAuthorityEvaluatorTests.cs`, `ProductionPipelinePhase6ConcurrencyTests.cs`, recovery/resume fault matrix. **Entry:** P6.4 complete-set validator. **Exit:** transaction marker, exact snapshots, staged/readback validation, authority+validation+manifest commit, committed readback, deterministic rollback/restoration verification, stale crash recovery, cancellation policy, fault-injectable writes/moves and rollback-failure diagnostics all tested. **Frozen rules:** no generic cleanup destroys stable Phase 6; upstream read-only. **Rollback risk:** highest; use fault injection at every mutation and retain prior generation until verified commit.

### P6.6 — manifest/API/certification integration

**Files:** `ProductionPipelineExecutionService.cs`, `CertificationServices.cs`, RC2 result/status reader/orchestrator files proven active, DI; tests `StoryFramePhase6ManifestSecurityTests.cs`, `Rc2Phase6ValidationStateTests.cs`, `ProductionPipelinePhase6RoutingTests.cs`, RC2 API/certification tests. **Entry:** P6.5 committed aggregate. **Exit:** one phase owner/result; manifest includes safe roles, semantic+physical checksums and sizes; validation/API last-completed/failed state derives from committed evaluation; legacy certifier cannot overwrite state. **Frozen rules:** compatibility outputs remain non-authoritative and only for identified consumers. **Rollback risk:** medium routing regression; assert one integration invocation and byte-stable committed metadata on reuse.

### P6.7 — real RC2 Phase 1–6 execution and artifact certification

**Files:** no production change expected; certification fixtures/scripts and audit evidence only. **Tests:** full named matrix, combined `Phase6|StoryFrame` filter, Phase 1–5 regressions, and one real RC2 request ending at 6. **Entry:** P6.6 green in a .NET-capable environment. **Exit:** physical complete set is certified, Orion derives 12/4, no Phase 7/art generation occurs, exact totals and checksums are recorded. **Frozen rules:** end phase exactly 6; no external media providers. **Rollback risk:** operational output contamination; use isolated output root and never point at certified fixtures.

**First recommendation:** start P6.2. Without a committed, typed, variant-aware input, mapping or validator changes would encode the wrong authority and repeat the loose-JSON defect.

## 23. Test matrix

| Area | Required proof |
|---|---|
| Input | every Phase 4/5 file/gate missing, corrupt, uncommitted, stale and lineage-mismatched; allowed/unexpected variants |
| Mapping | Long-only-from-Long, Short-only-from-Short, immutable collections, no truncation, per-variant order, deterministic IDs; arbitrary profile counts and Orion 12/4 |
| Bijection | missing, duplicate, extra, reordered and cross-variant source scenes |
| Content | every required field; exactly one primary knowledge ref; duration ordering; unsupported-claim and narration leakage |
| Identity | aggregate/variant/certification/editorial/profile/runtime/source-scene/frame/artifact semantic checksum mutations |
| Paths/manifest | absolute, traversal, case/ADS, staging/backup, duplicate path, invalid role, wrong physical hash/size |
| Transaction | fail/cancel at every stage/write/move/readback/cleanup; new install and replacement; rollback failure and restart recovery |
| Resume | valid byte-stable reuse; incomplete/corrupt/stale/runtime-incompatible regeneration; no validation rewrite |
| Routing | one integration/build/commit/result per request; legacy publisher zero calls; correct last completed/failed state |
| Regression | all Phase 1–5 tests unchanged; planner tests unchanged; no Phase 7 invocation |

Current command result: `dotnet test Backend/tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-restore --filter "FullyQualifiedName~Phase6|FullyQualifiedName~StoryFrame" --logger "console;verbosity=minimal"` could not start (`dotnet: command not found`), so exact executable totals remain N/A/N/A/N/A/N/A. Run each named class filter and the combined filter under the repository SDK before P6.2 changes and record total/passed/failed/skipped separately.

## 24. RC2 acceptance criteria

One request ending at phase 6 invokes one owner once; committed Phase 4/5 gates pass; Orion committed variants yield exactly 12 Long and 4 Short independent frames; every certified scene maps exactly once; all required content and lineage exist; no narration leakage or unsupported fact occurs; checksums are deterministic and distinct from physical integrity hashes; three canonical artifacts, validation and manifest form one recoverable transaction; committed readback passes; API/last-state matches it; legacy code cannot overwrite it; Phase 1–5 bytes/state remain undisturbed; Phase 7 and media services are not invoked.

## 25. Risks and unresolved decisions

* The old RC2 output contract and active Phase 7 code still require `creative/*`; this is a migration dependency, not an unresolved canonical-authority decision.
* Decide during P6.3 whether required text snapshots live directly on frames or in an immutable referenced subrecord; semantics are fixed either way.
* Decide the exact primary-knowledge representation (`IsPrimary` record versus `PrimaryKnowledgeReferenceId`) while preserving exactly-one validation.
* Determine whether compatibility projections are generated transactionally by Phase 6 or remain frozen legacy inputs until Phase 7 migration. They must never share authority roles.
* The lack of a .NET SDK blocks empirical baseline totals. It does not block the contract plan, but implementation must not begin without collecting them in a capable environment.
* Current Phase 5 hardening audit itself records an incomplete fault matrix; P6.2 must rely on the implemented committed evaluator contract and tests, not infer safety from file existence.

None of these changes the required canonical paths or ordered work. The contract is sufficiently resolved to implement behind tests.

## 26. Final readiness verdict

PHASE6_AUDIT_COMPLETE_READY_FOR_IMPLEMENTATION
