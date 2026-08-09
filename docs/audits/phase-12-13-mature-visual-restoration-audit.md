# Phase 12 + Phase 13 mature visual restoration audit

**Audit date:** 2026-08-09  
**Scope:** repository and Git history only; Phase 11 is certified/frozen.  
**Change boundary:** this report is the only changed artifact. No production code, test, configuration, DI, contract, database, or generated media was changed. No Azure/OpenAI endpoint was called and no image was generated.

## Executive decision

The repository supports the requested product separation. The newest number is not the best restoration rule.

* **Phase 12:** keep the transactional `ResponsiveThumbnailAuthorityService` publisher, but remove its Phase 11/Phase 8 raster-selection and `PosterThumbnailRenderer/3.0` fact-card renderer from the authority path. Adapt the mature family planner in `ThumbnailAssetIntelligenceService`. Use dedicated per-aspect Azure Image2 **backgrounds** plus deterministic text for Meteor and for every certified factual field. The mature conjunction observation-poster planner is reusable, but its V9 “AI types the complete poster” mode is not fact-safe. Do not create V10.
* **Phase 13:** keep the `Phase13GalleryAuthority` transaction/validation shell, but replace Phase 10 scene selection and square composition with the retained, currently unreachable, mature branch of `AstroPulseGalleryService`: six role-specific Azure backgrounds, one per role, then deterministic overlays. Restore canonical production landscape pages at **1920×1080**. Do not create another Gallery version.
* Both generators must consume certified semantic facts through adapters, record field-level prompt/overlay lineage, and fail closed. Neither needs a Hero, Phase 8/9, or Phase 10 raster. Phase 10 has no Gallery raster-authority role after restoration.

“Last-good” below means the latest repository implementation matching the supplied quality description, not an assertion that every historical output was human-approved. No reference image files or approval ledger are present in this checkout; only the prompt's visual description can be compared. That limitation is material, especially for the contradictory Meteor RC1 guide-card evidence described below.

---

## 1. Exact current Phase 12 execution path

`ProductionPipelineExecutionService` registers phase 12 as `PhaseGenerateThumbnailsAsync`. Current HEAD no longer calls `IThumbnailAssetIntelligenceService`: it derives event identity from `ProductionEventIntelligence`/request and calls:

```text
PhaseGenerateThumbnailsAsync
  -> ResponsiveThumbnailAuthorityService.PublishAsync
     -> require committed Phase 11 manifest and its selected clean Phase 8 raster lineage
     -> stage 12-thumbnails.staging-<transaction>
     -> build deterministic copy/facts
     -> PosterThumbnailRenderer/3.0 + ThumbnailPosterPolicy/3.0
     -> render landscape 1280x720, square 1080x1080, portrait 1080x1920
     -> candidate physical/semantic/readback validation
     -> directory swap, committed readback, canonical validation
```

The service is in `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ResponsiveThumbnailAuthorityService.cs`; the runner entry is in `ProductionPipelineExecutionService.cs`. It publishes `12-thumbnails/thumbnail-{landscape,square,portrait}.png`, `thumbnail-asset-manifest.json`, `phase12-authority-diagnostics.json`, `phase12-publication-report.json`, and `validation/phase-12-validation.json`. On failure its backup/restore and `finally` cleanup preserve prior authority.

The old public/API path still exists independently: `IThumbnailAssetIntelligenceService` is declared in `Core/ThumbnailIntelligence.cs`, implemented by `ThumbnailAssetIntelligenceService`, registered scoped in `ServiceCollectionExtensions.cs`, and exposed by `/api/astronomy-intelligence/generate-thumbnail-assets` in `Api/Program.cs`. It is **API/direct-call reachable**, but not the current pipeline Phase 12 authority route.

`Phase12ThumbnailRouter` has an always-true V9 default. Its “V8” option/method is an alias: it routes Images to `GenerateThumbnailV8AiNativeImagesAsync`, whose identity is `ThumbnailV9AiFinalThumbnailComposer`; V7 is not selected in normal production and router failure does not fall back.

## 2. Exact mature Thumbnail inventory and reachability

| File / type | Exact member | Behavior | Reachability at HEAD |
|---|---|---|---|
| `Core/ThumbnailIntelligence.cs` | `IThumbnailAssetIntelligenceService.GenerateThumbnailAssetsAsync` | four legacy subphases | DI/API/direct only; not pipeline P12 |
| `ThumbnailAssetIntelligenceService.cs` | `GenerateThumbnailAssetsAsync`, `GenerateThumbnailImagesAsync` | subphase dispatcher and version router | API/direct reachable |
| same | `GenerateThumbnailV8AiNativeImagesAsync` | V8-named **V9** complete AI thumbnail per aspect | selected by legacy router |
| same | `GeneratePureV3ThumbnailImagesAsync`, `BuildPureV3ThumbnailPrompt` | dedicated Azure backgrounds + code overlay architecture | unreachable behind always-on V9 except direct/reflection/tests |
| same | `GenerateMeteorShowerThumbnailImagesAsync`, `WriteMeteorThumbnailAsync` | older procedural radiant renderer | router bypass makes it unreachable in normal production |
| same | `BuildAzureImage2ThumbnailV5VariantPrompts` (the current name corresponding to the requested historical `BuildThumbnailV5AzurePrompts`; that exact symbol is absent from HEAD) | per-aspect background-only prompts; Meteor contract recognizes `RadiantBurstThumbnail` | mature prompt logic, currently unreachable |
| same | `WriteAzureImage2ThumbnailV5OverlayAsync` (the current name corresponding to the requested historical `WriteThumbnailV5OverlayAsync`; that exact symbol is absent from HEAD) | deterministic family-specific title/guide overlay | mature overlay logic, currently unreachable |
| same | `ValidateMeteorThumbnailRc1Contract` | enforces Meteor composition/text/prompt invariants | reached only by V3/V5 branch |
| same | `BuildRc1ThumbnailTextLines` | family-specific two/three-line deterministic copy | reusable helper, legacy path |
| same | `AllowsConjunctionVocabulary` | restricts conjunction words to conjunction/pairing/grouping | reusable validator/helper |
| same | `BuildPlanetaryGuideCard`, `ThumbnailGuideCardFactory` | Date/Best Time/Direction/Equipment/Separation and localized cards | legacy intelligence/V5/V9 contracts |
| `ThumbnailV7Engine.cs` | `ThumbnailV7CinematicOverlayRenderer.RenderAsync` | three Azure backgrounds + deterministic cinematic observation overlays | DI type registration and tests; router delegate exists but V9 wins |
| same | `ThumbnailV7BackgroundPromptBuilder`, `ThumbnailV7VariantRenderer`, validator | background prompt, overlay rendering, layout validation | utility/test reachable |
| `Phase12ThumbnailRouter.cs` | `RouteAsync`, `IsThumbnailV9Enabled` | V9 selection; V8 naming alias | legacy service authority |
| `ResponsiveThumbnailAuthorityService.cs` | `PublishAsync` | current transactional, zero-provider Phase-8-derived posters | **current pipeline authority** |
| `ServiceCollectionExtensions.cs` | scoped registrations | registers V7 and `IThumbnailAssetIntelligenceService` | active DI |
| `Program.cs` | thumbnail generation endpoint | invokes legacy interface | active API |

There is no standalone implemented `ThumbnailV9AiFinalThumbnailComposer`; `Phase12ThumbnailRouter.cs` contains an empty marker class and the actual implementation is the V8-named method.

## 3. Thumbnail version/history matrix

| Version | Generation/source | AI responsibility | deterministic overlay / cards | size and status | intent and disposition |
|---|---|---|---|---|---|
| RC1 | family-aware `BuildThumbnailV5AzurePrompts`; also an older procedural Meteor branch | Azure background only in mature Azure branch | `BuildRc1ThumbnailTextLines` + V5 overlay; family-dependent cards | final 1280×720, 1080×1080, 1080×1920; legacy-unrouted | first CTR/family restoration; retain Meteor radiant and concise-copy rules, not blanket card policy |
| V3 | `GeneratePureV3ThumbnailImagesAsync` | new per-aspect background only | deterministic overlays | same final sizes; legacy-unrouted | removes Hero requirement and establishes independent product |
| V5 | `AzureImage2ThumbnailV5Variants`, V5 prompt/overlay writers | new background only | extensive guide cards/panels for planetary/moon; special family branches | same final sizes; legacy-unrouted | mature observation-guide poster, especially conjunction; too dense for Meteor |
| V6 | validation/CTR compatibility residue | mixed legacy behavior | legacy overlay | same product targets; no selected current router | superseded by V7; compatibility only |
| V7 | `ThumbnailV7CinematicOverlayRenderer` | one new background per aspect | deterministic observation overlay, no AI text | same final sizes; test/DI reachable, not selected | strongest general fact-safe renderer base; reuse compositor/validation |
| V8 alias | option and method naming only | none distinct | none distinct | aliases V9 | retain only until option callers migrate |
| V9 | `GenerateThumbnailV8AiNativeImagesAsync` / marker `ThumbnailV9AiFinalThumbnailComposer` | complete finished poster, including text/UI, separately per aspect | explicitly `manualOverlayUsed=false`; guide information is AI pixels | provider native request then final 1280×720, 1080×1080, 1080×1920; legacy router active, pipeline inactive | later, visually ambitious observation poster; unsafe for factual text and not uniformly better |
| current new authority | Phase 11-selected clean Phase 8 raster reuse | no provider | deterministic manual fact-card poster | same three finals; **pipeline active** | governance is strong; product-specific visual generation regressed |

No commit metadata before the repository import reliably explains every supersession; reasons above are derived from renderer diagnostics, validators, test names, and routing. Treat undocumented “approved” status as an uncertainty rather than inventing chronology.

## 4. Event-family Thumbnail strategy matrix

| Family | Mature composition and builder | Copy / factual fields | Recommended restored mode |
|---|---|---|---|
| MeteorShower | `RadiantBurstThumbnail` via `BuildThumbnailV5AzurePrompts`; high-contrast dark sky, visible radiant, spreading streaks, horizon and negative space | `BuildRc1ThumbnailTextLines`: meteor name + `METEOR SHOWER PEAK`; optional certified date only | **Azure background + exactly two deterministic lines**; no guide/date/time/equipment/moon/tips panels unless a separately approved contract says otherwise |
| PlanetConjunction / PlanetPairing | observation-poster family; `ThumbnailGuideCardFactory`, V5 prompts and V7 composer; recognizable pair, callouts | object pair, certified Date, Best Time, Direction, Separation, Equipment, labels/tips | dedicated per-aspect background + deterministic observation card. Reuse V9 prompt composition language, never V9 AI-rendered facts |
| PlanetGrouping | conjunction vocabulary allowed intentionally; grouping object line and `PLANET GROUPING` | up to four certified objects, certified observation fields | V7/V5 background + deterministic compact card |
| Constellation | no distinct V9 builder exists; falls through current-event/default or current `ResponsiveThumbnailAuthorityService` constellation poster | name/title; current shell additionally supports certified identification, bright stars and deep-sky facts | cinematic recognition background plus deterministic short title and at most a small certified identification/highlight block; not AI-complete guide |
| Solar/Lunar Eclipse | PureV3 eclipse branch and V9 `EclipseObservationGuidePromptBuilder` | title, certified date/time and solar-safety statements | dedicated geometry-aware explanatory/cinematic background; deterministic facts/safety only. Validate subtype and never infer safety text |
| MoonEvent | PureV3 moon branch, moon guide-card overlay, V9 Moon builder | phase/title, illumination/moonrise/moonset only when certified | background + deterministic compact phase/observation overlay; never ask AI to type numbers |
| generic fallback | PureV3 `CurrentEvent` vocabulary | title/type and certified objects only | fail closed if family/identity is insufficient; otherwise simple cinematic background + deterministic title |

Constellation's latest mature purpose-specific evidence is split: the old independent engine has only a generic cinematic/family fallback, while current shell tests encode strong deterministic Orion fact selection. The safe restoration is to use the old background-generation seam with the current certified constellation copy selector—not the Phase 8 raster selector or whole manual poster architecture.

## 5. RC1 versus V9, and the Meteor contradiction

They are distinct quality targets:

* V9 declares `imageSource=AzureImage2`, `cropMode=PerAspectGenerated`, empty Hero/scene sources, and a complete final thumbnail. It is later and appropriate as **composition inspiration**, not factual-pixel authority.
* RC1/V3/V5/V7 split visual generation from deterministic typography. This is the safer basis for facts and the repository's actual overlay machinery.
* Current pipeline HEAD combines neither: it bypasses both and runs the Phase-8-derived new authority renderer. The old API route still routes V9.

The asserted “last-good Meteor has no guide card” is supported by `BuildThumbnailV5AzurePrompts` language prohibiting embedded panels and by the desired RadiantBurst/two-line contract. However, existing `ThumbnailAssetIntelligenceServiceTests` also asserts that the Meteor prompt contains “guide card,” and `BuildPureV3ThumbnailPrompt` says V5 will add guide cards/metadata. That is contradictory repository evidence. Therefore the audit recommends the supplied visually approved baseline (RadiantBurst + two lines, no cards) and explicitly requires updating the contradictory legacy test when implementation begins; it does **not** falsely claim HEAD already implements that exact combination end-to-end.

The older `WriteMeteorThumbnailAsync` does draw radiant streaks but also renders viewing-window and moon-interference text, so it is not the exact desired baseline and should not be restored as-is.

## 6. Recommended Phase 12 baseline, dimensions, and text safety

Use a family planner adapted from `BuildAzureImage2ThumbnailV5VariantPrompts` (historical/requested name `BuildThumbnailV5AzurePrompts`), `BuildPureV3ThumbnailPrompt`, `ThumbnailGuideCardFactory`, `BuildRc1ThumbnailTextLines`, and V7 profile/compositor classes. Generate each requested aspect independently. Feed the resulting candidates into the current transaction shell.

| Stage | Landscape | Square | Portrait |
|---|---:|---:|---:|
| Azure request | 1792×1024 | repository V9 defaults landscape-like provider request; square has no true provider-native size recorded | 1024×1792 |
| optional mature design coordinate language | prompts sometimes say 3840×2160 | 1:1 native composition | 9:16 native composition |
| canonical published | **1280×720** | **1080×1080** | **1080×1920** |

Keep published sizes because current manifests, tests, responsive profiles, and publisher adapters recognize them. The `3840×2160` text is prompt/design intent, not a published contract. Replace V9's `ResizeMode.Stretch` normalization with aspect-preserving crop/resize and readback. A square provider request should use the configured provider's supported square size at implementation time rather than pretending 1792×1024 is square.

**Text policy:** AI generates only sky, objects, lighting, diagrammatic/background structure, and negative space. Code generates title, subtitle, Date, Best Time, Direction, Separation, Equipment, labels, callouts, safety statements and tips. An AI-complete poster may be retained only as a non-authoritative experiment; current validation proves prompt strings, not OCR-perfect factual pixels, so no family qualifies for production AI text today.

Minimum semantic authority is verified `ProductionEventIntelligence` plus certified Phase 2 facts for science/observation claims and Phase 4/6 editorial intent when copy needs it. Phase 7 narration is unnecessary. Phase 11 Hero and Phase 8 scenes are unnecessary. Record `promptSemanticInputs`, artifact paths/pointers/checksums, family, objects, certified temporal/direction fields, composition, aspect, and negative constraints.

## 7. Phase 12 shell to preserve and code disposition

### Preserve/reuse as-is

* `12-thumbnails` canonical root and output names.
* `ResponsiveThumbnailAuthorityService` staging cleanup, candidate readback, physical metadata/hash checks, atomic backup/swap, committed readback, publication report, canonical validation, reason codes, `downstreamReady`, and failure restoration.
* current applicability: Phase 12 only when Thumbnail is requested; partial execution need not request Hero.

### Reuse with adapter

* `IThumbnailAssetIntelligenceService` as the mature engine seam, changed later to accept a certified semantic request and return staged candidates rather than write stable authority.
* `ThumbnailV7ProfileResolver`, observation builder, composer, renderer and validator.
* current certified copy/fact selection in `ResponsiveThumbnailAuthorityService`, especially constellation handling, but not its raster sourcing.

### Reuse prompt logic only

* `BuildAzureImage2ThumbnailV5VariantPrompts` (historical/requested `BuildThumbnailV5AzurePrompts`), `BuildPureV3ThumbnailPrompt`, V9 family prompt builders, negative constraints, and `AllowsConjunctionVocabulary`.

### Reuse overlay logic only

* `WriteAzureImage2ThumbnailV5OverlayAsync` (historical/requested `WriteThumbnailV5OverlayAsync`), `BuildRc1ThumbnailTextLines`, `ThumbnailGuideCardFactory`, V7 overlay/safe-area measurement.

### Compatibility only

* `GenerateMeteorShowerThumbnailImagesAsync` procedural raster, V6 residues, V8-named options, `thumbnail-final.png`, old manifest/debug artifacts, API's four-subphase workflow.

### Remove from authority path

* Phase 11 → selected Phase 8 clean-raster requirement and checksum binding;
* `PosterThumbnailRenderer/3.0`, `ThumbnailPosterPolicy/3.0`, and full manual fact-card visual architecture;
* V9 AI-rendered factual text and stretch resize;
* silent Hero/scene/procedural fallback.

Utilities for typography, hashes, dimensions, staging and collision measurement remain reusable. Do not delete compatibility code in the restoration change unless a separate consumer audit proves it dead.

---

## 8. Exact current Phase 13 path

Current runner fixes `galleryRoot = OutputRoot/13-gallery`, requests `AstroPulseGalleryAspect.Square`, and calls the one DI binding `IAstroPulseGalleryService -> AstroPulseGalleryService`. At the first line of `GenerateGalleryAsync`, the service returns `Phase13GalleryAuthority.PublishAsync`; the former mature body remains below that return under `#pragma warning disable CS0162` and is unreachable.

```text
PhaseGenerateGalleryAsync
 -> AstroPulseGalleryService.GenerateGalleryAsync(13-gallery, Square)
 -> Phase13GalleryAuthority.PublishAsync
    -> hydrate certified Phase 2/4/6 semantics
    -> require Phase 10 scene certification
    -> select Phase 8/9 certified raster per role
    -> SafeSquareFocalCrop or ScientificContainOnSameSourceBackdrop
    -> deterministic 1080x1080 overlay
    -> stage six pages + manifest/guide/diagnostics/report
    -> candidate readback -> atomic swap -> committed readback
    -> validation/phase-13-validation.json
```

Current roles are new internal IDs (`cover-identity`, `what-happens`, `how-to-identify`, `when-to-observe`, `bright-stars-or-key-objects`, `observation-checklist`) and current provider-call count is exactly zero.

## 9. Exact mature `AstroPulseGalleryService` path

The retained branch in `Backend/src/Astronomy.MediaFactory.Rendering/AstroPulseGalleryService.cs` proves the mature model:

1. load `plan-input/production-event-intelligence.json` through `LoadGalleryContext`;
2. normalize family/localization and resolve `GalleryContentContract`;
3. `BuildTopics` returns six `GalleryTopic` objects;
4. loop topics and increment `azureCalls` once each;
5. call `GenerateBackgroundWithAzureImage2Async` with a unique role prompt/background path;
6. require provider success (no local fallback);
7. `RenderTopicAsync` center-crops to the requested aspect, grades, and calls deterministic `DrawOverlay`;
8. save six final pages, delete temporary backgrounds, hash and reject duplicate final hashes;
9. write manifest, review, prompt/composition/story/blueprint, localization/overlay/provider diagnostics, validation, and sibling observation guide.

The diagnostics records `azureCallsCount`; success requires six images, six unique final hashes and `azureCalls >= 6`. Thus one successful run performs **six successful Azure calls** (more attempts are not implemented here) and produces six independently generated backgrounds. Six different final hashes are enforced; background hashes are not retained, so the stronger statement `unique background hashes = 6` is intended but not actually validated.

The interface, result/aspect records and mature implementation share the same file. DI is scoped in `ServiceCollectionExtensions.cs`. There is no Gallery feature-flag router or alternate DI implementation. Production reachability changed only because `GenerateGalleryAsync` now immediately delegates to `Phase13GalleryAuthority`.

## 10. Six mature Gallery roles and prompt purposes

| # | Internal/public scene role | Prompt purpose / visual difference | deterministic overlay/source data |
|---:|---|---|---|
| 01 | `Opening view` / localized Opening view | strongest cinematic event relationship/hook, large primary subject | scene title/subtitle/detail; `01/06`; localized role; footer |
| 02 | `What happens` | conceptual explanation of apparent geometry/mechanism | resolved scene copy and certified explanation |
| 03 | `Where to look` | observer, horizon/location/sky-direction context | certified direction/location only; no inferred place |
| 04 | `When to observe` | observing-window, timing/progression context; may request explanatory eclipse geometry/progression | certified date/time/window only |
| 05 | `Key objects` | recognizable object close-up/identification; conjunction prompts require visible Jupiter bands and bright Venus disk | certified object names/details |
| 06 | `Viewing checklist` | memorable observation/safety/checklist scene | certified equipment/safety/tips only |

`BuildTopics` composes a shared Gallery V3 visual policy with event name/family/subtype, localized title, date/time/direction/window, resolved objects, visual/prompt hints, language and forbidden terms, then appends scene role, visual intent, `BuildPageSpecificTreatment`, required/forbidden objects, and repeated “no embedded text/labels/watermark.” `GalleryContentResolver` supplies the six `SceneContents`; localization maps the exact English labels above to Hindi equivalents.

For special explanatory pages, AI is intended to create the **visual explanation**—objects, apparent geometry, progression/diagram structure and editorial panels/icons if requested without text. Code must add all factual labels. This preserves eclipse progression capability while preventing an unlabeled AI diagram from becoming scientific authority: require prompt lineage, role-specific geometry validation, and deterministic labels; reject rather than silently reuse a scene.

## 11. Gallery dimensions and AI/overlay split

The aspect contract exposes Landscape 1920×1080, Square 1080×1080 and Portrait 1080×1920. Before the redesign, pipeline production explicitly invoked **Landscape only**. The provider request is 1792×1024 for landscape/square and 1024×1792 for portrait, after which `RenderTopicAsync` center-crops to the exact final size.

**Recommendation: restore canonical Phase 13 pages to 1920×1080 landscape.** This matches the prior production invocation and supplied reference description: full-frame 16:9, large role-specific subject, lower information overlay, `NN/06`, Drashyam Astronomy footer, no square containment/pillarbox. Square and portrait may remain callable legacy surfaces, but no downstream repository consumer proves they are canonical.

Azure generates the full background visual, objects, environment, visual explanation/geometry, and non-textual composition. `DrawOverlay` deterministically adds the localized role marker, wrapped title/subtitle/detail blocks, and footer. Mature prompts explicitly prohibit embedded text, labels and watermarks. Date/time are present in prompt context (and may influence the scene) and resolved scene copy; all displayed facts must be deterministic after restoration.

No reference image files were available to pixel-inspect. Structurally, the mature branch matches every described reference attribute except that its center crop has no subject-aware validation and its dark lower grade can be improved only within the existing overlay engine—not by introducing a third architecture.

## 12. Gallery semantic authority and observation guide

Do not restore `LoadGalleryContext`'s recursive `TryFindProperty`, generic fallback context, or hard-coded observation tips as authority. Add an adapter before `BuildTopics` that builds `GalleryContentContract` from verified `ProductionEventIntelligence`, certified Phase 2 knowledge, Phase 4 editorial intent and Phase 6 viewer-facing semantics. Every display field and prompt fact needs artifact path, JSON pointer, checksum and allowed transformation. Missing required role facts must fail or select a versioned family-appropriate role; never create filler.

The observation guide belongs **inside `13-gallery/observation-guide.json` as a supporting structured projection**, as current authority shell already does. It must contain only certified facts already available to pages and share manifest lineage. The former sibling `observation-guide/observation-guide-v2.json`, diagnostics and templated `BuildObservationGuideTips` are compatibility only; do not restore false tips.

Phase 8/9/10 images, Hero and Thumbnail have no role in restored Gallery raster authority. Phase 10 may remain an upstream pipeline phase for other products, but is not a Phase 13 dependency or prompt fact source.

## 13. Phase 13 shell to preserve and code disposition

### Preserve/reuse as-is

* Gallery applicability/request mapping, `13-gallery` ownership, exactly six candidate gate, semantic hydration from certified authority, physical metadata, staged readback, backup/swap/rollback, committed readback, checksum, diagnostics, report, validation, cleanup, reason-code mapping and `downstreamReady` in `Phase13GalleryAuthority`.
* `IAstroPulseGalleryService`, `AstroPulseGalleryAspect`, and `AstroPulseGalleryResult` compatibility surface.

### Reuse with adapter

* `GalleryContentResolver`, `BuildTopics`, localization, family profiles and event-object context, fed from certified typed inputs rather than recursive compatibility JSON.
* `GenerateBackgroundWithAzureImage2Async` behind injected/configured HTTP/provider infrastructure and transaction cancellation.

### Reuse prompt logic only

* `BuildPageSpecificTreatment`, role-specific visual intent, required/forbidden object and no-text constraints, visual prompt policy.

### Reuse overlay logic only

* `RenderTopicAsync`, cinematic grade, `DrawOverlay`, wrapping/font/localization/safe-padding logic, upgraded to measured bounds/readback on 1920×1080.

### Compatibility only

* old `GalleryArtifactManifest.json`, `page##-role.png`, prompt/review/story/blueprint diagnostics, callable Square/Portrait, and sibling observation-guide files.

### Remove from authority path

* Phase 10-certified source selection, Phase 8/9 raster reuse and source-role matching;
* `SafeSquareFocalCrop`, `ScientificContainOnSameSourceBackdrop`, same-source blurred backdrops, square black-bar/balance policy;
* 1080×1080 canonical output and the current Gallery scene-reuse visual-quality model.

Keep hash/readback/layout helpers where general. Replace the engine *inside* `Phase13GalleryAuthority.PublishAsync` or inject a mature candidate generator into it; do not add GalleryV4.

## 14. Provider and failure policy

| Product | dependency and request | success calls | retry/timeout/error behavior in mature code | restored policy |
|---|---|---:|---|---|
| Thumbnail V9/V5/V7 | configured `AzureOpenAIForImageOptions.Endpoint` + `ImageDeployment`, API `2024-10-21`, managed identity or key, `n=1`; 1792×1024 default / 1024×1792 portrait | one per requested aspect (normally 3) | V9 retries once per aspect, linked 300-second cancellation, accepts base64/URL; aggregates failures; clients use infinite HTTP timeout bounded by CTS | retain one retry only for classified transient failures; whole phase fails unless every requested aspect validates |
| mature Gallery | same Azure Image2 configuration, aspect request 1792×1024 or 1024×1792, `n=1` | exactly 6 | no explicit retry and no explicit HTTP timeout in retained method; throws on provider/content/response failure | add bounded configured timeout and transient retry at provider adapter, while preserving six successful unique candidates |

Both have six/three paid generations plus possible retries; exact currency cost cannot be derived from source/config and must be monitored by deployment pricing. Content-safety/provider HTTP failures are phase failures. Never silently fall back to Phase 8, Hero, square compositor, procedural image, generic background, or stale committed files. Transaction failure reports may describe the error, but the prior committed package remains authority and must not be labeled current-run success.

`overwriteExisting` should generate a complete candidate and commit only after validation. `retryFailedOnly` can retry provider work in a transaction journal, but cannot publish a mixture unless every candidate shares the same semantic/renderer identity and all are reread. Reuse is allowed only after committed manifest/checksum/physical/provider-policy identity validation.

## 15. Current versus target

### Phase 12

| Concern | current pipeline | last-good target |
|---|---|---|
| image source | Phase 11-selected clean Phase 8 raster | dedicated family/aspect Azure background |
| provider | zero | Azure Image2, normally one call/aspect |
| aspect | three deterministic derivative posters | independently composed backgrounds; same three published sizes |
| copy | current certified short/fact copy | retain certified selectors + mature family copy |
| overlay | manual `PosterThumbnailRenderer/3.0` fact card | RC1/V5/V7 family-specific deterministic overlay |
| family | strongest for Constellation, generic poster policy | explicit Meteor/conjunction/grouping/eclipse/moon/constellation/fallback policy |
| quality | derivative informational poster | purpose-built CTR visual |
| authority shell | transactional and strong | **retain** |

### Phase 13

| Concern | current pipeline | last-good target |
|---|---|---|
| image source/provider | Phase 8/9 via Phase 10; zero calls | six independent Azure role backgrounds; six calls |
| dimensions | 1080×1080 | **1920×1080** |
| page count | six | six |
| roles | certified new IDs | six mature public educational roles |
| overlay | deterministic bottom square card | deterministic lower information overlay + role counter/footer |
| semantic authority | certified Phase 2/4/6 | retain via adapter; remove recursive plan input |
| publication | transactional `13-gallery` | **retain** |
| quality | cropped/reused source carousel | purpose-built 16:9 visual per educational role |

## 16. Tests: preserve, reinterpret, and update

### Thumbnail tests to preserve/mine

* `ThumbnailAssetIntelligenceServiceTests.cs`: `GenerateThumbnailAssetsAsync_PlanetConjunctionCompositionUsesCompactObjectCopy`, `...PlanetGroupingCompositionUsesVisibleObjectsAndMotifCopy`, `...PlanetConjunctionImagesUseCompactThumbnailText`, `...PlanetConjunctionIntelligenceBuildsCleanVisualGuideProfile`, Hindi guide metadata, three aspect output, current-object filtering, V3 no-Hero requirement, and Meteor `RadiantBurstThumbnail` assertions.
* `ThumbnailV7CinematicOverlayRendererTests.cs`: per-aspect Azure-background paths, deterministic renderer identity, provider/overlay diagnostics and layout.
* `ThumbnailRendererTests.cs` / `PromptBuilderTests.cs`: guide-field formatting, localization and prompt contracts.
* `ResponsiveThumbnailAuthorityServiceTests.cs`: checksum agreement, certified copy, collision/fact safety, staging cleanup, transaction presence, current-run evidence and upstream preservation.

Update tests that require Phase 11/Phase 8 raster lineage, `PosterThumbnailRenderer/3.0`, manual fact cards, or V9 AI-complete posters. Resolve the Meteor test that currently expects “guide card” against the no-card approved contract. Add provider-stub tests for one call per aspect, background-only prompts, deterministic pixel text, no fallback, candidate/committed readback and rollback. No live provider tests are needed.

### Gallery tests to preserve/mine

* `AstroPulseGalleryServiceTests.cs`: exactly six topics/images, role labels and prompt uniqueness, Landscape 1920×1080, localization, event-family/object correctness, overlay safe padding, observation display/guide, manifest/diagnostics and six-call counters.
* `VisualQualityFrameworkTests.cs`: Gallery content contract and topic construction.
* `Phase13GallerySemanticAuthorityTests.cs` and `Phase13GalleryCopyDiversityTests.cs`: verified event identity, semantic claim selection, missing-timing/direction safety and diversity. Transactional/readback/cleanup assertions also live in the broader production pipeline test suite; there is no file named `Phase13GalleryAuthorityTests.cs` in HEAD.

Update tests requiring Square, Phase 10 sources, source-role scoring, SafeSquare/ScientificContain, reused-source counts, or zero providers. Add six stubbed provider calls, six unique background hashes (not merely final hashes), exact 1920×1080 readback, six role-specific prompt snapshots, no embedded text, deterministic overlay bounds/footer/counter, explanatory-role contract, and fail-closed retry tests.

## 17. Manifest, downstream, cleanup, and migration safety

1. Keep canonical Phase 12/13 roots, canonical manifest/report/validation locations and top-level consumer fields. Add renderer/provider/prompt-lineage fields version-tolerantly; do not rename established outputs in the first restoration.
2. Replace source-raster lineage arrays with semantic authority references. Preserve fields as empty/deprecated compatibility members if strict consumers deserialize them.
3. Keep exact physical path, width, height, media type, SHA-256, transaction ID, authority checksum, publication state and `downstreamReady` semantics.
4. Make phase ownership only `12-thumbnails`, `13-gallery`, and their validation files. Never clean Phase 8–11. Provider temporary files live only in staging.
5. For partial runs, hydrate certified semantic artifacts directly. Do not demand that Phase 8, 10 or 11 execute in the same request.
6. Phase 14+ and packaging consume manifests, not globbed legacy roots. If they require old filenames, write bounded post-commit compatibility projections derived from committed files and mark them non-authoritative.
7. `retryFailedOnly` and reuse must validate the full current semantic/prompt/renderer identity; `overwriteExisting` must never destroy previous authority before candidate success.

## 18. Minimal implementation plan and expected files (future work, not performed)

1. Define typed certified semantic adapters for Thumbnail and Gallery using existing Phase 2/4/6 hydration and `ProductionEventIntelligence` identity.
2. Extract/adapt the mature Thumbnail planner/generator as a candidate producer behind `ResponsiveThumbnailAuthorityService`; remove only raster-dependency calls from the active route.
3. Lock the event-family policy, especially Meteor no-card and Constellation compact-information decisions, with stubbed provider tests.
4. Move the retained six-topic Gallery candidate loop behind `Phase13GalleryAuthority`; change runner canonical aspect back to Landscape and remove Phase 10 source selection from the active graph.
5. Extend manifests/diagnostics with prompt semantic inputs/authority references/provider attempts and validate background/final uniqueness.
6. Run transaction, failure, partial execution, retry/reuse, publication and downstream compatibility suites.

Expected implementation changes are concentrated in:

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`;
* `.../ResponsiveThumbnailAuthorityService.cs`, `ThumbnailAssetIntelligenceService.cs`, `ThumbnailV7Engine.cs`, and possibly `Phase12ThumbnailRouter.cs`;
* `Backend/src/Astronomy.MediaFactory.Rendering/AstroPulseGalleryService.cs` and `Phase13GalleryAuthority.cs`;
* typed request/result contracts only if existing records cannot carry authority references;
* the corresponding Thumbnail/Gallery authority and mature-engine test files.

No Phase 1–11 file, DI registration, database, generated artifact, or new V10/GalleryV4 implementation is required.

## 19. Risks

1. **Scientific geometry:** generated skies/diagrams are visual interpretation, not positional authority. Prompts and overlays must not certify generated geometry.
2. **Text leakage:** Azure may ignore “no text”; use OCR/text-like artifact checks where practical and reject conspicuous generated labels.
3. **Meteor contract ambiguity:** code/tests conflict on guide cards; obtain the actual approved output/hash or product sign-off before implementation.
4. **Provider size/crop:** landscape provider ratio is not exact 16:9 and square currently requests landscape; subject-aware aspect composition/readback is required.
5. **Cost/latency:** three Thumbnail plus six Gallery successes, with retries, materially changes the zero-provider runtime.
6. **Content safety/outage:** fail-closed behavior lowers availability by design; prior authority must remain available but not masquerade as current execution.
7. **Consumer schema drift:** manifests currently encode Phase 8/10 source fields; additive/deprecated migration is safer than abrupt deletion.
8. **Old semantic reconstruction:** calling retained mature methods without the adapter would restore unsafe defaults/hard-coded tips.
9. **Visual approval evidence:** no supplied reference files, golden hashes, or approval record exists in the checkout.

## 20. Final recommendation and remaining uncertainties

**GO for a restoration implementation after the Meteor policy and provider budget are approved.** The target architecture is supported by repository evidence:

```text
Phase 12
certified semantic authority
 -> existing family-specific mature planner
 -> dedicated Azure background per requested aspect
 -> deterministic factual overlay
 -> current candidate validation + transactional 12-thumbnails publication

Phase 13
certified semantic authority
 -> existing six mature GalleryTopic prompts
 -> six dedicated Azure backgrounds/compositions
 -> existing deterministic public overlay
 -> current candidate validation + transactional 13-gallery publication
```

This answers the restoration question without raster reuse or a parallel architecture: Meteor uses RadiantBurst/two-line RC1 logic; conjunction/grouping use the mature observation-card planner with deterministic facts; eclipse/moon use their mature family prompts with deterministic facts; Constellation combines the independent cinematic background seam with current certified compact copy; generic events fail closed when identity is inadequate. Gallery uses the retained `BuildTopics` → `GenerateBackgroundWithAzureImage2Async` → `RenderTopicAsync` loop for all six 1920×1080 roles, wrapped—not replaced—by `Phase13GalleryAuthority` governance.

Remaining decisions requiring evidence outside this checkout are: the actual last human-approved Meteor and conjunction files/hashes; whether any AI-complete V9 poster ever passed OCR/factual review; a supported square Azure request size; provider retry/cost SLOs; whether external clients require old Gallery/observation-guide filenames; and pixel comparison against the user's reference images when those files are made available.

## Audit method

Read-only repository work used `find` for `AGENTS.md`, `rg --files`, focused `rg -n` symbol/test/route/provider searches, `sed` source reads, and `git log`/`git show` history inspection. Builds/tests are validation of this Markdown-only change and do not call image providers.
