# Phase 12 Thumbnail New-Architecture Audit

**Audit date:** 2026-08-08
**Scope:** repository evidence only, plus current official publisher documentation linked below.
**Constraint:** audit only. No production code, tests, configuration, contracts, DI, database, or generated media were changed.

## Executive decision

**Current state: NO-GO. Target design: GO for a separate implementation only after the contracts and consumer migration below are approved.** The current production route is unambiguously **V9**, despite methods and flags named V8. It makes **three Azure Image2 generation calls in the success case** (one independently generated complete raster for landscape, portrait, and square), retries each once, bakes text into AI output, then uses a stretching resize when the provider dimensions differ. It consumes neither the frozen `11-hero/hero-asset-manifest.json` nor the Phase 10 certification and records empty Hero/scene lineage. This is incompatible with the frozen architecture, deterministic scientific preservation, and no-blind-resize requirements.

The recommended architecture is **A: reuse the three committed Phase 11 responsive variants, then add deterministic, thumbnail-specific editorial composition**. Phase 12 Azure image-generation calls must be **exactly zero**. Phase 11 is the visual authority; Phase 10 is a mandatory lineage gate. Phase 12 must never reselect, regenerate, move, reconstruct, or repair astronomy geometry.

Canonical package:

```text
12-thumbnails/
  thumbnail-landscape.png   # 1280x720, 16:9
  thumbnail-square.png      # 1080x1080, 1:1
  thumbnail-portrait.png    # 1080x1920, 9:16
  thumbnail-asset-manifest.json
  phase12-authority-diagnostics.json
  phase12-publication-report.json
validation/
  phase-12-validation.json  # canonical cross-phase validation result
```

This root is recommended only with an explicit consumer adapter/migration because present publishers do **not** consume these names: `PlatformThumbnailResolver` expects `thumbnails/thumbnail-long.jpg` and `thumbnails/thumbnail-short.jpg` and otherwise enters legacy fallback. The old `thumbnails/` files may temporarily be materialized as compatibility projections, never as semantic authority.

---

## 1. Current Phase 12 registry entry

`ProductionPipelineExecutionService.PhaseDefinitions()` registers `(12, "Generate Thumbnails", PhaseGenerateThumbnailsAsync)`. Phase 12 is selected only when `RequestedOutputs` explicitly contains `Thumbnail`; `ShortVideo`, `LongVideo`, and `HeroAsset` do not imply it.

**Recommended applicability:** `Thumbnail` alone makes Phase 12 applicable. When applicable, Phase 12 requires an already committed, downstream-ready `11-hero` authority regardless of whether `HeroAsset` remains in persisted requested outputs. Thus Orion needs only `startPhaseNo=12`, `endPhaseNo=12` and no output override when `Thumbnail` is already persisted. Dependency is artifact-based, not applicability-based.

## 2. Exact call graph

| Call | Input | Reads | Writes / output | Provider, validation, reuse |
|---|---|---|---|---|
| Registry → `PhaseGenerateThumbnailsAsync` | `ProductionPhaseContext` | request, `ProductionEventIntelligence`, execution paths/options | list of generated paths | loops four string phases; catches and writes V9 diagnostics/validation |
| Orchestrator → `IThumbnailAssetIntelligenceService.GenerateThumbnailAssetsAsync` | `ThumbnailAssetGenerationRequest` with event/region/language, `ProductionContext`, overwrite, V8=true, `ScrollStopping`, `PhotoCinematic` | request | `ThumbnailAssetGenerationResponse` | dispatches by phase string |
| `Intelligence` → `GenerateThumbnailIntelligenceAsync` | same | active production intelligence; legacy no-context route loads Hero story | `thumbnail-intelligence.json` | no image provider; `File.Exists` reuse |
| `Composition` → `GenerateThumbnailCompositionModelAsync` | same | thumbnail intelligence; legacy no-context additionally loads `hero-assets` manifests | `thumbnail-composition-model.json` | V9 model when router enabled; `File.Exists` reuse |
| `SceneSelection` → `GenerateThumbnailSceneManifestAsync` | same | production intelligence; no real upstream visual manifest in V9 | `thumbnail-scene-manifest.json` | V9 creates three synthetic output entries; `File.Exists` reuse |
| `Images` → router → `GenerateThumbnailV8AiNativeImagesAsync` | same plus prompt contracts | production intelligence/request-derived event lock | three prompt TXT files, three PNGs, `thumbnail-manifest.json`, diagnostics, `phase-12-validation.json` | 3 Azure calls normally, up to 6 with retry; no fallback; validators described below |
| outer `ValidateThumbnailV8Contract` | thumbnail root | diagnostics, validation, files | none | guards V9 identity/presence and absence of V7 artifacts; not full physical authority validation |

`GenerateThumbnailAssetsAsync` accepts only Intelligence, Composition, SceneSelection, or Images and rejects other values. Responses expose requested/executed phase, success, primary artifact, readiness, generated files, warnings, renderer routing fields, and layout-validation path.

## 3. Four internal subphases

| Subphase | Current purpose and semantic sources | AI | Artifact | New-architecture disposition |
|---|---|---:|---|---|
| Intelligence | Builds hook, alternatives/scores, guide card, platform targets and avoid-list. Production route uses `ProductionEventIntelligence`; legacy route loads `hero-assets/hero-asset-story.json`, ultimately reconstructed from question-era fields. | No image call; deterministic scoring in this service | `thumbnail-intelligence.json` | **ADAPT/MERGE** into candidate preparation; copy must come only from frozen editorial identity and allowable shortening |
| Composition | Builds layout family, blocks, platform variants, copy. V9 says AI-complete observation guide. | No call | `thumbnail-composition-model.json` | **ADAPT** mature per-aspect layout ideas, but deterministic overlays only; fold into manifest/diagnostics |
| SceneSelection | V9 does not select a scene; it declares its own future outputs as primary/secondary/support, with empty Hero and scene sources. | No call | `thumbnail-scene-manifest.json` | **REMOVE from authority path**; replace with direct one-to-one Hero-variant binding |
| Images | Builds family/aspect prompt, calls Azure once per aspect (retry once), writes complete AI thumbnail and stretches to target. | **3 success calls; max 6 attempts** | PNGs, prompts, manifest, validation, diagnostics | **REPLACE authority behavior** with deterministic editorial compositor over Phase 11 images |

Sequential direct writes mean Intelligence, Composition, and SceneSelection can remain after Images fails. Old outputs can coexist with new partial JSON. There is no package transaction.

## 4. Active production version and naming mismatch

`phase12ThumbnailV8DefaultEnabled` and `IsThumbnailV8Enabled` are misleading compatibility names. Both outer service and `Phase12ThumbnailRouter` default true; router aliases V8 to `IsThumbnailV9Enabled`, prints V9 selected, and routes only to `GenerateThumbnailV8AiNativeImagesAsync`, whose renderer identity is `ThumbnailV9AiFinalThumbnailComposer`. The router throws rather than fall back. Therefore **active production is V9**; V8 is a flag/method alias, not a distinct active renderer.

## 5. Version routing matrix

| Version | Entry path | Renderer | Authority/source | Production status | Retain? | Retire from authority? |
|---|---|---|---|---|---:|---:|
| V3 | unrouted legacy/PureV3 methods | procedural/local `PureV3` and `PhotoCinematic` branches | request/event intelligence; sometimes no Hero requirement | unreachable under default router; compatibility/tests | No, except isolated utilities proven useful | Yes |
| V5 | legacy guide variants | `AzureImage2ThumbnailV5Variants` / V3 overlay naming | generated background/event reconstruction | compatibility residue | No | Yes |
| V6 | outer fallback validator | CTR V6 contract/legacy renderers | legacy files | unreachable because V9 default cannot be disabled through router | No | Yes |
| V7 | supplied router delegate but router never selects it | `ThumbnailV7CinematicOverlayRenderer` | per-variant Azure backgrounds + deterministic observation overlay | compatibility/test-only; actively forbidden in V9 output | retain compositor techniques only | Yes |
| V8 | config and method alias | none distinct in production | aliases V9 | naming shim | only until callers migrate | Yes as semantic version |
| V9 | `IsThumbnailV9Enabled` → `GenerateThumbnailV8AiNativeImagesAsync` | `ThumbnailV9AiFinalThumbnailComposer` | three AI-complete rasters from request/intelligence prompts | **active production** | retain prompt diagnostics ideas only | **Yes; replace authority behavior** |

There is no hidden successful fallback in the current V9 router: failures propagate. The hidden risk is older direct service routes and downstream publisher fallbacks, not V9 internally falling back.

## 6. Applicability

Exact rule is case-insensitive explicit membership: phase 12 iff `RequestedOutputs` contains `Thumbnail`. Short and long video do not imply Thumbnail. Hero does not imply Thumbnail. Keep this deterministic rule. Do not require a `HeroAsset` request override: require the committed artifact when Thumbnail runs.

## 7–12. Input authority classification and frozen lineage

| Input/path | Current V9 use | Classification today | Required future status |
|---|---|---|---|
| `ProductionEventIntelligence` / production request | direct, extensive | **AUTHORITATIVE for current runtime**, but insufficiently frozen for visual lineage | event identity only; bind checksum/identity |
| `question-engine/` | indirect only through legacy Hero story/no-context helpers; not read by active V9 production image path | **LEGACY / UNSAFE RECONSTRUCTION** | forbidden semantic authority |
| `hero-assets/hero-asset-story.json`, Hero scene/composition files | active only when `ProductionContext` is null; `LoadHeroStoryAsync`, hook/copy derivation | **LEGACY / COMPATIBILITY** | compatibility only, never authority |
| `hero/` (`ExecutionContext.HeroRoot`) | no V9 authority read | **LEGACY compatibility** | forbidden authority |
| `11-hero/hero-asset-manifest.json` and three Hero PNGs | **not read** | frozen upstream **AUTHORITATIVE but currently ignored** | primary responsive visual authority |
| `10-scene-validation/scene-asset-certification.json` | **not read** | frozen certification **AUTHORITATIVE but currently ignored** | mandatory lineage gate (option C) |
| `08-scene-assets/scene-asset-manifest.json` | not read | inherited frozen authority | verify through Phase 11→10→8 lineage; do not reselect by default |
| `09-long-scenes/` | not read | unrelated/diagnostic for Phase 12 | no direct dependency |
| `04-blueprint/`, `06-story-frames/` | no direct V9 filesystem read | upstream editorial authority | access only through a certified copy identity/manifest reference, not reconstruction |
| `scene-approval-v3/` | no active V9 read | legacy | forbidden authority |
| options/Azure endpoint/deployment | active | **CONFIGURATION** | Azure configuration irrelevant in new Phase 12 |

**Recommendation: C.** Require both Phase 11 and Phase 10. Use Phase 11 variants as the images and Phase 10 only as an immutable lineage/certification gate. Validate `publicationCommitted`, committed readback, checksum, and `downstreamReady`; follow the Hero manifest’s source references/checksums to Phase 10/8. Never rewrite upstream.

## 13. Current source-image strategy

Active V9 uses no certified scene, Hero, collage, smart crop, or `VisualSourceResolver`. It asks Azure Image2 to create a complete event-specific raster separately per aspect. Source selection is prompt-family selection from request/intelligence, not image selection. Cost is three paid generations (up to six attempts); lineage is weak/empty; geometry and text fidelity risk are high; appearance can be cinematic but the prompt explicitly asks for integrated infographic UI/observation cards. Provider failure has retry, then aggregate failure—no local image fallback.

Legacy routes include V7 per-aspect Azure backgrounds plus deterministic overlays, V5/V6 guide layouts, procedural astronomy and photo-cinematic/approved-scene-style methods. They are not active V9 and must not become fallback authority.

## 14. Azure/provider calls

`GenerateThumbnailV8AiNativeImagesAsync` builds three contracts and loops them. `GenerateThumbnailWithAzureImage2Async` posts to the configured Azure OpenAI image deployment at API version `2024-10-21`, `n=1`. It requests provider sizes associated with aspect generation, accepts base64 or URL bytes, waits up to 300 seconds, and retries once. The deployment/model name is configuration (`AzureOpenAIForImageOptions.ImageDeployment`), not hard-coded.

The prompt tells the model to render title, subtitle, labels, values, icons, panels, callouts, and footer **inside** the raster. Aspect outputs are landscape 1280×720, square 1080×1080, portrait 1080×1920. Any mismatched decoded result is resized with `ResizeMode.Stretch`, violating geometry preservation. Firm future policy: **0 Azure image-generation calls, without exception or hidden opt-in.** Repository evidence shows cost and risk, not a material authority-safe advantage.

## 15–17. Prompt builders, event families, and text authority

| Family | Builder | Current visual/text approach | Scientific constraints and risks |
|---|---|---|---|
| Planetary/default | `PlanetaryObservationGuidePromptBuilder` | large event objects, integrated guide/card, title and observation facts | says no extra objects and circular planets; AI may still invent position/count/text |
| Meteor | `MeteorObservationGuidePromptBuilder` | meteor/radiant observation guide | event-derived objects and tips; AI can invent radiant/sky geometry |
| Moon | `MoonObservationGuidePromptBuilder` | Moon phase/guide | prompts geometry language; AI can render wrong phase |
| Eclipse | `EclipseObservationGuidePromptBuilder` | eclipse visual, safety/observation text | includes solar-safety rules; AI can generate wrong geometry or unsafe/garbled text |

All use `ThumbnailPromptBuilder`, aspect-specific contracts, current event lock and `ProductionEventIntelligence`. Negative rules prohibit random/extra objects, watermarks, branding, location, technical identifiers, cropping, distortion, and clutter, but prompt constraints are not physical certification.

Hard-coded terms such as Moon, Sun, Earth shadow, Naked Eye, eclipse safety, east/west defaults, and family badges are production fallbacks, not merely examples. The code also contains old Jupiter/Venus-specific validation/rendering facts and Orion-sensitive object extraction. These may be valid only when certified event identity supplies them; generic inference/default directions are unsafe.

Current headline/title comes primarily from `HeroTitle`, `ShortTitle`, `Title`, and request/current event lock in V9. Observation secondary/micro fields come from intelligence fields and guide-card builders. Legacy `LoadHeroStoryAsync`, `BuildThumbnailHookScores`, `DeriveSecondaryThumbnailText`, and `DeriveMicroThumbnailText` are active for Intelligence when running without production context, not active in the normal V9 production context. They reconstruct HeroHook/HeroAction/What/When and must be removed from the authoritative route.

**Future copy authority:** an explicitly identified frozen editorial title/short title (prefer Phase 11 manifest’s accepted title/copy identity, otherwise Phase 4 certified editorial identity). Allowed transformations: whitespace/case normalization, deterministic shortening without adding nouns/numbers/claims, line breaking, and omission. Event badge may repeat a certified event-family label. No generative hook, new What/Where/When/Why, inferred direction/date, or unverified AI text.

## 18. Scene selection

The current V9 `thumbnail-scene-manifest.json` is misnamed: its three entries point to Phase 12’s own output PNGs, `SourceHeroAssets` and `SourceSceneAssets` are empty, and its reason says Azure independently generates each ratio. It selects no upstream scene. Under the new design, eliminate raw scene selection: bind landscape→Hero landscape, square→Hero square, portrait→Hero portrait. A different raw Phase 8 scene is allowed only in a future separately certified upstream Hero authority, not by Phase 12.

## 19. Current and recommended dimensions

| Variant/file | Current pixels | Ratio | Actual/intended use | Recommendation |
|---|---:|---:|---|---|
| `thumbnail-landscape.png` | 1280×720 | 16:9 | long-form/YouTube-style | **keep 1280×720**; canonical PNG, publication adapter may emit ≤2 MB JPG |
| `thumbnail-square.png` | 1080×1080 | 1:1 | square social/feed | **keep 1080×1080** |
| `thumbnail-portrait.png` | 1080×1920 | 9:16 | Shorts/Reels/vertical | **keep 1080×1920** |
| `thumbnail-final.png` | effectively landscape alias in older paths | 16:9 | compatibility | not authoritative; retire/project only if a consumer proves necessary |
| publisher `thumbnail-long.jpg` | consumer-defined, dimensions only decoded | unspecified in resolver | YouTube/Facebook long | compatibility publication projection from landscape |
| publisher `thumbnail-short.jpg` | consumer-defined | unspecified in resolver | YouTube Short/Reel | compatibility publication projection from portrait, subject to platform capability |

These dimensions are not copied blindly from Phase 11: landscape follows YouTube’s recommended 1280×720 16:9 thumbnail guidance and current code’s 2 MB gate; square uses the established 1080-pixel social canvas; portrait uses the 1080×1920 9:16 vertical canvas. Official references checked: [YouTube custom-thumbnail guidance](https://support.google.com/youtube/answer/72431?hl=en) and [YouTube Data API thumbnail upload](https://developers.google.com/youtube/v3/docs/thumbnails/set). The repo supports JPG/PNG and compresses oversized YouTube thumbnails. Platform policies can change; re-verify at implementation/certification time. The Meta URL was inaccessible from this environment, so the exact Meta API acceptance rule remains a testable publication-adapter concern, not a reason to weaken the canonical variants.

Each variant needs its own crop/framing plan, safe area, protected scientific region, font size, wrapping, overlays and margins. Never stretch. If Phase 11 composition is already responsive and certified, use a protected crop/window or overlays only; fail rather than damage the subject.

## 20–21. Current physical outputs and artifact classification

Current root artifacts observed in code:

| Artifact | Classification / future disposition |
|---|---|
| `thumbnail-landscape.png`, `thumbnail-square.png`, `thumbnail-portrait.png` | current active outputs; become canonical only under new manifest/readback |
| `thumbnail-final.png` | legacy compatibility alias; remove from authority |
| `thumbnail-manifest.json` | active V9 scene-manifest-shaped file; **insufficient**, adapt/replace with `thumbnail-asset-manifest.json` |
| `thumbnail-scene-manifest.json` | orchestration-required but semantically synthetic/redundant in V9; compatibility only |
| `thumbnail-intelligence.json` | intermediate/useful diagnostics; merge or diagnostic only |
| `thumbnail-composition-model.json` | intermediate/useful layout diagnostics; merge or diagnostic only |
| `thumbnail-layout-validation.json` | legacy/V7 validation evidence; diagnostic only |
| `phase-12-validation.json` under thumbnail root/debug | current active gate but not canonical location/contract; compatibility; canonical copy must be `validation/phase-12-validation.json` |
| `thumbnail-v9-diagnostics.json` | useful routing/provider diagnostics; replace by authority diagnostics |
| `thumbnail-generation-diagnostics.json`, `thumbnail-review.json` | legacy diagnostics/review |
| `thumbnail-prompt.json`, `thumbnail-prompt-contract.json`, `visual-prompt-diagnostics.json`, `PromptAssemblyReport.json` | prompt evidence; unnecessary with zero-provider policy |
| three `thumbnail-*-prompt.txt`, `thumbnail-prompt-diff.md` | prompt/debug evidence; obsolete in authority package |
| `thumbnail-composition-profile.json`, `thumbnail-storytelling-strategy.json`, `visual-directing-profile.json`, `thumbnail-formatted-guide-card.json` | optional debug-only; most writes are disabled; redundant as authority |
| V7 backgrounds and generic `landscape.png`/`portrait.png`/`square.png` | forbidden compatibility residue in V9; remove from authority path |

The existing `ThumbnailSceneManifestDto` records event/plan/title, three scene entries, source arrays, generated paths, background, and string validation facts. It lacks execution/language, upstream authority checksums, per-file decoded format/bytes/checksum, protected regions, safe-area evidence, publication transaction/readback, deterministic authority checksum, and `downstreamReady`. It should not be stretched into the new contract if doing so preserves its false “scene selection” semantics; create a focused thumbnail authority DTO in the Phase 12 implementation layer.

## 22–25. Root, cleanup, reuse, and publication

**Current root:** `BuildProductionExecutionContext` assigns `<planRoot>/thumbnails`. `PhaseOutputTargetResolver` gives Phase 12 deletion ownership of exactly `ExecutionContext.ThumbnailRoot`; it does not list 11-hero, 10, 8, hero, gallery, or scene roots for phase 12. For a 12-only overwrite, current registry therefore does not delete upstream. However it deletes the whole legacy thumbnail root, and current direct writes have no transaction.

**Recommended root:** `<planRoot>/12-thumbnails`, with only that directory owned/deletable by Phase 12. Keep `validation/phase-12-validation.json` as the sole canonical validation artifact outside it under the established validation ownership mechanism. Compatibility projection to `thumbnails/` must be performed by a downstream adapter/publishing package, not treated as Phase 12 semantic authority. Phase 12 may read but never delete/write/regenerate Phases 1–11.

**Current reuse:** several stages return existing parseable files when `overwriteExisting=false`; image paths can be reused based on existence plus portions of old validation. There is no binding to Hero/certification checksum, source image bytes, copy identity, renderer/layout version, dimensions, output checksum, or committed state. This permits stale mixed packages. `retryFailedOnly` operates at phase-result orchestration level, not authority identity.

**Current transaction:** none. Files are written directly into stable `thumbnails/`; prompt and individual image writes occur incrementally. A failed later aspect or Images phase leaves partial/stale artifacts. No staging, candidate readback, atomic directory swap, backup/rollback, or committed readback exists.

**Required publication:** create a sibling staging directory; copy/decode source variants; render all variants; build manifest; validate candidate and physically read it back; fsync/close; atomically swap the whole `12-thumbnails` directory (with recoverable backup); read back manifest and every committed file; only then write publication report and set `publicationCommitted`, `committedReadbackPassed`, and `downstreamReady`. On any failure, discard candidate and preserve the prior committed package. Canonical deterministic checksum must hash canonical manifest content excluding its own checksum plus ordered physical file checksums.

## 26–27. Current validation and result propagation

Active V9 validation layers are prompt-token/rule diagnostics, `ValidateThumbnailV8Outputs`, service-written manifest/semantic validation, outer `EnsureThumbnailV8Phase12ValidationAsync`, `ValidateThumbnailV8Contract`, and special pipeline-success parsing. They strongly check V9 renderer identity, expected three PNG names/existence, prompt metadata and absence of V7-named artifacts. Some code decodes/resizes outputs and checks target dimensions.

They do **not** provide authoritative certification of all of: format signature, size limit for each target, stored physical SHA-256, upstream manifest/checksum lineage, stale identity, protected scientific region, subject visibility, embedded forbidden/garbled AI text, committed-state readback, deterministic authority checksum, or transaction status. Safe area/contrast/text/overflow/CTR fields are substantially declared diagnostics rather than robust pixel/semantic proofs. Scientific correctness cannot be certified for AI-created geometry.

Current `phase-12-validation.json` may live in root or debug and is produced both by the service and outer “ensure” logic. The 12-only success gate accepts `validationPassed` **or** `semanticValidationPassed`, optional/`Succeeded` status, V9 renderer/version, and mere existence of three outputs. The requested new fields—`authorityChecksum`, `publicationCommitted`, `validationStatus`, `checksumValidationPassed`, `manifestValidationPassed`, `committedStateValidationPassed`, `downstreamReady`—are not a complete mandatory propagated contract. This is a critical certification gap.

Existing diagnostic fields such as version, renderer, layout, output paths, safe-area, overlay/visual percentage, portrait limit, overflow, badges and legacy-execution guards are populated across the service’s anonymous validation/diagnostic objects and outer ensure methods. Retain only renderer/layout versions, per-variant output facts, safe-area/overflow, subject/protected-region results and explicit legacy/provider-call guards. Remove V6/V7 execution and observation-card percentage fields from authoritative truth; they can remain temporary compatibility telemetry.

## 28–31. Downstream consumers

`PlatformThumbnailResolver` is the clearest active consumer. It looks for:

* long: `<output>/thumbnails/thumbnail-long.jpg`;
* short/reel: `<output>/thumbnails/thumbnail-short.jpg`;
* long fallback: root or thumbnails `thumbnail-1.(png|jpg|jpeg)`;
* short fallback: several `shorts/` legacy paths.

For YouTube Shorts it deliberately refuses fallback if the generated short thumbnail is invalid. For other cases it logs and enters legacy fallback. It validates file existence, non-empty bytes, JPG/PNG extension, decodability/dimensions, and YouTube ≤2 MB—not exact aspect/dimensions/checksum/manifest.

`YouTubePublishService` consumes request-resolved platform thumbnail paths, uploads supported thumbnail formats, records MIME/dimensions/bytes, retries upload failures according to policy, and contains automatic compression for >2 MB (covered by tests). Long maps to the resolver’s long JPG; Shorts maps to short JPG when custom-thumbnail upload is enabled/supported. The repository does not currently map Phase 12’s three PNG names to either.

Facebook long-video publishing consumes `PlatformThumbnailPath`; Meta/Reels flow also goes through resolved thumbnail paths. There is no repository evidence that `thumbnail-square.png` is directly consumed by Instagram/Facebook publishing. Therefore square remains a valuable canonical social/feed asset but requires explicit Phase 20 mapping; it must not be conflated with Hero or Gallery.

**Migration requirement:** add one manifest-aware resolver/adapter that reads only committed/downstream-ready `12-thumbnails/thumbnail-asset-manifest.json`, maps long→landscape and short/reel→portrait, and maps square only for consumers declaring square. It may encode deterministic JPG compatibility copies with checksums. Disable all legacy fallback as a success condition when new authority exists; missing/invalid authority must fail publication readiness transparently.

## 32–34. Scientific, cinematic, and hard-coded risks

* **Scientific:** V9 regenerates geometry with AI, may add/remove/move objects, render wrong phases/counts, misspell labels, and has no source lineage. Stretch normalization can distort circles and separations. Observation overlay/crop can hide scientific relationships.
* **Cinematic:** V9 requests premium cinematic atmosphere but also “integrated polished infographic UI,” observation cards and many fields. Older V5/V7 routes explicitly emphasize guide cards/observation infographic and procedural drawings. These can regress from Cinematic/HybridCinematic authority into flat guide cards.
* **Hard-coded:** default east/west cues, Naked Eye/equipment, family labels, inferred Moon/eclipses and object fallback names can become claims without certified input. Location/date token removal is useful, but lexical filtering is not semantic validation.

Future per-variant manifest must record `sourceStyle` and require `Cinematic` or `HybridCinematic`, `geometrySensitive`, protected subject/scientific rectangles or masks inherited from Hero, crop transform, overlay exclusion regions, `scientificPreservationStatus` (`Preserved` only after deterministic checks), and human/machine review evidence. If preservation cannot be proven, fail; do not reconstruct or generate.

## 35–42. Recommended architecture and authority contract

### Hero → Thumbnail relationship

1. Validate Phase 11 manifest and Phase 10 lineage/certification.
2. Bind each target to its matching responsive Hero; never use one Hero for all ratios.
3. Apply a bounded, non-generative editorial transform: optional stronger focal crop only within Hero-declared safe crop/protected region, localized deterministic short copy, increased contrast, platform-safe placement, and a certified event-family badge if useful.
4. Preserve pixels/geometry inside protected scientific regions; never move, redraw, relabel or AI-regenerate objects.
5. Require output perceptual/pixel difference from Hero due to presentation value, while semantic identity remains identical.

### Manifest minimum

Top level: schema/version, planId, executionId, eventId, language, created UTC, renderer/layout versions, copy-authority path/type/checksum, Phase 11 manifest path/authority checksum, Phase 10 certification path/checksum, inherited Phase 8 lineage, provider policy/call count (must be zero), variants, publication state, candidate/committed readback results, canonical deterministic checksum, `downstreamReady`.

Per variant: role, canonical relative path, source Hero role/path/physical SHA-256, inherited source scene IDs/checksums, source style, crop/framing transform, protected regions, text/safe/platform margins, font/wrap/layout identity, decoded width/height/aspect, detected format/MIME, byte length, physical SHA-256, text-safe/subject-visible/scientific-preservation/no-forbidden-text results, validation state.

### Exact validation

Physically decode every candidate and committed output and verify PNG signature/format, exact dimensions and rational ratio, nonzero and platform-bounded byte length, recomputed SHA-256, source checksums/identity, all three requested roles, safe areas, no clipping/overflow, subject visibility, protected scientific region, permitted text only, no stale identity, no duplicate/alias output, cinematic style inheritance, no legacy authority, zero provider calls, manifest deterministic checksum, publication/readback flags and `downstreamReady`.

### Provider policy

**Azure image calls = 0, unconditional.** No emergency or family-specific fallback. Provider configuration must not influence Phase 12. A future policy change requires a new certified authority decision outside this implementation.

### Failure policy/codes

| Condition | Required result |
|---|---|
| Phase 11 missing/invalid/not committed/not ready | `P12_HERO_AUTHORITY_MISSING` / `P12_HERO_AUTHORITY_INVALID`; fail without repair |
| Phase 10 invalid or lineage mismatch | `P12_SCENE_CERTIFICATION_INVALID` / `P12_SOURCE_LINEAGE_MISMATCH` |
| source missing/checksum mismatch | `P12_SOURCE_IMAGE_MISSING` / `P12_SOURCE_CHECKSUM_MISMATCH` |
| copy absent/unverifiable | `P12_COPY_AUTHORITY_MISSING` |
| overflow/safe-area failure | `P12_LAYOUT_OVERFLOW` / `P12_TEXT_SAFE_AREA_FAILED` |
| subject/protected science loss | `P12_SUBJECT_VISIBILITY_FAILED` / `P12_SCIENTIFIC_REGION_NOT_PRESERVED` |
| decode/dimension/format/size/checksum failure | `P12_PHYSICAL_VALIDATION_FAILED` with precise fact |
| renderer failure | `P12_RENDER_FAILED` |
| candidate/readback failure | `P12_CANDIDATE_READBACK_FAILED` |
| atomic publication failure | `P12_PUBLICATION_FAILED`; preserve old authority |
| committed readback failure | `P12_COMMITTED_READBACK_FAILED`; `downstreamReady=false`, rollback |
| legacy authority/provider call observed | `P12_FORBIDDEN_AUTHORITY_PATH` / `P12_PROVIDER_POLICY_VIOLATION` |

## 43–44. Reuse contract

Reuse only a committed package whose manifest validates and whose identity includes Phase 11 authority checksum, each source Hero checksum, Phase 10 checksum, copy checksum, renderer/layout/template version, exact target profiles, every recomputed output checksum, manifest deterministic checksum, publication committed and committed readback. `File.Exists` alone is never reuse. `overwriteExisting=true` replaces only Phase 12 atomically. `retryFailedOnly` may reuse a fully valid prior package; otherwise rebuild candidate from immutable inputs.

## 45. Existing tests

The principal inventory follows; parameterized/adjacent cases are grouped where they prove the same legacy contract.

| Test file / tests | What they prove | Status / action |
|---|---|---|
| `ThumbnailAssetIntelligenceServiceTests`: Intelligence write/dry run, composition reuse/family copy, localized guide metadata, SceneSelection-only, three-image/validation output, object filtering, V3 no-Hero dependency, unsupported phase | four-stage service and current event/prompt contracts | mostly active V9 mechanics but wrong authority; rewrite around Phase 11 lineage; retain localization/object-negative cases |
| `ProductionPipelineExecutionServiceTests`: phase gating including `PhaseGating_ThumbnailOnly_RunsSceneAudioSyncButSkipsVideoPhasesNotRequested`, overwrite/cleanup and phase validation cases | applicability/orchestration/cleanup | retain explicit Thumbnail gate; rewrite success/cleanup for `12-thumbnails` transaction |
| `PlatformThumbnailResolverTests` | long/short canonical JPG resolution and fallback behavior | active downstream legacy contract; adapt to manifest-aware authority and remove hidden fallback success |
| `YouTubePublishingIntegrationTests`: Short generated/missing/disabled; valid long; >2 MB compression; wrong extension/missing/failure | upload, compression and nonfatal behavior | retain upload mechanics; rewrite mapping/readiness semantics |
| `MetaPublishingTests`, notably `FacebookLong_UsesFinalVideoAndLongThumbnail_WithoutReelEndpoint` and Reel cases | Meta path selection | retain publication behavior; add canonical role mapping |
| `ThumbnailV7CinematicOverlayRendererTests` | per-aspect deterministic overlay/layout | legacy renderer; mine useful layout/safe-area algorithms, do not retain authority routing |
| `ThumbnailConfigurationTests` | deprecated AI config behavior | compatibility; retire from Phase 12 authority |
| `ThumbnailCinematicAiPhase3Tests`, `ThumbnailGenerationTests`, `ThumbnailGeneratorServiceTests`, `ThumbnailRendererTests`, `CinematicThumbnailServiceTests` | older independent thumbnail systems | legacy/parallel systems; keep only if consumed outside Phase 12 |
| `Phase11ExecutionGateTests`, `ResponsiveHeroAuthorityServiceTests`, `ResponsiveHeroPositiveExecutionTests` | Hero applicability and committed authority | frozen; do not modify for Phase 12 |

## 46. Missing/future test matrix

Add tests for: explicit Thumbnail applicability with no override; no implication from video/Hero; committed Hero loading despite persisted outputs lacking HeroAsset; missing/invalid/not-ready Hero; Phase 10 checksum lineage; each source-role mapping; exact 1280×720, 1080×1080, 1080×1920 decode/format/size/SHA; no stretch; independent crop/safe/font/wrap/margins; protected geometry and subject visibility; Cinematic/HybridCinematic inheritance and no infographic/procedural fallback; no question-engine/hero-assets reads; Azure provider spy count zero; permitted-copy transformation and no new claims; event badge identity; forbidden internal text; stale plan/event/language/source rejection; deterministic reuse; corrupt output rejection; staging isolation; candidate readback; atomic swap/rollback; committed readback; overwrite cleanup limited to Phase 12; YouTube long→landscape and Short→portrait capability behavior; Meta long/reel and square mappings; compatibility projections non-authoritative; Orion start=end=12 with Thumbnail persisted and no override.

## 47–48. Minimum future file set

### MUST MODIFY

* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs` — invoke singular authority publisher, canonical success propagation and exact gate.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase1Authority.cs` — register `12-thumbnails` ownership and ensure only Phase 12 root is deletable (leave frozen roots untouched).
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ThumbnailAssetIntelligenceService.cs` **or preferably a new focused Phase 12 authority service plus a thin adapter here** — remove V9 provider/legacy semantic path from production and implement responsive composition/validation.
* `Backend/src/Astronomy.MediaFactory.Publishing/PlatformThumbnailResolver.cs` — consume committed manifest roles, with compatibility mapping explicit rather than fallback authority.
* Phase 12-specific tests in `Backend/tests/Astronomy.MediaFactory.Tests/` — new authority, transaction, validation, cleanup and consumer tests; adapt the Phase 12 cases listed above.

A separate new core/infrastructure DTO/service file is preferable to expanding `ThumbnailSceneManifestDto`; registration may require the existing DI composition root. That DI file is “must modify” only if constructor registration cannot use an existing service slot.

### MAY MODIFY

* `Backend/src/Astronomy.MediaFactory.Core/ThumbnailIntelligence.cs` — only if a shared manifest contract is intentionally public; otherwise leave legacy DTO intact.
* `Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase12ThumbnailRouter.cs` — collapse to compatibility adapter or remove only after callers/tests migrate.
* YouTube/Meta publishing services — only if resolver output is insufficient for explicit role/MIME/capability handling.
* Output/publishing packaging code in Phase 20 — to create deterministic JPG projections and record mapping.

### DO NOT MODIFY

All Phase 1–11 production implementations, tests, artifacts and contracts, especially `08-scene-assets`, `09-long-scenes`, `10-scene-validation`, `11-hero`, Phase 6 story authority, Responsive Hero authority implementation, databases, configuration, and existing certified artifacts. Phase 12 reads these immutable authorities only.

## 49. Keep/adapt/compatibility/remove summary

| Disposition | Components |
|---|---|
| **KEEP** | explicit Thumbnail applicability; per-aspect target concepts; ImageSharp decode; deterministic overlay/layout primitives where proven; localization; physical YouTube size/MIME checks; provider/upload diagnostics; cleanup registry concept |
| **ADAPT** | orchestration into one transaction; composition/safe-area algorithms; event-family badge selection; diagnostics; resolver; checksum/readback patterns from mature upstream authority publishers |
| **COMPATIBILITY ONLY** | `thumbnails/`, `thumbnail-final.png`, long/short JPG projections, V8-named flags/methods, old manifest DTO, direct legacy service endpoints if externally required |
| **REMOVE FROM AUTHORITATIVE PATH** | V9 AI-complete generation; Azure calls; stretch normalization; V3/V5/V6/V7 fallbacks; procedural/star-chart/guide-card authority; question-engine and `hero-assets` reconstruction; synthetic scene selection; File.Exists reuse; publisher legacy fallback success |
| **DO NOT TOUCH** | frozen Phase 1–11 roots, files, certifications, generation and authority contracts |

## 50. Exact implementation plan and Go/No-Go

1. Approve this contract and consumer migration.
2. Add a focused Phase 12 authority DTO/publisher that reads Phase 11 and Phase 10 read-only.
3. Implement strict authority/checksum/readback gates and copy authority binding.
4. Implement three deterministic, aspect-specific editorial compositions with protected scientific regions and no stretch/provider.
5. Implement candidate validation and atomic directory publication.
6. Produce the six-file canonical `12-thumbnails` package plus canonical `validation/phase-12-validation.json`.
7. Adapt orchestration and cleanup ownership without touching upstream.
8. Adapt the resolver/Phase 20 projection for landscape/portrait/square and eliminate legacy semantic fallback.
9. Add the future matrix, including zero-provider assertions and Orion no-override execution.
10. Certify only after physical committed readback proves exact pixels, format, byte size, checksums, source lineage, safe areas, subject/science preservation, copy identity and `downstreamReady`.

**Final assessment: NO-GO for certifying or preserving current V9. GO to implement the exact architecture above in a separate task.** Authoritative inputs, variants, dimensions, root, manifest, provider policy, cleanup ownership, consumers, and transaction are now explicit. No legacy path is accepted as semantic authority.
