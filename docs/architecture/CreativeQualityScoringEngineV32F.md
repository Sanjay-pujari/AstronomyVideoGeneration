# V3.2F — Creative Quality Scoring Engine

## 1. Purpose

The Creative Quality Scoring Engine provides an objective, provider-independent evaluation framework for generated visual assets. It measures whether an image faithfully implements the intended Creative Direction Language (CDL), Drashyam Brand Design System, and Astronomical Rendering Specification after prompt generation and image generation have completed.

The scoring engine evaluates creative quality only. It does not generate images, rewrite prompts, modify CDL, or change rendering rules. Its role is to determine whether a generated asset is fit for publication and to make the quality decision explainable to humans and future automation.

The engine should determine:

- Does the image match the intended creative direction?
- Is the astronomy scientifically correct?
- Does it follow Drashyam branding?
- Is it suitable for publication?
- Should it be accepted, warned, or regenerated?

In V3.2F, this is a documentation-only architecture specification. It introduces no implementation code and does not change V3.1 behavior, Azure Image2 integration, narration, prompt generation, rendering, or publication pipeline behavior.

## 2. Design Principles

### Provider-independent

Quality evaluation must operate on provider-neutral inputs and outputs. Azure Image2, GPT Image, Flux, Stable Diffusion XL, Midjourney, Imagen, or future providers may all be evaluated through the same scoring model.

### Objective where possible

The engine should prefer measurable or contract-based checks over subjective taste. Examples include safe-margin compliance, text overlap, planet shape, required element presence, label readability, and adherence to known rendering constraints.

### Modular scoring

Each quality dimension should be scored independently so failures are localized, explainable, and tunable without rewriting the entire evaluation model.

### Extensible

New quality dimensions, scoring modules, asset types, providers, platforms, and future analytics fields should be added without breaking existing quality reports or publication decisions.

### Explainable

Every significant deduction should include a human-readable explanation. Reviewers must understand what failed, why it matters, and what action is recommended.

### Deterministic

Given the same generated asset, CDL, brand configuration, rendering rules, provider metadata, scoring version, and thresholds, the engine should produce the same quality report.

### Brand-aware

The engine must understand Drashyam visual identity expectations including documentary restraint, premium astronomy tone, color discipline, typography usage, observation-card behavior, label hierarchy, and avoidance of decorative clutter.

### Astronomy-aware

The engine must evaluate scientific credibility, astronomical plausibility, planet-specific rendering rules, illumination behavior, circularity of spherical bodies, object relationships, and misleading visual artifacts.

### Platform-aware

The engine should account for target platform constraints such as aspect ratio, safe zones, thumbnail readability, mobile legibility, social-media cropping risk, and publication context.

### Backward compatible

The engine is additive. If disabled, unavailable, or running in observation mode, existing V3.1 publication behavior should continue unchanged.

### Additive to V3.2A-E

V3.2F consumes and evaluates the outputs and metadata created by V3.2A through V3.2E. It does not replace the VisualCreativeDirector, Brand Design System, Planet Rendering Rules, Creative Direction Language, or PromptComposerV2.

## 3. Position in Architecture

```text
Event Intelligence
→ VisualCreativeDirector
→ CDL
→ PromptComposerV2
→ Image Provider
→ Creative Quality Scoring Engine
→ Publication Decision
```

The Creative Quality Scoring Engine sits after the image provider has produced a visual asset and before a final publication decision is made. It is the quality gate between generated imagery and publication workflow decisions.

## 4. Responsibilities

### The engine SHOULD

- Evaluate generated assets.
- Score multiple quality dimensions.
- Produce diagnostics.
- Recommend regeneration when necessary.
- Record quality metrics.
- Support future analytics.
- Preserve traceability to CDL, brand, rendering-rule, prompt-composer, provider, and scoring versions.
- Distinguish warnings from critical publication blockers.
- Support observation mode before automated enforcement.

### The engine MUST NOT

- Generate prompts.
- Generate images.
- Modify CDL.
- Modify rendering rules.
- Change branding.
- Replace human review when required.
- Change Azure Image2 integration.
- Change narration.
- Change the rendering pipeline.
- Change existing prompt text.
- Make hidden creative decisions not represented in upstream contracts.

## 5. Quality Dimensions

Scores should use a normalized `0.0` to `1.0` range unless an implementation profile explicitly defines another compatible representation. A score of `1.0` means the category fully satisfies expectations, `0.7` means generally acceptable with issues, `0.4` means materially weak, and `0.0` means absent or unacceptable.

Weighting guidance is intentionally flexible. Publication-critical dimensions should receive higher weights for assets where they are relevant. Optional or inapplicable dimensions should be excluded from the weighted denominator rather than scored as failures.

| Category | Purpose | Typical checks | Score range | Weighting guidance |
| --- | --- | --- | --- | --- |
| Astronomical Accuracy | Verify that the scene is scientifically credible and does not mislead the viewer. | Correct object relationships, plausible illumination, realistic scale cues where required, no impossible conjunction geometry unless explicitly stylized, no invented celestial event facts. | `0.0–1.0` | Very high for educational, documentary, and event-explainer assets. |
| Planet Rendering Accuracy | Ensure planet-specific appearance follows the Astronomical Rendering Specification. | Spherical/circular hero planets, realistic cloud bands, recognizable surface or atmospheric features, correct phase behavior, no distorted rings or melted limbs. | `0.0–1.0` | Very high when planets are hero or secondary subjects; lower when planets are absent. |
| Brand Consistency | Confirm adherence to Drashyam visual identity. | Premium documentary tone, restrained effects, approved palette feel, disciplined overlays, no generic fantasy poster style, no off-brand typography or clutter. | `0.0–1.0` | High for all public Drashyam assets. |
| Composition | Evaluate layout fidelity to intended creative direction. | Subject placement, negative space, framing, balance, focal structure, safe margins, no accidental cropping of hero objects. | `0.0–1.0` | High for thumbnails, hero images, and publication covers. |
| Visual Hierarchy | Confirm the intended subject priority is visually clear. | Hero subject dominance, secondary subject restraint, observation card subordinate to astronomy, labels do not compete with main subject. | `0.0–1.0` | High when CDL defines explicit hierarchy. |
| Typography | Evaluate text quality when text is expected. | Correct tone, legibility, spelling, alignment, size, contrast, no warped or garbled text, no unauthorized text additions. | `0.0–1.0` | High when text is present or required; excluded when text is intentionally absent. |
| Observation Card | Check whether observation-card elements are accurate, restrained, and usable. | Card presence when requested, safe placement, readable contents, no overlap with hero subject, appropriate opacity, no clutter. | `0.0–1.0` | Medium to high for educational or observing-guide assets. |
| Label Quality | Verify labels support clarity without clutter. | Correct object labels, leader-line discipline, readable size, no label collisions, no labels for nonexistent objects. | `0.0–1.0` | Medium; high for diagrams and explanatory assets. |
| Platform Optimization | Assess fitness for the target channel and aspect ratio. | Mobile readability, crop-safe content, thumbnail contrast, platform safe zones, format suitability, no important details outside expected view. | `0.0–1.0` | High for YouTube thumbnails, shorts, and social cards. |
| Readability | Measure whether important visual and textual information can be understood quickly. | Contrast, clarity at small sizes, separation between foreground/background, minimal visual noise, readable text and labels. | `0.0–1.0` | High for public distribution and thumbnail assets. |
| Scientific Credibility | Evaluate whether the image feels trustworthy as astronomy communication. | Documentary realism, absence of misleading fantasy elements, plausible sky context, no exaggerated impossible effects unless clearly editorial. | `0.0–1.0` | High for educational content; medium for promotional art. |
| Documentary Aesthetic | Confirm the asset maintains Drashyam's premium educational tone. | Cinematic but restrained lighting, naturalistic astronomy, editorial polish, no excessive neon, explosions, magical particles, or game-art styling. | `0.0–1.0` | Medium to high across Drashyam visual assets. |
| Overall Production Quality | Capture general execution quality and artifact control. | Resolution, sharpness, compression artifacts, malformed shapes, visible generation errors, unwanted watermarks, broken overlays. | `0.0–1.0` | High for all assets because it affects publication suitability. |

## 6. Scoring Model

### Per-category scores

Each applicable category should produce:

- `score`: normalized quality score.
- `weight`: contribution to the weighted overall score.
- `confidence`: confidence in the category score.
- `status`: `pass`, `warning`, `fail`, or `not_applicable`.
- `deductions`: explainable reasons for score reductions.

### Weighted overall score

The overall score should be computed from applicable categories only:

```text
overallScore = sum(categoryScore × categoryWeight) / sum(applicableCategoryWeights)
```

The scoring model should preserve category scores even when an issue is severe enough to trigger a publication blocker. This allows dashboards and reviewers to distinguish a generally strong image with one critical flaw from a broadly weak image.

### Confidence score

The confidence score represents how reliable the engine believes the evaluation is. It should account for input completeness, provider metadata quality, asset availability, ambiguous visuals, unsupported checks, and whether manual review is recommended.

### Threshold guidance

Default thresholds should be versioned and configurable:

| Decision | Suggested rule | Meaning |
| --- | --- | --- |
| Approved | `overallScore >= 0.85`, no critical issues, confidence `>= 0.75` | Asset is suitable for publication. |
| Approved with Warning | `overallScore >= 0.75`, no critical issues, limited warnings | Asset may publish but should record known minor issues. |
| Needs Regeneration | `overallScore < 0.75` or regeneration-specific critical issue | Asset likely failed creative, brand, astronomy, or production requirements and should be regenerated. |
| Needs Manual Review | Confidence below threshold, ambiguous result, or human-review rule triggered | Automation should not make final decision. |
| Rejected | Severe scientific, brand, policy, or production failure that should not be regenerated blindly | Asset is unsuitable for publication and may require upstream correction. |

Thresholds should initially be conservative and tuned against manual review outcomes.

## 7. Example Evaluation Rules

The following checks are architectural examples, not implementation algorithms. They describe the type of evaluation the engine should support without prescribing computer vision, OCR, model, or provider implementation details.

- Hero planet remains perfectly circular unless CDL explicitly requests an eclipse, crescent crop, atmospheric distortion, or partial framing.
- Jupiter cloud bands are realistic, horizontally banded, and not replaced with random colorful stripes.
- Venus illumination is plausible for the requested phase and does not show impossible surface details through an opaque cloud deck unless the style explicitly permits educational cutaway treatment.
- Saturn's rings remain elliptical, centered, and physically attached to the planet rather than floating or broken.
- Mars appears reddish and rocky without Earth-like oceans or vegetation.
- The Moon shows plausible cratered surface detail and phase illumination.
- Text does not overlap the hero subject or obscure scientifically important features.
- Observation card fits within safe margins and remains visually subordinate to the astronomical scene.
- Labels are legible, correctly associated with objects, and do not create clutter.
- Composition matches the requested intent, including hero subject priority, camera framing, and negative-space expectations.
- Excessive decorative effects are avoided when the asset is intended to be documentary, educational, or scientifically realistic.
- Generated artifacts, watermarks, malformed geometry, and unreadable pseudo-text are flagged.
- Platform-specific crop risks are identified before publication.

## 8. Diagnostics

A quality report should contain diagnostics that are useful for publication decisions, manual review, analytics, and future prompt/provider tuning.

Diagnostic output should include:

- Overall score.
- Per-category scores.
- Confidence score.
- Publication decision.
- Warnings.
- Critical issues.
- Recommendations.
- Missing expected elements.
- Ignored constraints.
- Provider notes.
- Version information.
- Scoring mode, such as `observation`, `advisory`, or `enforced`.
- Trace identifiers for generated asset, CDL, prompt package, event, provider, and scoring run.

Diagnostics should distinguish between observed failures and uncertain findings. For example, `text_overlap_detected` is stronger than `possible_text_overlap` and should carry a different confidence level.

## 9. Quality Report JSON

The quality report JSON should be provider-independent and stable across image providers. Provider-specific details may be nested under `providerInformation` without changing top-level scoring semantics.

### Schema shape

```json
{
  "qualityReportVersion": "3.2F",
  "overallScore": 0.0,
  "confidence": 0.0,
  "publicationDecision": "Needs Manual Review",
  "categoryScores": {},
  "warnings": [],
  "criticalIssues": [],
  "recommendations": [],
  "missingExpectedElements": [],
  "ignoredConstraints": [],
  "providerInformation": {},
  "versions": {}
}
```

### Complete sample JSON

```json
{
  "qualityReportVersion": "3.2F",
  "scoringMode": "observation",
  "assetId": "visual_asset_2026_venus_evening_001",
  "overallScore": 0.82,
  "confidence": 0.78,
  "publicationDecision": "Approved with Warning",
  "categoryScores": {
    "astronomicalAccuracy": {
      "score": 0.86,
      "weight": 1.3,
      "confidence": 0.81,
      "status": "pass",
      "deductions": [
        {
          "code": "minor_phase_ambiguity",
          "severity": "minor",
          "message": "Venus illumination is broadly plausible, but the visible phase is slightly ambiguous for the requested evening-sky framing."
        }
      ]
    },
    "planetRenderingAccuracy": {
      "score": 0.9,
      "weight": 1.2,
      "confidence": 0.84,
      "status": "pass",
      "deductions": []
    },
    "brandConsistency": {
      "score": 0.8,
      "weight": 1.1,
      "confidence": 0.76,
      "status": "warning",
      "deductions": [
        {
          "code": "slightly_excessive_glow",
          "severity": "minor",
          "message": "The rim glow is stronger than the preferred restrained Drashyam documentary aesthetic."
        }
      ]
    },
    "composition": {
      "score": 0.84,
      "weight": 1.0,
      "confidence": 0.79,
      "status": "pass",
      "deductions": []
    },
    "visualHierarchy": {
      "score": 0.88,
      "weight": 0.9,
      "confidence": 0.8,
      "status": "pass",
      "deductions": []
    },
    "typography": {
      "score": 0.76,
      "weight": 0.8,
      "confidence": 0.72,
      "status": "warning",
      "deductions": [
        {
          "code": "small_secondary_text",
          "severity": "minor",
          "message": "Secondary text may be difficult to read on small mobile thumbnails."
        }
      ]
    },
    "observationCard": {
      "score": 0.83,
      "weight": 0.8,
      "confidence": 0.77,
      "status": "pass",
      "deductions": []
    },
    "labelQuality": {
      "score": 1.0,
      "weight": 0.4,
      "confidence": 0.9,
      "status": "not_applicable",
      "deductions": []
    },
    "platformOptimization": {
      "score": 0.79,
      "weight": 1.0,
      "confidence": 0.74,
      "status": "warning",
      "deductions": [
        {
          "code": "right_edge_crop_risk",
          "severity": "minor",
          "message": "The observation card is close to the right safe margin for some cropped social previews."
        }
      ]
    },
    "readability": {
      "score": 0.81,
      "weight": 1.0,
      "confidence": 0.78,
      "status": "pass",
      "deductions": []
    },
    "scientificCredibility": {
      "score": 0.87,
      "weight": 1.2,
      "confidence": 0.8,
      "status": "pass",
      "deductions": []
    },
    "documentaryAesthetic": {
      "score": 0.78,
      "weight": 0.9,
      "confidence": 0.75,
      "status": "warning",
      "deductions": [
        {
          "code": "decorative_particles",
          "severity": "minor",
          "message": "Decorative particles add visual energy but slightly reduce documentary restraint."
        }
      ]
    },
    "overallProductionQuality": {
      "score": 0.86,
      "weight": 1.2,
      "confidence": 0.82,
      "status": "pass",
      "deductions": []
    }
  },
  "warnings": [
    {
      "code": "mobile_readability_risk",
      "message": "Secondary text may be small on mobile thumbnails.",
      "category": "typography"
    },
    {
      "code": "brand_restraint_risk",
      "message": "Glow and particles are slightly more decorative than preferred.",
      "category": "brandConsistency"
    }
  ],
  "criticalIssues": [],
  "recommendations": [
    {
      "code": "reduce_decorative_effects_next_generation",
      "message": "If regenerated, request a more restrained documentary finish with less glow and fewer particles."
    },
    {
      "code": "increase_secondary_text_size",
      "message": "Increase secondary text size or simplify copy for thumbnail usage."
    }
  ],
  "missingExpectedElements": [],
  "ignoredConstraints": [
    {
      "code": "safe_margin_near_limit",
      "message": "The observation card respects safe margins but is close to the right boundary."
    }
  ],
  "providerInformation": {
    "providerName": "Azure Image2",
    "providerAssetId": "provider_asset_placeholder",
    "providerModelVersion": "provider_version_placeholder",
    "providerNotes": [
      "Typography fidelity should be monitored across repeated generations."
    ]
  },
  "versions": {
    "visualCreativeDirector": "3.2A",
    "brandDesignSystem": "3.2B",
    "planetRenderingRules": "3.2C",
    "creativeDirectionLanguage": "3.2D",
    "promptComposerV2": "3.2E",
    "creativeQualityScoringEngine": "3.2F",
    "thresholdProfile": "default-observation-v1"
  },
  "trace": {
    "eventId": "event_venus_evening_2026_001",
    "cdlId": "cdl_venus_evening_001",
    "promptPackageId": "prompt_pkg_venus_evening_001",
    "scoringRunId": "quality_run_venus_evening_001"
  }
}
```

## 10. Explainability

Every major deduction should be accompanied by a human-readable explanation. Reviewers should be able to understand why a score changed without inspecting internal implementation details.

Explanations should include:

- What was expected.
- What was observed.
- Why the difference matters.
- Severity of the issue.
- Whether regeneration, manual review, or acceptance is recommended.
- Which category and upstream constraint are affected when traceable.

Explainability is required for trust, threshold tuning, provider comparison, future analytics, and human review workflows. Numeric scores without explanations are not sufficient for publication-quality evaluation.

## 11. Integration

### Inputs consumed by the engine

The Creative Quality Scoring Engine consumes existing V3.2 artifacts and metadata without mutating them:

- **V3.2A VisualCreativeDirector:** intended scene strategy, visual hierarchy, composition intent, creative rationale, and publication purpose.
- **V3.2B BrandDesignSystem:** Drashyam color discipline, typography expectations, documentary tone, overlay behavior, and brand constraints.
- **V3.2C PlanetRenderingRules:** planet-specific rendering expectations, astronomical correctness rules, object constraints, and scientific plausibility requirements.
- **V3.2D CDL:** provider-neutral creative direction, subject hierarchy, camera, lighting, atmosphere, typography, labels, observation-card intent, and avoid-list constraints.
- **V3.2E PromptComposerV2 metadata:** provider profile, prompt package identifiers, prompt diagnostics, unsupported capabilities, translated constraints, and provider execution context.

The engine also consumes the generated visual asset, target platform metadata, publication context, scoring threshold profile, and scoring engine version.

### Results consumed by downstream systems

Quality reports may be consumed by:

- **Publication workflow:** decide whether to approve, warn, regenerate, request manual review, or reject an asset.
- **Future analytics:** compare providers, prompts, CDL patterns, event families, threshold settings, and recurring failure categories.
- **Monitoring dashboards:** track quality trends, provider regressions, brand consistency, astronomy failure rates, and production quality over time.
- **Human review tools:** show score summaries, visual issue categories, explanations, recommendations, and traceability to upstream decisions.

V3.2F should not force a pipeline phase change. Initial integration should allow quality reports to be generated and stored without blocking publication.

## 12. Extensibility

Future quality dimensions should be added without changing existing scoring interfaces. The `categoryScores` object should allow additive category keys, and consumers should ignore unknown categories they do not understand while preserving them in stored reports.

A new module should define:

- Category identifier.
- Purpose.
- Applicability conditions.
- Score range.
- Default weight.
- Confidence behavior.
- Deduction codes.
- Warning and critical issue mappings.
- Version metadata.

Possible future modules include:

- Accessibility.
- Localization quality.
- Animation quality.
- Video frame consistency.
- Multi-image gallery consistency.
- Cultural-region suitability.
- Caption and subtitle visual integration.
- A/B testing prediction quality.
- Cross-platform asset-family consistency.

## 13. Non-goals

V3.2F explicitly does not include:

- Implementation code.
- Computer vision implementation.
- AI model selection.
- Provider SDK integration.
- Image generation.
- Prompt generation.
- Pipeline phase changes.
- Azure Image2 changes.
- Narration changes.
- Rendering pipeline changes.
- Prompt changes.
- Replacement of required human editorial review.

## 14. Migration Plan

The Creative Quality Scoring Engine should be introduced gradually:

1. **Observation mode:** Generate quality reports but do not block publication or alter existing V3.1 behavior.
2. **Manual-review comparison:** Compare engine scores and explanations with human reviewer judgments across event families, providers, and asset types.
3. **Threshold tuning:** Adjust category weights, warning thresholds, critical issue mappings, and confidence requirements based on observed outcomes.
4. **Dashboard monitoring:** Track recurring failures such as typography issues, unsafe margins, off-brand effects, planet inaccuracies, and provider-specific regressions.
5. **Advisory mode:** Surface recommendations to reviewers while still requiring human or existing workflow approval.
6. **Feature-flagged enforcement:** Enable automated publication decisions only behind a feature flag after confidence is established.
7. **Backward-compatible fallback:** Retain V3.1 behavior whenever the feature flag is disabled, the scoring engine is unavailable, confidence is too low, or manual review is required.

Automated blocking should not be enabled until scores are validated against manual review and publication outcomes.

## 15. Acceptance Criteria

This architecture is accepted when it satisfies the following criteria:

- Documentation-only.
- No code changes.
- Provider-independent.
- Explainable scoring.
- Reusable scoring model.
- Compatible with V3.2A-E.
- No implementation ambiguity.
- Does not modify existing pipeline behavior.
- Does not introduce implementation code.
- Does not change Azure Image2 integration.
- Does not change narration.
- Does not change rendering pipeline.
- Does not change prompts.
- Suitable as the canonical quality evaluation architecture for future Drashyam visual assets.
