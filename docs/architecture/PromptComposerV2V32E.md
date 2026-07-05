# V3.2E — PromptComposerV2 Architecture & Provider Translation Specification

## 1. Purpose

PromptComposerV2 is the provider-specific translation engine that converts Creative Direction Language (CDL) into optimized prompt packages for Azure Image2 and future Drashyam image providers.

PromptComposerV2 is responsible only for translating already-decided creative intent into the input format, wording style, constraints, and metadata preferred by a target provider. It does **not** make creative decisions. It must not decide what the visual should show, which astronomical objects matter, how the Drashyam brand should be expressed, or what rendering rules should apply.

In V3.2E, PromptComposerV2 is a documentation-only architecture specification. It does not change V3.1 behavior, prompt text, Azure Image2 integration, narration, validation, rendering, or pipeline orchestration.

## 2. Design Principles

### Provider-specific translator

PromptComposerV2 translates provider-neutral CDL into provider-specific prompt dialects. Azure Image2, GPT Image, Flux, Stable Diffusion XL, Midjourney, Imagen, and future providers may each receive different prompt shapes while preserving the same upstream creative intent.

### Stateless

PromptComposerV2 should behave as a deterministic stateless translator for a given CDL artifact, brand configuration, rendering rules, contract, provider profile, and quality target. It should not retain session state, mutate source contracts, or learn business rules over time.

### Renderer-independent

PromptComposerV2 must not depend on FFmpeg, ImageSharp, canvas, SVG, template rendering, browser layout engines, or any final media renderer. It prepares prompt packages for image providers only.

### CDL-driven

CDL is the primary source of creative intent. PromptComposerV2 may read supporting contracts and configuration for translation fidelity, but it must not invent creative direction that CDL does not authorize.

### Business-logic free

PromptComposerV2 must not determine product strategy, episode positioning, event-family prioritization, publication rules, monetization logic, or user-facing editorial choices.

### Astronomy-logic free

PromptComposerV2 must not decide astronomy facts, planet selection, relative importance, scientific interpretation, visibility constraints, orbital geometry, or event-family semantics. It only verbalizes astronomy decisions made upstream.

### Brand-logic free

PromptComposerV2 must not define Drashyam visual identity, color palettes, typography policy, premium tone, spacing rules, or label discipline. It only translates the Brand Design configuration and CDL brand directives into provider-ready language.

### Rendering-rule free

PromptComposerV2 must not create or alter planet rendering rules, observation-card rules, output safe zones, overlay mechanics, or image validation criteria. It may include those rules as prompt constraints when the target provider supports them.

### Extensible

New providers should be added through provider profiles and capability mappings rather than by adding business, astronomy, brand, or rendering logic to PromptComposerV2.

### Backward compatible

PromptComposerV2 is additive. If disabled or unavailable, existing V3.1 prompt generation and provider integration should continue unchanged.

### Additive to V3.2A-D

PromptComposerV2 consumes and translates outputs from V3.2A through V3.2D. It does not replace the VisualCreativeDirector, Brand Design System, Planet Rendering Rules, or CDL.

## 3. Position in Architecture

```text
Event Intelligence
→ VisualCreativeDirector
→ CDL
→ PromptComposerV2
→ Image Provider
```

PromptComposerV2 sits after CDL creation and before provider execution. It is the final translation boundary between Drashyam's provider-neutral creative architecture and provider-specific image generation systems.

## 4. Responsibilities

### PromptComposerV2 SHOULD

- Read CDL.
- Read Brand Design configuration.
- Read Planet Rendering Rules.
- Read CreativeDirectionContract.
- Produce a provider-ready prompt.
- Produce an optional negative prompt when supported or useful.
- Produce provider metadata.
- Produce prompt diagnostics.
- Produce prompt version information.

### PromptComposerV2 MUST NOT

- Decide composition.
- Decide colors.
- Decide planets.
- Decide typography.
- Decide camera angle.
- Decide artistic direction.
- Modify astronomy rules.
- Modify rendering rules.
- Validate images.

## 5. Internal Pipeline

PromptComposerV2 should be implemented as a logical translation pipeline. The stages below describe responsibilities, not mandatory classes or files.

```text
CDL Input
↓
Normalization
↓
Provider Capability Analysis
↓
Prompt Section Builder
↓
Prompt Assembly
↓
Provider Optimization
↓
Diagnostics
↓
Final Prompt Package
```

### CDL Input

Accept the CDL artifact and supporting versioned inputs: Brand Design configuration, Planet Rendering Rules, CreativeDirectionContract, quality targets, and provider profile.

### Normalization

Convert optional, synonymous, or nested CDL fields into a stable internal translation shape. Normalization must preserve meaning and must not add new creative choices.

### Provider Capability Analysis

Compare the normalized translation shape against the selected provider capability model. Determine whether negative prompts, structured input, typography guidance, image references, JSON payloads, seeds, multi-image generation, or other features can be used.

### Prompt Section Builder

Build reusable prompt sections from normalized CDL fields and supporting configuration. Each section should remain traceable to its source input.

### Prompt Assembly

Combine sections into the prompt format preferred by the provider profile: prose, structured sections, weighted phrases, JSON-like blocks, compact tags, or hybrid forms.

### Provider Optimization

Apply provider-specific formatting, ordering, compression, and emphasis rules without changing creative intent. Optimization may shorten text, reorder sections, convert constraints into negative prompts, or move hints into metadata when capabilities allow.

### Diagnostics

Record missing fields, fallbacks, ignored hints, unsupported capabilities, warnings, prompt length, and optimization decisions.

### Final Prompt Package

Return a provider-independent package containing prompt text, optional negative prompt, provider metadata, diagnostics, and version identifiers.

## 6. Prompt Sections

Prompt sections are reusable translation units. A provider profile may include, omit, merge, reorder, or compress sections based on capabilities and prompt style preference.

| Section | Purpose | Source | Required/Optional |
| --- | --- | --- | --- |
| Scene Summary | Summarize the full image intent in concise provider-ready language. | CDL scene intent and CreativeDirectionContract. | Required |
| Hero Subject | Describe the primary visual subject and its intended emphasis. | CDL subject hierarchy and event intelligence as carried by CDL. | Required |
| Supporting Subjects | Describe secondary celestial, environmental, or contextual subjects. | CDL supporting subjects. | Optional |
| Composition | Translate layout, hierarchy, negative space, safe-zone, and framing intent. | CDL composition and CreativeDirectionContract. | Required |
| Camera | Translate lens, perspective, viewpoint, scale, and camera language. | CDL camera guidance. | Optional |
| Lighting | Translate light source, contrast, glow, exposure mood, and readability constraints. | CDL lighting and Brand Design configuration. | Required |
| Atmosphere | Translate mood, sky conditions, documentary tone, dust, haze, stars, or cosmic environment. | CDL atmosphere and brand tone. | Optional |
| Astronomical Rendering | Translate astronomy realism and planet rendering constraints. | Planet Rendering Rules and CDL astronomy directives. | Required when astronomical bodies are present |
| Typography | Translate text presence, hierarchy, font tone, readability, and text safety. | CDL typography directives and Brand Design configuration. | Optional |
| Observation Card | Translate observation-card presence, placement, contents, and restraint. | CreativeDirectionContract, CDL observation-card guidance, and rendering rules. | Optional |
| Labels | Translate object labels, label discipline, and avoidance of clutter. | CDL label directives and Brand Design configuration. | Optional |
| Brand Style | Translate Drashyam identity into provider-ready stylistic constraints. | Brand Design configuration and CDL brand directives. | Required |
| Quality Targets | Translate quality bar, realism, resolution expectations, premium finish, and artifact avoidance. | Quality target configuration and CreativeDirectionContract. | Required |
| Negative Constraints | Translate prohibited artifacts, misleading astronomy, bad typography, clutter, and low-quality output. | CDL avoid-list, rendering rules, brand constraints, and provider profile. | Optional; required when provider supports negative prompts and constraints exist |
| Output Expectations | Translate aspect ratio, deliverable type, background needs, and output assumptions. | CreativeDirectionContract, quality target, and provider capabilities. | Required |

## 7. Provider Capability Model

PromptComposerV2 should use a provider capability abstraction so translation adapts to provider features rather than hardcoded provider checks.

Example capabilities include:

- Supports negative prompts.
- Supports typography.
- Supports structured input.
- Supports image references.
- Supports JSON input.
- Supports style presets.
- Supports safety options.
- Supports seed.
- Supports multi-image generation.
- Supports transparent background.
- Supports image editing.

A provider profile should expose capabilities as declarative data. PromptComposerV2 should ask what the selected provider supports, then map CDL sections into the strongest supported form. For example, a provider that supports negative prompts may receive clutter and artifact constraints separately; a provider without negative prompts may receive those constraints in the main prompt as avoidance language.

This approach prevents scattered `if provider == ...` checks and keeps provider behavior isolated in profiles.

## 8. Provider Profiles

Provider profiles describe how a target image model prefers to receive prompt information. The profiles below are first-pass guidance and should be refined through empirical testing.

### Azure Image2

- **Strengths:** Enterprise integration, safety controls, predictable API usage, good general visual synthesis, strong fit for current Drashyam provider path.
- **Weaknesses:** Provider-specific limits may constrain prompt length, typography fidelity, and strict astronomical layout control.
- **Prompt style preference:** Clear structured prose with explicit subject, composition, style, realism, and quality constraints.
- **Special considerations:** Preserve backward compatibility with the existing Azure Image2 integration. Do not require SDK or pipeline changes in V3.2E.

### GPT Image

- **Strengths:** Strong instruction following, multimodal context handling, natural-language prompt understanding, potential support for edits and reference-driven workflows.
- **Weaknesses:** May require careful constraint wording to avoid overinterpreting artistic latitude or rendering text incorrectly.
- **Prompt style preference:** Natural structured instructions with explicit hierarchy, visual intent, and constraints.
- **Special considerations:** Useful for future workflows that need image references, edits, or richer structured context.

### Flux

- **Strengths:** High visual quality, strong aesthetic rendering, flexible prompt interpretation, good cinematic and design-oriented outputs.
- **Weaknesses:** May need provider-specific phrasing or weighting to maintain strict astronomy and typography discipline.
- **Prompt style preference:** Concise high-signal prompt phrases with strong aesthetic descriptors and explicit constraints.
- **Special considerations:** Negative constraints and style phrasing should be tuned to avoid over-stylization when scientific realism is required.

### Stable Diffusion XL

- **Strengths:** Broad ecosystem, local or hosted deployment options, seeds, negative prompts, adapters, LoRAs, and controllable generation workflows.
- **Weaknesses:** Quality depends heavily on checkpoint, scheduler, sampler, prompt format, and deployment configuration.
- **Prompt style preference:** Weighted or tag-like prompt sections, explicit negative prompts, and compact quality tokens where appropriate.
- **Special considerations:** Provider profile should separate base SDXL capability from deployment-specific extensions such as ControlNet, IP-Adapter, custom checkpoints, or LoRAs.

### Midjourney

- **Strengths:** Strong cinematic aesthetics, composition, mood, and premium visual polish.
- **Weaknesses:** Less API-like structure, weaker deterministic control, limited native negative-prompt semantics compared with some diffusion workflows, and possible typography challenges.
- **Prompt style preference:** Compact descriptive prompts with clear subject, mood, camera, lighting, style, and aspect cues.
- **Special considerations:** Provider profile should avoid relying on strict JSON or detailed structured fields unless supported by the execution path.

### Imagen

- **Strengths:** Strong photorealism, language understanding, and high-quality image synthesis in supported Google workflows.
- **Weaknesses:** Capability details may vary by product surface, region, and model version.
- **Prompt style preference:** Clear natural-language descriptions with explicit subject, visual realism, safety, and output constraints.
- **Special considerations:** Provider profile should keep safety options, structured input, and editing capabilities versioned because they may vary across Imagen releases.

## 9. Prompt Package

PromptComposerV2 should return a provider-independent output package even when its contents are optimized for one provider.

Required package fields:

- `prompt`
- `negativePrompt`
- `providerMetadata`
- `diagnostics`
- `promptVersion`
- `cdlVersion`
- `brandVersion`
- `renderingVersion`
- `qualityTargetVersion`

Example:

```json
{
  "prompt": "Premium documentary astronomy image: a realistic Jupiter and Venus conjunction over a calm pre-dawn horizon, composed with Jupiter as the hero subject, restrained labels, cinematic sky gradient, and Drashyam brand spacing discipline.",
  "negativePrompt": "cartoon planets, incorrect planet colors, cluttered labels, unreadable text, exaggerated scale, low-resolution artifacts",
  "providerMetadata": {
    "provider": "azure-image2",
    "profileVersion": "v3.2e-provider-profile-azure-image2.1",
    "capabilitiesUsed": [
      "structured_prompt",
      "safety_options"
    ],
    "capabilitiesUnavailable": [
      "seed",
      "transparent_background"
    ]
  },
  "diagnostics": {
    "missingFields": [],
    "fallbacks": [],
    "ignoredProviderHints": [],
    "unsupportedCapabilities": [
      "seed"
    ],
    "warnings": [],
    "promptLength": 197,
    "optimizationDecisions": [
      "merged atmosphere into scene summary for Azure Image2 prose preference"
    ]
  },
  "promptVersion": "PromptComposerV2.3.2E",
  "cdlVersion": "CDL.3.2D",
  "brandVersion": "BrandDesign.3.2B",
  "renderingVersion": "PlanetRenderingRules.3.2C",
  "qualityTargetVersion": "CreativeQualityTargets.3.2"
}
```

## 10. Diagnostics

Diagnostics make prompt translation inspectable without changing provider execution. PromptComposerV2 should report:

- **Missing fields:** CDL or supporting fields expected by the profile but not provided.
- **Fallbacks:** Safe substitutions or omissions used when optional inputs are absent.
- **Ignored provider hints:** Provider-specific hints that could not be applied because the selected provider or profile does not support them.
- **Unsupported capabilities:** Requested or desirable capabilities absent from the provider profile.
- **Warnings:** Non-fatal translation concerns such as long prompts, typography risk, weak label support, or compressed astronomy constraints.
- **Prompt length:** Character count, token estimate when available, and provider limit status.
- **Optimization decisions:** Reordering, merging, compression, negative-prompt extraction, metadata relocation, or other transformations applied during provider optimization.

Diagnostics must not validate generated images. They describe prompt-package construction only.

## 11. Versioning

PromptComposerV2 should preserve explicit versions for every upstream and translation artifact that can affect output.

- **PromptComposer:** Version the translator behavior, prompt assembly strategy, diagnostics schema, and provider-selection rules.
- **CDL:** Version the source creative language schema and semantics.
- **Brand Design:** Version Drashyam design tokens, typography policy, color language, spacing rules, and style guidance consumed during translation.
- **Rendering Rules:** Version planet rendering constraints, astronomy realism requirements, label rules, and observation-card rendering constraints consumed as prompt constraints.
- **CreativeDirectionContract:** Version contract shape, required fields, safe-zone directives, aspect-ratio assumptions, and quality target references.
- **Provider Profiles:** Version provider capability declarations, prompt style preferences, known limitations, formatting strategies, and optimization rules.

A prompt package should be reproducible by storing all relevant version identifiers with diagnostics.

## 12. Integration

### Consumes V3.2A

PromptComposerV2 consumes the CreativeDirectionContract produced by V3.2A VisualCreativeDirector. It uses that contract as authoritative creative intent and does not replace or reinterpret the director's decisions.

### Consumes V3.2B

PromptComposerV2 reads the V3.2B Drashyam Brand Design System to translate approved brand style, typography discipline, color language, and premium visual tone into provider-ready wording.

### Consumes V3.2C

PromptComposerV2 reads V3.2C Planet Rendering Rules to include astronomy rendering constraints in prompt sections when relevant. It does not modify those rules or decide astronomy realism policy.

### Consumes V3.2D

PromptComposerV2 uses V3.2D CDL as the primary provider-neutral input. CDL supplies the structured visual intent that becomes provider-specific prompt text, negative constraints, and metadata.

### Feeds CreativeQualityScoringEngine

PromptComposerV2 diagnostics and prompt packages can be logged for future CreativeQualityScoringEngine comparisons between intended prompt constraints and generated image quality.

### Feeds Image Providers

PromptComposerV2 produces the provider-ready prompt package consumed by Azure Image2 and future image providers.

### Feeds future logging

Prompt packages, diagnostics, and versions can be persisted for debugging, reproducibility, audit trails, and release comparisons.

### Feeds future analytics

Prompt metadata can support future analytics about provider performance, prompt length, capability usage, fallback frequency, and quality outcomes.

## 13. Extensibility

New image providers should be added by defining a provider profile with capabilities, prompt style preference, limitations, metadata mapping, and optimization rules. The core PromptComposerV2 translation flow should remain unchanged.

A new provider profile should answer:

- Which capabilities are supported?
- Which prompt sections should be included, merged, or omitted?
- Does the provider prefer prose, structured sections, tags, JSON, weights, or metadata?
- How should unsupported CDL hints be reported?
- How should constraints become negative prompts, main-prompt avoidance language, or metadata?
- Which versioned provider-specific assumptions must be recorded?

Adding a provider must not introduce business logic, astronomy logic, brand logic, rendering rules, or image-validation behavior into PromptComposerV2.

## 14. Non-goals

V3.2E explicitly does not include:

- Implementation code.
- Provider SDK integration.
- Azure calls.
- Image generation.
- Business logic.
- Astronomy logic.
- Validation.
- Pipeline changes.

## 15. Migration Plan

1. Run PromptComposerV2 alongside current prompt generation without changing provider calls.
2. Compare generated PromptComposerV2 prompt packages against current prompt strings.
3. Validate outputs through offline review, prompt diagnostics, and future quality scoring.
4. Enable PromptComposerV2 behind a feature flag for controlled experiments.
5. Retain the V3.1 fallback path until provider quality, prompt parity, and operational safety are proven.

The migration must not modify existing pipeline behavior until a later implementation explicitly opts in.

## 16. Acceptance Criteria

- The change is documentation-only.
- No implementation code is introduced.
- Existing pipeline behavior is unchanged.
- Existing Azure Image2 integration is unchanged.
- Existing narration behavior is unchanged.
- Existing rendering pipeline behavior is unchanged.
- Existing prompt generation behavior is unchanged.
- The architecture is provider-neutral at the CDL boundary.
- The architecture supports provider-specific translation after CDL.
- The design is compatible with V3.2A, V3.2B, V3.2C, and V3.2D.
- PromptComposerV2 responsibilities are clearly separated from business logic, astronomy logic, brand logic, rendering rules, image validation, and provider execution.
- The specification is implementation-ready and can serve as the canonical PromptComposer architecture for future Drashyam image providers.
