# Content Intelligence Platform V4 Specification — Astronomy First

## 1. Product Vision

Content Intelligence Platform V4 is the master specification for evolving the current Astronomy V3 media factory and Hero V4 work into a complete social-media publishing platform. The product is not only an astronomy content generator; it is a reusable Content Intelligence Platform that turns verified domain events into high-quality, platform-native stories, visuals, videos, publishing packages, diagnostics, and quality evidence.

The platform must preserve the working Astronomy V3 foundation while introducing V4 intelligence layers that reason about events, editorial intent, story structure, visual communication, composition, deliverables, platform requirements, and output artifact governance. Astronomy remains the first production domain, but the core architecture must remain domain-agnostic so future domains can reuse the same editorial, story, visual, composition, artifact, quality, and publishing systems.

The V4 product direction is:

- Convert one verified event into one coherent story.
- Expand that story into many deliverables for social platforms.
- Keep each deliverable native to its platform and purpose.
- Preserve production stability with explicit artifacts, manifests, diagnostics, comparisons, and quality gates.
- Build astronomy completely first, without hard-coding the platform so deeply that other domains cannot adopt it later.

## 2. Astronomy-First Strategy

Astronomy is Domain #1. The first business and engineering goal is to complete astronomy publishing end to end before starting any new domain implementation.

Astronomy-first means:

- The next V4 milestones must complete the astronomy editorial, story, visual, composition, and publishing workflow.
- Astronomy knowledge, event reasoning, narration, validation, and visual rules may be domain-specific.
- Shared platform subsystems must remain reusable and must avoid astronomy-only names, contracts, or assumptions where a generic abstraction is appropriate.
- Future domains should be able to plug into the platform through a Domain Adapter while reusing the platform's editorial intelligence, visual intelligence, composition engines, output artifact management, publishing package generation, diagnostics, and quality gates.

The intended sequence is not “build a generic platform in isolation.” The intended sequence is “finish astronomy with clean seams,” then allow additional domains to reuse the stable platform layers.

## 3. Domain-Agnostic Architecture

V4 should separate reusable platform intelligence from domain-specific knowledge and rules.

### 3.1 Domain Adapter

The Domain Adapter is the boundary between a domain and the generic Content Intelligence Platform. It translates domain events, knowledge, vocabulary, validation rules, visual constraints, and publishing priorities into generic platform contracts.

Responsibilities:

- Normalize raw domain events into verified event candidates.
- Expose domain knowledge and source attribution.
- Provide domain-specific validation rules.
- Provide domain-specific visual rules and forbidden/required elements.
- Map domain terminology into editorial and story contracts.
- Keep downstream platform modules from depending directly on domain internals.

### 3.2 Content Intelligence

Content Intelligence is the top-level reasoning layer that decides what content should be produced from a verified event.

Responsibilities:

- Identify audience value.
- Determine whether an event is publish-worthy.
- Select the primary content angle.
- Define audience promise and educational outcome.
- Coordinate editorial, story, visual, composition, and publishing reasoning.

### 3.3 Editorial Intelligence

Editorial Intelligence determines why the audience should care, what the content should say, and what editorial stance the story should take.

Responsibilities:

- Produce the editorial angle.
- Define the hook, audience promise, and key takeaway.
- Rank story claims and decide what to emphasize or omit.
- Identify uncertainty, caveats, and validation requirements.
- Provide downstream guidance for hero, thumbnail, gallery, long video, short video, and post copy.

Editorial Intelligence is the next coding milestone: **V4.2A — Editorial Reasoning Engine**.

### 3.4 Story Intelligence

Story Intelligence turns editorial intent into a structured story model.

Responsibilities:

- Create the narrative arc.
- Define beats, sequence, stakes, explanation, and resolution.
- Produce a Visual Story Model that can drive multiple deliverables.
- Separate story intent from platform-specific rendering.

### 3.5 Visual Intelligence

Visual Intelligence converts story intent into visual direction.

Responsibilities:

- Define visual motifs, composition goals, evidence visuals, cinematic tone, typography needs, and platform constraints.
- Support professional, documentary-style visual outputs.
- Preserve existing Visual Intelligence foundation behavior and avoid replacing production prompts without approved comparisons.
- Feed Hero, Thumbnail, Gallery, and Story Frame generation with explicit visual intent.

### 3.6 Publishing Intelligence

Publishing Intelligence maps content packages to target platforms and formats.

Responsibilities:

- Decide which deliverables are required for each platform.
- Enforce platform-native aspect ratios and composition rules.
- Prepare captions, metadata, descriptions, hashtags, titles, thumbnails, and gallery ordering.
- Package assets for YouTube, YouTube Shorts, Facebook, and Instagram.

### 3.7 Output Artifact Management

Output Artifact Management governs production outputs, diagnostics, comparisons, manifests, and future asset-specific artifact contracts.

Responsibilities:

- Keep production artifacts stable and easy to consume.
- Store diagnostics separately from production outputs.
- Store comparison artifacts separately from approved production artifacts.
- Track generated assets with manifests, beginning with `HeroArtifactManifest`.
- Extend the same pattern to future thumbnail, gallery, long story frame, and short story frame manifests.

### 3.8 Quality Gates

Quality Gates are explicit checks that prevent unapproved or low-quality content from silently replacing production behavior.

Responsibilities:

- Score scientific accuracy, storytelling, visual composition, documentary feel, typography, brand consistency, and platform optimization.
- Require comparison approval before production prompt replacement.
- Preserve legacy fallback behavior until a future milestone explicitly removes it.
- Emit diagnostics for failures and uncertain decisions.

## 4. Domain-Specific Components — Astronomy

Astronomy-specific components should live behind the Astronomy Domain Adapter and feed the reusable V4 platform contracts.

### 4.1 Astronomy Knowledge Source

The astronomy knowledge source provides verified facts, event context, object metadata, visibility context, source provenance, and safety/caveat information. It should preserve the existing Astronomy V3 data foundation and expose only the normalized knowledge needed by V4 intelligence layers.

### 4.2 Astronomy Event Intelligence

Astronomy Event Intelligence identifies, validates, ranks, and explains astronomy events. It should handle event families such as meteor showers, eclipses, planetary alignments, conjunctions, supermoons, comets, occultations, deep-sky objects, constellations, and generic astronomy events.

### 4.3 Astronomy Story Engine

The Astronomy Story Engine converts verified event intelligence into a domain-specific story premise that the generic Story Intelligence layer can structure into a Visual Story Model.

Responsibilities:

- Identify the scientific phenomenon.
- Explain why the event matters.
- Choose the most audience-relevant astronomy angle.
- Define educational beats in astronomy-safe language.
- Preserve uncertainty and observing caveats.

### 4.4 Astronomy Narration Engine

The Astronomy Narration Engine produces clear, accessible narration that is scientifically accurate, dramatic without exaggeration, and suitable for long-form and short-form content.

Responsibilities:

- Generate long documentary narration.
- Generate short high-impact narration.
- Respect scientific caveats and avoid misleading claims.
- Support localization paths already present in the V3 foundation.

### 4.5 Astronomy Validation Rules

Astronomy validation rules protect scientific accuracy and audience trust.

Rules should verify:

- Event dates and visibility claims.
- Object identities and relative positions.
- Eclipse, conjunction, meteor shower, comet, supermoon, and occultation terminology.
- Claims about rarity, brightness, distance, danger, and observability.
- Local visibility caveats where applicable.

### 4.6 Astronomy Visual Rules

Astronomy visual rules protect visual truthfulness while allowing cinematic presentation.

Rules should govern:

- Planet, Moon, Sun, comet, meteor, galaxy, nebula, and starfield representation.
- Forbidden misleading compositions.
- Required labels or context where visuals are explanatory rather than literal.
- Scale, alignment, atmosphere, horizon, telescope, and night-sky constraints.
- Typography readability over dark, high-contrast astronomical imagery.

## 5. Core Model: One Event → One Story → Many Deliverables

V4 uses a core production model:

```text
Verified astronomy event
  → Editorial Reasoning
  → Visual Story Model
  → Platform-native deliverables
  → Publishing package
```

### 5.1 Verified Astronomy Event

A verified astronomy event is the source unit. It contains the event identity, timing, location/visibility context where applicable, event family, source provenance, scientific facts, caveats, and content opportunity.

### 5.2 Visual Story Model

The Visual Story Model is the shared story contract for downstream assets. It should define:

- Editorial angle.
- Hook.
- Audience promise.
- Story beats.
- Key facts and caveats.
- Visual beats.
- Required diagrams, evidence visuals, or cinematic moments.
- Platform adaptation guidance.
- Quality requirements.

### 5.3 Hero

The Hero is a high-impact image designed to stop scrolling and create immediate curiosity. It should express the editorial hook visually, not explain the full story.

### 5.4 Thumbnail

The Thumbnail is a click-through asset for video publishing. It should create a clear video promise, visual contrast, readable text if used, and strong curiosity without misleading the viewer.

### 5.5 Gallery

The Gallery is a visual teaching sequence. It should explain the event through ordered panels or carousel slides, each with a clear role in the audience's understanding.

### 5.6 Long Video Story Frames

Long video story frames support a documentary explanation in landscape format. They should be paced for narration, evidence, diagrams, transitions, and deeper context.

### 5.7 Short Video Story Frames

Short video story frames support a quick high-impact takeaway in portrait format. They should be designed natively for vertical viewing and should not be cropped from long video frames.

### 5.8 Publishing Package

The Publishing Package bundles approved deliverables, metadata, captions, descriptions, platform-specific variants, manifests, quality scores, and diagnostics needed for publication.

## 6. Publishing Strategy

V4 publishing targets are platform-native, not one-size-fits-all.

### 6.1 YouTube

Required deliverables:

- Long video.
- Thumbnail.

Primary goal: documentary explanation and durable search/discovery value.

### 6.2 YouTube Shorts

Required deliverables:

- Short video in 9:16 portrait format.

Primary goal: fast, high-impact discovery and concise takeaway.

### 6.3 Facebook

Required deliverables:

- Long video.
- Short video.
- Hero post.
- Gallery post.

Primary goal: mixed-feed reach, shareability, and visual explanation.

### 6.4 Instagram

Required deliverables:

- Reel in 9:16 portrait format.
- Hero post.
- Gallery carousel.

Primary goal: visual-first discovery, carousel education, and vertical short-form impact.

## 7. Visual Product Contracts

Each visual product has a distinct job. V4 must not treat these assets as interchangeable crops.

| Asset | Purpose | Primary Success Signal |
| --- | --- | --- |
| Hero | Stop scrolling | Immediate curiosity and professional visual impact |
| Thumbnail | Drive click-through | Clear video promise and high CTR potential |
| Gallery | Teach and explain visually | Ordered understanding across panels |
| Long video | Documentary explanation | Complete, credible, paced learning experience |
| Short video | Quick high-impact takeaway | Immediate retention and shareable insight |

## 8. Platform-Native Composition

V4 must generate native compositions per platform.

Rules:

- Long video should be landscape 16:9.
- Short video should be portrait 9:16.
- Do not crop long video frames into short video frames.
- Generate native compositions for each platform and asset type.
- Scene assets should later become Visual Story Frames.
- Scene Assets must not be redesigned before the Visual Story Model is defined.

The platform should treat each deliverable as a composition problem with its own aspect ratio, focal hierarchy, typography, safe areas, motion needs, and viewing context.

## 9. V4 Roadmap

The V4 development order is fixed as follows.

### 9.1 Completed / Current

- **V4.0A — Hero Prompt Migration**: completed.
- **V4.0B — Hero Prompt Quality Cleanup**: completed.
- **V4.0C — Hero Image Comparison Framework**: completed.
- **V4.0D — Output Artifact Management**: completed.
- **V4.1A — Creative Knowledge Library**: initial implementation completed.

### 9.2 Next

- **V4.2A — Editorial Reasoning Engine**.

This is the next coding milestone. Development should resume here.

### 9.3 Later Milestones

- **V4.3A — Visual Story Model**.
- **V4.4 — Hero Finalization**.
- **V4.5 — Thumbnail V4**.
- **V4.6 — Gallery V4**.
- **V4.7 — Scene Composition Engine**.
- **V4.8 — Long Story Frames, 16:9**.
- **V4.9 — Short Story Frames, 9:16**.
- **V4.10 — Publishing Package Generator**.

Do not jump to Thumbnail V4 before Editorial Reasoning and the Visual Story Model. Do not change Scene Assets before the Visual Story Model.

## 10. Current Development Handoff — Where We Stopped

### 10.1 Last Completed Work

The last completed V4 work is the initial Creative Knowledge Library implementation after the Hero V4 foundation work. The completed/current Hero V4 stack includes:

- Hero prompt migration.
- Hero prompt quality cleanup.
- Hero comparison framework.
- Output artifact management.
- `HeroArtifactManifest`.
- Creative Knowledge Library initial implementation.

### 10.2 Current Artifact Structure

The current artifact model separates:

- Production artifacts: approved outputs consumed by the production pipeline.
- Diagnostics: structured evidence for decisions, failures, configuration, and quality results.
- Comparison artifacts: side-by-side or candidate outputs used to approve prompt and visual changes before production replacement.
- `HeroArtifactManifest`: the current manifest pattern for hero outputs.

Future asset families should follow the same manifest pattern:

- `ThumbnailArtifactManifest`.
- `GalleryArtifactManifest`.
- `LongStoryFrameArtifactManifest`.
- `ShortStoryFrameArtifactManifest`.
- `PublishingPackageManifest`.

### 10.3 Current Known State

Known state at this handoff:

- Astronomy V3 foundation is frozen and tagged.
- Visual Intelligence foundation is implemented.
- Hero V4 work has reached the Creative Knowledge Library initial implementation.
- The platform has enough Hero V4 infrastructure to move into editorial reasoning.
- The next required intelligence layer is editorial, not thumbnail generation.
- Scene Assets should remain unchanged until the Visual Story Model is defined.
- Legacy fallback behavior must remain in place until a future milestone explicitly removes it.

### 10.4 Exact Next Step

The next implementation prompt should start with:

> Implement V4.2A — Editorial Reasoning Engine.

The V4.2A implementation should define editorial reasoning contracts and behavior that can later feed the Visual Story Model, Hero finalization, Thumbnail V4, Gallery V4, Scene Composition Engine, Long Story Frames, Short Story Frames, and Publishing Package Generator.

### 10.5 Explicit Do-Not-Start Items

Do not start Thumbnail V4 yet.

Do not start Scene Assets redesign yet.

Do not replace production prompts without comparison approval.

Do not remove legacy fallback yet.

## 11. Success Criteria

V4 is successful when:

- One verified astronomy event produces a complete social-media campaign.
- Production outputs remain stable across approved changes.
- Visual assets look professional, credible, and platform-native.
- Short video is generated as portrait 9:16.
- Long video is generated as landscape 16:9.
- Hero posts are suitable for Facebook and Instagram feeds.
- Gallery posts are suitable for Facebook and Instagram carousel/feed education.
- Thumbnail assets are suitable for video publishing and click-through.
- Output artifacts, manifests, diagnostics, and comparisons are clear enough to audit production decisions.
- The architecture remains reusable for future domains through domain adapters and generic platform contracts.

## 12. Non-Goals for Immediate Next Work

The immediate next work is V4.2A Editorial Reasoning Engine only. Non-goals:

- No new domain implementation yet.
- No thumbnail migration before Editorial Reasoning and Visual Story Model.
- No scene asset redesign before Visual Story Model.
- No production prompt replacement without comparison approval.
- No removal of legacy fallback yet.
- No platform publishing automation changes unless they are required only as documentation or contract references for editorial reasoning.

## 13. Output Artifact Model

V4 output artifacts must make production behavior reproducible, auditable, and safe to evolve.

### 13.1 Production Artifacts

Production artifacts are approved outputs used by downstream production or publishing systems. They must be stable, named predictably, and protected from unapproved experimental replacement.

Examples:

- Approved hero image.
- Approved thumbnail image.
- Approved gallery panels.
- Approved long video frames.
- Approved short video frames.
- Approved publishing package metadata.

### 13.2 Diagnostics

Diagnostics explain how and why an output was produced.

Examples:

- Prompt inputs and selected strategy identifiers.
- Quality scores.
- Validation results.
- Feature flag state.
- Fallback decisions.
- Provider metadata where safe to store.
- Errors, warnings, and skipped-stage reasons.

### 13.3 Comparison Artifacts

Comparison artifacts support safe prompt and visual evolution.

Examples:

- Legacy output versus candidate output.
- Multiple candidate hero generations.
- Quality rubric scoring across variants.
- Human approval evidence.
- Notes explaining why a candidate was accepted or rejected.

### 13.4 HeroArtifactManifest

`HeroArtifactManifest` is the current manifest pattern for hero outputs. It should track the hero artifact identity, source event, prompt lineage, generation metadata, production path, diagnostics path, comparison references where applicable, quality scores, and approval status.

### 13.5 Future Manifests

Future manifests should follow the same pattern and be introduced only when their asset families enter implementation:

- Thumbnail manifest during Thumbnail V4.
- Gallery manifest during Gallery V4.
- Long story frame manifest during Long Story Frames, 16:9.
- Short story frame manifest during Short Story Frames, 9:16.
- Publishing package manifest during Publishing Package Generator.

## 14. Quality Rubric

Each major deliverable should be scored with a common V4 rubric. Scores should use a consistent 0–5 scale:

| Score | Meaning |
| --- | --- |
| 0 | Unusable or unsafe |
| 1 | Poor; major issues |
| 2 | Weak; significant revision needed |
| 3 | Acceptable baseline |
| 4 | Strong production candidate |
| 5 | Excellent; publication-ready |

### 14.1 Scientific Accuracy

Measures factual correctness, astronomy terminology, event timing, object identity, visibility caveats, and avoidance of misleading claims.

### 14.2 Storytelling

Measures hook strength, audience promise, narrative flow, clarity, emotional engagement, educational value, and conclusion strength.

### 14.3 Visual Composition

Measures focal hierarchy, framing, balance, contrast, clarity, aspect-ratio fit, visual storytelling, and absence of confusing or misleading visual structure.

### 14.4 Documentary Feel

Measures credibility, cinematic quality, pacing support, observational tone, and the sense that the output belongs in a serious educational/documentary astronomy brand.

### 14.5 Typography

Measures readability, text hierarchy, font suitability, safe-area compliance, contrast, localization readiness, and restraint.

### 14.6 Brand Consistency

Measures consistency with the platform's visual identity, tone, quality bar, educational posture, and recurring design language.

### 14.7 Platform Optimization

Measures suitability for target platform behavior, including YouTube thumbnail click-through, YouTube long-form clarity, Shorts/Reels vertical retention, Facebook feed readability, Instagram carousel flow, and platform-native aspect ratios.

## 15. Development Guardrails

To preserve context and production stability:

- Keep Astronomy V3 foundation frozen unless a future task explicitly opens it.
- Complete Astronomy domain first.
- Keep reusable architecture seams clean for future domains.
- Continue from V4.2A Editorial Reasoning Engine.
- Use comparison artifacts before replacing any production prompt.
- Keep diagnostics separate from production outputs.
- Introduce new manifests only with the relevant asset family milestone.
- Avoid large asset redesigns until the Visual Story Model exists.
