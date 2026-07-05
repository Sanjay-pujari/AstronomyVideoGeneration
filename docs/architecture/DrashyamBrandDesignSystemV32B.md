# V3.2B — Drashyam Brand Design System

## 1. Purpose

The Drashyam Brand Design System defines the reusable visual identity for Drashyam V3.2 independently from event families, renderers, Azure services, narration, and pipeline phases. It is the visual brand foundation that future systems can consume when creating Hero assets, YouTube thumbnails, gallery images, observation cards, and other premium astronomy visuals.

V3.2B is intentionally document-only. It does not alter V3.1 release-candidate behavior, existing prompts, Azure Image2 integration, validation, rendering, narration, SRT, TTS, or pipeline orchestration. Its role is to make Drashyam's visual identity explicit, reusable, platform-aware, and implementation-ready for later V3.2 work.

The Brand Design System answers: **what should Drashyam visuals consistently look and feel like before any event-family or renderer-specific decision is applied?**

## 2. Design Principles

### Premium astronomy documentary feel

Drashyam visuals should feel like a high-quality astronomy documentary: cinematic, credible, spacious, emotionally engaging, and respectful of the night sky.

### Consistent Drashyam identity

Every asset should be recognizable as part of the same Drashyam visual world through disciplined color, typography, spacing, label treatment, observation-card styling, and restrained cinematic contrast.

### Platform-aware

The system must adapt to YouTube Shorts, YouTube thumbnails, gallery posters, square posts, and future web or mobile assets while preserving the same brand personality.

### Renderer-independent

Brand rules must not depend on FFmpeg, ImageSharp, browser canvas, CSS, Azure Image2, or any image provider. The system defines visual intent, not rendering mechanics.

### Family-independent

Brand rules apply across solar eclipses, lunar eclipses, planetary conjunctions, meteor showers, full moons, comets, occultations, and future event families. Event-family profiles may specialize the brand, but should not replace it.

### Additive to V3.1

V3.2B adds a reusable brand layer for future adoption. It does not replace or mutate V3.1 prompts, templates, generation logic, or validation rules.

### No pipeline behavior change

The document introduces no new pipeline phases, runtime checks, provider calls, rendering behavior, retries, or validation gates.

### Human creative director style

The visual system should read like guidance from a human creative director: intentional hierarchy, purposeful restraint, clear mood, and tasteful editorial decisions rather than mechanical prompt fragments.

### Avoid generic AI poster look

Drashyam should avoid exaggerated fantasy composites, random glowing UI, over-sharpened nebula backgrounds, implausible planet scale, and crowded text blocks that make assets feel generic or AI-generated.

## 3. Brand Personality

Drashyam's visual tone is:

- **Premium:** refined color, restrained effects, strong hierarchy, and polished editorial composition.
- **Scientific but emotional:** fact-respecting astronomy imagery with a sense of wonder, not dry diagrams.
- **Cinematic:** deep contrast, atmospheric lighting, meaningful negative space, and documentary-grade framing.
- **Calm and trustworthy:** no panic colors, sensational claims, noisy overlays, or misleading visual exaggeration.
- **Indian audience friendly:** clear Hindi and English readability, culturally accessible presentation, and observation guidance that feels useful for Indian sky-watchers when relevant.
- **Global documentary quality:** suitable beside international science media, streaming documentaries, and premium editorial astronomy layouts.
- **Not childish:** avoid toy-like planets, cartoon moons, novelty fonts, mascot styling, or playful clutter.
- **Not cluttered:** prioritize one clear subject, one clear message, and minimal supporting information.
- **Not horoscope-style:** avoid zodiac wheels, mystical astrology symbols, fortune-telling aesthetics, and decorative spiritual motifs unrelated to observational astronomy.

## 4. Visual References

The intended quality direction is inspired by the discipline and premium feel of:

- **NASA:** scientific credibility, authentic space textures, observational seriousness, and respect for celestial scale.
- **National Geographic:** editorial storytelling, documentary-grade composition, and accessible science communication.
- **Apple:** restrained typography, clean hierarchy, premium spacing, and minimal interface noise.
- **Netflix documentary:** cinematic drama, polished key art, high contrast, and emotionally engaging visual framing.
- **Premium astronomy magazines:** informative but elegant layouts, useful annotations, and strong astronomical subject clarity.
- **Luxury editorial design:** spacious composition, refined type hierarchy, tasteful accents, and controlled visual rhythm.

These are inspiration references only. Drashyam must not copy logos, layouts, trademarks, proprietary brand assets, exact title treatments, or recognizable design systems from these organizations.

## 5. Color System

### Shared color rules

- Use deep, cinematic backgrounds with high-contrast foreground text.
- Prefer restrained accent colors tied to the event's physical light: solar gold, lunar copper, planet cream, meteor cyan, moon silver.
- Use one dominant background family and one to two accent colors per asset.
- Avoid rainbow gradients, neon overload, random purple-pink fantasy skies, and low-contrast gray text.
- Text and labels should remain legible on mobile screens after compression.

### Deep space base palette

**Use for:** default Drashyam identity, generic astronomy, planetary events, hero backdrops.

- **Background colors:** near-black navy, deep indigo, blue-black, subtle star-field charcoal.
- **Accent colors:** star white, muted gold, cool cyan, soft silver.
- **Text guidance:** primary titles in warm white or moon white; secondary text in pale blue-gray; accents in restrained gold or cyan.
- **Warning colors to avoid:** saturated magenta, neon green, bright red backgrounds, rainbow gradients.
- **Contrast guidance:** maintain strong luminance separation between text and sky; avoid placing thin type over dense stars.

### Solar eclipse palette

**Use for:** total, annular, and partial solar eclipse assets.

- **Background colors:** eclipse black, corona charcoal, deep blue-black, twilight navy.
- **Accent colors:** corona white, solar gold, amber rim light, subtle warm orange.
- **Text guidance:** titles in white or pale gold; time and safety notes in high-contrast off-white; observation card accents may use solar gold.
- **Warning colors to avoid:** fiery disaster red, excessive orange flames, religious aura effects, fake rainbow corona.
- **Contrast guidance:** never let the solar corona wash out title text; keep text in negative space away from the eclipse disk.

### Lunar eclipse palette

**Use for:** total, partial, and penumbral lunar eclipse assets.

- **Background colors:** deep midnight blue, charcoal violet, black-blue gradient, sparse star field.
- **Accent colors:** copper red, muted rust, moon silver, soft amber.
- **Text guidance:** titles in moon white; lunar phase or timing accents in copper; supporting text in pale gray-blue.
- **Warning colors to avoid:** blood-dripping horror red, over-saturated crimson, Halloween orange-black styling.
- **Contrast guidance:** copper moon should remain readable against the sky; labels must not sit directly on the moon unless heavily simplified.

### Planetary conjunction palette

**Use for:** close approaches, planet pairings, planet groupings, alignments.

- **Background colors:** deep navy, dusk indigo, pre-dawn blue, horizon charcoal.
- **Accent colors:** Venus cream, Jupiter warm beige, Mars muted rust, Saturn gold, cool label cyan.
- **Text guidance:** title in white or warm cream; planet names in small high-contrast labels; location/timing text in muted off-white.
- **Warning colors to avoid:** arbitrary planet colors, neon outlines, oversized rainbow glows, fake sci-fi UI grids.
- **Contrast guidance:** planet dots and labels must be visible without exaggerating planet size beyond credible visual storytelling.

### Meteor shower palette

**Use for:** annual meteor showers and peak-night assets.

- **Background colors:** black-blue sky, dark mountain silhouette, deep violet-blue, moonless charcoal.
- **Accent colors:** meteor white, ionized cyan, pale green-blue, subtle gold horizon light.
- **Text guidance:** title in white; peak time and direction in pale cyan or warm white; keep labels sparse.
- **Warning colors to avoid:** too many colored streaks, fireworks aesthetic, neon laser beams, fantasy nebula clutter.
- **Contrast guidance:** meteor streaks should not compete with the title; reserve a clean text zone separate from the radiant area.

### Full moon palette

**Use for:** full moon, supermoon, named moon, and moon observation assets.

- **Background colors:** midnight navy, deep blue-gray, soft black, subtle atmospheric horizon.
- **Accent colors:** moon silver, pearl white, soft gold, light blue rim.
- **Text guidance:** titles in crisp white or pearl; observation details in pale blue-gray; avoid placing heavy text over lunar texture.
- **Warning colors to avoid:** cartoon yellow moon, over-warm cheese yellow, mystical purple horoscope gradients.
- **Contrast guidance:** moon texture must retain detail; text should sit in surrounding negative space or a translucent card.

## 6. Typography System

Do not mandate a specific font dependency. Future implementations may use system fonts, licensed fonts, or provider-native typography, but the characteristics should remain consistent.

### Hero title

- Large, premium, editorial, and cinematic.
- High weight or strong optical presence without novelty styling.
- Short wording preferred: ideally 2-6 words.
- Should remain readable on mobile and compressed video previews.

### Subtitle

- Smaller than the title, calm, and informative.
- Use for event date, visibility promise, or concise context.
- Avoid long scientific sentences in subtitle position.

### Observation card text

- Clear, compact, and practical.
- Use tabular or aligned hierarchy where possible: date, time, direction, visibility, location.
- Should feel like documentary lower-third information, not a crowded infographic.

### Labels

- Small but legible, high contrast, and minimally styled.
- Use thin connector lines sparingly.
- Prefer subtle capsules or glow-backed text only when needed for contrast.

### Timestamp/location text

- Use a calm supporting style.
- Keep location/time text readable but subordinate to the main title and celestial subject.
- Avoid tiny timestamp text that disappears in YouTube thumbnails or mobile feeds.

### Hindi and English readability rules

- Support Devanagari and Latin scripts with fonts that have clear counters, matras, and punctuation spacing.
- Avoid overly condensed Devanagari, decorative Hindi lettering, and thin strokes for small text.
- Preserve correct Hindi word breaks; do not compress multi-word Hindi titles into unreadable blocks.
- If bilingual text is used, establish one primary language and one secondary line rather than mixing scripts randomly.

## 7. Layout System

### Hero vertical layout

- Optimized for 9:16 or tall mobile assets.
- Primary celestial subject in upper or middle visual field, with a protected title zone.
- Observation card may sit in the lower third if it does not block the subject or platform UI.
- Use strong vertical hierarchy: subject → title → observation details.

### YouTube Shorts cover layout

- Mobile-first, instantly readable, and safe from Shorts interface overlays.
- Keep critical text away from the bottom UI zone and right-side action rail.
- Use one main title and at most one compact supporting card.

### Thumbnail layout

- 16:9, high-impact, and simple at small sizes.
- One main subject, one title block, and optional small observation cue.
- Avoid crowded diagrams, excessive labels, or multiple competing celestial elements.

### Gallery image layout

- 4:5 or 1:1 editorial poster style.
- More spacious than thumbnails; can include a refined observation card or caption block.
- Suitable for sharing as a standalone visual guide.

### Observation card placement

- Prefer lower third, lower-left, or lower-right depending on subject position and platform UI.
- Never cover the eclipse disk, moon face, planetary grouping, meteor radiant, or main title.
- Use translucent dark panels or subtle glass-like cards only when they improve readability.

### Safe margin rules

- Maintain generous outer margins for all text and labels.
- Preserve platform UI safe zones for YouTube Shorts and thumbnails.
- Keep important celestial subjects away from extreme edges unless intentionally cropped for cinematic effect.

### Text density rules

- Use low text density for thumbnails and Shorts covers.
- Use medium text density only for gallery/poster assets with sufficient space.
- Prefer fewer, clearer words over full explanatory paragraphs.

### Mobile-first rules

- Design for readability at phone size first.
- Test visual hierarchy mentally at small preview scale: title, subject, and key observation cue should remain clear.
- Avoid fine-line labels and low-contrast type that require zooming.

## 8. Observation Card System

### Purpose

The observation card gives viewers practical sky-watching information without turning the asset into a dense infographic. It should support the emotional hero visual with concise, trustworthy guidance.

### Recommended fields

Use only fields that are relevant and verified for the asset:

- Event date
- Best viewing time or peak time
- Direction in the sky
- Visibility region or city/region context
- Object names or event type
- Safety note for solar events
- Simple viewing tip when useful

### Visual treatment

- Premium lower-third or compact editorial card.
- Dark translucent background, soft border, subtle accent line, or minimal glass effect.
- High-contrast text with clear hierarchy.
- No heavy boxes, thick borders, random icons, or decorative dashboards.

### Placement rules

- Place in available negative space.
- Keep away from platform UI overlays.
- Do not cover the main celestial subject.
- Maintain enough margin from edges for crop safety.

### Maximum text rules

- Prefer 3-5 short fields.
- Avoid more than 2 short lines per field.
- Do not include full paragraphs.
- For thumbnails, consider omitting the card or reducing it to one short cue.

### Hindi support

- Hindi observation cards must use readable Devanagari with sufficient line height.
- Use concise Hindi labels and avoid overly formal or long phrases when space is limited.
- Mixed Hindi-English astronomy terms are acceptable if they improve audience comprehension.

### When to omit card

Omit the observation card when:

- The platform size makes it unreadable.
- The card would cover the hero subject.
- The asset is purely brand/hero key art.
- Observation details are uncertain or not yet verified.
- The thumbnail needs maximum emotional impact with minimal text.

## 9. Label and Annotation Rules

### Planet labels

- Label only key planets needed for comprehension.
- Keep labels close to the object with minimal connector lines.
- Avoid labeling every visible dot in a star field.
- Do not use labels to justify inaccurate planet scale or placement.

### Constellation labels

- Use sparingly, only when constellation context helps viewers locate the event.
- Avoid full constellation-line overlays unless the asset is explicitly educational.
- Keep constellation labels subdued compared with event subjects.

### Direction markers

- Use simple direction markers such as East, West, Southwest, or horizon cue when useful.
- Avoid compass clutter and technical sky-map grids in premium hero assets.

### Date/time/location labels

- Keep date, time, and location concise.
- Treat them as practical observation metadata, not the main visual headline unless the event timing is the key story.
- Use local relevance when event intelligence supports it.

### Do rules

- Use high-contrast labels.
- Keep annotations minimal and purposeful.
- Prioritize the primary subject and main message.
- Use consistent label styling across asset types.

### Do-not rules

- Do not overload the sky with labels.
- Do not place labels over bright celestial textures without contrast backing.
- Do not use random arrows, UI widgets, or decorative callouts.
- Do not use astrology symbols as astronomy labels.

## 10. Brand Rules Object for Future CreativeDirectionContract

Future V3.2 implementation can represent this system as a reusable `brandRules` object inside or alongside `CreativeDirectionContract`. The object should be serializable, renderer-neutral, and partial-adoption friendly.

### Proposed structure

- `brandPersonality`: tone, audience, quality bar, prohibited style categories.
- `colorPalette`: palette name, background colors, accents, text guidance, contrast rules, avoid colors.
- `typography`: title, subtitle, label, observation-card, Hindi and English readability rules.
- `layout`: composition pattern, focal hierarchy, platform treatment, safe placement guidance.
- `observationCard`: enabled state, field guidance, placement, styling, maximum text.
- `labels`: planet, constellation, direction, date/time/location annotation rules.
- `safeMargins`: platform and crop safety guidance.
- `textDensity`: low/medium/high constraints by asset type.
- `accessibility`: contrast, small-screen, Hindi legibility, and compression readability rules.
- `negativeBrandRules`: explicit prohibitions to preserve premium identity.

### Sample JSON

```json
{
  "brandRules": {
    "brandPersonality": {
      "tone": ["premium", "scientific but emotional", "cinematic", "calm", "trustworthy"],
      "audience": ["Indian audience friendly", "global documentary quality"],
      "avoid": ["childish", "cluttered", "horoscope-style", "generic AI poster"]
    },
    "colorPalette": {
      "name": "planetaryConjunction",
      "backgroundColors": ["deep navy", "dusk indigo", "pre-dawn blue", "horizon charcoal"],
      "accentColors": ["Venus cream", "Jupiter warm beige", "Saturn gold", "cool label cyan"],
      "textColorGuidance": "Use warm white for titles, muted off-white for metadata, and restrained cyan or gold for accents.",
      "avoidColors": ["neon outlines", "rainbow planet colors", "bright red backgrounds"],
      "contrastGuidance": "Keep labels readable on mobile without exaggerating planet size or glow."
    },
    "typography": {
      "heroTitle": "Large premium editorial title, short and readable at mobile preview size.",
      "subtitle": "Calm supporting line for date, visibility, or context.",
      "observationCardText": "Compact practical text with clear hierarchy.",
      "labels": "Small high-contrast labels with minimal connector lines.",
      "languageReadability": {
        "hindi": "Use legible Devanagari with adequate line height and clear matras.",
        "english": "Use clean modern letterforms and avoid ultra-condensed small text."
      }
    },
    "layout": {
      "compositionPattern": "hero subject with protected negative space for title",
      "focalHierarchy": ["primary celestial subject", "title", "observation cue"],
      "platformAdaptation": "mobile-first with asset-specific safe zones",
      "observationCardPlacement": "lower third only when it does not block the subject or platform UI"
    },
    "observationCard": {
      "enabled": true,
      "recommendedFields": ["date", "bestViewingTime", "direction", "visibilityRegion"],
      "visualTreatment": "translucent dark premium lower-third with subtle accent line",
      "maxFields": 5,
      "omitWhen": ["unreadable at target size", "blocks hero subject", "facts are not verified"]
    },
    "labels": {
      "planetLabels": "Label only key planets needed for comprehension.",
      "constellationLabels": "Use sparingly when location context is valuable.",
      "directionMarkers": "Use simple horizon or compass direction cues only when helpful.",
      "dateTimeLocation": "Keep concise and subordinate to title and subject."
    },
    "safeMargins": {
      "general": "Maintain generous outer margins for all text and labels.",
      "youtubeShorts": "Avoid bottom UI and right-side action rail.",
      "thumbnail": "Keep title and subject readable after crop and compression.",
      "gallery": "Use editorial spacing with balanced margins."
    },
    "textDensity": {
      "hero": "low",
      "youtubeShortsCover": "low",
      "youtubeThumbnail": "very low",
      "galleryPoster": "medium"
    },
    "accessibility": {
      "contrast": "High luminance contrast for all text and labels.",
      "smallScreen": "Readable at phone preview size.",
      "hindiLegibility": "Avoid thin Devanagari strokes and cramped line height.",
      "avoidTextOverload": true
    },
    "negativeBrandRules": [
      "no generic AI fantasy poster",
      "no overcrowded infographic",
      "no cartoonish planets",
      "no fake distorted planets",
      "no horoscope or zodiac aesthetic",
      "no excessive glow",
      "no too many labels",
      "no low-resolution planet textures",
      "no random decorative UI",
      "no text blocking hero subject"
    ]
  }
}
```

## 11. Platform Adaptations

### YouTube Shorts 9:16

- Use vertical cinematic framing.
- Keep title readable in the upper or middle safe zone.
- Avoid bottom UI conflicts and right-side action rail conflicts.
- Observation card should be compact or omitted.
- Prioritize one emotional visual hook and one practical cue.

### YouTube thumbnail 16:9

- Use bold but premium title treatment.
- Keep text extremely short.
- Use one dominant subject and avoid small annotations.
- Favor strong contrast and simple silhouettes over detailed observation cards.

### Gallery/poster 4:5 or 1:1

- Use editorial spacing and balanced composition.
- Allow a refined observation card if readable.
- Support richer context than thumbnails while preserving restraint.
- Suitable for social sharing and future collection views.

### Future web/mobile assets

- Preserve brand personality while adapting to responsive layouts.
- Prefer reusable tokens for palette, typography hierarchy, spacing, safe margins, labels, and card style.
- Ensure assets can degrade gracefully across screen sizes and compression contexts.

## 12. Accessibility and Readability

- Maintain strong contrast between text and background in all palettes.
- Avoid placing text over dense star fields, bright corona, lunar texture, or meteor streaks unless a readable backing is used.
- Hindi text must remain legible at small screen sizes, with adequate line height and clear Devanagari rendering.
- English text should avoid ultra-thin, ultra-condensed, or decorative letterforms when used below title size.
- Avoid text overload; if every detail is important, move details to the description or future supporting content rather than the image.
- Avoid low-contrast labels, tiny connector lines, and metadata that disappears after platform compression.

## 13. Negative Brand Rules

Drashyam visual assets must explicitly avoid:

- Generic AI fantasy poster aesthetics.
- Overcrowded infographic layouts.
- Cartoonish planets, moons, stars, or meteors.
- Fake distorted planets or physically implausible planet textures.
- Horoscope, zodiac, astrology, fortune-telling, or mystical chart aesthetics.
- Excessive glow, bloom, lens flare, or neon effects.
- Too many labels or callouts.
- Low-resolution planet textures or blurry celestial subjects.
- Random decorative UI, sci-fi dashboards, fake HUD elements, and meaningless icons.
- Text blocking the hero subject.
- Panic-driven disaster colors for normal astronomical events.
- Dense paragraphs in image compositions.

## 14. Integration with V3.2A

V3.2B complements the V3.2A Visual Creative Director architecture by supplying the reusable brand layer referenced by future creative direction contracts.

### VisualCreativeDirector

The VisualCreativeDirector can consume the Brand Design System to merge Drashyam identity with event intelligence, family profiles, platform constraints, and subject-specific rules before emitting a `CreativeDirectionContract`.

### FamilyCreativeProfiles

FamilyCreativeProfiles can specialize brand guidance for specific event families, such as solar eclipse corona treatment or meteor shower streak restraint, while inheriting shared Drashyam personality, typography, safe margins, and negative rules.

### PromptComposerV2

PromptComposerV2 can translate structured brand rules into provider-specific prompt language later, preserving visual hierarchy, palette choices, typography intent, observation-card rules, and negative brand rules.

### CreativeDirectionContract

The future CreativeDirectionContract can carry `brandRules` as a structured field so downstream systems can inspect, persist, score, and reuse brand decisions independently from prompt prose.

### CreativeQualityScoringEngine

The CreativeQualityScoringEngine can evaluate generated assets against brand criteria such as premium tone, contrast, text density, subject clearance, label restraint, palette discipline, and absence of generic AI poster artifacts.

## 15. Non-goals for V3.2B

V3.2B explicitly does not include:

- Implementation code.
- Prompt replacement.
- Azure calls.
- Renderer changes.
- Pipeline phase changes.
- Image generation changes.
- Validation changes.
- Narration, SRT, TTS, or localization pipeline changes.
- New runtime configuration files.
- New provider-specific prompt syntax.

## 16. Migration Plan

This document can later evolve into implementation artifacts in carefully staged, opt-in steps:

1. **BrandDesignSystem configuration:** convert palettes, typography rules, layout rules, safe margins, and negative rules into a provider-neutral configuration object.
2. **PromptComposerV2 input:** pass selected brand rules into PromptComposerV2 so final prompts preserve Drashyam identity without hardcoding style text in multiple places.
3. **Creative quality scoring rules:** map brand rules to scoring criteria for readability, contrast, subject clarity, text density, and negative visual artifacts.
4. **Platform-specific visual presets:** derive presets for YouTube Shorts, thumbnails, gallery posters, and future web/mobile assets.
5. **Feature-flagged adoption:** introduce implementation only behind a future opt-in path so V3.1 behavior remains unchanged until explicitly migrated.

## 17. Acceptance Criteria

- Existing V3.1 release-candidate behavior remains unchanged.
- The pull request is documentation-only.
- The brand system is reusable across event families.
- The document provides practical, structured, implementation-ready guidance with no implementation ambiguity.
- No code files are modified.
- The design is compatible with the V3.2A Visual Creative Director document.
- No existing prompts are changed.
- No Azure Image2 integration is changed.
- No narration, SRT, TTS, validation, rendering, or pipeline logic is changed.
