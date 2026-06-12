# AstroPulse Visual Source Resolver V1

## Purpose

AstroPulse cannot depend only on generic local artwork or primitive scene drawing. Astronomy events may require computed sky scenes, scientific image assets, AI cinematic realistic visuals, hybrid visuals, or safe editorial fallback.

The Visual Source Resolver decides which visual source should be used for each scene before Phase 8/9 image generation.

## Pipeline Position

```text
ProductionEventIntelligence
↓
Question Engine
↓
Scene Plan
↓
Scene Enrichment
↓
Visual Source Resolver
↓
Scene Image Generation
↓
Scene Validation
↓
Hero
↓
Thumbnail
↓
Narration / TTS / Video Assembly
```

## Core Rule: Realistic Celestial Object Rendering

For production mode, any required celestial object must be rendered from a realistic visual source. Production scenes must not satisfy required objects with symbolic placeholders.

Forbidden in production:

* flat circles for Moon/planets
* plain ellipses for comets
* generic dots for deep sky objects
* simple icons as final celestial objects
* debug placeholders
* primitive vector-only object rendering

Allowed only in debug mode:

* primitive circle Moon
* simple planet icon
* symbolic comet
* generic DSO blob

## Resolver Metadata Contract

Every Phase 8/9 scene spec that contains required celestial objects must record:

* `visualSourceType`
* `assetKey` or `generatedRealisticPrompt`
* `realisticObjectRequired`
* `primitivePlaceholderUsed`

Resolver defaults:

```text
RealisticObjectRequired = true
AllowPrimitivePlaceholder = false
MinimumVisualQuality = Realistic
PreferredAssetKind = ScientificRealImage | ScientificTexture | AICinematicRealistic
```

Phase 10 must fail if:

```text
realisticObjectRequired = true
primitivePlaceholderUsed = true
AllowPrimitivePlaceholder = false
```

## Visual Source Types

```text
ComputedAstronomyScene
ScientificAsset
AICinematicScene
Hybrid
GenericFallback
```

## Visual Source Priority

1. Local real celestial asset library
2. Scientific public asset source, for example NASA / ESA / Hubble / JWST / ESO / NOIRLab
3. AI cinematic realistic generation
4. Primitive/procedural placeholder only if `DebugFallbackEnabled = true`

## Priority Rules

### MeteorShower

Preferred:

1. ComputedAstronomyScene
2. AICinematicScene enhancement

Required:

* meteor streaks
* radiant or direction hint
* dark sky
* viewing window

Meteor scenes can use realistic streaks/radiant; this is not a symbolic-object issue.

### PlanetPairing / Conjunction / PlanetParade

Preferred:

1. ComputedAstronomyScene
2. Scientific planet textures/icons
3. AI background polish

Required:

* exact primary/secondary objects
* correct labels
* recognizable real-looking planet textures
* no unrelated planet leakage

Planet-specific requirements:

* Mars must look like Mars, with rusty red terrain/albedo detail.
* Jupiter must show banded cloud texture and a Great Red Spot style feature when possible.
* Venus must be bright, cloud-covered, white/yellow.
* Saturn must include rings.
* Generic colored circles are forbidden in production.

### NamedFullMoon / FullMoon / BlueMoon / SnowMoon

Preferred:

1. `Moon.FullMoon` realistic Moon asset
2. generated realistic full Moon texture if the asset is missing
3. AI cinematic moonrise/seasonal background
4. computed direction/timing overlay

Required:

* visible full Moon
* Moon/name label where relevant
* realistic crater/maria texture
* moon glow
* eastern/moonrise context when applicable

Named full Moons are presentation styles around the same real Moon:

* Snow Moon: cold winter/moonrise atmosphere.
* Wolf Moon: cold winter atmosphere.
* Strawberry Moon: warm reddish/golden summer moonrise atmosphere.
* Blue Moon: natural full Moon with subtle cool-blue cinematic mood; not literally fake blue unless content explains it.
* Blood Moon: red/copper Moon only for lunar eclipse.

### LunarEclipse

Preferred:

1. Scientific or AI eclipse Moon
2. computed timing overlay

Required:

* red/copper/eclipsed Moon
* eclipse timing

### SolarEclipse

Preferred:

1. Scientific or AI eclipse geometry
2. computed timing overlay

Required:

* Sun/Moon eclipse geometry
* eye safety message

### Comet

Preferred:

1. Scientific comet asset if available
2. AI realistic comet if missing
3. computed sky position overlay if available

Required:

* visible comet nucleus
* coma
* tail

Do not use a plain ellipse or a simple streak line unless the streak is explicitly a separate motion-path annotation.

### DeepSkyObject

Preferred:

1. Scientific image asset if available
2. AI realistic nebula / galaxy / star cluster if missing
3. computed sky position overlay if available

Required:

* named object/category visibly represented with astrophotography-like detail

Do not use a generic glow circle or dot.

## Generic Fallback Rule

GenericFallback is allowed only for explanatory scenes that do not require a visible celestial object.

If required visual objects exist and no provider can render them, Phase 8/9 must fail.

Generic sky backgrounds must never pass Phase 10 when required objects are missing.

## Validation Rules

Scene validation must check:

* required visual objects are present
* forbidden objects are absent
* title or short title appears where required
* labels match ProductionEventIntelligence
* no stale Golden Pilot terms
* no Venus/Jupiter leakage unless part of event intelligence
* required realistic-object metadata is present
* primitive placeholders are not used when realistic objects are required and primitive placeholders are disallowed

## Acceptance Tests

### Snow Moon Full Moon

Expected:

* real-looking full Moon with craters/maria texture
* Snow Moon / Full Moon text
* glow and eastern/moonrise context
* no meteor
* no Venus/Jupiter

### Mars and Jupiter Close Pairing

Expected:

* Mars and Jupiter only
* Mars has recognizable red Mars texture
* Jupiter has recognizable banded texture / Great Red Spot style detail when possible
* no Venus
* labels match event objects
* hero/thumbnail use Mars-Jupiter copy

### Geminids / Perseids

Expected:

* realistic meteor streaks
* radiant/dark sky
* no regression

## Design Principle

This resolver is not astronomy-only.

Future domains can reuse the same model:

```text
Domain Intelligence
↓
Scene Intent
↓
Visual Source Resolver
↓
Renderer
↓
Validation
```

For Astrology, Mythology, History, and Education, only strategy and source providers should change.
