# O2.ORCH.4 Task 1 — Documentary Blueprint repository audit and deterministic Long/Short planning design

**Audit date:** 2026-07-31  
**Scope:** repository truth and implementation-ready Phase 4 correction design only. No Phase 1–3 production code, narration, visual generation, endpoint, Phase 5 behavior, or broad Phase 4 publication behavior was changed.  
**Governing baseline:** *Drashyam RC2 Pipeline Development Guide v1.1 — Final Implementation Baseline*, *Drashyam RC2 Orchestration Code Integration Guide v1.0 — Frozen Execution Baseline*, and *RC2 Pipeline Implementation Guide / RPIG v1.1 — Frozen Baseline*.

## 1. Governing contract summary

Phase 4 is **Documentary Blueprint**, not Story Intelligence. It consumes only the certified Phase 2 intelligence/knowledge authorities and Phase 3 question/objective authorities:

* `02-intelligence/production-event-intelligence.json`
* `02-intelligence/certified-knowledge-context.json`
* `03-questions/viewer-question-bank.json`
* `03-questions/learning-objectives.json`

Its sole canonical authority is `04-blueprint/documentary-blueprint.json`. It must express independently planned Long and Short structures. `knowledge-selection.json`, the two scene indexes, `blueprint-build-report.json`, and `compatibility/story-graph.json` support or project that authority. Existing `.long.json` and `.short.json` files may remain as deterministic variant projections, but must not become competing authorities.

Every scene needs exactly one primary viewer question, a valid objective and editorial outcome, deterministic identity/order, resolvable certified-knowledge references, transition intent, role/stage, and a duration target. Phase 3 questions guide and constrain the plan; they are neither a narration input nor a one-question/one-scene cardinality contract. The selected documentary profile, not a generic model or Orion branch, owns count, stage/role, coverage, and duration constraints. Phase 4 generates no prose narration and no media.

The Development Guide is unambiguous where the older Integration Guide is transitional: the former names `documentary-blueprint.json` as the authoritative artifact and requires the six supporting outputs; the latter calls the canonical file a “manifest” while also asking for variant files. Repository alignment therefore requires an **aggregate authority**, described in section 5, rather than a reference-only manifest or the current Master-shaped duplicate.

## 2. Current execution call chain

The exact active call chain is:

1. `ProductionPipelineExecutionService.RunAsync(ProductionPipelineRequest, CancellationToken)` in `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` builds `ProductionPhaseContext`, may run generic overwrite cleanup, enumerates `PhaseDefinitions()`, and dispatches phase 4 through `ExecutePhaseAsync(...)`.
2. `PhaseDefinitions()` labels phase 4 **“Story Intelligence”** and binds it to `PhaseChronicleStoryIntelligenceAsync` (the label is stale).
3. `PhaseChronicleStoryIntelligenceAsync(ProductionPhaseContext, CancellationToken)` reads `03-questions/viewer-question-bank.json`, `learning-objectives.json`, and the additional `question-plan.json`; validates them with `ViewerCuriosityArtifactValidator.Validate`; and verifies their shared-manifest entries with `Phase3ManifestIsValid`.
4. That method uses `context.ProductionEventIntelligence` (hydrated from Phase 2 by `RunAsync`) rather than reading both required Phase 2 files at the Phase 4 boundary. It does **not** read `certified-knowledge-context.json`.
5. It calls `IDocumentaryBlueprintIntegrationService.BuildAsync(...)`. DI in `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs` resolves this to `DocumentaryBlueprintIntegrationService`, with the registered singleton `DocumentaryBlueprintBuilder`.
6. `DocumentaryBlueprintIntegrationService.BuildAsync` filters questions, maps accepted questions with `MapScene`, constructs one Long `DocumentaryBlueprintBuildRequest`, and calls `DocumentaryBlueprintBuilder.Build` once.
7. It calls `Project` for Long, calls `SelectShortArc` over the built Long scenes, then calls `Project` again for Short. The certified builder is **not** invoked with an independently authored Short request.
8. It returns `DocumentaryBlueprintIntegrationResult` containing `Master`, `Long`, `Short`, and `BlueprintBuildDiagnostics`.
9. `PhaseChronicleStoryIntelligenceAsync` validates all three in memory using `DocumentaryBlueprintArtifactValidator.Validate`, writes four files to `.04-blueprint-staging-{guid}`, re-reads and validates the three blueprint files, directory-swaps staging into `04-blueprint`, and deletes the backup.
10. `ExecutePhaseAsync` subsequently calls `WritePhaseValidationAsync`, producing `validation/phase-04-validation.json`; `RunAsync` then calls `WritePhaseManifestAsync`, which reconstructs the shared `phase-manifest.json` and includes four Phase 4 entries.

Thus the physical outputs today are written in three separately owned operations: the `04-blueprint` directory swap, generic phase-validation write, and full shared-manifest rewrite. They are not one atomic Phase 4 generation.

## 3. Current files and components

| Concern | Repository file | Public/active entry point | Repository truth |
|---|---|---|---|
| Pipeline dispatch and persistence | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` | `RunAsync`; `PhaseChronicleStoryIntelligenceAsync` | Active Phase 4 owner; writes only three blueprint artifacts plus diagnostics. |
| Integration contract | `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryBlueprintIntegrationContracts.cs` | `IDocumentaryBlueprintIntegrationService.BuildAsync` | Request has profile/language/variants but no resolved profile constraints or certified knowledge context. Result assumes Master/Long/Short. |
| Integration implementation | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/DocumentaryBlueprintIntegrationService.cs` | `BuildAsync` | Performs editorial planning that should be extracted into a deterministic planner; derives Short from Long. |
| Pure certified mapper | `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryBlueprintBuilder.cs` | `DocumentaryBlueprintBuilder.Build` | Correctly copies caller-approved values without selection/sorting/rewriting. Reuse unchanged. |
| Build request | same file | `DocumentaryBlueprintBuildRequest` | Complete single-variant mapper input; useful as the planner’s variant output. |
| Domain | `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryBlueprintContracts.cs` | `DocumentaryBlueprint`, `DocumentarySceneBlueprint` | Strong immutable per-variant aggregate. It has no Long+Short canonical envelope or explicit source question ID. |
| Artifact validation | `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryBlueprintArtifactValidator.cs` | `Validate` | Checks basic identity/checksum/order/reference membership/high-priority coverage, but no profile count/stages/roles/duration/independence/master reconciliation or exactly-one-primary-question rule. |
| Editorial validation | `Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryBlueprintEditorialValidation.cs` | `DocumentaryBlueprintEditorialValidator.Validate` | Certified reusable editorial rule set; currently Phase 5 integration uses it indirectly, not Phase 4 publication. |
| Checksums | `DocumentaryBlueprintChecksum.cs`, `DocumentaryBlueprintCertificationChecksum.cs` | `Calculate`, `HasValidChecksum`, `SourcePhase4` | Logical artifact checksums exist; physical manifest SHA-256 is separate. |
| Phase 5 integration | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/DocumentaryBlueprintCertificationIntegrationService.cs` | `BuildAsync` | Current production consumer of all three Phase 4 files. |
| StoryGraph projector | **No Phase 4 projector exists** | none | Current Phase 4 neither writes `compatibility/story-graph.json` nor uses one. Legacy `SceneIntentBuilder`/RC2 StoryGraph code exists elsewhere but is not a documentary-blueprint compatibility projector. |
| Shared manifest | `ProductionPipelineExecutionService.WritePhaseManifestAsync` | private static method | Rewrites the entire manifest; Phase 4 roles are Authority, two VariantAuthority entries, Supporting diagnostics; hashes are omitted for Phase 4 entries. |
| Phase validation | `ProductionPipelineExecutionService.WritePhaseValidationAsync` | private method | Generic post-action writer, outside the directory swap; no Phase 4-specific lineage/publication proof. |
| Cleanup/invalidation catalog | `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase1Authority.cs` | `PhaseOutputTargetResolver.Resolve` | For start 4 it selects `04-blueprint`, phases 5–20 roots that are catalogued, and validation files 4–20. It does not select phases 1–3. |
| Generic cleanup | `ProductionPipelineExecutionService.ClearPhaseRangeOutputsForOverwrite` | private method | Runs before Phase 4 build when overwrite is true; deletion precedes successful replacement, so rollback cannot restore the complete prior Phase 4/downstream state. |
| DI | `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | service registrations | Builder and integration service are already registered; Story Frame certified adapter/integration is also registered. |
| Tests | `Backend/tests/Astronomy.MediaFactory.Tests/DocumentaryBlueprint/*`, `Phase5BlueprintCertificationIntegrationTests.cs`, Story Frame integration tests | xUnit tests | Strong certified component unit coverage; no high-fidelity current Phase 4 planner/publication/freeze transaction suite. |

## 4. Existing certified CG-A2 component inventory

All entries below use namespace `Astronomy.MediaFactory.Core.DocumentaryBlueprint` unless explicitly noted. “Production” means reachable from the current `ProductionPipelineExecutionService`, not merely registered or unit-tested.

| Component/file | Public entry; inputs → outputs | Current production usage | Target/reuse status | Gap |
|---|---|---|---|---|
| `DocumentaryBlueprintBuilder.cs` | `Build(DocumentaryBlueprintBuildRequest)` → `DocumentaryBlueprint` | Phase 4, once for Long/Master source | Phase 4; **reuse unchanged** | Needs two independent planner requests upstream. |
| `DocumentaryBlueprintArtifactValidator.cs` | static `Validate(artifact, context)` → artifact result | Phase 4 generation/staging/resume and Phase 5 preflight | Phase 4 physical validation; **extend/reuse** | Missing profile, independence, aggregate and supporting-set rules. |
| `DocumentaryBlueprintEditorialValidation.cs` | `DocumentaryBlueprintEditorialValidator.Validate(blueprint)` → findings | Phase 5 integration | Phase 5; **reuse** | Pause Test is not a distinct implementation and required stage/profile coverage is incomplete. |
| `DocumentaryBlueprintCertificationArtifactValidator.cs` | static `Validate(result, request)` → errors | Phase 5 generation/staging/resume | Phase 5; **reuse** | Directory name currently `05-blueprint-certification`, differing from guide’s `05-editorial`; out of this task. |
| `DocumentaryBlueprintCertificationIntegrationService.cs` (Infrastructure.Persistence) | `IDocumentaryBlueprintCertificationIntegrationService.BuildAsync(request)` → certification/editorial/diagnostics | Active Phase 5 | Phase 5; **reuse/adapt consumer** | Assumes current Master/Long/Short schemas. |
| Pause Test | no class, rule code, report model, or production entry point found | None | Phase 5; **gap—compose using existing editorial validation rather than a second blueprint engine** | Must define evaluator/report and integrate with certification. |
| `DocumentaryCertificationValidator.cs` | static validator methods over certification objects | Not in Phases 4–7; later certified package fixtures | Later certification; **reuse when lifecycle reaches it** | It is final documentary/package certification, not a substitute for Phase 5 blueprint certification. |
| `DocumentaryCertificationEvaluator.cs` | `Evaluate(DocumentaryCertificationRequest)` → evaluation/record | Not current production phases 4–7 | Final CG-A2 certification; **reuse** | Needs accepted lifecycle/package/provenance inputs. |
| `StoryFrameAuthorityContracts.cs` | `IStoryFrameIntegrationService.BuildAsync`; validators/projectors | Active Phase 6 | Phase 6; **reuse** | Must reconcile new aggregate/variant lineage. |
| `CertifiedStoryFrameBuilderAdapter.cs` (Infrastructure.Orchestration.RC2) | `ICertifiedStoryFrameBuilder.BuildAsync(request)` → frames | Active through Story Frame integration | Phase 6; **reuse** | Adapter ultimately uses `CreativeStoryboardBuilder`; preserve one-frame-per-certified-scene validation. |
| `StoryFrameIntegrationService.cs` (Infrastructure.Orchestration.RC2) | `BuildAsync(StoryFrameIntegrationRequest)` → authority/index/diagnostics | Active Phase 6 | Phase 6; **reuse** | Current Phase 6 request reads current Phase 5/current variant contract. |
| `StoryFrameAuthorityPersistence.cs` (Infrastructure.Orchestration.RC2) | committer/recovery/lock abstractions | Active Phase 6 atomic publication | Phase 6; **reuse pattern**, not code-copy | Useful transaction precedent for Phase 4. |
| `DocumentaryNarrativeComposition.cs` | `DocumentaryNarrativeComposer.Compose(request)` → composition | Unit-tested; not current V5 production path | Phase 7 planning; **reuse, do not reimplement** | Must bind independently certified variant scenes. |
| `DocumentaryNarrativeDraft.cs` | `DocumentaryNarrativeDraft` and draft models | Unit-tested; V5 does not publish this CG-A2 lifecycle | Phase 7; **reuse** | Needs adapter from actual V5 output. |
| `DocumentaryNarrativeDraftValidator.cs` + `DocumentaryNarrativeDraftValidation.cs` | `Validate(draft)` → quality findings/result | Unit-tested, not current Phase 7 production | Phase 7; **reuse** | Must be inserted after generation, before acceptance. |
| Revision files (`DocumentaryNarrativeRevisionRequestBuilder.cs`, `...Binder.cs`, `...WorkPackageBuilder.cs`, `...SubmissionAssembler.cs`, `...CyclePlanner.cs`, `...CycleCompleter.cs`, convergence starter/advancer/validator/summarizer) | deterministic request/work/cycle/convergence transformations | Unit-tested, not current production | Phase 7; **reuse all** | External prose editor/adapter remains required; components deliberately do not generate revised prose. |
| `DocumentaryNarrativeAcceptanceEvaluator.cs` and `...AcceptanceCoordinator.cs` | `Evaluate(request)` / `Accept(request, metadata)` → decision/release candidate | Unit-tested, not current Phase 7 authority | Phase 7; **reuse** | Must wrap V5 lifecycle and persist accepted candidate. |
| `DocumentaryNarrativeReleaseCandidateBuilder.cs` / summarizer | build/summarize release candidate | Called by acceptance coordinator in tests/domain | Phase 7; **reuse** | Needs production persistence/lineage. |
| Narration certification | `NarrationGeneratorV5` plus `Phase7Certifier`/semantic source-policy certifiers in Infrastructure | generation and semantic certification active in Phase 7 | Phase 7; **reuse/adapt** | Not the CG-A2 acceptance lifecycle; both gates are needed. |
| Export specification/materialization files (`DocumentaryExportSpecificationBuilder.cs`, `DocumentaryExportMaterializer.cs`, validators/contracts) | build/materialize deterministic export contracts | Not phases 4–7 production | Later export; **reuse** | Require accepted candidate and media providers. |
| Media projection/pipeline files (`DocumentaryMediaProjector.cs`, `DocumentaryMediaPipelinePlanner.cs`, `DocumentaryMediaPipelineOrchestrator.cs`, validators/contracts) | accepted package → media project/requests/execution | Not phases 4–7 production | Later media; **reuse** | No Phase 4 change should embed these concerns. |

The inventory establishes that a planner and Phase 4 transaction are missing, but the blueprint mapper, validators, editorial/certification foundation, Story Frame authority, narrative lifecycle, and export/media contracts already exist. None should be duplicated.

## 5. Current-versus-required artifact matrix and authority reconciliation

| Artifact | Current writer | Current role | Required role | Current schema | Required action |
|---|---|---|---|---|---|
| `documentary-blueprint.json` | `PhaseChronicleStoryIntelligenceAsync` | “Master” artifact; its blueprint is the same Long build before Long projection | Sole canonical authority | `DocumentaryBlueprintArtifact` with one `DocumentaryBlueprint` and coverage | Replace with a canonical aggregate envelope containing both complete Long and Short variant blueprints (or their embedded variant records), common lineage, per-variant checksums/coverage, and aggregate checksum. Never treat “Master” as a third editorial variant. |
| `documentary-blueprint.long.json` | same | Variant authority | Optional deterministic projection/cache | Same artifact with variant `Long` | Retain for existing consumers; manifest role `VariantProjection`; verify exact equivalence to canonical aggregate Long. |
| `documentary-blueprint.short.json` | same | Variant authority derived from Long | Optional deterministic projection/cache | Same artifact with variant `Short` | Retain but build independently; verify exact equivalence to aggregate Short. |
| `knowledge-selection.json` | none | absent | Supporting certified-knowledge allocation/deferral contract | none | Add deterministic selection records and source pointers/checksums; no copied invented facts. |
| `long-scene-index.json` | none | absent | Downstream index/projection | none | Add ordered Long scene IDs, stage/role, primary question ID, duration, checksum. |
| `short-scene-index.json` | none | absent | Downstream index/projection | none | Add independently ordered Short index with distinct variant identity. |
| `blueprint-build-report.json` | none | absent | Deterministic build/coverage/reconciliation report | none | Add profile resolution, allocation outcomes, coverage/defer reasons, counts, lineage, validation summary; exclude wall-clock timing from deterministic checksum. |
| `blueprint-build-diagnostics.json` | integration service | supporting diagnostics | Optional non-authoritative diagnostics | `BlueprintBuildDiagnostics` | Retain only if useful; correct source paths and counts; never substitute for report. Build duration/timestamps make it non-deterministic and non-authoritative. |
| `compatibility/story-graph.json` | none | absent | One-way legacy projection | none | Add an explicit `StoryGraphCompatibilityProjector` from committed canonical aggregate only; mark compatibility; validate equivalence; never read it to build Phase 4. |
| `validation/phase-04-validation.json` | generic `WritePhaseValidationAsync` | post-action generic status | Committed-generation proof and API reconciliation | generic anonymous JSON | Stage/commit with generation; include reason code, aggregate/physical hashes, upstream lineage, transaction and rollback state, supporting-set validation. |
| Phase 4 manifest entries | `WritePhaseManifestAsync` | four entries; no Phase 4 physical hashes | One authoritative aggregate entry plus supporting/projection/compatibility roles, each with physical SHA-256 and lineage | anonymous shared-manifest arrays | Phase-scoped atomic merge preserving byte-equivalent Phase 1–3 entries; include all required files and validation. |

### Canonical interpretation

`documentary-blueprint.json` should be the **canonical aggregate containing Long and Short**, not a path-only manifest and not the current Master blueprint. This is the only interpretation that simultaneously honors (a) the Development Guide’s “authoritative artifact” statement, (b) independent complete structures, (c) existing strong per-variant domain aggregates, and (d) downstream use without introducing a second authority. Variant files are byte-stable projections/caches reconciled against embedded canonical variants. A reference-only manifest would make the referenced files co-authoritative; calling the current Long-derived Master authoritative invents a third structure and duplicates Long.

## 6. Confirmed design defects

1. **Editorial-attention exclusion:** `BuildAsync` creates `attention` from `QuestionPlan.QuestionsRequiringEditorialAttention` and sets `accepted` to all bank questions not in that set. Editorial-only questions cannot shape scene intent or risk.
2. **One accepted question → one scene:** `accepted.Select((q,index) => MapScene(...))` establishes exact current cardinality. There is no profile count allocation or consolidation/expansion.
3. **High editorial-only contradiction:** every High question in `attention` causes an exception before planning. The required policy instead permits safe editorial shaping while prohibiting unsupported factual promotion.
4. **Long first:** the only builder request uses `LongDocumentary`; Master and Long originate from it.
5. **Short projection:** `SelectShortArc(built.Scenes)` groups Long scenes into `observe`, `explain`, `hook`, or `close`, selects `First()` per group, and `Take(4)`. It preserves selected Long scene IDs, values, relative order, duration, questions, stages, roles, and transitions (only scene numbers are rewritten). This is group-and-select projection, not independent composition. “Not first four” is immaterial.
6. **Orion result:** with six total questions and four in editorial attention, `accepted.Length == 2`; current Long/Master therefore contain **2 scenes**. For the certified Orion grounded pair represented by the repository’s Orion blueprint fixtures (Recognition and Scientific Explanation), the groups differ, so Short contains **2 scenes**, not 4. More generally the algorithm can collapse two accepted questions in the same group to one Short scene—another reason count-only evidence cannot validate it. It can never reach 12/4 from this authority.
7. **No profile constraint resolution:** `Profile` is an opaque string used in IDs/metadata only. No expected counts, required stages/roles, coverage, or duration budget are resolved.
8. **Missing required knowledge authority:** Phase 4 receives normalized `ProductionEventIntelligence` and question-level reference IDs, but never reads `certified-knowledge-context.json`; its diagnostics incorrectly claim `plan-input/production-event-intelligence.json`.
9. **Missing outputs:** five required supporting/projection artifacts are absent; only diagnostics exist.
10. **Ambiguous authority:** Master duplicates the built Long structure, while Long and Short are called variant authorities. This creates three authority-like objects.
11. **Weak validation:** a scene’s `ViewerQuestion.Text` is not linked to exactly one Phase 3 question ID; coverage map may be absent; zero/invalid scene numbers can pass constructor rules; profile and independent structures are not certified.
12. **Non-atomic publication:** directory, manifest, and validation do not commit/rollback together. Generic overwrite destroys current outputs before replacement.
13. **Stale naming:** production still calls Phase 4 “Story Intelligence,” obscuring the frozen architecture.

Phase 3 question count must support but not dictate scene count because questions express viewer curiosity, while scenes express profile-specific rhetorical pacing. A resolved recognition question can legitimately support separate orientation, pattern recognition, object identification, and practical finding scenes, each referencing only certified knowledge. Multiple questions can be consolidated under one science stage, and editorial-only questions can constrain clarification/risk without yielding a factual claim. Consequently 2 grounded questions can safely support 12 Long scenes when certified knowledge, objectives, roles, and outcomes justify the decomposition; it does not require 12 resolved Q&A records.

## 7. Profile-resolution findings

### Current resolution table

| Value | Current source/resolution | Finding |
|---|---|---|
| Documentary profile | `context.Request.PlannedFormat ?? context.Request.Category` | Opaque string; often format/category rather than an editorial profile. |
| Expected Long count | nowhere | Missing. |
| Expected Short count | nowhere | Missing. |
| Required narrative stages | `MapScene` category/role switch per question | Inferred opportunistically, not profile policy. |
| Required scene roles | same switch | Missing profile inventory/allocation. |
| Duration budget | hardcoded `25` seconds for PracticalObservation, otherwise `20` | Per-scene magic values; no per-variant budget. |
| Variant request | `ResolveViewerVariants(RequestedOutputs)` | Correctly normalizes Long/Short, but Phase 4 then materializes both regardless. |
| Language | request/execution context; Phase 3 validator checks it | Single string carried through; no explicit Phase 2/3 cross-artifact single-language precondition. |
| Format | `BlueprintPublicationFormat.LongDocumentary` then projection to Short | Short is not resolved before planning. |
| Question coverage policy | cover all non-attention; High attention fails | Hardcoded policy, unsuitable. |
| Knowledge coverage policy | copy each accepted question’s references | No certified-context selection/distribution policy. |

No generic contract hardcodes `Long = 12` or `Short = 4`; however Orion values appear in test fixtures/docs and downstream assumptions, while Phase 4 has no usable count resolver at all. The integration service hardcodes the unrelated `Take(4)` ceiling and 20/25-second durations.

Existing configuration/profile systems that should be evaluated and extended—not duplicated—are: `ContentGenerationPlan`/`ProductionPipelineRequest` (`PlannedFormat`, category, requested outputs, language); the seeded constellation plan; `AstronomyFamilyProfileCatalogV1`, `AstronomyFamilyProfileV1`, `FamilyNarrativeStructureV1`, and `FamilyNarrativeBeatV1` under Infrastructure narration semantics; `AstronomyFamilyProfileResolver`; Core `FamilyCertificationProfileRegistry`/family profiles; and execution-contract family resolution types. The implementation must select one existing canonical family/profile catalog as owner and add a Phase 4 projection interface over it. Because narration-semantic profiles are later-phase Infrastructure concerns, the preferred boundary is a Core `IDocumentaryBlueprintProfileResolver` adapter backed by the existing family/execution profile catalog—not a new independent catalog. Orion Gold’s 12/4 policy belongs in the selected Orion profile data/registration only.

## 8. Long/Short independence findings and policy

Current Short is conclusively non-independent: its input is `built.Scenes`; selection is group/first/take; its scene objects retain Long IDs and all planning fields; and no Short request reaches the builder.

The correction must plan variants in separate functions from the same immutable upstream snapshot and resolved profile:

* distinct blueprint IDs and variant-qualified scene IDs;
* separately allocated order, stages, roles, duration, questions, transitions, objectives/outcomes, and coverage;
* no planner API may accept the other variant plan or blueprint;
* shared question/knowledge IDs are allowed, but equality of a shared fact reference is not structural dependence;
* requested-variant semantics must be explicit: the canonical profile can require both, while delivery request controls later materialization; it must not silently change profile counts.

**Explicit independence test:** construct a valid input/profile, plan both variants, then produce a second input differing only in a Long-only allocation constraint (for example Long stage template/order/count) while holding all common upstream authorities and Short profile fixed. Assert the serialized Short build request/checksum is byte-identical and the Long request changes. Repeat symmetrically with a Short-only constraint. Also assert (1) no Short scene ID occurs in Long, (2) Short scene order is not a subsequence projection of Long allocation identities, (3) the planner call graph gives neither plan as input to the other, and (4) mutating/reordering a returned Long plan cannot change a subsequent Short build. A mere assertion that Short differs from `Take(4)` is insufficient.

## 9. Question-allocation policy

### Deterministic rules

1. Normalize questions by ID/order from the certified bank; never normalize away identity.
2. Classify as `ResolvedGrounded` when every selected factual reference resolves in certified knowledge, or `EditorialOnly` when Phase 3 flags editorial attention/unresolved support. Mixed questions may carry grounded and constrained aspects separately.
3. Cover High questions in every profile-required variant unless the selected profile explicitly permits a documented variant deferral. “Coverage” for an editorial-only High question means an editorial constraint/intent is represented, **not** that an unsupported answer is asserted.
4. Allocate profile stage/role slots first; allocate questions to slots second. Every scene gets exactly one `primaryViewerQuestionId` and its text snapshot. One question may be primary for multiple scenes when each scene has a distinct objective/outcome/role and only certified knowledge references. This is the core 2-grounded-to-12 mechanism.
5. Consolidate related questions deterministically by category, priority, normalized intent, and stable ID. One becomes primary; others become `supportingViewerQuestionIds` at the stage/coverage level, not additional primary questions for that scene.
6. Several questions may support one documentary stage across multiple scenes. Stage coverage records the relationship independently from scene primary ownership.
7. Variant deferral requires a stable reason code, profile permission, and report entry. Long coverage never implies Short coverage.
8. Editorial-only questions may set `intent`, `missingInformationConstraint`, `editorialRisk`, `mustNotClaim`, or clarification objectives. They contribute **zero certified fact references** unless a grounded sub-reference independently resolves.
9. Unsupported source answer text is never copied into a scene objective/outcome as fact and never becomes a knowledge selection. The planner works from question text, certified references, and explicit constraints—not compatibility `question-answer-set.json` answers.

### Representation

* `knowledge-selection.json`: per variant/scene list `selectedKnowledgeEntryIds`, purpose, source artifact/pointer/checksum, primary flag; plus `questionEvidenceStatus`, `unsupportedReferenceIds`, `mustNotClaim`, and selection/deferral reason codes. It contains references or certified snapshots defined by the existing knowledge contract, never planner-invented facts.
* Scene record: add `primaryViewerQuestionId`, optional `supportingViewerQuestionIds`, `questionEvidenceStatus`, `objectiveId`, explicit editorial constraints, and keep exactly one `ViewerQuestion` value. Existing `KnowledgeReferences` must all resolve.
* `blueprint-build-report.json`: per-variant covered/deferred/editorial-only question IDs, allocation counts, duplicate-use/consolidation mappings, High coverage, objective/knowledge coverage, unsupported-promotion count (must be zero), scene/profile reconciliation, and stable reason codes.

## 10. Deterministic planner design

The thin planner sits between validated upstream/profile resolution and the existing builder.

```csharp
public interface IDocumentaryBlueprintPlanner
{
    DocumentaryBlueprintPlanningResult Plan(
        DocumentaryBlueprintPlanningRequest request);
}

public sealed record DocumentaryBlueprintPlanningRequest(
    string ExecutionId,
    string EventId,
    string Language,
    DocumentaryBlueprintProfile Profile,
    ProductionEventIntelligenceAuthority ProductionIntelligence,
    CertifiedKnowledgeContext CertifiedKnowledge,
    ViewerQuestionBank QuestionBank,
    ViewerLearningObjectives LearningObjectives,
    ViewerQuestionPlan QuestionPlan,
    IReadOnlySet<DocumentaryBlueprintVariant> RequestedVariants,
    DocumentaryBlueprintSourceLineage Lineage);

public sealed record DocumentaryBlueprintPlanningResult(
    DocumentaryBlueprintBuildRequest LongDocumentaryBlueprintBuildRequest,
    DocumentaryBlueprintBuildRequest ShortDocumentaryBlueprintBuildRequest,
    KnowledgeSelection KnowledgeSelection,
    BlueprintCoverageReport LongCoverage,
    BlueprintCoverageReport ShortCoverage,
    BlueprintPlanningReport BuildReport,
    DocumentaryBlueprintPlanningLineage Lineage);
```

If stronger type distinction is desired, `LongDocumentaryBlueprintBuildRequest` and `ShortDocumentaryBlueprintBuildRequest` should be thin wrappers around the existing `DocumentaryBlueprintBuildRequest`, enforcing only their publication format. They must not duplicate its fields or mapper.

### Algorithm

1. Validate and canonicalize immutable inputs; resolve a single profile before planning.
2. Expand the profile’s Long stage/role slot template and duration budget independently; do the same from its Short template.
3. Stable-sort source questions by priority, Phase 3 order, then ID; classify evidence safety.
4. Allocate primary/supporting questions to each variant’s slots under the policy in section 9.
5. Allocate objectives and certified knowledge by resolvable ID and profile knowledge policy; record all omissions/constraints.
6. Generate objective/outcome/transition intent from deterministic policy templates and source fields only—no generated prose or facts.
7. Generate IDs as versioned SHA-256-derived identifiers over normalized execution/event/profile-version/variant/slot/stage/role/primary-question identity. Short IDs include `short`; Long IDs include `long`.
8. Allocate integer durations with a deterministic largest-remainder rule so scene targets sum exactly to the variant budget and respect profile min/max.
9. Construct two existing build requests and reports; validate result invariants before returning.

The planner is synchronous and pure: no clock, random GUID, filesystem, network/OpenAI, StoryGraph input, narration, image, or mutation. The integration service remains a thin orchestrator calling `Plan`, then `builder.Build(long)` and `builder.Build(short)` exactly once each. `DocumentaryBlueprintBuilder` remains the certified pure mapper.

## 11. Phase 4 preconditions and publication design

### Fail-closed preconditions and reason codes

| Preconditions | Proposed stable reason code |
|---|---|
| Phase 1 validation exists and is `Succeeded`/valid | `P4_UPSTREAM_P1_VALIDATION_NOT_SUCCEEDED` |
| Phase 2 validation exists and is `Succeeded`/valid | `P4_UPSTREAM_P2_VALIDATION_NOT_SUCCEEDED` |
| Phase 3 validation exists and is `Succeeded`/valid | `P4_UPSTREAM_P3_VALIDATION_NOT_SUCCEEDED` |
| Every required authoritative/supporting input exists and is non-empty | `P4_UPSTREAM_AUTHORITY_MISSING` |
| Required input is readable and schema-valid | `P4_UPSTREAM_AUTHORITY_INVALID` |
| Shared-manifest path/role exists and physical SHA-256 matches | `P4_UPSTREAM_MANIFEST_CHECKSUM_MISMATCH` |
| Phase 2 plan/execution/event/language/source lineage matches Phase 1 | `P4_UPSTREAM_P2_LINEAGE_INVALID` |
| Phase 3 source Phase 2 checksum plus plan/event/language lineage matches | `P4_UPSTREAM_P3_LINEAGE_INVALID` |
| All authorities resolve to one language | `P4_MULTIPLE_OR_MISMATCHED_LANGUAGES` |
| requested variants normalize and the documentary profile resolves uniquely | `P4_VARIANT_PROFILE_UNRESOLVED` or `P4_VARIANT_PROFILE_AMBIGUOUS` |
| No compatibility path/schema supplied in an authority slot | `P4_COMPATIBILITY_AUTHORITY_SUBSTITUTION` |
| Certified knowledge references needed by selected facts resolve | `P4_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID` |

The preflight must name exact allowed paths and deserialize expected authority types. It must not “find a nearby” artifact or fall back to `plan-input`, `question-answer-set.json`, or StoryGraph.

### Complete transaction

Introduce a Phase 4 publication coordinator patterned after the mature Phase 1/Story Frame transactions, with a filesystem abstraction, execution lock, staging recovery, manifest merger, validator, and rollback record.

1. Acquire plan/execution Phase 4 lock and snapshot physical hashes/bytes for the current Phase 4 generation, Phase 4 manifest slice, and phase-04 validation; separately snapshot Phase 1–3 freeze evidence.
2. Validate all preconditions and upstream lineage/checksums without writes.
3. Resolve profile; create independent Long/Short plans; invoke the existing builder twice.
4. Validate both blueprints and canonical aggregate in memory, including profile, safety, coverage, independence and master/variant reconciliation.
5. Write the **complete** `04-blueprint` artifact set to an execution-local staging directory. Project StoryGraph only from the staged canonical aggregate.
6. Re-read every staged JSON file into its declared schema; validate the staged physical set and compute SHA-256.
7. Prepare a Phase 4-only manifest merge preserving all non-Phase-4 entries semantically and preserving Phase 1–3 entries byte-for-byte/canonically identical. Stage the full merged manifest.
8. Stage a provisional Phase 4 validation record tied to transaction ID and staged hashes.
9. Under a non-interruptible commit section, swap `04-blueprint`, atomically replace the shared manifest, atomically replace phase-04 validation, then re-read committed files and verify SHA-256/lineage/reconciliation.
10. Reconcile `ProductionPhaseResult`/API result strictly from the committed validation record.
11. Only after the new Phase 4 generation is certified committed, quarantine/invalidate phases 5–20. Commit deletion/quarantine after verification. Never include phase 4 itself in downstream invalidation.
12. On any failure after mutation, restore prior `04-blueprint`, prior Phase 4 manifest slice/full manifest, phase-04 validation, and any quarantined Phase 5–20 outputs; verify restoration. Emit failure diagnostics outside authoritative paths. If no prior generation existed, remove the partial generation coherently.

**Rollback owner:** the Phase 4 publication coordinator alone. **Generation unit:** `04-blueprint/**` + Phase 4 shared-manifest entries + `validation/phase-04-validation.json`. The shared manifest file is a physical shared resource, so rollback stores/restores its entire prior bytes while logical ownership remains only the Phase 4 slice. Phases 1–3 are read-only preconditions/freeze evidence and never members of either commit or rollback.

## 12. Upstream freeze-protection and invalidation design

`PhaseOutputTargetResolver.Resolve(context, 4, 20)` currently cannot return Phase 1–3 catalog entries because `Add` filters `phase < start`; its phase-4 path is the narrow `04-blueprint` directory, not the workspace parent. Validation targets begin at phase 4. Containment/deduplication cannot promote these to `OutputRoot`. This is a good boundary.

However, the target catalog is incomplete for many later roots, and generic cleanup happens before publication. The correction should use a Phase 4-owned resolver/catalog assertion and transactional quarantine, not rely solely on current generic deletion. `UpstreamPhaseMutationGuard.AssertAllowed` must be invoked for every planned mutation, and tests must assert no target equals the workspace root or contains `01-plan`, `02-intelligence`, `03-questions`, or validations 01–03.

Freeze test procedure:

1. Before a Phase 4-only overwrite, recursively enumerate regular files (stable relative-path ordinal order) beneath `01-plan/**`, `02-intelligence/**`, and `03-questions/**`; add validations 01–03.
2. Record each file’s bytes, length, and SHA-256. Parse `phase-manifest.json` and record the exact canonical JSON/serialized bytes for Phase 1, 2, and 3 artifact entries and their order/roles/checksums.
3. Execute only phase 4 with overwrite and read-only dependency expansion.
4. Re-enumerate and assert identical path set, bytes, lengths and SHA-256; assert Phase 1–3 manifest slices are identical.
5. Assert invalidated paths have owners/phases 5–20 only, all Phase 4 files remain, and validations 01–03 remain.
6. Repeat for success, planner failure before staging, commit fault at every replace boundary, cancellation, and rollback.

## 13. Phase 5–7 forward compatibility

### Phase 5

The canonical aggregate plus variant projections supplies profile identity/version, independent sequences, primary question/objective/outcome, stages/roles, knowledge references, transitions, and durations. Add explicit constraint/safety and coverage fields so blueprint validation, Pause Test, knowledge/question coverage, transition validation, and editorial certification do not infer policy from prose. Phase 5 should consume the aggregate and certified knowledge authority; `.long/.short` are verified projections only.

### Phase 6

Each variant scene needs stable `sceneId`, sequence, stage, role, primary question ID/text, objective, editorial outcome, transition intent, certified knowledge references, duration min/target/max, and safe visual-opportunity intent. This supports exactly one frame per certified scene, narration brief, visual direction and traceability without narration text. Story Frame lineage must carry canonical aggregate checksum, variant checksum, profile version, and source scene ID.

### Phase 7

Separate blueprint/scene identities and sequences let V5 generate Long/Short independently. Knowledge claims must originate in certified knowledge references; editorial outcome and duration flow through Story Frames. Accepted release-candidate lifecycle must record variant blueprint and Story Frame checksums. Phase 4 must not add narration text.

### Currently missing or insufficient downstream fields

* canonical aggregate schema/version and embedded variant identity/checksum;
* explicit `primaryViewerQuestionId` (text alone is not traceable);
* optional supporting/consolidated question IDs and per-variant deferral reasons;
* explicit learning `objectiveId` (current scene has only copied/generated strings);
* profile ID **and version**, profile slot/template identity, and expected count;
* variant duration budget and per-scene duration range (current model has target only);
* question evidence status and editorial-only `mustNotClaim`/missing-information constraints;
* certified knowledge source artifact checksum/pointer and selection reason;
* per-variant coverage report/checksum;
* transition type/from/to identity and closing-scene terminal semantics;
* canonical aggregate ↔ variant projection reconciliation hashes;
* source Phase 1/2/3 lineage and physical manifest hashes;
* safe visual-direction intent may reuse `VisualOpportunity`, but it needs validator-backed source traceability where scientifically required.

## 14. Modified-file plan for implementation

No implementation files are changed by this audit. A precise follow-on change should modify/add only the following Phase 4/downstream-consumer surfaces:

1. Extend the selected existing family/profile catalog and its adapter with Phase 4 Long/Short constraints; add no parallel profile registry.
2. Add planner contracts/models beside `DocumentaryBlueprintIntegrationContracts.cs` and a pure deterministic planner implementation in Infrastructure or Core according to established dependency boundaries.
3. Update `DocumentaryBlueprintIntegrationService.cs` to delegate all allocation to the planner and call the existing builder once per independent request; delete `SelectShortArc` and question-count planning.
4. Add a canonical aggregate/supporting artifact contract and extend `DocumentaryBlueprintArtifactValidator.cs`; leave `DocumentaryBlueprintBuilder.cs` as a pure mapper.
5. Add the one-way StoryGraph compatibility projector.
6. Add Phase 4 publication transaction/lock/filesystem/recovery/manifest-merge components, preferably separate from the already-large `ProductionPipelineExecutionService.cs`.
7. Update only the Phase 4 method/label, Phase 4 resume reader, Phase 5/6 readers, Phase 4 manifest catalog, and DI registrations. Do not edit Phase 1–3 producers or schemas.
8. Add focused planner, integration, physical artifact, transaction, freeze, invalidation, and high-fidelity Orion tests under the existing test project.
9. Update Phase 4 specification/audit documentation and any contract map that still calls it Story Intelligence.

## 15. Test plan

| Test | Required proof |
|---|---|
| Profile-driven Long count | Non-Orion synthetic profile and Orion Gold both produce profile count; generic contract contains no 12 literal. |
| Profile-driven Short count | Different synthetic counts plus Orion Gold 4; no `Take`, truncation, or Long input. |
| Independent planning | Bidirectional perturbation test in section 8, disjoint IDs, no subsequence projection. |
| Deterministic IDs/order | Equivalent reconstructed inputs and permuted non-semantic dictionary/enumeration order yield identical requests/JSON/checksums. |
| One primary question | Every scene has one known primary ID; zero/two is rejected. Reuse across scenes is accepted. |
| Objective/outcome | Every scene has known objective ID, nonblank valid objective and valid editorial outcome. |
| Knowledge resolution | Every selected reference resolves to certified context and exact source pointer/checksum; unknown/compatibility reference fails. |
| Consolidation | Related questions deterministically map primary/supporting IDs; report reconciles without multiplying scenes. |
| High coverage | Required High question is covered per variant or only deferred with profile-authorized reason; editorial-only High is constraint-covered safely. |
| Editorial-only safety | It influences intent/risk/`mustNotClaim` but contributes no unsupported facts. |
| No fact promotion | Inject unsupported answer text and assert it appears nowhere in blueprint/selection; promotion count zero. |
| Long/Short reconciliation | Coverage is independently computed; aggregate equals both variant projections; shared references are allowed. |
| Master/variant reconciliation | Any field/order/checksum drift in `.long`/`.short` fails physical validation. No third Master variant exists. |
| StoryGraph projection | Projection derives only from canonical aggregate, is deterministic/equivalent, and cannot be accepted as planner input. |
| Freeze preservation | Byte/SHA/path/manifest-slice procedure in section 12 across success/failure/cancellation. |
| Invalidation boundary | Resolver and mutation journal contain only phases 5–20 after Phase 4 commit; no parent target and no Phase 1–3/Phase 4 deletion. |
| Repeat run | Fixed authorities/profile produce identical authoritative/supporting/projection bytes and SHA-256 (diagnostic elapsed time excluded). |
| Rollback | Fault injection at each directory/manifest/validation/invalidation boundary restores the prior complete generation and downstream set. |
| Preconditions | Each reason code is exercised independently; errors occur before mutation; compatibility substitution is rejected. |

### High-fidelity Orion Gold acceptance test

Load the frozen certified Orion Phase 2/3-shaped fixtures with `questionCount=6`, `resolvedReferenceCount=2`, `unresolvedReferenceCount=4`, and `questionsRequiringEditorialAttentionCount=4`; resolve Orion Gold from the actual profile catalog; plan twice. Assert:

* Long has exactly **12** scenes and Short exactly **4**;
* all 16 scenes have one primary ID from the six-question bank;
* at least one grounded question is primary for multiple Long scenes, proving 12 does not require 12 resolved questions;
* four editorial-only questions contribute constraints/intent and no unsupported knowledge claim;
* both grounded references resolve to certified knowledge wherever selected;
* profile counts, required stages/roles and duration budgets reconcile;
* independent IDs/order/allocation/coverage and the perturbation test pass;
* serialized authoritative results are deterministic across repeat runs.

Run existing builder determinism/mapping/immutability/validation/editorial tests unchanged to prove the certified mapper was not reimplemented, then run Phase 5 and Story Frame contract tests only as compatibility checks—do not implement or execute a new Phase 5 publication in this task.

## 16. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Transitional guide calls canonical file a manifest | Adopt the newer authoritative aggregate interpretation; encode roles and reconciliation tests. |
| Existing Phase 5 expects three `DocumentaryBlueprintArtifact` objects | Keep `.long/.short` projections during migration; teach consumer to verify them against aggregate before later removal consideration. |
| Existing family profile systems overlap | Nominate one owner through an architecture test and adapter; prohibit a new count dictionary in integration code. |
| Two grounded questions might be stretched into repetitive scenes | Profile slots require distinct role/objective/outcome; editorial validator rejects duplicate question/title/purpose patterns; coverage report exposes reuse. |
| Editorial-only questions leak unsupported facts through question/expected-outcome strings | Explicit evidence classification, `mustNotClaim`, certified-reference-only selection, and adversarial leakage tests. |
| Determinism defeated by clocks/elapsed time/dictionary order | Supply publication time outside authority or fixed clock; canonical sorting/JSON; exclude runtime diagnostics from authoritative checksums. |
| Shared-manifest rewrite mutates upstream entries | Phase-scoped merger plus exact Phase 1–3 slice preservation test and whole-file rollback snapshot. |
| Commit succeeds but invalidation fails | Treat invalidation journal as transaction-owned and roll back both new Phase 4 generation and quarantined downstream state. |
| Generic overwrite deletes before successful build | Bypass it for Phase 4 and use coordinator quarantine/commit after validation. |
| Compatibility StoryGraph regains authority | One-way projector API accepts only canonical aggregate; preflight path allowlist and substitution reason code; manifest role `CompatibilityProjection`. |
| Missing Pause Test is mistaken for Phase 4 blocker | Phase 4 provides all necessary intent/transition/coverage fields; implement Pause Test in Phase 5 by composing certified validators, not by changing blueprint authority. |

## 17. Final verdict

Repository truth is sufficiently established to issue a precise implementation task: current behavior is 2 Long/2 Short for the certified Orion grounded pair, Short is structurally a Long projection, profile counts are unresolved, required artifacts/knowledge authority are missing, and the publication is not a coherent transaction. The proposed aggregate authority, existing-profile adapter, pure independent planner, question-safety policy, complete publication unit, freeze guard, and focused tests resolve the contract ambiguity without duplicating certified CG-A2 components or modifying frozen upstream phases.

READY_FOR_PHASE_4_IMPLEMENTATION
