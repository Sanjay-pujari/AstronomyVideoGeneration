# V3.2D — Creative Direction Language (CDL)

## 1. Purpose

Creative Direction Language (CDL) is the provider-neutral intermediate creative representation between `VisualCreativeDirector` and `PromptComposerV2`.

CDL expresses **creative intent**, not final prompt text. It captures what a Drashyam visual should communicate, prioritize, protect, and avoid before any downstream component translates that intent into provider-specific wording, renderer instructions, or quality checks.

In V3.2D, CDL is a documentation-only architecture proposal. It does not change V3.1 release-candidate behavior, prompts, Azure Image2 integration, narration, SRT, TTS, validation, rendering, or pipeline phase ordering.

## 2. Why CDL Exists

CDL exists to make Drashyam's creative decisions explicit, portable, inspectable, and reusable before they become provider-specific prompts.

It is needed because CDL:

- **Avoids hardcoding creative logic into provider prompts.** Subject hierarchy, observation-card choices, typography safety, and astronomy rendering rules should not be buried inside long prompt strings.
- **Supports Azure Image2 and future image providers.** CDL is written above any provider API so the same visual intent can be compiled differently for Azure Image2, future image models, local renderers, or hybrid workflows.
- **Makes creative decisions testable.** A structured artifact can be logged, snapshot-tested, reviewed, compared, and scored before and after prompt composition.
- **Keeps `VisualCreativeDirector` renderer-neutral.** The director decides creative intent without knowing whether the final asset is produced by an image model, template renderer, canvas renderer, or another system.
- **Keeps `PromptComposerV2` provider-specific but business-light.** Prompt composition can focus on translating structured intent into the best provider wording instead of inventing family, brand, or astronomy decisions.
- **Reduces future rework.** New providers, aspect ratios, platforms, and quality scoring systems can consume the same CDL shape instead of requiring repeated rewrites of creative rules.

## 3. Design Principles

### Provider-neutral

CDL must not contain Azure-specific, OpenAI-specific, model-specific, or vendor-specific prompt phrasing. It describes intent that can be translated into many provider dialects later.

### Renderer-independent

CDL must not require FFmpeg, ImageSharp, browser canvas, CSS, SVG, template engines, or image model APIs. It describes visual decisions, not rendering mechanics.

### Human creative director language

CDL should read like structured guidance from a human creative director: clear hierarchy, mood, framing, restraint, and design rationale rather than mechanical prompt fragments.

### Structured but expressive

CDL should be JSON-serializable and implementation-ready while still allowing nuanced creative direction such as tone, atmosphere, realism, label discipline, and typography safety.

### Family-aware

CDL should preserve event-family context such as `PlanetPairing`, `PlanetGrouping`, `MeteorShower`, `SolarEclipse`, `LunarEclipse`, and future families.

### Brand-aware

CDL should carry Drashyam brand decisions from the Brand Design System: premium documentary tone, restrained color, safe spacing, readable typography, and non-sensational visual treatment.

### Astronomy-aware

CDL should preserve astronomy truthfulness expectations, including realistic planet appearance, plausible relative emphasis, credible sky context, and avoidance of misleading celestial geometry.

### Platform-aware

CDL should adapt to platform and aspect-ratio needs, including YouTube Shorts safe zones, thumbnail readability, mobile compression, and text placement discipline.

### Backward compatible with V3.1

CDL is additive. If not enabled, generated, consumed, or persisted, the V3.1 prompt flow and release-candidate behavior remain unchanged.

### Additive to V3.2A/B/C

CDL consumes the intent established by V3.2A VisualCreativeDirector, V3.2B Drashyam Brand Design System, and V3.2C Planet Rendering Rules. It does not replace those documents.

## 4. Position in Data Flow

```text
Event Intelligence
→ VisualCreativeDirector
→ Creative Direction Language
→ PromptComposerV2
→ Image Provider
```

### Flow Notes

1. **Event Intelligence** supplies facts: family, event type, objects, timing, region, visibility, and observation context.
2. **VisualCreativeDirector** interprets those facts with brand, platform, family, and astronomy rules.
3. **Creative Direction Language** stores the director's provider-neutral visual intent in a structured form.
4. **PromptComposerV2** translates CDL into provider-specific prompt text later.
5. **Image Provider** receives only the final provider-specific prompt or structured request, never raw business logic.

## 5. CDL Responsibilities

### CDL Does

CDL is responsible for capturing:

- **Hero subject:** the primary visual subject and how it should be treated.
- **Supporting subjects:** secondary celestial, environmental, or contextual elements.
- **Visual hierarchy:** what must dominate, support, or stay subtle.
- **Composition intent:** layout, balance, negative space, subject placement, and safe zones.
- **Camera/framing intent:** perceived lens, crop, point of view, distance, and editorial framing.
- **Lighting intent:** rim light, glow discipline, contrast, natural brightness, and readability.
- **Atmosphere intent:** mood, sky condition, depth, haze, star density, and documentary tone.
- **Typography intent:** title placement, script safety, density limits, readability, and multilingual layout constraints.
- **Observation card intent:** whether an observation card exists, where it sits, what information it prioritizes, and how it remains legible.
- **Label/annotation intent:** planet labels, leader-line discipline, minimal annotation strategy, and safe placement.
- **Planet rendering intent:** planet-specific appearance rules such as Jupiter cloud bands, Venus brightness, Saturn rings, Mars color, and scale restraint.
- **Brand design intent:** Drashyam color, tone, spacing, typography, premium feel, and anti-clutter rules.
- **Quality expectations:** target clarity, realism, mobile readability, astronomy credibility, and compression resilience.
- **Negative constraints:** visual mistakes, misleading imagery, provider failure modes, typography issues, and brand violations to avoid.

### CDL Does Not Do

CDL is not responsible for:

- Calling providers.
- Generating images.
- Replacing prompts directly.
- Validating image outputs.
- Changing V3.1 pipeline behavior.
- Depending on Azure.
- Selecting implementation libraries.
- Mutating narration, SRT, TTS, validation, rendering, publishing, or storage behavior.

## 6. Proposed CDL Structure

The proposed top-level CDL object is JSON-serializable and intentionally explicit:

```json
{
  "cdlVersion": "3.2D",
  "eventFamily": "PlanetPairing",
  "eventType": "planetary_conjunction",
  "language": {
    "primary": "hi",
    "secondary": "en",
    "scriptPolicy": "Hindi and English safe layout"
  },
  "platform": "YouTube Shorts",
  "aspectRatio": "9:16",
  "creativeIntent": {},
  "heroSubject": {},
  "supportingSubjects": [],
  "visualHierarchy": [],
  "composition": {},
  "framing": {},
  "lighting": {},
  "atmosphere": {},
  "typography": {},
  "observationCard": {},
  "labels": {},
  "astronomicalRendering": {},
  "brandDesign": {},
  "negativeConstraints": [],
  "qualityTargets": {},
  "providerHints": {}
}
```

## 7. Field Descriptions

| Field | Required | Purpose | Expected values | Example |
| --- | --- | --- | --- | --- |
| `cdlVersion` | Required | Identifies the CDL schema/version used for generation, logging, and compatibility checks. | Stable string version such as `3.2D`; future versions may use `3.2E`, `3.3`, or semver-like values. | `"3.2D"` |
| `eventFamily` | Required | Carries the reusable astronomy family that shaped the creative direction. | `PlanetPairing`, `PlanetGrouping`, `MeteorShower`, `NamedFullMoon`, `SolarEclipse`, `LunarEclipse`, `Comet`, `Occultation`, or future family names. | `"PlanetPairing"` |
| `eventType` | Required | Provides the more specific event classification from event intelligence. | Normalized event strings such as `planetary_conjunction`, `close_approach`, `meteor_shower_peak`, `total_lunar_eclipse`. | `"planetary_conjunction"` |
| `language` | Required | Defines language and script considerations without writing final copy. | Object with `primary`, optional `secondary`, `scriptPolicy`, and optional `copyDensity`. ISO language codes are preferred. | `{ "primary": "hi", "secondary": "en" }` |
| `platform` | Required | Identifies the distribution context that affects readability, safe zones, and visual density. | `YouTube Shorts`, `YouTube Thumbnail`, `YouTube Video`, `Instagram Reel`, `Gallery`, `Web`, or future platform labels. | `"YouTube Shorts"` |
| `aspectRatio` | Required | Defines the intended canvas ratio for composition and safe-zone planning. | String ratios such as `9:16`, `16:9`, `1:1`, `4:5`, `3:4`. | `"9:16"` |
| `creativeIntent` | Required | Summarizes the overall visual goal, tone, emotional promise, realism level, and viewer takeaway. | Object with fields such as `tone`, `mood`, `viewerPromise`, `realismLevel`, `assetRole`. | `{ "tone": "premium documentary" }` |
| `heroSubject` | Required | Defines the primary subject that should receive maximum visual attention. | Object with `name`, `type`, `role`, `visualTreatment`, `truthfulnessNotes`. | `{ "name": "Jupiter", "type": "planet" }` |
| `supportingSubjects` | Optional | Lists secondary subjects that support the story without overpowering the hero. | Array of subject objects with `name`, `type`, `role`, `priority`, `visualTreatment`. | `[{ "name": "Venus", "role": "supporting planet" }]` |
| `visualHierarchy` | Required | Declares the priority order of visual elements so downstream prompts preserve focus. | Ordered array or object describing primary, secondary, tertiary, and suppressed elements. | `["Jupiter", "Venus", "Observation card", "Title"]` |
| `composition` | Required | Captures layout, safe zones, negative space, balance, and placement intent. | Object with `layout`, `subjectPlacement`, `negativeSpace`, `safeZones`, `edgeDiscipline`. | `{ "layout": "vertical cinematic sky poster" }` |
| `framing` | Required | Captures camera/framing language such as crop, distance, viewpoint, and lens feel. | Object with `cameraLanguage`, `viewpoint`, `crop`, `depth`, optional `lensMood`. | `{ "cameraLanguage": "premium telephoto sky framing" }` |
| `lighting` | Required | Defines brightness, contrast, glow, rim light, and illumination discipline. | Object with `keyLight`, `glowTreatment`, `contrast`, `exposure`, `avoid`. | `{ "glowTreatment": "controlled natural planetary glow" }` |
| `atmosphere` | Required | Defines sky mood, clarity, haze, stars, horizon, and emotional environment. | Object with `sky`, `starDensity`, `haze`, `mood`, `environmentContext`. | `{ "sky": "deep pre-dawn navy" }` |
| `typography` | Required | Captures text design intent without final prompt text or generated copy. | Object with `titleZone`, `scriptSafety`, `density`, `readability`, `avoid`. | `{ "density": "low", "scriptSafety": "Hindi and English readable" }` |
| `observationCard` | Optional | Defines observation-card intent, contents, placement, and readability if the asset needs one. | Object with `enabled`, `placement`, `contentPriorities`, `style`, `safeArea`. | `{ "enabled": true, "placement": "lower safe zone" }` |
| `labels` | Optional | Defines label/annotation strategy for celestial subjects. | Object with `enabled`, `items`, `leaderLines`, `placementRules`, `density`. | `{ "enabled": true, "items": ["Jupiter", "Venus"] }` |
| `astronomicalRendering` | Required | Captures astronomy-specific rendering expectations and truthfulness constraints. | Object with subject rules, scale policy, color policy, geometry policy, and family-specific notes. | `{ "geometry": "perfect circular planets" }` |
| `brandDesign` | Required | Carries Drashyam brand intent from the Brand Design System. | Object with `palette`, `tone`, `spacing`, `overlayStyle`, `antiPatterns`. | `{ "tone": "premium astronomy documentary" }` |
| `negativeConstraints` | Required | Lists errors and unwanted outputs to prevent in prompt composition and future scoring. | Array of strings or categorized objects. Should remain provider-neutral. | `["no cartoon planets", "no misspelled text"]` |
| `qualityTargets` | Required | Defines measurable or reviewable expectations for generated visuals. | Object with `subjectClarity`, `mobileReadability`, `astronomyCredibility`, `brandFit`, `compositionQuality`. | `{ "mobileReadability": "high at Shorts preview size" }` |
| `providerHints` | Optional | Provides non-binding capability hints for prompt composition without provider-specific prompt text. | Object with booleans or capability tags such as `prefersShortPrompt`, `supportsNegativePrompt`, `supportsTypography`. | `{ "supportsNegativePrompt": true }` |

## 8. Sample CDL JSON

Example: `PlanetPairing` — Jupiter and Venus conjunction — YouTube Shorts `9:16` — Hindi/English safe layout.

```json
{
  "cdlVersion": "3.2D",
  "eventFamily": "PlanetPairing",
  "eventType": "planetary_conjunction",
  "language": {
    "primary": "hi",
    "secondary": "en",
    "scriptPolicy": "Hindi and English safe layout",
    "copyDensity": "low"
  },
  "platform": "YouTube Shorts",
  "aspectRatio": "9:16",
  "creativeIntent": {
    "assetRole": "vertical hero visual for astronomy short",
    "tone": "premium documentary",
    "mood": "calm wonder, clear observation guidance, cinematic anticipation",
    "viewerPromise": "Jupiter and Venus are easy to identify in the sky without sensational exaggeration",
    "realismLevel": "truthful cinematic astronomy illustration"
  },
  "heroSubject": {
    "name": "Jupiter",
    "type": "planet",
    "role": "hero subject",
    "priority": 1,
    "visualTreatment": {
      "shape": "perfect circular disk",
      "surface": "realistic warm beige cloud bands with subtle belts",
      "scale": "visually dominant but not absurdly oversized",
      "edge": "clean circular silhouette with gentle atmospheric rim light",
      "glow": "restrained natural planetary glow"
    },
    "truthfulnessNotes": [
      "Jupiter must remain recognizable by cloud-band structure",
      "do not add rings",
      "do not use fantasy colors"
    ]
  },
  "supportingSubjects": [
    {
      "name": "Venus",
      "type": "planet",
      "role": "supporting subject",
      "priority": 2,
      "visualTreatment": {
        "shape": "small bright circular point or tiny disk",
        "brightness": "bright natural Venus, clean cream-white light",
        "scale": "smaller than Jupiter and clearly secondary",
        "glow": "small controlled halo, not a starburst explosion"
      },
      "truthfulnessNotes": [
        "Venus should look naturally bright and not blue, green, or neon",
        "do not render surface continents or invented texture"
      ]
    },
    {
      "name": "subtle horizon silhouette",
      "type": "environment",
      "role": "scale and observation context",
      "priority": 4,
      "visualTreatment": {
        "style": "minimal dark horizon or observer silhouette",
        "density": "very low",
        "purpose": "provide sky-watching context without stealing focus"
      }
    }
  ],
  "visualHierarchy": [
    "Jupiter as the largest and most detailed visual anchor",
    "Venus as bright secondary point near Jupiter",
    "clean Hindi/English title zone",
    "compact observation card",
    "minimal labels for Jupiter and Venus",
    "subtle star field and horizon context"
  ],
  "composition": {
    "layout": "vertical cinematic astronomy poster",
    "subjectPlacement": {
      "Jupiter": "upper-middle or upper-right hero position, away from text safe zones",
      "Venus": "near Jupiter with clear conjunction relationship and enough separation for label readability"
    },
    "negativeSpace": "large clean area for short bilingual title and platform UI safety",
    "safeZones": {
      "top": "avoid critical text and planet details under Shorts UI area",
      "bottom": "reserve readable observation card above bottom controls",
      "leftRight": "keep labels and leader lines inside comfortable margins",
      "title": "clear Hindi/English title zone with no dense stars behind text"
    },
    "edgeDiscipline": "no important planet, label, or card clipped by the canvas edge"
  },
  "framing": {
    "cameraLanguage": "premium telephoto sky framing with documentary editorial composition",
    "viewpoint": "ground-based sky-watcher perspective",
    "crop": "vertical 9:16 with generous breathing room",
    "depth": "distant celestial subjects against deep sky",
    "lensMood": "clean, compressed, elegant, not wide-angle distortion"
  },
  "lighting": {
    "keyLight": "natural planetary brightness against dark pre-dawn sky",
    "glowTreatment": "controlled glow around planets only",
    "contrast": "high contrast for mobile readability without crushed planet detail",
    "exposure": "Jupiter cloud bands visible; Venus bright but not blown out",
    "avoid": [
      "neon outlines",
      "oversized lens flares",
      "fake sci-fi beams",
      "washed-out text zones"
    ]
  },
  "atmosphere": {
    "sky": "deep navy pre-dawn or twilight gradient",
    "starDensity": "sparse and realistic",
    "haze": "very subtle atmospheric depth near horizon",
    "mood": "premium, calm, observational, awe-inspiring",
    "environmentContext": "optional minimal Indian urban or landscape silhouette kept nearly black"
  },
  "typography": {
    "titleZone": "upper-left or middle-left safe negative space, away from planets",
    "scriptSafety": "Hindi Devanagari and English Latin text must both have sufficient line height and contrast",
    "density": "low; short title plus minimal supporting line only",
    "readability": "readable on mobile Shorts preview after compression",
    "hierarchy": "one bold title, one smaller context line, observation card separate",
    "avoid": [
      "text over Jupiter cloud bands",
      "text touching Venus glow",
      "too many mixed font styles",
      "misspelled generated text",
      "thin low-contrast Devanagari"
    ]
  },
  "observationCard": {
    "enabled": true,
    "placement": "lower safe zone above platform controls",
    "contentPriorities": [
      "date or viewing window",
      "direction in sky",
      "best time",
      "visibility note"
    ],
    "style": {
      "background": "translucent dark navy glass card",
      "border": "thin subtle warm-gold or cool-cyan accent",
      "text": "high-contrast off-white, bilingual-safe spacing",
      "density": "compact"
    },
    "safeArea": "must not overlap bottom UI, subtitles, or planet labels"
  },
  "labels": {
    "enabled": true,
    "density": "minimal",
    "items": [
      {
        "target": "Jupiter",
        "textIntent": "planet name only",
        "placement": "near hero disk without crossing title zone"
      },
      {
        "target": "Venus",
        "textIntent": "planet name only",
        "placement": "near bright point with short unobtrusive leader line if needed"
      }
    ],
    "leaderLines": "thin, subtle, straight or gently angled, no technical UI clutter",
    "placementRules": [
      "labels must not obscure planets",
      "labels must remain inside Shorts safe margins",
      "labels should not compete with the observation card"
    ]
  },
  "astronomicalRendering": {
    "geometry": "perfect circular geometry for planets; no warped ellipses or melted shapes",
    "scalePolicy": "cinematic emphasis allowed, but relative dominance must stay credible and educational",
    "Jupiter": {
      "requiredTraits": [
        "perfect circular disk",
        "realistic cloud bands",
        "warm beige and cream tones",
        "subtle belts and zones",
        "no rings"
      ]
    },
    "Venus": {
      "requiredTraits": [
        "bright natural Venus",
        "cream-white point or small disk",
        "controlled glow",
        "no invented surface detail"
      ]
    },
    "relationship": "Jupiter and Venus should appear as a clear close pairing/conjunction without implying physical contact",
    "truthfulnessConstraints": [
      "do not show planets touching",
      "do not imply collision",
      "do not add unrelated planets unless event intelligence requires them",
      "do not use astrology symbols or horoscope styling"
    ]
  },
  "brandDesign": {
    "tone": "premium astronomy documentary",
    "palette": [
      "deep navy",
      "star white",
      "warm Jupiter beige",
      "Venus cream",
      "restrained gold accent"
    ],
    "spacing": "generous margins, clean visual rhythm, strong hierarchy",
    "overlayStyle": "minimal editorial overlays with translucent observation card",
    "brandPersonality": "scientific, calm, cinematic, trustworthy, Indian audience friendly",
    "antiPatterns": [
      "generic AI poster clutter",
      "rainbow gradients",
      "sci-fi HUD overload",
      "sensational collision imagery",
      "cartoon planets"
    ]
  },
  "negativeConstraints": [
    "no Azure-specific prompt syntax in CDL",
    "no cartoon or toy-like planets",
    "no rings around Jupiter",
    "no blue or green Venus",
    "no oval, warped, or melted planet shapes",
    "no exaggerated collision or planets touching",
    "no horoscope, zodiac, or astrology motifs",
    "no crowded text blocks",
    "no unreadable Hindi or English typography",
    "no labels covering planets",
    "no observation card under platform UI controls",
    "no random spacecraft, astronauts, or fantasy nebula clutter"
  ],
  "qualityTargets": {
    "subjectClarity": "Jupiter and Venus identifiable within one second",
    "mobileReadability": "title, labels, and observation card readable on a phone screen",
    "astronomyCredibility": "planet appearance and relationship feel truthful for a conjunction explainer",
    "brandFit": "premium Drashyam documentary identity with restrained cinematic polish",
    "compositionQuality": "clear hierarchy, safe text zones, no edge clipping",
    "compressionResilience": "major subjects and text remain clear after social video compression"
  },
  "providerHints": {
    "prefersShortPrompt": false,
    "supportsNegativePrompt": true,
    "supportsTypography": true,
    "supportsStructuredInput": true,
    "supportsReferenceImages": false
  }
}
```

## 9. CDL vs CreativeDirectionContract

CDL and `CreativeDirectionContract` are related but not identical concepts.

- **CDL is creative, internal, and director-facing.** It preserves the language of creative direction: hierarchy, tone, atmosphere, planet treatment, brand discipline, and negative intent.
- **`CreativeDirectionContract` is the formal downstream contract.** It may be stricter, smaller, more normalized, and more implementation-focused once consuming systems are known.
- **`PromptComposerV2` may consume either CDL directly or a compiled contract later.** Early adoption can use CDL directly for prompt composition experiments; later adoption can compile CDL into the formal contract.
- **V3.2G will finalize the contract after CDL, `PromptComposerV2`, and Quality Scoring docs are complete.** Finalizing the contract too early would risk losing requirements discovered by prompt composition and visual scoring design.

Practical relationship:

```text
VisualCreativeDirector
→ CDL
→ optional compiler/normalizer
→ CreativeDirectionContract
→ PromptComposerV2 and CreativeQualityScoringEngine
```

## 10. Provider Hints

`providerHints` are optional, non-binding capability hints for downstream prompt composition. They help `PromptComposerV2` decide how to compile the same creative intent for different providers, but they must not contain provider-specific prompt text yet.

Allowed examples include:

- `prefersShortPrompt`
- `supportsNegativePrompt`
- `supportsTypography`
- `supportsStructuredInput`
- `supportsReferenceImages`

Provider hints should describe capabilities, limits, or preferences at a high level. They should not include:

- Vendor-specific prompt syntax.
- Model names.
- API parameters.
- Magic words optimized for one provider.
- Provider-specific negative prompt strings.
- Provider-specific JSON request bodies.

## 11. Integration with Previous and Future V3.2 Docs

### CDL Consumes V3.2A VisualCreativeDirector

V3.2A defines the director layer that converts event intelligence into structured creative direction. CDL is the proposed language that can store the director's output before provider-specific prompt composition.

### CDL Consumes V3.2B BrandDesignSystem

V3.2B defines Drashyam's brand identity: premium documentary tone, typography hierarchy, color discipline, safe spacing, observation-card style, and anti-clutter principles. CDL carries those choices in `brandDesign`, `typography`, `observationCard`, `composition`, `negativeConstraints`, and `qualityTargets`.

### CDL Consumes V3.2C PlanetRenderingRules

V3.2C defines planet-specific rendering expectations and astronomy truthfulness constraints. CDL carries those choices in `heroSubject`, `supportingSubjects`, `astronomicalRendering`, and `negativeConstraints`.

### CDL Feeds V3.2E PromptComposerV2

V3.2E can use CDL to translate creative intent into provider-specific prompt text while keeping business and creative decision logic out of prompt templates.

### CDL Feeds V3.2F CreativeQualityScoringEngine

V3.2F can use CDL as the expected visual target for scoring generated assets: subject clarity, brand fit, safe typography, observation-card readability, planet accuracy, and negative-constraint violations.

### CDL Feeds V3.2G Final CreativeDirectionContract

V3.2G can finalize the formal contract after CDL, prompt composition, and quality scoring requirements are all known. CDL therefore acts as the creative source language that informs the final downstream contract.

## 12. Non-goals

V3.2D explicitly does not include:

- No implementation code.
- No provider integration.
- No Azure calls.
- No image generation changes.
- No prompt replacement yet.
- No validation changes.
- No pipeline phase changes.
- No narration changes.
- No SRT changes.
- No TTS changes.
- No rendering changes.
- No changes to existing prompts.
- No changes to Azure Image2 integration.

## 13. Migration Plan

CDL can later be introduced safely behind a feature flag without changing the V3.1 release-candidate flow.

1. **Generate CDL alongside the existing V3.1 prompt flow.** The existing prompt remains authoritative while CDL is produced as an additional diagnostic artifact.
2. **Log CDL for inspection.** Store or emit CDL in development and controlled environments so creative decisions can be reviewed by humans.
3. **Compare CDL-derived prompt with the existing prompt.** When `PromptComposerV2` exists, compare its provider-specific output against the V3.1 prompt for consistency, omissions, and improvements.
4. **Enable provider-specific `PromptComposerV2` only after review.** Provider-specific prompt composition should be opt-in, evaluated, and reversible.
5. **Preserve fallback to V3.1 behavior.** Any CDL generation, compilation, prompt composition, or scoring failure must fall back to existing V3.1 behavior until a later release deliberately changes that policy.

## 14. Acceptance Criteria

- Existing V3.1 release-candidate behavior remains unchanged.
- This is a documentation-only PR.
- CDL is provider-neutral.
- CDL is usable by `PromptComposerV2`.
- CDL is compatible with V3.2A VisualCreativeDirector, V3.2B BrandDesignSystem, and V3.2C PlanetRenderingRules.
- No code files are modified.
- No implementation ambiguity remains around CDL's purpose, responsibilities, field shape, provider hints, migration posture, and non-goals.
- No existing prompts are changed.
- No Azure Image2 integration is changed.
- No narration, SRT, TTS, validation, or rendering logic is changed.
