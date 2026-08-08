# Phase 11 new-architecture audit — Hero asset generation

**Audit date:** 2026-08-08  
**Scope:** production Phase 11 and its direct upstream/downstream edges.  
**Change policy:** audit only. No production code, configuration, registration, schema, artifact, or test was changed.

## Executive decision record

Phase 11 is currently a large legacy creative subsystem, not a presentation step over frozen authority. Its production entry point runs only for an explicitly requested `HeroAsset`. It rebuilds story semantics from three `question-engine` documents, uses request/plan event intelligence, selects legacy approved scenes only to build composition metadata, and ordinarily creates three new Hero images (Azure Image2 first, deterministic composition fallback). It does **not** read the Phase 8 manifest, Phase 9 manifest, or Phase 10 certification.

For the stated Orion request (`ShortVideo`, `LongVideo`, `Thumbnail`, no `HeroAsset`), Phase 11 is currently **NotApplicable** and is skipped. That is also the recommended rule: run Phase 11 iff `HeroAsset` is requested. Phase 12 has independent generation paths and can operate with an empty Hero source list, so `Thumbnail` is not evidence for an implicit Phase 11 prerequisite.

The smallest clean future responsibility is: verify committed Phase 10, resolve one certified cinematic Phase 8 asset, deterministically compose editorial overlays without changing scientific geometry, validate, transactionally publish a Hero image plus lineage manifest, and fail closed. Recommendation **A** (reuse a certified cinematic Phase 8 image) is preferred over a fresh AI generation or hybrid because it has the lowest cost and the strongest semantic/scientific lineage.

## 1. Registry, entry point, and call chain

### Production registry

| Item | Evidence |
|---|---|
| Registry | `ProductionPipelineExecutionService.PhaseDefinitions()` maps `(11, "Generate Hero", PhaseGenerateHeroAsync)`. |
| Entry method | `PhaseGenerateHeroAsync(ProductionPhaseContext, CancellationToken)` constructs `HeroAssetStoryGenerationRequest` with event, region, language, `DryRun=false`, overwrite flag, `Full`, execution context, and pipeline request. |
| Facade | `IHeroAssetIntelligenceEngine` / `HeroAssetIntelligenceEngine`. |
| Implementation | `IHeroAssetStoryGenerator` / `HeroAssetStoryGenerator`. |
| Supporting services | `IHeroAssetSceneSelector` / `HeroAssetSceneSelector`; `IHeroCompositionEngine` / `HeroCompositionEngine`; `IHeroPromptMigrationService` (optional); `IVisualIntelligenceOrchestrator` (optional diagnostics contract). |
| Renderer | Azure Image2 HTTP path followed by Hero V6.5 overlays, or `AstronomyVisualCompositionEngine` generic deterministic rendering. |
| Validator | in-engine story/blueprint/composition/layout checks, then pipeline `ValidateAndMaterializeHeroContractAsync` and `ValidateHeroVisualStyle`. There is no separate injected Phase 11 validator. |
| Writer/helpers | direct `Directory`, `File`, `ImageSharp`, `OutputArtifactRegistry`, `IRuntimeAssetPathResolver`. |
| DI | scoped registrations for `IHeroAssetIntelligenceEngine`, `IHeroAssetSceneSelector`, and `IHeroAssetStoryGenerator`; composition and prompt-migration registrations are adjacent in `ServiceCollectionExtensions`. |
| Configuration | `RenderingOptions`, `AzureOpenAIForImageOptions`, `ThumbnailFontOptions`, `OutputArtifactsOptions`; strict overlay validation is passed from `RenderingOptions`. |

### Full production call graph

1. `RunAsync` computes applicable phases, dispatches Phase 11, and uses the generic phase-result/validation writer.
2. `PhaseGenerateHeroAsync` calls `heroEngine.GenerateHeroAssetsAsync(...Full...)`.
3. `HeroAssetIntelligenceEngine` delegates without transformation to `HeroAssetStoryGenerator`.
4. `GenerateFullHeroAssetsAsync` executes hook/story, blueprint, then images and merges their responses.
5. Story generation reads `question-answer-set.json`, enriched question scene plan, and legacy question-driven narration, constructs What/Where/When/Why, localizes it, validates it, and writes `hero-asset-story.json`.
6. Hook scoring may use the configured prompt/AI path; it selects the highest score and retains alternatives.
7. Blueprint generation creates three platform variants, validates and writes `hero-asset-blueprint.json`.
8. Image generation loads legacy approved scene candidates (or generic reconstructed candidates), invokes the scene selector, and builds `hero-composition-model.json`. Although a manifest is emitted, the current image background is not a selected certified scene.
9. If Azure Image2 is configured, the exact production prompt builder is `BuildAzureHeroPromptV2`/the Azure Hero prompt helpers in `HeroAssetIntelligenceEngine`; an HTTP request creates a new cinematic base image, then deterministic Hero overlay composition writes variants. Non-planet-family provider failure falls back to the generic renderer. Planet grouping blocks that fallback.
10. With Azure disabled, the generic renderer creates a new deterministic sky/astronomy composition using local celestial textures and writes PNG variants.
11. In-engine diagnostics/review/intelligence/registry manifest writers run.
12. `ValidateAndMaterializeHeroContractAsync` checks required files and non-empty final image, forbidden leakage, and layout diagnostics; it returns the distinct output list to generic phase result mapping.

### Step behavior

| Step | Input | Output | Reads / calls | Writes / fallback / reuse |
|---|---|---|---|---|
| Applicability | requested outputs | execute/skip | request only | skipped reason is `Output type not requested` |
| Story | request + legacy question data | `HeroAssetStoryDto` | 3 question-engine JSON files | existing parseable story is reused when overwrite is false; missing sources use “golden pilot defaults” warning—unsafe reconstruction |
| Blueprint | story + event intelligence | blueprint/variants | story; possible hook scoring provider | existing story/blueprint can be loaded; writes two JSON files |
| Scene metadata | story/blueprint + approved candidates | selected scene manifest/composition | normalized/staged `scene-approval-v3` PNGs | fewer than 3 candidates causes generic candidates; selection scores semantic tokens |
| Azure base | request/event objects/composition | new raster | Azure OpenAI image endpoint | provider failure → generic renderer except planet grouping; prompt can create new astronomy meaning |
| Generic base | local textures + composition | new raster | celestial asset library | procedural sky/stars/planet placement; not certified upstream imagery |
| Overlay | generated base + title/footer | 3 PNG variants | fonts/assets | contain-fit base, deterministic text/gradients; final copied from landscape |
| Validation | files + JSON diagnostics | pass/throw | Hero artifacts | diagnostics are direct writes, not staged; failure can delete image outputs |

## 2. Applicability and actual consumers

### Exact current rule

`IsPhaseRequiredForRequestedOutputs` maps Phase 11 solely to `IsRequestedOutput(context, "HeroAsset")`. Requested-output completion maps `HeroAsset` to Phase 11 alone. Phase 12 maps independently to `Thumbnail`; Phase 18 maps to either video type. Therefore the supplied Orion plan must **not** execute Phase 11.

`plannedProductionSteps`/asset planning likewise adds `hero_landscape` only when `HeroAsset` is present. Some planning defaults include Hero, but those defaults do not override an explicit request at execution.

### Thumbnail dependency proof

Phase 12 is invoked with its own request and renderer. `ThumbnailAssetIntelligenceService` can populate `SourceHeroAssets`, but multiple successful/fallback manifest constructors set it to `[]`; Hero manifests are consulted only by branches which elected to use Hero assets. It also selects approved scenes and local/AI thumbnail sources. Thus Hero is optional input, not a required Phase 12 authority.

Other consumers:

* Phase 12 optionally consumes `hero-scene-manifest.json`/Hero paths.
* Phase 18 contains Hero scene-manifest and `UseHeroAssetAsOpening` concepts, but video assembly has its own scene inputs and no applicability dependency on Phase 11.
* Phase 13 does not establish a required Hero dependency in its production entry.
* Phase 20/compatibility projection copies Hero files if present and reports requested-output completion; this is optional packaging, not generation dependency.

### Recommended deterministic matrix

| Requested outputs | Phase 11 |
|---|---|
| ShortVideo only | NotApplicable |
| LongVideo only | NotApplicable |
| Short + Long | NotApplicable |
| Thumbnail only | NotApplicable |
| HeroAsset only | Required |
| Short + Long + Thumbnail | NotApplicable |
| Short + Long + Thumbnail + HeroAsset | Required |

Recommended NotApplicable code: `P11_HERO_ASSET_NOT_REQUESTED`; reason: `HeroAsset was not requested; Phase 11 has no required downstream consumer.`

## 3. Input authority audit

| Current read | Classification | Use |
|---|---|---|
| `question-engine/question-answer-set.json` | **LEGACY / UNSAFE RECONSTRUCTION** | What/Where/When/Why semantic source |
| `question-engine/question-driven-scene-plan.enriched.json` | **LEGACY / UNSAFE RECONSTRUCTION** | story source and scene intent |
| `question-engine/question-driven-narration.json` | **LEGACY / UNSAFE RECONSTRUCTION** | story and overlay text |
| `scene-approval-v3/{long,short}/scene-*.png` and normalized plan-root equivalent | **COMPATIBILITY / LEGACY** | candidates and asset paths; not certified Phase 8 selection |
| `plan-input/production-event-intelligence.json` and in-memory `ProductionEventIntelligence` | **AUTHORITATIVE only for event identity under current pre-Phase-11 model** | type, title, objects, date/window/region |
| `ProductionPipelineRequest` / database-mapped plan request | **UNSAFE RECONSTRUCTION** for documentary semantics; acceptable identity input | title, event type, objects, schedule, location, language |
| existing Hero JSON/PNG files | **LEGACY reuse** | load/reuse when overwrite is false; diagnostics validation |
| `RenderingOptions`, Azure image options, fonts, output-artifact options | **CONFIGURATION** | provider, paths, typography, diagnostics |
| celestial textures (`hero.png`, transparent assets, etc.) | **COMPATIBILITY** | deterministic fallback objects |
| visual-intelligence contracts/comparison output | **DIAGNOSTIC** | optional/non-blocking V4 comparison and intelligence contract |
| `08-scene-assets/scene-asset-manifest.json` | **AUTHORITATIVE, NOT READ** | gap |
| `09-long-scenes/long-scene-image-manifest.json` | **AUTHORITATIVE, NOT READ** | gap |
| `10-scene-validation/scene-asset-certification.json` | **AUTHORITATIVE, NOT READ** | critical gap |
| `04-blueprint`, `06-story-frames`, `07-narration` authorities | **AUTHORITATIVE, NOT READ DIRECTLY** | gap; old projections substitute for them |

There is no Phase 10 gate. “Validity” is inferred from legacy approved-scene presence/selection, story/blueprint heuristics, output existence, and Hero-local layout checks. It never proves Phase 10 publication commit, committed readback, downstream readiness, source checksums, or certified counts.

## 4. Semantic field lineage

| Hero field | Current source |
|---|---|
| title/hook | reconstructed Hero story; hook candidates/scoring; event-specific V6.5 resolver may prefer pipeline/intelligence title/objects |
| subtitle/message | What/Where/When/Why story and composition extraction, then family/title resolver |
| event name/type | pipeline request first in some helpers, then production intelligence/context/event id fallback |
| primary/secondary objects | request or `ProductionEventIntelligence`; local asset resolution derives labels/textures |
| date/time/observing window | request `PeakUtc`, `StartUtc`, `ScheduledUtc`, `BestViewingWindowLocal`, plus story “When”; metadata normalizer compacts it |
| location/direction | visibility region/request and story “Where”; direction extraction compacts it |
| observation metadata | reconstructed from request/intelligence/question answers, not certified narration/story frames |
| visual subject | primary object/event/title plus blueprint visual focus |
| background | newly generated Azure base or deterministic renderer; selected approved scene is composition metadata, not reliable raster lineage |
| scene source | token-scored legacy approved candidates; generic candidates if fewer than three |
| labels/badges/overlays | composition model, family rules, Hero V6.5 title/metadata resolver and deterministic template |

The engine can therefore invent new hooks, facts/claims, visual relationships, scene meaning, and AI visual astronomy. It is not currently presentation-only.

## 5. Source image, providers, scientific and cinematic behavior

### Selection algorithm

`HeroAssetSceneSelector` scores candidates separately for primary, secondary, and support roles using question type, narrative/visual intent, source-answer tokens, and story tokens; ties use scene number. Long normalized approved files outrank short/staged files. Identity is written to `hero-scene-manifest.json`, but the Azure base does not reuse that file and the deterministic renderer creates its own scene. Consequently the manifest does not provide physical source lineage.

### Provider-capable routes

* **Azure OpenAI image generation:** used when Image2 configuration is enabled. The prompt is constructed from event family, intelligence title/type/objects, story hook, composition, cinematic constraints and variant dimensions. It creates a new image. Risks: wrong objects/geometry, fantasy astronomy, and embedded text despite negative constraints.
* **Other AI:** optional hook scoring/prompt migration/visual-intelligence comparison services may invoke configured intelligence providers; those do not establish source certification.
* **Deterministic/image composition:** `AstronomyVisualCompositionEngine` creates twilight sky, stars, constellation/reference overlays, procedural/local-texture planets, horizon, gradients, and typography. It creates new imagery and can yield flat chart/card-like results.
* **Stellarium/Hipparcos:** no direct production call was found in the Phase 11 call graph. Any reference-star geometry supplied to the generic renderer is local model input, not a Phase 10-certified read.

The generic fallback route can output a deterministic observation/infographic card and procedural star field, contrary to “cinematic by default” unless explicitly authorized. Azure is cinematic-first, but newly generated visuals sever scientific certification. Current contain-fit composition avoids simple stretching and preserves the whole AI base, yet neither route preserves certified Phase 8 geometry because it does not consume a certified image. Labels and object placement can be added/changed; AI can regenerate everything. Scientific certification therefore does **not** survive.

## 6. Visual contract and dimensions

The engine declares three PNG profiles:

| Variant | Pixels | Aspect |
|---|---:|---:|
| Landscape | 1920×1080 | 16:9 |
| Square | 1080×1080 | 1:1 |
| Portrait | 1080×1920 | 9:16 |

`hero-final.png` is the canonical landscape copy. PNG encoding is used; there is no lossy quality/compression setting for production Hero PNGs. The shared composition engine also supports JPEG quality 92, but Phase 11’s declared Hero outputs are PNG.

Current reported renderer/contract version is V6.5 in pipeline diagnostics. The architecture uses family-dependent `CinematicHero`/guide contracts, `AzureHeroRendererV2` or generic renderer, a shared footer, safe margins proportional to canvas, title/subtitle backdrops, CTA/direction accents, gradients, local typography resolution, and bottom metadata. There is no separately versioned external template file: template geometry is hard-coded in rendering methods (`BuildHeroTemplateTextBlocks`/`HeroTemplateBounds`).

The pipeline’s `Phase11HeroDiagnostics` projects `heroVersion`, output/canonical paths and sizes, date/time/location/event-code policy, overlap/clipping/footer/safe-area flags, and fixed 85% visual / 15% metadata claims. Most values are populated by reading `HeroOverlayDiagnosticsDto` from `hero-layout-validation.json` or generation diagnostics; the fixed percentages are diagnostic assertions rather than pixel-segmentation measurement.

### Title/subtitle and metadata policy

Hook generation requires at least five candidates and selects the highest total score. Composition compacts direction/timing/CTA by word limits; renderer typography scales/wraps within hard-coded blocks and layout diagnostics detect clipping/overlap. V6.5 title resolution includes family-specific and language-specific behavior. There is no single authoritative maximum title/subtitle character count contract; limits are distributed across prompt, resolver, compaction, typography and validator logic.

Hero overlay policy intentionally exposes date and time in the bottom bar while removing location and internal event code. This explains `heroDateAdded`, `heroTimeAdded`, `heroLocationRemoved`, and `heroEventCodeRemoved`: they assert a cleaner public editorial overlay and prevent internal/location leakage. The code applies family normalization for planetary grouping, Moon, meteor and eclipse families, but this is heuristic. It is not proven appropriate for every listed family; location can be scientifically material to eclipse/occultation visibility, while exact time/date may be meaningless or region-dependent for constellation/deep-sky evergreen content. Preserve these only as a policy input from authority, not a universal Phase 11 rule.

## 7. Physical artifacts and authority model

Current root is the execution context’s `HeroRoot` (normally `hero-assets/`; compatibility projection also uses `hero/`). Actual outputs include:

* `hero-final.png`, `hero-landscape.png`, `hero-square.png`, `hero-portrait.png` — production image outputs.
* `hero-asset-story.json`, `hero-asset-blueprint.json`, `hero-composition-model.json` — legacy creative/reconstruction contracts.
* `hero-scene-manifest.json` — legacy selection metadata, not certified lineage authority.
* `hero-layout-validation.json`, `hero-review.json`, `hero-generation-diagnostics.json`, prompt/comparison/editorial-review/intelligence diagnostics — validation/diagnostic outputs depending on `OutputArtifactsOptions`.
* output artifact manifest generated by `OutputArtifactRegistry` — registry of paths, not a transactional Hero authority.
* `validation/phase-11-validation.json` — generic phase execution record.

Existing DTOs (`HeroAssetGenerationResponse`, `HeroSceneManifestDto`, `HeroLayoutValidationDto`, `HeroOverlayDiagnosticsDto`, Hero intelligence/review contracts) can reuse event ID, selected scenes, renderer/layout diagnostics, dimensions, validity and generated paths. None is sufficient for the requested authority lineage: Phase 10 checksum, Phase 8 asset/semantic ID, source checksum, Hero physical SHA-256, atomic publication/readback, and `downstreamReady` are absent or not authoritative.

## 8. Validation and result mapping

### Active production checks

* story, hook count/scores, blueprint/review structural validity;
* required strategy assets/objects and composition blocks;
* output existence/non-empty, all three variants;
* duplicate blocks, text overlap, object visibility/cropping metadata;
* title/subtitle clipping/overflow, title/metadata overlap, safe area;
* bottom bar, date/time visible, location/event code removed via overlay diagnostics;
* forbidden leakage in story/blueprint/composition/diagnostic text;
* renderer/validator contract agreement and layout `IsValid`/composition reports.

### Missing production checks

No authoritative source identity, Phase 10 acceptance, event/semantic match to certified asset, scientific validity, input or output SHA-256, committed publication/readback, rollback, or measured visual/metadata percentage. Image decode/dimensions occur in rendering/layout paths, but the final contract gate principally checks existence/non-empty and diagnostic claims rather than independently decoding and verifying every committed file.

Review/intelligence/comparison documents are diagnostic or legacy; they must not be mistaken for authority.

Generic `WritePhaseValidationAsync` supplies status/reason/reasonCode, input/output lists, retry metadata, and validation path. Phase 11 has no dedicated accepted reason code and no retained publication result analogous to Phase 9/10. `publicationCommitted` and `committedStateValidationPassed` therefore cannot truthfully represent a Hero atomic transaction today. The files-only phase action also loses structured lineage.

## 9. Reuse, fallback, cleanup, and transaction audit

### Reuse

With `overwriteExisting=false`, story/blueprint helpers load existing JSON; existing intelligence contracts may be accepted; image paths can survive into the run. Reuse does not compare Phase 10 checksum, Phase 8 asset/source checksum, event identity, title/subtitle, template/renderer version, physical checksum, or dimensions. This creates severe stale-reuse risk. `retryFailedOnly` is handled generically from phase validation status rather than a Hero lineage-aware resume decision.

With overwrite true, generic phase cleanup runs and the Hero generator also deletes existing Hero outputs. It regenerates rather than transactionally replacing them.

### Fallback classification

| Fallback | Classification / future policy |
|---|---|
| missing What/Where/When/Why → golden pilot defaults | unsafe legacy; fail closed |
| fewer than 3 scenes → generic candidates | unsafe reconstruction; fail closed |
| Azure unconfigured → generic renderer | legacy; remove from authoritative path |
| Azure failure → generic renderer | unsafe for scientific/cinematic authority; fail closed (already blocked for planet grouping) |
| local asset missing → warning/procedural visual | unsafe; fail closed |
| missing V4 intelligence → diagnostic fallback contract | safe only while explicitly diagnostic/non-authoritative |
| existing parseable Hero story/file → reuse | unsafe unless lineage and physical validation pass |
| landscape → canonical final copy | safe deterministic publication operation |

### Cleanup ownership

The phase-owned cleanup registry resolves Phase 11 to the Hero root and only deletes roots whose owner is in the rebuild set. For an isolated `start=11,end=11,overwrite=true` execution, Phase 8/9/10 roots and legacy scene-assets roots are read-only dependencies and are not valid Phase 11 deletion targets. No critical upstream deletion route was found. Defense is present in `PhaseOwnedCleanupExecutor` denial tracking. The generator’s internal cleanup is scoped to `heroAssetsRoot`.

### Transactional publication gap

There is no candidate/staging directory, candidate readback, atomic directory/file swap, committed readback, or rollback. Files and diagnostics are written directly to the stable Hero root; failures may delete image outputs and can leave partial JSON/PNG state. This is the largest publication integrity gap.

## 10. Event hard-coding audit

Production contains generic family branches/keywords for constellation, Moon, planet/planet grouping, meteor, eclipse, conjunction and related event aliases. Those are valid generic family behavior when used only to select layout/prompt policy, but they become a defect when they synthesize scientific content. No Orion-specific production branch was found in the Phase 11 entry graph. Specific objects/dates/locations occur primarily in tests, prompts/examples, and celestial asset configuration. “Golden pilot defaults” and generic `SKY EVENT` are production legacy defects because they allow a semantically incomplete Hero rather than failing closed.

## 11. Test inventory

The principal test file is `HeroAssetStoryGeneratorTests.cs`. Its named tests cover: dry-run/write behavior for story/hook/blueprint/images; legacy What/Where/When/Why inputs; scene selection and normalized-long preference; deterministic role scoring; meteor missing-assets behavior; family-specific cinematic Azure prompts and diagnostics; planet/eclipses metadata compaction; Hindi/English V6.5 titles; overlap/validation ordering; conjunction variant prompts. These remain useful renderer/layout regression tests, but story/scene-input and legacy artifact-path assertions encode architecture to retire.

`ProductionPipelineExecutionServiceTests.cs` covers cinematic hook policies, canonical/fallback overlay diagnostics, clipping/overflow/overlap, safe-area precedence, renderer contract selection and layout summary decisions. Preserve these validation behaviors, adapting fixtures to the new manifest.

`HeroPromptMigrationServiceTests.cs` and `HeroImageV4ComparisonTests.cs` prove comparison flags, non-blocking failures, natural-language constraints and unchanged production Hero. They are diagnostic/legacy if recommendation A removes production AI; retain only for explicitly non-authoritative experiments.

`ThumbnailAssetIntelligenceServiceTests.cs` covers optional Hero manifest/source lists and proves Hero paths are one source strategy, not universal Phase 12 prerequisite. `VideoAssemblyIntelligenceServiceTests.cs` covers optional Hero manifest/opening behavior. Planning/output-selection tests prove explicit `HeroAsset` mapping. No test proves Phase 10 → Hero lineage, Hero authority checksums, transactional commit/readback/rollback, or lineage-aware reuse. No dedicated cleanup test proves isolated Phase 11 preservation of all Phase 8–10 roots; add one during implementation.

## 12. Recommended Phase 11 contract

### Responsibility

**Phase 11 — Hero Asset Generation** is a presentation/materialization boundary, not story planning:

1. Applicable only when `HeroAsset` is explicitly requested.
2. Read and validate committed `10-scene-validation/scene-asset-certification.json` (`publicationCommitted`, committed readback, `downstreamReady`, counts/checksum).
3. Read `08-scene-assets/scene-asset-manifest.json`; select one Phase-10-certified cinematic asset through a deterministic suitability rule (prefer landscape/long only when crop-safe; otherwise a certified short asset).
4. Read only event identity and, if necessary, editorial title/subtitle already accepted by Phase 4/6/7 authority. Never derive facts from request prose or legacy question-engine data.
5. Deterministically compose overlay without stretching, moving objects, relabeling scientific geometry, or AI regeneration. Crop only when the source manifest explicitly records crop-safe bounds.
6. Validate candidate, atomically publish, read back and verify checksum, then publish manifest/report.

### Minimal artifact shape

Use a single new owned root `11-hero/` unless compatibility consumers require the existing `hero-assets/` root during migration:

* `hero.png` (one canonical landscape Hero initially; add other profiles only when requested contract demands them);
* `hero-asset-manifest.json` (authority);
* `phase11-authority-diagnostics.json` (diagnostic);
* `phase11-publication-report.json` (transaction evidence).

The manifest must record: schema/version; event/plan identity; Phase 10 certification path and SHA-256; Phase 8 manifest path/checksum; source asset ID/semantic identity/path/physical SHA-256; title/subtitle and their authority field IDs/checksums; renderer/template/layout versions; width/height/aspect/format; Hero SHA-256; validation result; transaction ID; publication committed; committed readback; `downstreamReady`.

Recommendation **A** is definitive: reuse one certified cinematic Phase 8 image and add deterministic editorial composition. It preserves product quality and authority, avoids another provider cost, prevents semantic/scientific drift, and makes reuse decidable by checksums. A dedicated AI Hero (B) or hybrid (C) would require a new scientific certification boundary and is not the smallest alignment.

### Failure policy

| Condition | Required behavior |
|---|---|
| Phase 10 missing/invalid/not committed/not ready | fail closed; retryable after upstream state changes; never repair upstream |
| source absent or not certified | fail closed; no placeholder/provider fallback |
| source checksum mismatch | fail closed as authority-integrity error |
| render failure | fail without changing committed Hero; retryable |
| candidate validation failure | reject candidate, retain old committed Hero, fail |
| publication failure | rollback/retain prior committed state, fail retryably |
| committed readback/checksum failure | rollback if possible, set not-ready, fail |

Success code: `P11_HERO_ASSET_AUTHORITY_ACCEPTED`; reason: `Hero asset generated, validated, committed and read back.` Set `publicationCommitted=true`, `committedStateValidationPassed=true`, `canRetry=false`, and list exact authority inputs/committed outputs. Do not report success merely because files exist.

## 13. Minimum future implementation file set

### MUST MODIFY

* `ProductionPipelineExecutionService.cs`: replace the Phase 11 action with authority gate, transaction/result mapping and precise codes; keep applicability mapping unchanged.
* `HeroAssetIntelligence.cs`: adapt the existing response/manifest DTO rather than add parallel creative contracts; add lineage/publication fields or a narrowly named authority record.
* `HeroAssetIntelligenceEngine.cs`: remove legacy semantic reconstruction from the production `Full` path; load Phase 10/8, select certified source, deterministic composition, validation and transaction.
* Phase 11-focused tests in `HeroAssetStoryGeneratorTests.cs` and `ProductionPipelineExecutionServiceTests.cs` during implementation (not during this audit).

### MAY MODIFY

* `HeroAssetSceneSelector.cs`: adapt selection to certified Phase 8 manifest entries, or replace its production use with a small certified selector.
* `HeroCompositionEngine.cs` / `AstronomyVisualCompositionEngine.cs`: only if needed to accept immutable source pixels/crop-safe bounds and authoritative overlay text.
* output-artifact registry/options and compatibility projector: only to register `11-hero` and temporarily project old paths.
* Phase 12/18 consumers: only to consume the new manifest when Hero is present; they must remain independent when it is absent.

### DO NOT MODIFY

Phase 1–10 implementation/contracts/artifacts; production configuration; DI registrations unless a later implementation demonstrably needs one small replacement registration; database schema; unrelated phases/renderers; existing tests during this audit. Avoid a broad visual-intelligence or pipeline refactor.

## 14. Audit conclusion

What works and should be preserved: explicit applicability, three mature responsive layouts if still required, deterministic overlay/safe-area validation, family-aware compact metadata, canonical-final copy, phase-owned cleanup protection, and optional downstream consumption.

What must not survive as production authority: question-engine reconstruction, legacy approved-scene trust, generic/golden defaults, new AI/procedural scientific imagery, file-existence reuse, fixed diagnostic percentages, direct stable-root writes, and success without committed readback.

The alignment is intentionally narrow: preserve the rendering/layout surface, replace only its semantic/source/publication boundary, and make Phase 10 the mandatory gate.
