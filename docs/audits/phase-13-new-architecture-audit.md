# Phase 13 new-architecture audit: Gallery and Observation Guide authority

**Audit date:** 2026-08-09  
**Scope:** current repository production path; audit only. No provider was called, no image was generated, and no production, test, DI, configuration, contract, database, or Phase 1–12 file was changed.

## Executive decision record

Phase 13 currently is an always-applicable, Azure-dependent **six-slide landscape social carousel generator**. The RC2 registry calls `ProductionPipelineExecutionService.PhaseGenerateGalleryAsync`, which calls the sole DI implementation of `IAstroPulseGalleryService`, `AstroPulseGalleryService.GenerateGalleryAsync`. It reads only the compatibility `plan-input` projection, constructs six role-based pages, makes six Azure Image2 requests, crops each response to 1920×1080, adds deterministic text overlays, and writes directly into `gallery/` plus a sibling `observation-guide/`. It does **not** read or verify any Phase 8–12 authority.

That design conflicts with the frozen authority chain in four material ways:

1. it creates six new, potentially hallucinated astronomy backgrounds rather than reusing certified cinematic scene imagery;
2. it does not gate selected imagery through Phase 10 certification;
3. its scientific and observation copy is partly reconstructed by recursive field-name searches and hard-coded family templates;
4. publication is non-transactional and reuse is absent.

The smallest certifiable redesign is therefore **not** a refinement of the Azure path. Phase 13 should become a deterministic carousel compositor over certified Phase 8/9 images, certified Phase 10 lineage, and certified Phase 2/4/6 semantics. It should publish exactly six 1080×1080 pages under `13-gallery/`. The current Azure generation path and `gallery/` names should remain only as explicitly bounded compatibility projections until consumers migrate.

`observation-guide-v2.json` has no independent production consumer outside Phase 13 validation/diagnostics. Choose architecture **B**: an optional/supporting Phase 13 artifact derived only from certified observation facts; it is not a separate semantic authority and must not be required when the request does not include Gallery.

## 1. Registry, applicability, service, and exact active call graph

### Registry and routing

| Item | Current evidence |
|---|---|
| Registry | `ProductionPipelineExecutionService.PhaseDefinitions()` entry `(13, "Generate Gallery", PhaseGenerateGalleryAsync)` (`ProductionPipelineExecutionService.cs:410-423`). |
| Execution | `PhaseGenerateGalleryAsync` fixes root to `OutputRoot/gallery`, fixes aspect to `Landscape`, calls `galleryEngine.GenerateGalleryAsync`, appends the sibling guide, validates, enriches diagnostics, and returns output paths (`:3545-3557`). |
| Interface | `IAstroPulseGalleryService.GenerateGalleryAsync(string outputDirectory, AstroPulseGalleryAspect aspect, CancellationToken)` (`AstroPulseGalleryService.cs:22-25`). |
| Implementation | One implementation exists: sealed `AstroPulseGalleryService` (`:36`). |
| DI | One scoped binding, `IAstroPulseGalleryService -> AstroPulseGalleryService` (`ServiceCollectionExtensions.cs:948`). No flag, keyed route, alternate production implementation, or fallback implementation exists. |
| Applicability | `IsPhaseRequiredForRequestedOutputs` returns `true` unconditionally for phase 13 (`ProductionPipelineExecutionService.cs:461-480`). Gallery, Thumbnail, HeroAsset, ShortVideo, LongVideo, and an empty/unknown output set all therefore cause Phase 13 to be considered required whenever it lies in the selected range. |

There is no evidence that Thumbnail or Hero *implies* Gallery semantically; the implication is merely the unconditional rule. Phase 20 is also unconditional, but its production publishing assembly does not read gallery artifacts. Recommended rule: `phaseNo == 13 => IsRequestedOutput(requestedOutputs, "Gallery")`. If a future explicit publication-package contract includes Gallery, request resolution may add `Gallery` before execution; Phase 13 itself must not infer it from Hero, Thumbnail, or either video.

### Complete active stage sequence

1. **Entry/root:** `PhaseGenerateGalleryAsync` chooses `gallery/` and landscape 1920×1080.
2. **Direct directory creation:** service creates stable root, `diagnostics/`, unused `comparison/`, and sibling `observation-guide/`.
3. **Configuration gate:** `EnsureAzureImage2Configured`; absent Azure configuration fails before context loading.
4. **Context reconstruction:** `LoadGalleryContext` reads `plan-input/production-event-intelligence.json`; absent input silently returns generic event defaults. It separately reads `plan-input/content-plan-production-request.json` only for requested language.
5. **Normalization/planning:** family resolution and `GalleryContentResolver.Resolve` select Meteor, Moon, Planet Pairing, Solar Eclipse, Lunar Eclipse, or Generic provider; `BuildTopics` turns its six `GallerySceneContent` records into prompts and overlays.
6. **Generation loop:** for each topic, call Azure Image2 once into temporary `gallery-NN-azure-background.png`; any failed configured request aborts with no local fallback.
7. **Composition:** ImageSharp center-crops/resizes the generated background to the requested aspect, applies a grade, and draws title/body/footer.
8. **Per-image checks:** save PNG, delete background, SHA-256 output, reject byte-identical duplicate hash.
9. **Artifact writes:** write prompt/content, composition, story, blueprint, manifest, guide, review, diagnostics, visual-policy reviews, overlay/localization diagnostics, and local Phase 13 validation directly to stable roots.
10. **Runner validation:** `ValidateGalleryContract` checks six recognized names, existence of manifest/review/diagnostics/guide, and that local validation is not failed.
11. **Mutation after validation:** `WriteGalleryPhaseExecutionDiagnosticsAsync` reopens and rewrites diagnostics and validation with execution-range fields.
12. **Canonical runner record:** generic `WritePhaseValidationAsync` writes `validation/phase-13-validation.json` from the returned paths/status, not as a committed authority-validation projection.

### Call/data table

| Call | Inputs | Reads | Writes/output | Providers, fallback, validation |
|---|---|---|---|---|
| `PhaseGenerateGalleryAsync` | phase context, cancellation | result artifacts via validator | returned list and later canonical runner validation | no provider itself; validates names/existence/status |
| `GenerateGalleryAsync` | output path, aspect | two `plan-input` JSONs | all Gallery and guide artifacts | six Azure calls; no successful fallback path |
| `LoadGalleryContext` | parent of Gallery root | production intelligence and production request | `GalleryContext` | missing intelligence becomes unsafe generic defaults; malformed intelligence throws; malformed language request is ignored |
| `GalleryContentResolver.Resolve` | normalized context | memory only | six-scene content contract | first matching family provider; Generic is the fallback and yields a warning |
| `BuildTopics` | content contract | memory only | six prompts/topics | hard-coded prompt policy and page-specific treatments; forbidden-term guard runs only after images were generated |
| `GenerateBackgroundWithAzureImage2Async` | prompt, aspect, options | Azure HTTP response / optional response URL | temporary background | Azure OpenAI images endpoint, `n=1`; catches non-cancellation errors and reports failure; caller throws |
| `RenderTopicAsync` | topic, temporary image, aspect | decoded generated image | in-memory final page | ImageSharp center crop/resize and overlay; no geometry-aware crop |
| writers in `GenerateGalleryAsync` | current in-memory state | output hashes and file existence | JSON package listed below | direct stable writes; timestamps make diagnostics nondeterministic |
| `ValidateGalleryContract` | outputs and manifest/review paths | local JSON and file system | exception or success | count/existence and validation boolean only; no decode/dimension/hash/authority/publication checks |
| `WriteGalleryPhaseExecutionDiagnosticsAsync` | context and local JSON paths | just-written or pre-existing local JSON | mutates two local JSON files | no freshness/run-id guard |

No active Phase 13 call reaches the repository's separate Gallery intelligence-alignment planners/review writers in `GalleryIntelligenceAlignment.cs`; those types have tests and artifact-registry presence but are not called by `PhaseGenerateGalleryAsync` or `AstroPulseGalleryService`. They are retained planning ideas/diagnostic infrastructure, not the production Phase 13 graph.

## 2. Current input authorities and authority disagreement

### What is actually read

| Source | Current use | Classification |
|---|---|---|
| `plan-input/production-event-intelligence.json` | recursively searches event type/name, objects, forbidden terms, date/time/window/direction/location/timezone/language/visibility and story/visual theme | **COMPATIBILITY + UNSAFE SEMANTIC RECONSTRUCTION**. It is a legacy projection, not the named Phase 2 certified artifact. Recursive first-match lookup weakens schema ownership. |
| `plan-input/content-plan-production-request.json` | requested language only | **PRESENTATION INPUT / COMPATIBILITY**. |
| in-process event-family profiles and prompt policies | family selection, required/forbidden visual language, presentation prompts | **PRESENTATION POLICY**, not event-fact authority. |
| hard-coded family providers/tips | six roles, titles, generic viewing directions and equipment suggestions | **LEGACY / UNSAFE NEW CLAIMS** where not traceable to certified facts. |

The active path reads none of `02-intelligence/`, `03-questions/`, `04-blueprint/`, `06-story-frames/`, `08-scene-assets/`, `09-long-scenes/`, `10-scene-validation/`, `11-hero/`, `12-thumbnails/`, `question-engine/`, `scene-approval-v3/`, `hero-assets/`, `hero/`, or `thumbnails/`. It receives no `ProductionEventIntelligence` or `ContentGenerationPlan` object directly.

### Resolution of Definition A versus Definition B

Neither historical definition describes current execution. Definition A names the right *kinds* of semantic and certification authority but omits story-frame/asset lineage necessary for image selection. Definition B is architecturally wrong: the current generator does not consume `thumbnail-manifest.json`, and Phase 12 is a discovery-poster presentation authority with no ownership of Gallery semantics.

**Final recommended input contract:**

1. `02-intelligence/certified-knowledge-context.json` — authoritative event identity and scientific/observation facts;
2. `04-blueprint/documentary-blueprint.json` — authoritative editorial intent/learning objectives where a page requires them;
3. `06-story-frames/<canonical manifest>` — authoritative page-to-scene identity, sequence, and viewer takeaway;
4. `10-scene-validation/scene-asset-certification.json` — mandatory certified lineage/checksum gate and the only route to eligible Phase 8/9 physical imagery;
5. production request — requested output, language, and presentation locale only.

Phase 8/9 images are **CERTIFIED LINEAGE** reached through the Phase 10 manifest, never discovered by directory enumeration alone. Phase 11 and 12 are neither required inputs nor semantic authorities. A Hero/Thumbnail may be used only if a future explicit presentation-reuse policy declares it as a visual derivative and the original certified scene lineage remains provable; there is no current need.

Mandatory certification gate: parse Phase 10, require its overall certification/downstream-ready status, verify every selected asset identity and expected SHA-256 against the certification entry, decode it, verify declared physical dimensions, and reject an asset absent from the certification set. Never fall back to an uncertified file or generate a replacement.

## 3. Product meaning, roles, copy, imagery, count, and dimensions

### Product responsibility

Current code calls the output a `social-media carousel`, rejects a PowerPoint-style infographic, caps claimed text area at 25%, and assigns one educational message per image. The six generic scene roles are:

| # / canonical current file | Actual content role | Copy source | Current image source / renderer |
|---|---|---|---|
| 1 `page01-hook.png` | Opening view / hook; event title, date, time | reconstructed intelligence + templates | unique Azure prompt; ImageSharp overlay |
| 2 `page02-recognition.png` | What happens / recognition | objects + family provider | same route, distinct prompt |
| 3 `page03-explanation.png` | Where to look in the provider contract, despite filename “explanation” | observation display + templates | same route |
| 4 `page04-observation.png` | When to observe / sky guide | date/time resolver | same route |
| 5 `page05-memory.png` | Key objects / memorable identity | object arrays + templates | same route |
| 6 `page06-checklist.png` | Viewing checklist / close | hard-coded CTA and family templates | same route |

The older `GalleryIntelligenceAlignmentEngine` models Hook, Recognition, Explanation, Observation, and Memory (often five pages), but is not active. The active six-page provider maps `Opening view`, `What happens`, `Where to look`, `When to observe`, `Key objects`, and `Viewing checklist`. Therefore the current product covers WHAT, WHEN, WHERE, HOW/tips, HIGHLIGHTS/objects, and a modest SCIENCE explanation; WHY is family/template dependent rather than guaranteed.

Recommended responsibility: a **six-page educational/social carousel that complements discovery surfaces**. Hero establishes landing-page identity and Thumbnail wins discovery; Gallery teaches event identity, meaning, certified viewing facts, object/highlight identity, and safe observation guidance. Roles may select family-specific labels/content, but the role IDs and count remain deterministic:

1. `cover-identity`;
2. `what-happens`;
3. `where-to-look` (or family-approved identification role);
4. `when-to-observe`;
5. `certified-highlight-or-science`;
6. `observation-checklist`.

For families where “where” or “when” is inapplicable, a versioned family policy must substitute a declared role; it must not invent filler. Every field in rendered copy must carry artifact path + JSON pointer/source identity. Phase 13 may shorten/reformat certified text, but it must not invent dates, times, directions, object names, visibility, equipment, safety, or science claims. The current hard-coded tips (“Face the radiant area”, “Binoculars are optional”, “Face the suggested sky direction”) cannot survive without certified supporting fields.

### Exact count policy

Six is simultaneously hard-coded in: six provider scenes, `NN/06` overlay, `imagePaths.Count == 6`, six-or-more Azure-call validation, runner contract validation, six artifact names, and cleanup tests. No configuration, minimum/maximum range, or event-family count exists. It is thus an entrenched product/contract invariant, not merely a test fixture, even though older alignment code produces five pages.

**Decision:** exactly **6 canonical pages for every applicable Gallery request**, page policy versioned. This is the smallest deterministic policy compatible with current downstream contracts and avoids conditional package shape. Event-family behavior changes roles/content within those six slots, not count.

### Exact dimension policy

The type exposes landscape 1920×1080 (16:9), square 1080×1080 (1:1), and portrait 1080×1920 (9:16). Azure request sizes are 1792×1024 for width ≥ height and 1024×1792 otherwise. However production invokes **only Landscape**, so every active final `page##-*.png` is 1920×1080. Square and portrait are callable/tested variants, not produced Phase 13 artifacts. There is no consumer proving responsive Gallery variants.

The declared product is a social carousel rather than video/hero imagery. **Decision:** one canonical **1080×1080 PNG (1:1)** per page, six total. Do not create responsive variants. This recommendation changes the current production shape and therefore requires a versioned consumer migration; until then, a bounded compatibility projection may retain 1920×1080 legacy files, but compatibility assets are not authority.

### Image/provider policy and scientific preservation

Current active provider capability is only Azure OpenAI Image2: six POSTs, one per page, prompts built from recursively extracted event data plus `VisualPromptPolicyComposer`, and aspect-dependent request size. Managed identity or API key authorizes it. There is no OpenAI-direct, procedural renderer, Stellarium, NASA, Phase 8/9, Hero, Thumbnail, or local fallback in the active graph. Other rendering services registered in DI are unrelated and unreachable from this service.

**Firm new policy:** Phase 13 makes **zero generative/procedural/Stellarium/provider calls**. Select distinct, certified cinematic/HybridCinematic Phase 8/9 images through Phase 10 and add deterministic informational overlays. Prefer six distinct source hashes; when fewer than six certified sources exist, deterministic crop variants are permitted only if the manifest records the repeated source and crop policy, subject-safe analysis passes, and at least role/crop diversity is achieved. Do not stretch. Preserve aspect ratio, celestial geometry, star positions, labels, and object relationships; never inpaint, reconstruct, or AI-replace. A geometry-sensitive sky guide must use a certified asset explicitly approved for that use and crop-safe bounds.

Current diversity validation proves only six distinct prompt strings/concepts and distinct final-byte hashes. Since overlays themselves differ, distinct final hashes do **not** prove source-image diversity. There is no perceptual hash, source hash comparison, role coverage gate beyond construction, or geometry preservation check. Center crop can remove a subject; no subject/safe-crop metadata is read.

## 4. Observation Guide determination

`AstroPulseGalleryService` creates sibling `observation-guide/observation-guide-v2.json` after images and manifest. Fields are: `guideVersion`, replacement flag, title, family-specific flag, event family, absolute output path, and three tips. A diagnostic sibling `observation-guide/diagnostics/observation-intelligence.json` records original/resolved time/window/visibility/provider metadata.

The runner requires the guide in `ValidateGalleryContract`, returns it as Phase 13 output, and reports it in Phase 13 diagnostics. Repository production searches show no Phase 14–20 semantic consumer. A legacy fallback accepts `gallery/observation-guide-v2.json`, but the active writer uses the sibling root.

Classification:

* `observation-guide-v2.json`: **SUPPORTING + COMPATIBILITY**, not authoritative; it duplicates page-six guidance and carries templated claims.
* `observation-intelligence.json`: **DIAGNOSTIC**; it is resolver evidence, not certified authority.
* legacy `gallery/observation-guide-v2.json`: **LEGACY** read compatibility only.

Choose **B**. Publish `13-gallery/observation-guide.json` only as a supporting structured projection of the exact certified facts rendered across observation pages. It shares the Gallery manifest authority checksum but has no independent authority checksum and cannot introduce facts. Remove the external sibling root from the canonical package. Do not make it universally required; require it only as part of an applicable, successfully committed Gallery package. Retain the v2 sibling solely if an identified external client still needs compatibility.

## 5. Artifacts, names, roots, and consumers

### Current active writes

| Path relative to output root | Classification |
|---|---|
| `gallery/page01-hook.png` … `page06-checklist.png` | current **AUTHORITATIVE-by-convention** images, but scientifically uncertified |
| `gallery/GalleryArtifactManifest.json` | current package **AUTHORITATIVE** manifest; version 4.5E written by service (contract helper independently says 4.6A) |
| `gallery/gallery-prompt.json`, `composition-model.json`, `asset-story.json`, `asset-blueprint.json` | **SUPPORTING/DIAGNOSTIC** generated contracts; not upstream authority |
| `gallery/diagnostics/GalleryReview.json` | **DIAGNOSTIC** self-review |
| `GalleryGenerationDiagnostics.json`, `VisualPromptDiagnostics.json`, `gallery-localization.json`, `gallery-overlay.json`, `VisualQualityFrameworkReview.json`, `VisualPromptPolicyReview.json` under diagnostics | **DIAGNOSTIC** |
| `gallery/diagnostics/phase-13-validation.json` | active local **SUPPORTING VALIDATION**, not canonical pipeline validation |
| `observation-guide/observation-guide-v2.json` | **SUPPORTING/COMPATIBILITY** |
| `observation-guide/diagnostics/observation-intelligence.json` | **DIAGNOSTIC** |
| `gallery/comparison/` | empty directory side effect; **OBSOLETE** in this path |

Artifact-registry-only diagnostics such as `GalleryIntelligenceContract`, `GalleryEditorialSequence`, information-density/narrative-flow/educational-storytelling/benchmark/editorial reviews and gallery comparison are not written by the active service. Classify them **DORMANT/LEGACY DIAGNOSTIC CONTRACTS**, not current Phase 13 artifacts.

Legacy compatibility names are `gallery-01.png` … `gallery-06.png`, `gallery-content-contract.json`, lowercase diagnostic filenames, and in-root `observation-guide-v2.json`. The runner recognizes the legacy image pattern only if no canonical `page##-role.png` names occur. Contract resolution also falls back to these legacy paths. No active writer produces them.

### Current consumers and downstream contract

Internal consumers are the Phase 13 runner validator, generic canonical validation writer, diagnostics builder, and `OutputArtifactRegistry` manifest/path resolver. No current Phase 19 video QA or Phase 20 publishing method reads the Gallery manifest/pages/guide. `ContentPlanProductionExecutionService` mentioning `gallery` is an output-folder list, not semantic consumption. Thus “Phase 20 requires Gallery” is false in current code despite both phases being unconditionally applicable.

Recommended canonical package:

```text
13-gallery/
  gallery-01.png ... gallery-06.png
  gallery-manifest.json
  observation-guide.json                 # supporting, if Gallery applicable
  phase13-authority-diagnostics.json      # current-run evidence
  phase13-publication-report.json
  .staging/<transactionId>/               # transient only
validation/
  phase-13-validation.json                # canonical committed-state validation
```

The `gallery-NN.png` name is preferred because role changes can be event-family-aware without renaming the contract. Role IDs belong in the manifest. Keep current page names only as compatibility projections while `OutputArtifactRegistry` consumers migrate.

Phases 19–20 should consume only `13-gallery/gallery-manifest.json` plus canonical `validation/phase-13-validation.json`, and only when the resolved publishing request explicitly includes Gallery. They must require `downstreamReady=true`, verify the manifest authority checksum and every physical hash/dimension, and obtain paths from the manifest—never glob folders or consume diagnostics as authority. Video-only Phase 19 remains independent.

## 6. Cleanup, transactionality, reuse, validation, and stale evidence

### Cleanup ownership

`PhaseOutputOwnershipRegistry` assigns Phase 13 only `OutputRoot/gallery` and the phase-13 canonical validation file. The Phase-13-only overwrite test proves `gallery/` and `validation/phase-13-validation.json` are removed while Hero, Thumbnail, questions, scenes, narration, TTS, videos, and Phase 1–12 validations survive. No upstream deletion violation was found.

There is nevertheless a correctness gap: the active service also owns/writes sibling `observation-guide/`, but Phase 13 cleanup does **not** register that root. A rerun can leave a stale guide/diagnostic behind. This is **HIGH**, not an upstream-mutation CRITICAL. Moving the guide inside `13-gallery/` resolves it. Phase 13 must own only `13-gallery/` plus its canonical validation; it must never delete `08-scene-assets`, `09-long-scenes`, `10-scene-validation`, `11-hero`, `12-thumbnails`, or earlier validation.

### Current publication and reuse

There is no staging, candidate validation/readback, atomic rename/swap, backup, committed readback, or rollback. Stable directories are created first, pages are written sequentially, JSON comes later, and failures leave a partial or mixed generation. Old extra files are not removed unless outer overwrite cleanup ran. The runner then mutates diagnostics after validation. Publication is not transactional.

There is also no reuse in the service: no `File.Exists` shortcut, authority checksum, source checksum, page identity, renderer/layout version comparison, output-hash readback, or manifest checksum. Outer pipeline retry/reuse can skip a prior successful phase based on generic phase satisfaction, but Phase 13 does not validate current physical/semantic identity before such reuse. This is a stale-reuse risk.

Recommended transaction:

1. load and validate all upstream authority before touching stable state;
2. compute deterministic input identity and page plan;
3. build under `13-gallery/.staging/<transactionId>` (or a sibling temporary root if atomic directory replacement requires it);
4. validate candidate semantics, lineage, decode/dimensions, hashes, layout, and manifest determinism;
5. read candidate back from disk;
6. atomically swap candidate with stable authority while retaining a temporary backup;
7. read and validate committed state;
8. write publication report and canonical validation as part of the same commit protocol;
9. remove backup and all staging on success; on any failure restore previous valid authority and delete candidate/staging;
10. API completion must leave no `.staging` transaction and no sibling `13-gallery.staging-*`.

Candidate failure must not overwrite a prior valid package. Failure evidence belongs in a current-run validation/failure report outside the restored authority or in a transaction journal with run identity; never relabel previous success diagnostics as current evidence.

### Reuse identity

Reuse only if committed readback proves all of:

* certified semantic authority checksum set (Phase 2/4/6);
* Phase 10 visual authority/certification checksum;
* exact selected source asset IDs and physical SHA-256 hashes;
* language/locale and six role/content identities;
* page-policy, renderer, layout, font/policy, and manifest-schema versions;
* canonical 1080×1080 dimensions and PNG decode;
* every output SHA-256;
* deterministic manifest checksum (excluding nondeterministic timestamps/paths);
* `publicationCommitted`, committed readback, and downstream readiness.

A manifest should use relative paths and deterministic ordering; absolute output paths and `generatedAtUtc` must not participate in authority identity.

### Validation gap matrix

| Check | Current active state | Required certification state |
|---|---|---|
| Count/existence | yes, exactly six | retain |
| Decode/dimensions/aspect | decode occurs only while rendering; runner does not read back or assert | decode each committed file; exactly 1080×1080 |
| Output checksums | generated and listed in manifest | recompute candidate and committed hashes |
| Source/certification checksums | absent | mandatory Phase 10 and physical-source verification |
| Copy authority/unsupported facts | forbidden-term and template validation only | field-level lineage; reject unsupported facts |
| Clipping/overlap/safe area | claimed booleans and simple character heuristics; no pixel/layout proof | measured text bounds, safe areas, collision and clipping checks |
| Subject visibility/scientific geometry | absent; center crop is unsafe | certified crop metadata/subject bounds, no stretch or reconstruction |
| Duplicate pages | exact final hash and distinct prompt/concept only | source/perceptual diversity plus role/content diversity |
| Information coverage | construction assumptions | required role coverage and source lineage |
| Publication state | absent | candidate + committed readback and atomic commit evidence |

The local validation lacks `authorityChecksum`, `manifestValidationStatus`, `semanticValidationPassed`, `checksumValidationPassed`, `manifestValidationPassed`, `publicationCommitted`, `committedStateValidationPassed`, and `downstreamReady`. The generic canonical writer does not turn those absent claims into a proper authority contract. The required canonical file must propagate all of them and report success only when every boolean is true.

Stale diagnostics risk is currently concrete: `WriteGalleryPhaseExecutionDiagnosticsAsync` reads whichever JSON exists at the stable paths and mutates it. If a service implementation returned stale paths or a partial execution failed around those writes, prior diagnostics could become apparent current-run evidence. Direct stable writes and the unowned sibling guide amplify this. Bind every diagnostic to `runId`, `transactionId`, input checksum, and candidate/committed state; canonical validation must read current transaction evidence, never previous stable diagnostics before commit.

## 7. Retain, retire, and smallest certification implementation

### Retain

* the `IAstroPulseGalleryService` orchestration seam (with an authority-oriented request/result contract in Phase 13 implementation work);
* deterministic ImageSharp overlay/font/localization mechanics after adding measurable layout validation;
* six-slot role model and family-aware presentation labels;
* manifest-based consumer resolution and SHA-256 utilities;
* forbidden-term/event-family presentation policies, provided they do not create facts;
* current cleanup guard principle and upstream-preservation tests.

### Compatibility/legacy only

* Azure Image2 background generation and its prompt builder;
* generic default context when authority is missing;
* recursive `FirstString`/array reconstruction from `plan-input`;
* hard-coded observation tips;
* `gallery/`, `page##-*.png`, sibling `observation-guide/`, and `gallery-##.png` fallback names;
* dormant Gallery alignment/review artifacts unless deliberately integrated as diagnostics;
* square/portrait callable variants unless a real consumer contract appears.

### Smallest certifiable implementation (future work; not performed)

1. Gate Phase 13 on explicit `Gallery` request.
2. Introduce a Phase 13 authority reader for Phase 2/4/6 semantics and Phase 10-certified Phase 8/9 imagery; fail closed on absence/mismatch.
3. Replace six Azure calls with deterministic certified-source selection and 1080×1080 safe-crop/overlay composition.
4. Keep exactly six versioned roles and require field-level copy lineage.
5. Publish the canonical `13-gallery/` package transactionally; make the guide supporting and internal.
6. Implement deterministic manifest, physical checksum/dimension/layout/diversity validation, committed readback, and complete canonical validation propagation.
7. Update Phase 19/20 only to consume the manifest when Gallery is explicitly included; retain compatibility projections only where a proved consumer requires them.

## 8. Severity-ranked findings

1. **CRITICAL — uncertified imagery:** every active Gallery background is newly generated; Phase 10 is never consulted.
2. **CRITICAL — unsafe claims:** recursive compatibility parsing and hard-coded tips can publish scientific/observation statements without certified field lineage.
3. **CRITICAL — non-transactional authority:** failure can leave a partial/mixed stable Gallery and cannot preserve/restore previous valid authority reliably.
4. **HIGH — unconditional execution:** Phase 13 runs for every requested-output combination in range and requires paid Azure configuration.
5. **HIGH — incomplete validation contract:** no authority/manifest/semantic/checksum/publication/committed-readback/downstream-ready propagation.
6. **HIGH — stale guide/evidence:** sibling observation root is outside cleanup ownership; stable diagnostics are reopened and mutated.
7. **HIGH — scientific crop/diversity gap:** center crops lack subject/geometry safety; final-hash uniqueness does not prove source diversity.
8. **MEDIUM — root/name/version drift:** `gallery/` and mixed service 4.5E / helper 4.6A contracts conflict with numbered authority convention.
9. **MEDIUM — dimension/product mismatch:** active 16:9 output conflicts with the code's declared social-carousel role; no consumer proves variants.

## Audit commands (read-only)

The audit used repository searches and targeted source reads only: `find .. -name AGENTS.md -print`, `rg --files`, focused `rg -n` queries for Phase 13/service/artifact/provider/consumer/cleanup references, and `sed -n` reads of the cited implementation, tests, DI, cleanup registry, and artifact contracts. `git diff --check` and targeted tests are recorded in the change summary when this audit is committed.
