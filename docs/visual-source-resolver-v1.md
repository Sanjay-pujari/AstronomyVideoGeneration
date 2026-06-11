\# AstroPulse Visual Source Resolver V1



\## Purpose



AstroPulse cannot depend only on local celestial asset images.



Astronomy events may require different visual sources:



\* computed sky scenes

\* scientific image assets

\* AI cinematic visuals

\* hybrid visuals

\* safe editorial fallback



The Visual Source Resolver decides which visual source should be used for each scene before Phase 8/9 image generation.



\## Pipeline Position



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



\## Core Rule



Phase 8/9 must not guess visuals directly.



They must ask the Visual Source Resolver using:



\* event type

\* strategy

\* primary objects

\* secondary objects

\* required visual objects

\* forbidden objects

\* scene purpose

\* local timing

\* direction hints



\## Visual Source Types



```text

ComputedAstronomyScene

ScientificAsset

AICinematicScene

Hybrid

GenericFallback

```



\## Priority Rules



\### MeteorShower



Preferred:



1\. ComputedAstronomyScene

2\. AICinematicScene enhancement



Required:



\* meteor streaks

\* radiant or direction hint

\* dark sky

\* viewing window



\### PlanetPairing / Conjunction / PlanetParade



Preferred:



1\. ComputedAstronomyScene

2\. Scientific planet icons/textures

3\. AI background polish



Required:



\* exact primary/secondary objects

\* correct labels

\* no unrelated planet leakage



\### NamedFullMoon / FullMoon / BlueMoon / SnowMoon



Preferred:



1\. Scientific Moon asset

2\. AI cinematic moonrise/seasonal background

3\. computed direction/timing overlay



Required:



\* visible full Moon

\* Moon/Snow Moon label

\* moon glow

\* eastern/moonrise context when applicable



\### LunarEclipse



Preferred:



1\. Scientific or AI eclipse Moon

2\. computed timing overlay



Required:



\* red/copper/eclipsed Moon

\* eclipse timing



\### SolarEclipse



Preferred:



1\. Scientific or AI eclipse geometry

2\. computed timing overlay



Required:



\* Sun/Moon eclipse geometry

\* eye safety message



\### Comet / DeepSkyObject



Preferred:



1\. Scientific asset if available

2\. AI cinematic scene if missing

3\. computed sky position overlay if available



Required:



\* named object/category visibly represented



\## Generic Fallback Rule



GenericFallback is allowed only for explanatory scenes that do not require a visible celestial object.



If required visual objects exist and no provider can render them, Phase 8/9 must fail.



Generic sky backgrounds must never pass Phase 10 when required objects are missing.



\## Validation Rules



Scene validation must check:



\* required visual objects are present

\* forbidden objects are absent

\* title or short title appears where required

\* labels match ProductionEventIntelligence

\* no stale Golden Pilot terms

\* no Venus/Jupiter leakage unless part of event intelligence



\## Acceptance Tests



\### Snow Moon Full Moon



Expected:



\* visible full Moon

\* Snow Moon / Full Moon text

\* eastern/moonrise context

\* no meteor

\* no Venus/Jupiter



\### Mars and Jupiter Close Pairing



Expected:



\* Mars and Jupiter only

\* no Venus

\* labels match event objects

\* hero/thumbnail use Mars-Jupiter copy



\### Geminids / Perseids



Expected:



\* meteor streaks

\* radiant/dark sky

\* no regression



\## Design Principle



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



