# O2.ORCH.5 Task 1 — Phase 7 Narration Authority Audit

**Audit date:** 2026-07-29  
**Repository HEAD:** `4fe184ca7731cadc600da9e8d2e6268a34772268`  
**Decision:** **READY FOR O2.ORCH.5 IMPLEMENTATION PLANNING**  
**Scope:** discovery only. This is not Phase 7 certification and changes no production contract, artifact, phase, or implementation.

## A. Executive conclusion

The repository evidence is sufficient to plan hardening of the mature Narration Studio V5 implementation. `ProductionPipelineExecutionService` is the sole production caller of `NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync`; the RC2 overlay no longer calls it. Tests can instantiate the generator directly but are not production endpoints. Phase 7 is not certifiable today: it does not consume or bind itself to certified `06-story-frames`, has no complete-set identity/checksum contract, uses direct non-atomic writes, has no same-plan lock, and its only reuse predicate is successful validation plus existence of root `narration.json` under `RetryFailedOnly`.

The current authority **candidate**, derived from actual current consumers and unique content, is the existing set `narration-v5/narration.json`, `narration-v5/long/narration.json`, `narration-v5/short/narration.json`, `narration-v5/narration-diagnostics.json`, plus the two format documentary scripts and two scene-fact-card sets. This is a candidate for future wrapping/validation, not a finalized design. Root narration alone is insufficient because certification readers explicitly inspect format scripts, cards, context and format narrations.

## B. Current production call graph

### Current graph

```text
POST /api/content-planning/rc2/batch-generate-from-plans
 ContentPlanningRc2Controller.BatchGenerateFromPlans
  -> Rc2ContentPlanningBatchOrchestrator.ExecuteAsync
     (story intelligence/creative overlay only; no narration call)
  -> ContentPlanBatchGenerationService.GenerateAsync
  -> ContentPlanProductionExecutionService.ExecuteAsync
  -> ProductionPipelineExecutionService.RunAsync
     -> phase definition 7: PhaseGenerateNarrationPlanAsync
        -> ValidatePhase7ChronicleCoreInputs
        -> DI/runtime semantic registry and MeteorActivity policy checks
        -> RuntimeCompositionDiagnostics.WriteAsync
        -> NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync
     -> WritePhase7RequiredOutputValidationDiagnosticsAsync
     -> EvaluatePhase7NarrationQualityAuthority
     -> WritePhaseValidationAsync (validation/phase-07-validation.json)
     -> WritePhaseManifestAsync (root phase-manifest.json)
```

### Before/current distinction

Static characterization tests preserve the architectural boundary: the RC2 orchestrator calls `SceneIntentBuilder` and `CreativeStoryboardBuilder`, while `Phase7SingleAuthorityStaticTests` asserts it does **not** call `narrationGeneratorV5.BuildAndWriteDiagnosticsAsync`. The one production call is at `ProductionPipelineExecutionService.cs:1044`. Direct generator calls occur only in tests (`Rc2NarrationGeneratorV5PreflightTests`, `Phase7ProductionResolverInputParityTests`, `Phase7ProductionApiPathSemanticContextTests`). No controller/background/compatibility service calls the V5 writer.

## C. Execution ownership

| Question | Evidence-based answer |
|---|---|
| Only production execution owner? | **Yes.** Only `ProductionPipelineExecutionService` calls the V5 method in `Backend/src`; direct calls elsewhere are tests. |
| RC2 orchestrator invokes Phase 7? | **No.** It writes scene intents and creative storyboard only. |
| Competing production output path? | No V5 competitor found. Legacy `QuestionDrivenNarrationGenerator` writes `question-engine/question-driven-narration.json`; it is a distinct later/compatibility path and does not write `narration-v5`. |
| Phase validation writer? | Production service owns the normal `validation/phase-07-validation.json`. `NarrationGeneratorV5.WritePreflightFailure` also writes that same path synchronously on missing prerequisites, so ownership is currently split and schemas differ. |
| Manifest authority registrar? | Only `ProductionPipelineExecutionService.WritePhaseManifestAsync` writes `phase-manifest.json`; however it has explicit Phase 3–6 artifact roles and **no Phase 7 artifact-role collection**, merely phase result output paths. |

`WriteRc2PhaseValidationAsync`, `ApplyRc2Phase7ResponseAsync`, and a Phase-7 call from `ExecuteRc2OverlayPhaseAsync` have no active production definitions/calls. Thus there is one generation owner but two validation-file writers.

## D. Component inventory

All paths below are under `Backend/src/Astronomy.MediaFactory.Infrastructure/`.

| Component | File | Type | Responsibility / inputs → outputs | Called by; important calls | Validation / active | Recommendation and reason |
|---|---|---|---|---|---|---|
| `NarrationGeneratorV5` | `Orchestration/RC2/NarrationGeneratorV5.cs` | sealed class | Reads editorial/creative/plan/question JSON; builds all `narration-v5` artifacts | Production phase 7 and tests; calls normalizer, semantic resolver, prompt composer, realizer, validators/writers | Blocking preflight, semantic, purity, language and quality; **active** | **Reuse/extend**: mature single builder; harden persistence/identity rather than replace. |
| `NarrationGeneratorV5Result` | same | record | Returns plan/brief/narration models, diagnostics and generated paths | generator → pipeline | Passive; active | **Extend** only if compatible metadata is needed. |
| `NarrationInputNormalizer` | same | static class | Converts context/facts into safe localized `NarrationSafeContext` | generator | Blocking errors plus detailed diagnostics; active | **Reuse**; preserve structured handoff. |
| `NarrationContextBuilder` | same | static class | Builds format/beat context from contracts and resolved facts | generator/normalizer | Schema/purity checked; active | **Reuse**; add lineage rather than rebuild semantics. |
| `ContextSchemaValidator` | same | static class | Validates context shapes and required beat fields | generator | Blocking; active | **Reuse/extend** into complete-set validation. |
| `SpeakableContextPurityValidator` / `NarrationContextPurityValidator` | same | static helpers | Detect unsafe metadata/directive leakage | generator/tests | Blocking; active | **Reuse**, replace message coupling with codes where absent. |
| `GeneratedNarrationValidator` | same | static helper | Regex leakage scan over generated narration | generator | Blocking; active | **Extend** structured rules. |
| `SceneFactCardGenerator` / `RawNarrativeGenerator` | same | static generators | Derive per-format cards and raw narrative from contracts/frames | generator | Counts/identity later checked; active | **Reuse**; derived/supporting products. |
| `INarrationRealizer` / `NarrationRealizer` | same | interface/class | Deterministic realization of safe contexts into narration text | DI → generator | Returns issues; blocking; active | **Reuse**; identity/version must enter resume compatibility. No Azure/OpenAI call. |
| `NarrationPromptComposer` | `Orchestration/RC2/NarrationPromptComposer.cs` | class | Creates preview/request contract from context, briefs, cards and style | generator | Preflight and prompt-quality checks; active | **Reuse**; add explicit contract/version identity. |
| `PromptQualityEvaluator` | `Production/Narration/PromptComposer/PromptQualityEvaluator.cs` | class | Scores prompt structure/leakage | generator | Quality failures block final Phase 7 quality authority; active | **Reuse**. |
| `LanguageOutputValidator` | `Orchestration/RC2/LanguageOutputValidator.cs` | class | English/Hindi script, mixed-language and output validation | generator | Blocking errors, warnings; active | **Reuse/extend** proper-noun policy/version identity. |
| `DocumentaryStyleDirector` | `Production/Narration/Style/Directors/DocumentaryStyleDirector.cs` | class | Produces style contract per scene | generator | Style diagnostics contribute validation; active | **Reuse**; version style contract. |
| `IRequiredSemanticFactResolver` / `RequiredSemanticFactResolver` | generator file | interface/class | Resolves required/optional facts via V1 engine/registry/policies and compatibility mapper | generator | Missing required/conflicts may block; active | **Reuse**; capture resolver, adapter and policy identities/checksums. |
| `IAstronomyFamilyProfileResolver` / resolver | `Orchestration/RC2/NarrationGeneratorV5.cs`; `Production/Narration/Semantics/AstronomyFamilyProfileResolver.cs` | interface/class | Resolves canonical family profile with V1 compatibility | generator | Missing/invalid family blocks; active | **Reuse**; version profile and fallback. |
| `LanguageProfileResolver` | generator file | static helper | Resolves `en`/`hi`, falling back to English | generator | Fallback warning, unsupported input preflight; active | **Reuse**, make fallback compatibility explicit. |
| `AstronomyFamilyProfileCatalog` / V1 catalog | generator file; `Production/Narration/Semantics/Families/AstronomyFamilyProfileCatalogV1.cs` | static/class | Family beats, required facts, forbidden claims | family resolver | Profile validation blocking; active | **Reuse/extend data** for constellation gaps. |
| Semantic resolution engine | `Production/Narration/Semantics/Resolution/V1/**` | interface/classes | collect → evaluate family compatibility/conflicts → select candidates | required resolver | Structured outcomes/blocking; active | **Reuse**. |
| Semantic adapter registry | `Production/Narration/Semantics/Sources/Adapters/**` | registry/adapters | Supplies capability candidates from event/knowledge inputs | resolver; pipeline runtime check | Empty/MeteorActivity absence blocks; active | **Reuse**, identity and full-family readiness required. |
| Semantic source policy catalog | `Production/Narration/Semantics/Sources/Catalog/**` | catalog/policies | Defines permissible/preferred sources and capability rules | resolver/pipeline | Consistency validation; active | **Reuse**, checksum/version missing. |
| `RuntimeCompositionDiagnostics` | `Production/Narration/Diagnostics/RuntimeCompositionDiagnostics.cs` | static writer | Captures runtime CLR/assembly composition | production phase | Diagnostic, non-authoritative; active | **Reuse supporting**, never treat type names alone as compatibility. |
| Knowledge/editorial/producer-note builders | generator file | static helpers/models | Build unique structured knowledge and editorial guidance contracts | generator | Diagnostics; active | **Reuse supporting**; producer notes contain unique non-spoken guidance. |
| `DocumentaryScriptFormatter` / performance diagnostics helpers | generator file | helpers | Projects realized text into format scripts/full script and measures style/performance | generator | Quality checks; active | **Reuse derived**, validate projection equivalence. |

## E. Artifact inventory

All rows are relative to the plan output root. `NarrationGeneratorV5.BuildAndWriteDiagnosticsAsync` writes directly unless another writer is named. JSON uses `JsonOptions` and UTF-8 without BOM. **No artifact has a complete-set authority checksum or certified source checksum.** Contract versions exist on several domain records, not uniformly; runtime identity is primarily separate diagnostics.

| Relative path | Model / purpose | Required / manifest role | Consumers; regenerability | Validation / recommendation |
|---|---|---|---|---|
| `narration-v5/narration.json` | aggregate narration (`NarrationOutput`) | blocking list; phase output | certification/summary; derived from format realization | quality/count checks; **authority candidate** |
| `narration-v5/long/narration.json` | long `NarrationOutput` projection | blocking | semantic certification; unique selected long text but duplicated in aggregate | language/count; **variant authority candidate** |
| `narration-v5/short/narration.json` | short projection | blocking | semantic certification | language/count; **variant authority candidate** |
| `narration-v5/narration-plan.json` | `NarrationPlan` | blocking | certification evidence | reproducible planning projection; **supporting** |
| `narration-v5/narration-briefs.json` | briefs | blocking | prompt/certification | contains editorial handoff; **supporting** |
| `narration-v5/narration-diagnostics.json` | aggregate diagnostics | blocking | certification | not reproducible from final text alone; **diagnostic authority candidate** |
| `narration-v5/narration-context.json` | `NarrationContextDocument` | blocking | prompt/certification | semantic handoff, reproducible only with source resolver/config; **supporting lineage evidence** |
| `narration-v5/narration-input-normalization-diagnostics.json` | normalization diagnostics | blocking | pipeline validator | **diagnostic** |
| `narration-v5/narration-realization-diagnostics.json` | realization issues/runtime | blocking | pipeline validator | **diagnostic** |
| `narration-v5/narration-validation-diagnostics.json` | post-generation structured-ish validation | not required-output list, certification reads it | certification | recomputable partly; **diagnostic** |
| `narration-v5/scene-identity-diagnostics.json` | `SceneIdentityDiagnostics` | blocking | pipeline validator | maps Phase6-named legacy story-frame sources, not certified 06 authority; **lineage diagnostic** |
| `narration-v5/required-output-validation-diagnostics.json` | pipeline-owned presence/root-kind report | written after generation; absent from its own required list | phase validation evidence | recomputable; **diagnostic** |
| `narration-v5/prompt-preview.md` | rendered prompt | blocking | human/debug | reproducible from prompt inputs/version; **supporting** |
| `narration-v5/prompt-diagnostics.json` | prompt composition report | blocking | pipeline quality | **diagnostic** |
| `narration-v5/prompt-quality.json` | `PromptQualityReport` | blocking | pipeline/certification | **diagnostic** |
| `narration-v5/llm-request.json` | request envelope | blocking despite deterministic realizer | pipeline list; no provider consumes it | duplicate of prompt/context presentation; **supporting/debug** |
| `narration-v5/generator-preflight-diagnostics.json` | anonymous failure report | optional; can also write phase validation | failure evidence | **diagnostic; remove validation ownership later, retain path** |
| `narration-v5/event-identity-diagnostics.json` | canonical event identity resolution | optional | semantic certification evidence | **supporting identity diagnostic** |
| `narration-v5/family-profile-v1-compatibility-diagnostics.json` | profile bridge report | optional | semantic certification | **diagnostic** |
| `narration-v5/semantic-registry-validation-report.json` | registry coverage + timestamp | optional | semantic certification | timestamp makes hash unstable; **diagnostic** |
| `narration-v5/resolver-input-presence-diagnostics.json` | source presence | optional | failure preservation | **diagnostic** |
| `narration-v5/required-semantic-fact-diagnostics.json` | family/resolution result | optional | semantic certification | unique provenance/conflicts; **supporting diagnostic** |
| `narration-v5/semantic-capability-diagnostics.json` | resolution diagnostics | optional | semantic certification | **supporting diagnostic** |
| `narration-v5/semantic-source-context-presence.json` | anonymous presence snapshot | optional | failure evidence | **diagnostic** |
| `narration-v5/domain-knowledge-diagnostics.json` | domain knowledge coverage | optional | failure evidence | **diagnostic** |
| `narration-v5/meteor-shower-shadow-validation.json` | execution-validation shadow report | optional catalog evidence | certification | **diagnostic** |
| `narration-v5/meteor-activity-*-diagnostics.json` (context, adapter, resolution, projection, beat-assignment) | lifecycle snapshots | optional/family conditional | diagnostics only | regenerable; **diagnostic** |
| `narration-v5/runtime-composition-diagnostics.json` | CLR runtime composition | returned but not required list | certification | environment-specific; **diagnostic** |
| `narration-v5/knowledge/knowledge-format-contract.json` | knowledge format contract | generator output but not required list | prompt/build evidence | unique structured knowledge; **supporting** |
| `narration-v5/knowledge/knowledge-format-diagnostics.json` | knowledge diagnostics | optional | diagnostics | **diagnostic** |
| `narration-v5/editorial-brief/editorial-brief-contract.json` | editorial brief contract | optional | prompt | unique editorial intent; **supporting** |
| `narration-v5/editorial-brief/editorial-brief-diagnostics.json` | diagnostics | optional | diagnostics | **diagnostic** |
| `narration-v5/producer-notes/producer-notes-contract.json` | non-spoken producer guidance | optional in required list | helper/possible editorial users | unique information; **supporting, must not lose** |
| `narration-v5/producer-notes/producer-notes-diagnostics.json` | diagnostics | optional | diagnostics | **diagnostic** |
| `narration-v5/style/documentary-style-contract.json` | `DocumentaryStyleContract` | conditional but absent from required list | prompt/realization | unique chosen style; **supporting compatibility input** |
| `narration-v5/style/documentary-style-diagnostics.json` | style report | optional | quality | **diagnostic** |
| `narration-v5/raw-narrative/{long,short}/raw-narrative.json` | `RawNarrative` | both blocking even if requested format differs | internal generator/certification paths | reproducible from contracts; **derived** |
| `narration-v5/raw-narrative/raw-narrative-diagnostics.json` | counts | blocking | pipeline | **diagnostic** |
| `narration-v5/scene-fact-cards/{long,short}/scene-fact-cards.json` | `SceneFactCardSet` | blocking | prompt/semantic certification | unique provenance/forbidden facts; **supporting authority candidate** |
| `narration-v5/scene-fact-cards/scene-fact-cards-diagnostics.json` | card coverage | blocking | pipeline | **diagnostic** |
| `narration-v5/documentary-script/{long,short}/documentary-script.json` | `DocumentaryScript` | blocking | certification | fullScript + per-scene narration; projection duplicates variant narration but preserves transitions/facts; **authority candidate** |
| `narration-v5/documentary-script/documentary-script-diagnostics.json` | script checks | blocking | pipeline | **diagnostic** |
| `narration-v5/documentary-script/performance-diagnostics.json` | duration/style measurements | blocking | pipeline | estimates only; **allowed Phase 7 diagnostic metadata** |

Duplication: root and variant narration hold the same realized scene text in aggregate/projection forms; documentary scripts repeat text plus transitions, facts and observation guidance; raw narrative is a pre-realization plan; cards are semantic constraints, not narration; plan/brief overlap but briefs preserve editorial specificity; preview is human rendering of the request; producer notes and editorial brief overlap but are separately shaped and contain non-spoken guidance.

## F. Upstream lineage (Phase 3 → 7)

```text
03-questions viewer-question-bank / learning-objectives
 -> 04-blueprint documentary-blueprint master/long/short
 -> 05-blueprint-certification authority/editorial contract/diagnostics
 -> 06-story-frames certified authority/index/diagnostics

Parallel legacy Chronicle Core:
 editorial/story-graph.json, scene-intents.json, editorial-contract.json,
 observation-metadata.json
 -> creative/documentary-contract.long.json, documentary-contract.short.json,
 documentary-architecture-diagnostics.json, creative-storyboard.json
 -> Phase 7 NarrationGeneratorV5
```

Phase 7 validates and reads the parallel `editorial/*` and `creative/*` set. It does **not** read `06-story-frames/story-frames.json`, `story-frame-index.json`, or `story-frame-diagnostics.json`. Its `StoryFrameSource` and `SourceStoryFrameId` come from creative documentary contracts/storyboard; names such as `Phase6SceneId` in diagnostics do not establish certified Phase 6 lineage. Therefore:

* creative contracts/storyboard are the real narration inputs today;
* there are overlapping representations across certified story frames, creative storyboard, documentary beats and scene intents;
* existing scene IDs/frame IDs could be mapped to `StoryFrameId`/`FrameId`, and certified Phase 6 checksums could bind the mapping, without recomputing semantics—but no such binding exists;
* counts are checked against the legacy story-frame source/expected documentary beats, not certified authority/index ordering;
* regenerating Phase 6 leaves old Phase 7 untouched; `RetryFailedOnly` can reuse it if root narration exists and old validation says Succeeded;
* exact Phase 6 lineage is therefore the highest certification blocker.

## G. Downstream consumer map

| Consumer | File read / fields | Required/fallback | Risk |
|---|---|---|---|
| `ProductionPipelineExecutionService` Phase 7 validation | 23 named required artifacts; existence, length, JSON root; diagnostics flags | required; no content fallback | Root-kind validation can bless semantically corrupt/mismatched sets. |
| `CertificationServices` | validation diagnostics, narration diagnostics, phase validation; status/message-like evidence | optional reads | Overlapping/self-reported evidence. |
| `SemanticCertificationServices` | context, briefs, cards, scripts, long/short narration and semantic diagnostics; searches facts/retention | optional evidence aggregation | Does not establish complete authority integrity. |
| `CertificationSemanticFactCatalog` | optional meteor shadow report | optional | Family-specific only. |
| Narration prompt composer (within Phase 7) | context, briefs/cards/style in memory and path-specific preflight wording | required in generation | Not a post-resume verifier. |
| Production phases 8–20 | no direct `narration-v5` read found | use separate `question-driven-narration.json`, narration text/video-assembly/TTS artifacts | Current Phase 7 authority is not the direct media/TTS handoff; architecture split can drift. |
| Hero/visual/video compatibility services | `question-engine/question-driven-narration.json` | legacy fallback paths | Separate narration representation, not competing V5 writer but downstream divergence risk. |

Smallest evidence-preserving candidate is the three narration files + two scripts + two card sets + narration diagnostics. Root narration alone is not sufficient. Long and short are variant projections currently required independently. Models have `SceneId` and `SceneOrder`, so a new index is not proven necessary, but uniqueness/order are not comprehensively certified. `narration-diagnostics.json` is useful but not a complete diagnostics authority. Wrapping/validating existing files is safer than replacing them.

## H. Current authority candidate

Existing files only (candidate, not final design):

1. `narration-v5/narration.json`
2. `narration-v5/long/narration.json`
3. `narration-v5/short/narration.json`
4. `narration-v5/documentary-script/long/documentary-script.json`
5. `narration-v5/documentary-script/short/documentary-script.json`
6. `narration-v5/scene-fact-cards/long/scene-fact-cards.json`
7. `narration-v5/scene-fact-cards/short/scene-fact-cards.json`
8. `narration-v5/narration-diagnostics.json`

The context, profile/style, semantic provenance, producer notes and validation diagnostics remain necessary supporting evidence and may prove part of a future compatibility envelope.

## I. Identity and compatibility findings

| Identity field | Current location | Needed on resume? | Stable? / gap |
|---|---|---:|---|
| `contractVersion` | plan, raw narrative, cards, context, scripts, style and diagnostics (inconsistent) | yes | Not uniform or cross-reconciled. |
| `schemaVersion` | some semantic V1 contracts | yes | Not on complete narration set. |
| `pipelineVersion` / orchestration version | validation and several records | yes | Present but insufficient for component changes. |
| generator type/version | runtime diagnostics/type; no explicit stable generator version | yes | CLR/assembly identity is not stable semantic identity. |
| realizer type/version | runtime diagnostics/type only | yes | Version absent. |
| prompt composer/contract version | prompt artifacts partially | yes | No complete-set compatibility key. |
| semantic resolver/adapter/policy identity | diagnostics expose types/coverage | yes | Versions/checksums absent; registry changes undetectable. |
| language profile version | profile name/used | yes | Version/checksum absent. |
| family profile version | V1 compatibility diagnostics/profile IDs | yes | Not bound to final narration. |
| style contract version | style contract | yes | Not bound to final narration/resume. |
| `sourceChecksum` | isolated semantic/story-frame concepts, not narration authority | yes | Certified Phase 6 checksum absent. |
| `authorityChecksum` / `contentChecksum` | none for Phase 7 complete set | yes | Critical gap. |
| `generatedUtc` | many diagnostics/reports | no for compatibility | Present and unstable; exclude from canonical checksums. |

Current artifacts cannot safely resume after any named component/profile/prompt/style/source change.

## J. Checksums and deterministic identity

No Phase 7 complete-set SHA-256 implementation was found. Semantic candidate diagnostics may carry semantic checksum/hash-like values from sources, and Phase 6 has certified checksums, but Phase 7 neither canonicalizes nor verifies them on read. Consequently it cannot reliably detect text edits, reorder/missing/duplicate scenes, projection mismatch, stale Phase 6, semantic/profile/style/prompt drift, or diagnostic mismatch. File existence and JSON root-kind are the reuse/required-output primitives.

Potentially unstable checksum inputs to exclude in future work are `generatedAtUtc`/`generatedUtc`, absolute paths in required-output/runtime diagnostics, assembly locations, unordered dictionaries unless key-sorted, provider metadata, and LLM/request timing. No current Phase 7 checksum is verified on read.

## K. Structured validation findings

| Category/rule | Validator | Structured code? | Blocking? | Artifact |
|---|---|---:|---:|---|
| Prerequisite existence/nonempty beats | generator preflight + pipeline input validator | partly | yes | editorial/creative inputs |
| Semantic registry/policy presence | pipeline reflection/runtime checks | partly | yes | DI composition |
| Family/profile/required facts/conflicts/provenance | family validator + resolution engine | yes | yes | semantic resolution |
| Context schema/completeness | `ContextSchemaValidator` | yes | yes | context |
| Production/directive/visual leakage | purity/generated validators and regex | partly (strings/regex) | yes | safe context/script |
| Language/native/mixed script | `LanguageOutputValidator` | yes, some heuristic text | yes/warn | narration/scripts |
| Prompt structure/leakage/quality | composer + evaluator | partly | yes | preview/quality |
| Scene counts/identity/order | generator diagnostics/count checks | partly | yes | formats/scripts/cards |
| Required file existence/length/root type | pipeline validator | yes status codes | yes | 23 artifacts |
| Narration quality | `EvaluatePhase7NarrationQualityAuthority` | self-reported JSON fields/string classification | yes | multiple diagnostics |
| Duration/TTS/subtitle readiness | performance/word estimates only | partly | limited | script diagnostics |
| Identity/lineage/checksum/reconciliation | none complete | no | no | complete set |

There is **not one complete-set structured validator**. Multiple overlapping validators differ in scope. Weak patterns include message/string-fragment classification in quality aggregation, broad parse failures returning invalid/false, existence/root-kind/count checks, and reliance on self-reported `validationPassed`-style fields without rebuilding all invariants.

## L. Resume findings

`startPhaseNo=7,endPhaseNo=7` silently expands start to 4 if the eight Chronicle Core files are absent. With them present, Phase 7 runs unless `RetryFailedOnly=true`; there is no ordinary Phase-7 reuse branch for `overwriteExisting=false`.

| Scenario | Current behavior | Safe? | Required future behavior |
|---|---|---:|---|
| Previous Succeeded + root narration + RetryFailedOnly | skip and overwrite validation as Skipped | no | validate full set, manifest, compatibility and lineage before reuse |
| Previous Skipped | not recognized for Phase 7 | conservative | recognize only a certified reuse reason |
| Missing/invalid validation | rerun | yes-ish | regenerate without exposing partial authority |
| Missing/invalid manifest | ignored by Phase 7 reuse | no | require verified manifest/authority envelope |
| Missing root narration | rerun | yes | full-set evaluator |
| Corrupt root narration | exists → reused under retry | no | deserialize and validate |
| Missing/corrupt other artifact | root exists → reused under retry | no | complete-set validation |
| Changed Phase 6/generator/resolver/composer/profile/contract | reused under retry | no | compatibility and lineage mismatch forces regeneration |
| Changed formats/language/region/event | reused if same output root/status/root file | no | bind request identity |
| overwriteExisting=true | cleanup deletes selected phase-range outputs before generation | no rollback | stage and commit after validation |
| dry run | no generator; writes Skipped phase validation and manifest | production-safe, evidence-mutating | keep explicit non-authority dry-run evidence |

Reuse is essentially absent normally and file-existence-based under retry; it is not manifest-, compatibility-, or lineage-aware.

## M. Persistence and failure-point matrix

All generator writes use direct `File.WriteAllTextAsync` into active `narration-v5`; preflight has a synchronous direct `File.WriteAllText`. There is no `.tmp`, staging directory, directory swap, backup, atomic rename, rollback or complete-set commit.

| Failure point | Observable active state | Cancellation | Rollback |
|---|---|---|---|
| Directory creation | empty/partial tree | possible before writes | none |
| Plan/brief/knowledge/editorial/notes writes | new supporting files with old/missing narration | token passed per async write | none |
| Raw/cards/style/semantic diagnostics | mixed partial generation | token passed; completed files remain | none |
| Context/normalization/realization | diagnostics may precede final narration | token passed | none |
| Semantic/purity failure | explicit failed diagnostics; earlier files remain | exception caught by pipeline only for selected types | none |
| Prompt/request writes | prompt can exist without narration | token passed | none |
| Per-format script/narration | one format may be new while other/root old | token passed | none |
| Aggregate narration then diagnostics | root can exist before final diagnostics/validation | token passed | none |
| Required-output validation | can report partial set | token passed | none |
| Phase validation | may fail after files exist | token passed | none |
| Manifest write | happens after phase result; may register output list of partial/failed evidence | token passed | none |
| overwrite cleanup | old valid files removed before build | cancellation/failure loses authority | archive/preserved diagnostics only, no authority rollback |

Individual writes are truncate-and-rewrite, not atomic; resume/concurrent readers can observe partial JSON. Manifest registration cannot make partial artifacts valid semantically, but it can record existing partial outputs.

## N. Concurrency findings

No `INarrationExecutionLock`, `NarrationExecutionLock`, Phase-7 `SemaphoreSlim`, keyed `ConcurrentDictionary`, or `AcquireAsync` scope exists. Same-plan requests can both resolve/realize and interleave writes to identical files and validation/manifest. Different plan roots are naturally path-independent. Cancellation while waiting is irrelevant because there is no wait. There are no entries to clean. The smallest safe future scope, based on evidence, must cover recovery, reuse evaluation, generation, complete-set validation, atomic commit, phase validation and manifest publication for one normalized plan authority root; it need not serialize different roots.

## O. Cancellation findings

| Boundary | Token propagated | Partial writes possible | Current handling |
|---|---:|---:|---|
| controller → batch → production pipeline | yes | yes | normal async propagation |
| pipeline → generator | yes | yes | passed directly |
| semantic resolver/engine/adapters | token appears in async boundary where available; much resolution is synchronous | yes after earlier writes | no transaction |
| prompt composer/validators | synchronous | yes | token checks only around surrounding async operations |
| narration realizer | asynchronous interface receives token; deterministic implementation | yes | cancellation propagates |
| file writes | async writes receive token; preflight synchronous write has none | yes | completed/truncated files remain |
| phase exception handler | catches `ArgumentException`, `InvalidOperationException`, `IOException`; not `OperationCanceledException` | yes | cancellation escapes, so phase validation/manifest may be absent |
| diagnostics writers | generally token; meteor lifecycle helpers include direct diagnostics behavior | yes | no rollback |

Direct `CancellationToken.None` calls are concentrated in tests/default helpers, not the production call. No production Phase-7 blocking `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` was found in the traced path. Cancellation safety remains inadequate because persistence is non-transactional.

## P. LLM/provider and TTS/subtitle boundary

| Component | Provider / production path | Fallback/config |
|---|---|---|
| `NarrationRealizer` | deterministic in-process realizer registered as `INarrationRealizer` | `SemanticDefaults.NarrationRealizer` only for direct/default construction; DI is required by production |
| `NarrationPromptComposer` | builds preview and `llm-request.json`; no network call | no provider config used |
| Azure/OpenAI | no reachable Phase-7 text provider call found | Azure OpenAI image services are unrelated |
| Tests | direct deterministic realizer and test substitutes | no production cache evidenced |

Realizer/provider/composer identity must nevertheless be resume-compatible because they affect text. Phase 7 does not invoke Azure Speech, generate audio, SRT or SSML, or assign voices. It produces text, word budgets, estimated duration/performance and observation guidance: allowed text-only preparation metadata. Actual voices, synthesis, SSML, timings, subtitles and audio remain downstream. No Phase-7 architecture violation was found at this boundary.

## O2. Language findings

* **English:** requested language is resolved through `LanguageProfileResolver`; English is the default/fallback. Validator checks expected Latin/English output and leakage. Proper names are preserved through semantic speakable formatting. Long/short scene order comes from contracts. Gap: fallback/profile version and exact requested language are not bound into authority compatibility.
* **Hindi:** `hi` profile uses Unicode JSON serialization with escaping disabled and UTF-8 no BOM; native Devanagari readability and mixed-script checks are implemented, with allowances for astronomical proper nouns. Both variants use the same authority models. Gap: heuristic script thresholds/proper-noun exceptions, localized semantic term/profile versions and fallback state are not authority identity.
* Same CLR models support both languages; output paths are not language-scoped, so a changed language in the same root can overwrite or be unsafely reused under retry.

## Q. Constellation readiness

Constellation is recognized in event mapping and the family registry; constellation/deep-sky object knowledge generation includes identity, pattern/sky-navigation science, seasonal/observation concepts, and separates cultural framing from claims that stars form a physical group. The same realizer can consume profile/policy data; evidence does not justify a new realizer.

Blockers: executable profile/policy/adapter coverage is less explicit than MeteorActivity (production preflight checks only MeteorActivity); principal-star lists, RA/declination/sky-location, observer-region seasonal visibility, best-viewing guidance and mythology provenance are not demonstrated as a complete required-capability set; Hindi constellation terminology tests are incomplete; no constellation Phase-7 end-to-end authority/lineage test binds scenes to Phase 6. Forbidden leakage rules are broadly family-aware through profiles but constellation science-vs-myth provenance needs certification.

## R. Existing test matrix

`dotnet test ... --list-tests` could not execute because the environment has no `dotnet`. Static discovery found **181 files** matching the mandated broad terms; the focused Phase-7/narration/semantic/language/style/production set contains **211 `[Fact]`/`[Theory]` declarations**. Counts below are static declarations, not executed results.

| Test class | Count | Scope | Production path? | Result |
|---|---:|---|---:|---|
| `ProductionPipelineExecutionServiceTests` | 81 | phases, validation, retry/dry-run/outputs | helper/service construction | not executed: SDK absent |
| `RequiredSemanticFactResolverTests` | 31 | legacy semantic resolution | direct helper | not executed |
| `RequiredSemanticFactResolverV1MigrationTests` | 29 | V1 parity/migration | direct resolver | not executed |
| `LanguageOutputValidatorTests` | 13 | English/Hindi/script validation | helper | not executed |
| `Phase7CanonicalEventDispatchV1Tests` | 12 | family dispatch | helper/DI | not executed |
| `NarrationContextPurityTests` | 10 | purity/leakage | helper | not executed |
| `Phase7AggregationRegressionTests` | 6 | aggregate/format regression | helper | not executed |
| `Phase7ProductionDiSemanticBindingTests` | 6 | DI registry binding | near-production composition | not executed |
| `DocumentaryStyleDirectorTests` | 6 | style | helper | not executed |
| `Phase7SingleAuthorityStaticTests` | 4 | ownership source assertions | static production-source check | not executed |
| `Rc2NarrationGeneratorV5PreflightTests` | 3 | missing/empty inputs, languages | direct generator | not executed |
| `Phase7EventIdentityProductionResolutionTests` | 2 | identity | direct resolver/generator | not executed |
| `Phase7NarrationHandoffTests` | 2 | handoff | helper | not executed |
| Six focused one-test Phase7 classes | 6 | API-path semantic context, resolver parity, source context, failure diagnostics, object knowledge, five-family fixture | mostly direct generator/helper | not executed |

Coverage exists for input validation, generation pieces, artifact checks, semantic resolution, runtime DI, English/Hindi, meteor/moon/eclipse/conjunction and regression. Material gaps remain for certified Phase-6 lineage, complete-set checksum/compatibility, safe resume/overwrite/retry, atomic commit, Phase-7 concurrency, cancellation failure points, manifest security, and constellation English+Hindi production API integration. Test names were not treated as proof of production integration.

## S. Gap register

| ID | Severity | Gap | Evidence | Required correction |
|---|---|---|---|---|
| O2.ORCH.5-G001 | Critical | No certified Phase 6 lineage | generator never reads `06-story-frames/*` | Bind source IDs/order/checksums without rebuilding Phase 6 semantics. |
| O2.ORCH.5-G002 | Critical | No atomic complete-set commit/rollback | direct active-path writes | Stage, validate, atomically publish and recover. |
| O2.ORCH.5-G003 | Critical | Unsafe retry reuse | Phase 7 predicate is successful validation + root file existence | Complete-set compatibility/lineage validator before reuse. |
| O2.ORCH.5-G004 | High | No same-plan execution lock | no Phase-7 lock symbols | Keyed cancellable lock across lifecycle. |
| O2.ORCH.5-G005 | High | No authority/content/source checksum | no verified Phase-7 checksum | Deterministic canonical checksums and read verification. |
| O2.ORCH.5-G006 | High | Runtime/component versions unbound | type/assembly diagnostics only | Stable generator/realizer/composer/resolver/profile/style identities. |
| O2.ORCH.5-G007 | High | Split validation-file ownership | generator preflight and pipeline both write same path | Make production validation writer sole owner while preserving generator diagnostics. |
| O2.ORCH.5-G008 | High | Required validator checks shape, not semantics | length/root JSON checks | One complete-set structured validator, reusing existing validators. |
| O2.ORCH.5-G009 | High | Manifest lacks Phase-7 role authority | explicit Phase 3–6 role arrays only | Register validated Phase-7 candidate/supporting/diagnostic roles. |
| O2.ORCH.5-G010 | High | Cancellation leaves mixed files | tokenized direct writes/no transaction | Cancellation-safe staging and cleanup/recovery. |
| O2.ORCH.5-G011 | Medium | Aggregate/variant/script equivalence not certified | duplicated text representations | Validate scene IDs/order/text projections and uniqueness. |
| O2.ORCH.5-G012 | Medium | Diagnostics overlap/self-report | several validators and message classification | Structured codes and reconciliation. |
| O2.ORCH.5-G013 | Medium | Language/profile compatibility incomplete | same paths, fallback/version absent | Bind requested/resolved language/profile/version. |
| O2.ORCH.5-G014 | Medium | Constellation executable coverage incomplete | recognized family, no complete capability/lineage matrix | Certify profile/policies/adapters/English/Hindi fixtures. |
| O2.ORCH.5-G015 | Medium | Downstream split with legacy narration | phases 8–20 read other narration paths | Document/validate handoff before changing any public flow. |
| O2.ORCH.5-G016 | Low | Build/test evidence unavailable locally | `dotnet: command not found` | Execute full matrix in .NET SDK CI environment. |

## T. Recommended implementation decomposition

1. **Task 2A — Authority contract and stable runtime identity:** define compatibility envelope over existing files; no renames.
2. **Task 2B — Phase 6 lineage and complete-set structured validator:** bind certified authority/index/diagnostics checksums, IDs, counts and order; reuse validators.
3. **Task 2C — Deterministic checksums and projection reconciliation:** canonical content/source/authority hashes; aggregate/long/short/script/card invariants.
4. **Task 2D — Atomic persistence, recovery and keyed execution lock:** staging/swap/rollback with cancellation and failure matrix.
5. **Task 2E — Resume/overwrite/retry/dry-run and sole validation/manifest ownership:** compatibility-aware lifecycle and role registration.
6. **Task 2F — Language/runtime compatibility certification:** English/Hindi, fallback, profile/style/prompt/resolver identities.
7. **Task 2G — Constellation executable readiness:** profile/policy/adapter data and bilingual fixtures only; no new realizer/API.
8. **Task 2H — Certification matrix:** production-path API tests, corruption/concurrency/cancellation/manifest security and CI evidence.

## U. Files inspected

Primary files inspected completely or by relevant executable regions/search matches:

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs`
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationPromptComposer.cs`
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/LanguageOutputValidator.cs`
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2ContentPlanningBatchOrchestrator.cs`
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ContentPlanProductionExecutionService.cs`
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ContentPlanBatchGenerationService.cs`
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs`
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/RuntimeCompositionDiagnostics.cs`
* all files under `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/PromptComposer`, `Semantics`, and `Style` returned by inventory searches
* `Backend/src/Astronomy.MediaFactory.Core/Certification/CertificationServices.cs`
* `Backend/src/Astronomy.MediaFactory.Core/Certification/SemanticCertificationServices.cs`
* `Backend/src/Astronomy.MediaFactory.Core/Certification/CertificationSemanticFactCatalog.cs`
* all 181 matching test files listed by the mandated test-term discovery search, with focused inspection of the 15 classes summarized above.

Searches also inspected matches in controllers, legacy narration, hero, visual, assembly, TTS, SRT/subtitle, weekly forecast and production phases 8–20. The generated local discovery target `phase7-tests.txt` could not be populated because the SDK executable is absent and is not committed.

## V. Commands executed and results

| Command | Result |
|---|---|
| `git status --short` | pass; clean before audit |
| `git rev-parse HEAD` | pass; `4fe184ca7731cadc600da9e8d2e6268a34772268` |
| `git log -10 --oneline` | pass; latest `4fe184c` Phase 6 certification merge |
| `dotnet --info` | environment warning; `dotnet: command not found` |
| `dotnet restore Astronomy.MediaFactory.slnx` | environment warning; exit 127, SDK absent |
| `dotnet build Astronomy.MediaFactory.slnx --no-restore` | environment warning; exit 127, SDK absent |
| `rg -n "NarrationGeneratorV5\|PhaseGenerateNarrationPlanAsync\|phase-07-validation\|phase7Artifacts\|narration-v5" Backend` | pass; 117 matches |
| `rg -n "BuildAndWriteDiagnosticsAsync" Backend` | pass; 15 matches, one V5 production caller |
| mandated narration writer search | pass; 16 direct filesystem operation matches |
| mandated checksum/version search | pass; 128 matches reviewed |
| mandated Phase 6 lineage search | pass; 46 matches; none proves V5 consumption of `06-story-frames` |
| mandated narration consumer search | pass; 69 matches |
| mandated constellation search | pass; 633 matches |
| flow/ownership search for overlay/validation/manifest/reuse symbols | pass; 65 matches |
| cancellation/concurrency/blocking search | pass; 1,151 broad matches, traced production boundary manually |
| `dotnet test tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj --no-build --list-tests > phase7-tests.txt` | environment warning; not runnable because SDK absent; static discovery: 181 matching files, focused 211 test declarations |

