# V3.2G — Visual Intelligence Contract Specification

## 1. Purpose

V3.2G consolidates the V3.2A–V3.2F Visual Intelligence architecture work into a stable, implementation-facing contract specification for Drashyam V3.3. Its purpose is to freeze the boundaries, payload shapes, ownership rules, versioning strategy, feature flag behavior, fallback expectations, diagnostics, and observability requirements needed to implement the system with minimal ambiguity.

This document is a documentation-only architecture specification. It introduces no implementation code, DTO classes, interface files, provider calls, prompt changes, validation behavior, rendering behavior, or pipeline phase changes.

V3.2G should be treated as the implementation contract bridge between:

- V3.2 architecture specifications, which define intent and responsibilities.
- V3.3 implementation work, which will introduce DTOs, engines, provider profiles, feature flag wiring, and observation-mode scoring.

## 2. Architecture Position

Visual Intelligence sits after Event Intelligence has determined the astronomy event context and before provider-specific image generation and publication decisioning.

```text
Event Intelligence
→ VisualCreativeDirector
→ CDL
→ CreativeDirectionContract
→ PromptComposerV2
→ PromptPackage
→ Image Provider
→ CreativeQualityScoringEngine
→ QualityReport
→ Publication Decision
```

### Stage responsibilities

| Stage | Responsibility |
| --- | --- |
| Event Intelligence | Determines event context, event family, astronomy facts, visibility context, educational focus, and platform intent. |
| VisualCreativeDirector | Converts event intelligence into visual strategy and creative intent. |
| CDL | Encodes visual intent in provider-neutral Creative Direction Language. |
| CreativeDirectionContract | Freezes the implementation-facing bundle of creative intent, brand rules, rendering rules, prompt requirements, and quality targets. |
| PromptComposerV2 | Converts the contract into provider-ready prompt packages without changing creative intent. |
| PromptPackage | Carries provider-ready prompt text, negative constraints, provider hints, metadata, and diagnostics. |
| Image Provider | Generates visual assets using provider-specific capabilities. |
| CreativeQualityScoringEngine | Scores generated assets against the contract, prompt package, brand rules, and rendering rules. |
| QualityReport | Reports scoring outcomes, findings, diagnostics, and recommended decision. |
| Publication Decision | Determines whether the asset is accepted, warned, blocked, regenerated, or falls back to V3.1 behavior. |

## 3. Contract Ownership

Each contract has a single conceptual owner. Other modules may read contracts, enrich downstream payloads, or report diagnostics, but they must not silently redefine upstream contract semantics.

| Contract | Owning module | Notes |
| --- | --- | --- |
| CDL | VisualCreativeDirector / Creative Direction Language module | Provider-neutral visual intent. |
| CreativeDirectionContract | VisualCreativeDirector | Frozen bundle passed to PromptComposerV2 and scoring. |
| BrandRules | BrandDesignSystem | Drashyam visual identity, tone, color, typography policy, and restraint. |
| PlanetRenderingRules | PlanetRenderingRules module | Astronomy rendering constraints and planet-specific visual rules. |
| TypographyRules | BrandDesignSystem | Text hierarchy, label discipline, readability, and safe text behavior. |
| ObservationCardRules | BrandDesignSystem / PlanetRenderingRules | Observation card layout, data display discipline, safe zones, and scientific clarity. |
| ProviderHints | PromptComposerV2 / provider profile layer | Provider capability-aware translation hints. |
| QualityTargets | CreativeQualityScoringEngine with upstream policy input | Thresholds and dimensions to evaluate generated assets. |
| NegativeConstraints | VisualCreativeDirector and PromptComposerV2 | Prohibited visual, scientific, brand, text, and provider behaviors. |
| PromptPackage | PromptComposerV2 | Provider-ready prompt payload and prompt diagnostics. |
| QualityReport | CreativeQualityScoringEngine | Provider-independent quality evaluation output. |
| PublicationDecision | Publication policy / pipeline decision layer | Final action recommendation or blocking decision. |

## 4. Core Contract Schemas

The structures below are implementation-ready JSON contract shapes for V3.3 DTO design. Field names are normative unless explicitly marked as examples. V3.3 may add internal DTO annotations, validation attributes, or language-specific types, but the serialized shape should remain compatible with these schemas.

### 4.1 CreativeDirectionContract

The CreativeDirectionContract is the primary implementation-facing bundle consumed by PromptComposerV2 and CreativeQualityScoringEngine.

```json
{
  "contractVersion": "3.2G",
  "contractId": "cdc_2026_001",
  "createdAt": "2026-07-05T00:00:00Z",
  "sourceEventId": "event_2026_mars_moon_conjunction",
  "eventFamily": "planetConjunction",
  "targetPlatform": "youtubeThumbnail",
  "language": "en",
  "aspectRatio": "16:9",
  "visualIntent": {
    "primarySubject": "Mars and Moon conjunction",
    "secondarySubjects": ["night sky", "subtle star field"],
    "narrativeRole": "premium educational astronomy thumbnail",
    "mood": "aweInspiring",
    "composition": "large Moon at left, Mars as distinct red disk at right, restrained observation card at lower third"
  },
  "cdl": {
    "cdlVersion": "3.2D",
    "documentId": "cdl_2026_001",
    "directives": []
  },
  "brandRules": {},
  "planetRenderingRules": {},
  "typographyRules": {},
  "observationCardRules": {},
  "providerHints": {},
  "qualityTargets": {},
  "negativeConstraints": {},
  "extensionFields": {}
}
```

Required fields: `contractVersion`, `contractId`, `createdAt`, `sourceEventId`, `eventFamily`, `targetPlatform`, `language`, `aspectRatio`, `visualIntent`, `cdl`, `brandRules`, `planetRenderingRules`, `qualityTargets`, and `negativeConstraints`.

### 4.2 BrandRules

BrandRules define Drashyam visual identity expectations.

```json
{
  "brandVersion": "3.2B",
  "brandName": "Drashyam",
  "visualTone": "premiumDocumentary",
  "colorPalette": {
    "primary": ["deepSpaceBlack", "astronomicalBlue"],
    "accent": ["softGold", "signalRed"],
    "avoid": ["neonRainbow", "oversaturatedComicColor"]
  },
  "stylePrinciples": [
    "cinematic restraint",
    "scientific credibility",
    "premium educational clarity"
  ],
  "logoPolicy": {
    "usage": "optional",
    "placement": "safeCornerOnly",
    "minimumContrast": 4.5
  },
  "clutterPolicy": "minimal",
  "extensionFields": {}
}
```

### 4.3 PlanetRenderingRules

PlanetRenderingRules define astronomical rendering expectations.

```json
{
  "renderingRulesVersion": "3.2C",
  "eventFamily": "planetConjunction",
  "subjects": [
    {
      "bodyName": "Mars",
      "bodyType": "planet",
      "requiredShape": "sphericalDisk",
      "colorBehavior": "rustRedNaturalistic",
      "surfaceDetail": "subtle",
      "illumination": "physicallyPlausible",
      "scalePolicy": "educationalNotLiteral",
      "forbiddenArtifacts": ["rings", "gasGiantBands", "cartoonFace"]
    },
    {
      "bodyName": "Moon",
      "bodyType": "moon",
      "requiredShape": "circularDisk",
      "surfaceDetail": "visibleCraters",
      "illumination": "phaseConsistentWhenKnown",
      "scalePolicy": "thumbnailReadable",
      "forbiddenArtifacts": ["extraMoons", "fictionalColors"]
    }
  ],
  "backgroundRules": {
    "starField": "subtle",
    "milkyWay": "allowedIfNotDistracting",
    "earthHorizon": "optional"
  },
  "extensionFields": {}
}
```

### 4.4 TypographyRules

TypographyRules define text usage, readability, and hierarchy.

```json
{
  "brandVersion": "3.2B",
  "typographySystem": "drashyamPremiumSans",
  "textPolicy": "minimalEssentialTextOnly",
  "allowedTextElements": ["title", "date", "observationLabel"],
  "titleRules": {
    "maxWords": 6,
    "caseStyle": "titleCase",
    "minimumContrast": 4.5,
    "mobileReadable": true
  },
  "labelRules": {
    "allowScientificLabels": true,
    "maxLabels": 3,
    "avoidOverlappingAstronomicalBodies": true
  },
  "forbiddenText": ["clickbait", "fake urgency", "unverified date claims"],
  "extensionFields": {}
}
```

### 4.5 ObservationCardRules

ObservationCardRules define structured observation data presentation.

```json
{
  "brandVersion": "3.2B",
  "cardUsage": "optionalWhenHelpful",
  "placement": "lowerThirdSafeZone",
  "maxFields": 4,
  "allowedFields": ["date", "timeWindow", "direction", "visibility"],
  "visualStyle": {
    "background": "translucentDarkPanel",
    "border": "subtle",
    "cornerRadius": "medium",
    "density": "low"
  },
  "dataIntegrity": {
    "requireVerifiedValues": true,
    "allowUnknownValues": false
  },
  "extensionFields": {}
}
```

### 4.6 ProviderHints

ProviderHints carry provider-neutral preferences and provider-specific capability hints.

```json
{
  "providerProfileVersion": "3.2E-azure-image2-v1",
  "preferredProvider": "azureImage2",
  "capabilitiesRequired": ["textAwarePrompting", "negativePromptSupport", "aspectRatioControl"],
  "promptStyle": "descriptiveCinematic",
  "renderingHints": {
    "detailLevel": "high",
    "textRenderingRisk": "medium",
    "avoidProviderOverstylization": true
  },
  "providerParameters": {
    "aspectRatio": "16:9",
    "quality": "high"
  },
  "extensionFields": {}
}
```

### 4.7 QualityTargets

QualityTargets define expected scoring dimensions and thresholds.

```json
{
  "qualityReportVersion": "3.2F",
  "mode": "observation",
  "overallThreshold": 0.82,
  "blockingThreshold": 0.65,
  "dimensions": [
    {
      "name": "creativeIntentMatch",
      "minimumScore": 0.8,
      "weight": 0.2,
      "blocking": false
    },
    {
      "name": "astronomicalPlausibility",
      "minimumScore": 0.9,
      "weight": 0.3,
      "blocking": true
    },
    {
      "name": "brandCompliance",
      "minimumScore": 0.8,
      "weight": 0.2,
      "blocking": false
    },
    {
      "name": "textReadability",
      "minimumScore": 0.75,
      "weight": 0.15,
      "blocking": false
    },
    {
      "name": "platformSuitability",
      "minimumScore": 0.75,
      "weight": 0.15,
      "blocking": false
    }
  ],
  "extensionFields": {}
}
```

### 4.8 NegativeConstraints

NegativeConstraints define prohibited output characteristics.

```json
{
  "scientific": [
    "do not show Mars with rings",
    "do not add fictional planets",
    "do not imply unsafe direct solar observation"
  ],
  "brand": [
    "avoid neon color treatment",
    "avoid meme or cartoon styling",
    "avoid cluttered overlays"
  ],
  "typography": [
    "avoid misspelled labels",
    "avoid dense paragraphs",
    "avoid tiny unreadable text"
  ],
  "provider": [
    "avoid over-sharpened AI artifacts",
    "avoid duplicated moons",
    "avoid watermark-like markings"
  ],
  "extensionFields": {}
}
```

### 4.9 PromptPackage

PromptPackage is the PromptComposerV2 output passed to an image provider abstraction.

```json
{
  "promptComposerVersion": "3.2E",
  "promptPackageId": "pkg_2026_001",
  "createdAt": "2026-07-05T00:00:00Z",
  "contractId": "cdc_2026_001",
  "providerName": "azureImage2",
  "providerProfileVersion": "3.2E-azure-image2-v1",
  "positivePrompt": "Create a premium documentary astronomy thumbnail showing the Moon and Mars conjunction in a realistic night sky...",
  "negativePrompt": "No fictional planets, no cartoon styling, no neon clutter, no unreadable text...",
  "promptSections": {
    "subject": "Moon and Mars conjunction",
    "composition": "large Moon left, Mars right, subtle observation card lower third",
    "style": "premium cinematic documentary",
    "constraints": "scientifically plausible, restrained Drashyam brand"
  },
  "providerParameters": {
    "aspectRatio": "16:9",
    "quality": "high"
  },
  "diagnostics": [],
  "extensionFields": {}
}
```

### 4.10 QualityReport

QualityReport is the scoring output produced after image generation.

```json
{
  "qualityReportVersion": "3.2F",
  "qualityReportId": "qr_2026_001",
  "createdAt": "2026-07-05T00:00:00Z",
  "contractId": "cdc_2026_001",
  "promptPackageId": "pkg_2026_001",
  "providerName": "azureImage2",
  "providerProfileVersion": "3.2E-azure-image2-v1",
  "mode": "observation",
  "overallScore": 0.86,
  "dimensionScores": [
    {
      "name": "astronomicalPlausibility",
      "score": 0.92,
      "passed": true,
      "findings": []
    },
    {
      "name": "textReadability",
      "score": 0.72,
      "passed": false,
      "findings": ["Observation card text may be too small on mobile."]
    }
  ],
  "diagnostics": [],
  "recommendedDecision": "warn",
  "extensionFields": {}
}
```

### 4.11 PublicationDecision

PublicationDecision records the final publication recommendation or action.

```json
{
  "decisionId": "pd_2026_001",
  "createdAt": "2026-07-05T00:00:00Z",
  "contractId": "cdc_2026_001",
  "qualityReportId": "qr_2026_001",
  "decision": "publishWithWarning",
  "reason": "Overall score passed, but mobile text readability warning was reported.",
  "blocking": false,
  "fallbackApplied": false,
  "fallbackReason": null,
  "requiresHumanReview": false,
  "diagnostics": [],
  "extensionFields": {}
}
```

## 5. Versioning Strategy

Visual Intelligence contracts must carry explicit version fields so V3.3 implementations can log, compare, migrate, and safely ignore unsupported additions.

| Version field | Applies to | Owner |
| --- | --- | --- |
| `contractVersion` | CreativeDirectionContract envelope | VisualCreativeDirector |
| `cdlVersion` | CDL payload | Creative Direction Language module |
| `brandVersion` | BrandRules, TypographyRules, ObservationCardRules | BrandDesignSystem |
| `renderingRulesVersion` | PlanetRenderingRules | PlanetRenderingRules module |
| `promptComposerVersion` | PromptPackage | PromptComposerV2 |
| `providerProfileVersion` | ProviderHints, PromptPackage, provider abstraction metadata | Provider profile layer |
| `qualityReportVersion` | QualityTargets and QualityReport | CreativeQualityScoringEngine |

Versioning rules:

- Required fields must not be removed without a deprecation window.
- Additive changes are the default compatibility mechanism.
- Unknown fields must be ignored safely by readers that do not understand them.
- Deprecated fields must remain readable for at least one major version.
- Version values must be logged at contract creation, prompt composition, provider request, quality scoring, fallback, and publication decision boundaries.
- Version mismatch should produce diagnostics rather than immediate failure unless a required capability is unavailable.
- New enum values should be treated as unknown-but-nonfatal unless the value controls safety, blocking, or provider execution.

## 6. Serialization Rules

- Format: JSON.
- Encoding: UTF-8.
- Naming: camelCase for all serialized field names.
- Timestamps: ISO-8601 UTC timestamps, for example `2026-07-05T00:00:00Z`.
- Nullable handling: use `null` only when a field is explicitly known to be empty or not applicable.
- Optional fields: omit optional fields when no value exists and no default is defined.
- Default values: defaults must be documented by the owning module and should be applied before downstream serialization when practical.
- Enum naming: lower camel case string values, for example `planetConjunction`, `youtubeThumbnail`, `publishWithWarning`, and `premiumDocumentary`.
- Future extension fields: each major contract may include `extensionFields` as an object for additive metadata that older readers can ignore.
- Arrays: preserve order when order affects prompt composition, scoring priority, or diagnostics presentation.
- Numeric scores: use decimal values from `0.0` to `1.0` unless a contract explicitly defines another range.
- Identifiers: use stable string IDs; do not rely on database integer IDs in serialized provider-facing contracts.

## 7. Feature Flags

V3.2G defines first-pass feature flags for safe V3.3 rollout.

| Feature flag | When enabled | Expected behavior when off |
| --- | --- | --- |
| `UseVisualCreativeDirector` | VisualCreativeDirector produces visual intent and CDL candidates. | V3.1 prompt planning remains the source of visual intent. |
| `UseCDL` | CDL becomes the provider-neutral creative intent artifact. | No CDL is required; existing V3.1 prompt flow remains usable. |
| `UseCreativeDirectionContract` | CreativeDirectionContract is emitted and passed downstream. | Prompt composition and image generation use V3.1 behavior. |
| `UsePromptComposerV2` | PromptComposerV2 creates PromptPackage payloads. | Existing prompt composer / Azure Image2 prompt flow remains unchanged. |
| `UseProviderProfiles` | Provider profiles influence prompt translation and provider parameters. | Existing provider settings remain in effect. |
| `UseQualityScoring` | CreativeQualityScoringEngine emits QualityReport in observation or blocking mode. | No new quality report is required; existing publication flow continues. |
| `UseQualityScoringBlocking` | Quality scoring may block, regenerate, or alter publication decision. | Quality scoring, if enabled, runs in observation mode only and must not block. |
| `UseExperimentalRenderingRules` | Experimental rendering constraints may be included when explicitly allowed. | Only stable rendering rules should be used. |

Feature flags must be logged at each Visual Intelligence boundary. If a required upstream flag is off, downstream V3.2/V3.3 modules must skip cleanly or operate in passive observation mode without changing V3.1 behavior.

## 8. Compatibility and Fallback

- V3.1 remains the default behavior until Visual Intelligence flags are explicitly enabled.
- New Visual Intelligence behavior must be additive.
- Existing prompt flow must remain usable without CDL, CreativeDirectionContract, PromptComposerV2, provider profiles, or QualityReport.
- Failed V3.2 contracts must fall back to V3.1 behavior.
- Quality scoring initially runs in observation mode.
- Observation-mode scoring must not block publication, regenerate assets, or alter existing release decisions.
- Fallbacks should preserve diagnostics so failures can be reviewed without impacting production output.
- If a contract is partially available, downstream modules should consume only safe, recognized fields and report unsupported or missing sections as diagnostics.

## 9. Provider Abstraction

V3.2G defines a provider-neutral image provider contract conceptually. This is architecture only and not interface code.

A future provider adapter should conceptually receive and emit:

```json
{
  "providerName": "azureImage2",
  "providerProfileVersion": "3.2E-azure-image2-v1",
  "promptPackage": {},
  "providerMetadata": {
    "region": "configuredExternally",
    "model": "configuredExternally",
    "capabilities": ["aspectRatioControl", "negativePromptSupport"]
  },
  "generationRequest": {
    "requestId": "gen_req_2026_001",
    "createdAt": "2026-07-05T00:00:00Z",
    "parameters": {}
  },
  "generationResult": {
    "resultId": "gen_res_2026_001",
    "status": "succeeded",
    "assetReferences": [],
    "providerResponseMetadata": {}
  },
  "diagnostics": []
}
```

The abstraction should isolate provider-specific parameters, response metadata, capability negotiation, and diagnostics while preserving provider-neutral Visual Intelligence contracts upstream.

## 10. Extension Mechanism

Future modules should extend Visual Intelligence through additive contract sections, `extensionFields`, provider capability negotiation, and explicit version fields. New modules must not require older readers to understand new fields before they can safely process existing contracts.

Potential future contract sections include:

- `animationRules`
- `videoRules`
- `galleryRules`
- `posterRules`
- `localizationRules`
- `accessibilityRules`
- `printRules`
- `arRules`
- `vrRules`

Extension rules:

- Add new sections as optional fields first.
- Include a module-specific version field when the section becomes implementation-facing.
- Provide fallback behavior when the section is absent or unsupported.
- Avoid changing the meaning of existing fields.
- Prefer capability discovery over hard-coded provider assumptions.

## 11. Validation Expectations

V3.2G does not implement validation. V3.3 and later should eventually validate:

- Required fields.
- Enum values.
- Aspect ratio format and supported values.
- Language code format and supported languages.
- Target platform values.
- Event family values.
- Provider capability support.
- Quality target completeness.
- Timestamp parseability.
- Version field presence.
- Score ranges.
- Diagnostic field consistency.

Validation should produce diagnostics and safe fallback by default. Hard failures should be reserved for cases where continuing could produce unsafe, misleading, or provider-invalid output.

## 12. Error Handling and Diagnostics

Diagnostics should use a standard shape across contract creation, prompt composition, provider abstraction, quality scoring, and publication decisioning.

```json
{
  "severity": "warning",
  "code": "missingProviderCapability",
  "message": "The selected provider profile does not advertise negative prompt support.",
  "sourceModule": "PromptComposerV2",
  "affectedField": "providerHints.capabilitiesRequired",
  "recommendation": "Continue without a provider-native negative prompt and include constraints in the positive prompt.",
  "fallbackApplied": true
}
```

Diagnostic fields:

| Field | Meaning |
| --- | --- |
| `severity` | `info`, `warning`, `error`, or `critical`. |
| `code` | Stable machine-readable diagnostic code. |
| `message` | Human-readable explanation. |
| `sourceModule` | Module that produced the diagnostic. |
| `affectedField` | Contract path most closely related to the issue. |
| `recommendation` | Suggested human or automated next step. |
| `fallbackApplied` | Whether fallback behavior was used. |

## 13. Logging and Observability

Visual Intelligence implementation should log enough structured metadata to audit creative decisions without logging sensitive secrets or oversized provider payloads.

Log at minimum:

- Contract versions: `contractVersion`, `cdlVersion`, `brandVersion`, `renderingRulesVersion`, `promptComposerVersion`, `providerProfileVersion`, and `qualityReportVersion`.
- Feature flags and evaluated flag values.
- Provider profile and provider capability summary.
- Prompt diagnostics, prompt package ID, and prompt section availability.
- Quality report summary, including overall score, failed dimensions, recommended decision, and mode.
- Fallback reason and fallback boundary.
- Contract IDs, prompt package IDs, quality report IDs, and publication decision IDs.
- Event family, platform, language, and aspect ratio.

Logs should avoid full prompt text by default unless an explicit debug mode is enabled and safe for the environment.

## 14. Integration with V3.2A-F

V3.2G consolidates the prior V3.2 Visual Intelligence documents as follows:

| Prior specification | Consolidated role in V3.2G |
| --- | --- |
| V3.2A VisualCreativeDirector | Owns visual strategy, CDL creation, and CreativeDirectionContract assembly. |
| V3.2B BrandDesignSystem | Owns BrandRules, TypographyRules, ObservationCardRules, and brand compliance expectations. |
| V3.2C PlanetRenderingRules | Owns PlanetRenderingRules and scientific rendering constraints. |
| V3.2D Creative Direction Language | Defines the provider-neutral creative intent payload embedded in the contract. |
| V3.2E PromptComposerV2 | Owns PromptPackage creation and provider profile translation behavior. |
| V3.2F CreativeQualityScoringEngine | Owns QualityTargets, QualityReport, scoring diagnostics, and observation-mode recommendation semantics. |

V3.2G does not replace those documents. It freezes their implementation-facing contract boundaries so V3.3 can implement them consistently.

## 15. Non-goals

V3.2G explicitly does not include:

- No implementation code.
- No DTO classes.
- No interface files.
- No Azure calls.
- No image generation changes.
- No prompt replacement yet.
- No validation implementation.
- No pipeline phase changes.
- No narration changes.
- No SRT changes.
- No TTS changes.
- No rendering logic changes.
- No publication behavior changes.

## 16. Migration Plan

V3.3 should implement Visual Intelligence in a staged, flag-gated sequence:

1. **V3.3A Contracts and DTOs**
   - Implement DTOs matching V3.2G serialized contracts.
   - Add version fields, extension fields, diagnostic structures, and serialization tests.
   - Keep all behavior disabled by default.

2. **V3.3B VisualCreativeDirector engine**
   - Implement VisualCreativeDirector output generation.
   - Emit CDL and CreativeDirectionContract behind feature flags.
   - Preserve V3.1 behavior when flags are off or contract generation fails.

3. **V3.3C PromptComposerV2**
   - Translate CreativeDirectionContract into PromptPackage.
   - Add provider profile support.
   - Keep existing prompt flow as fallback.

4. **V3.3D Quality scoring observation mode**
   - Generate QualityReport without blocking publication.
   - Log scores, diagnostics, and recommended decisions.
   - Do not alter publication decisions unless blocking mode is explicitly enabled in a later rollout.

5. **V3.3E Feature flag integration**
   - Wire feature flags end to end.
   - Confirm each disabled state cleanly preserves V3.1 behavior.
   - Add fallback logging and operational dashboards.

## 17. Acceptance Criteria

- Documentation-only PR.
- Existing V3.1 RC remains unchanged.
- Compatible with V3.2A-F.
- Implementation contracts are clear.
- Feature flag behavior documented.
- Fallback behavior documented.
- No code files modified.
- V3.3 implementation can proceed from this specification.
- Existing pipeline behavior is not modified.
- Existing prompts are not modified.
- Azure Image2 integration is not modified.
- Narration, SRT, TTS, validation, and rendering logic are not modified.
