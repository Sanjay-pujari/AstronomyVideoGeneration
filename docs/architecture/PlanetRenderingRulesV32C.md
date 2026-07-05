# V3.2C — Planet Rendering Rules & Astronomical Rendering Specification

## 1. Purpose

Planet Rendering Rules define how astronomical objects should appear visually regardless of image provider, renderer, model family, or image-generation vendor. They are the canonical visual truth layer for Drashyam's Visual Creative Director and all future visual generation systems.

This specification ensures:

- Scientific accuracy across rendered astronomical objects.
- Premium documentary quality suitable for educational and cinematic astronomy content.
- Consistent rendering across providers, prompt composers, and creative families.
- Family-independent behavior so object identity is never altered by stylistic profiles.
- Renderer independence so the same rules apply to Azure Image2, future providers, hand-authored artwork, or review tooling.
- Backward compatibility with the V3.1 visual pipeline and V3.2 creative architecture.

## 2. Design Principles

- **Physics-first:** Visual appearance must respect real geometry, illumination, shadowing, scale relationships, and atmospheric behavior.
- **Astronomy-first:** Object identity, phase, eclipse geometry, star color, and observing conditions take priority over decorative styling.
- **NASA-quality inspiration:** Imagery should evoke the clarity, restraint, and credibility of NASA/ESA/JPL documentary visuals without copying a specific source image.
- **Telescope realism:** Objects should look plausible for the stated observation mode, optical scale, and viewing context.
- **Documentary aesthetics:** Rendering may be cinematic, but it must remain educational, grounded, and premium rather than fantasy-driven.
- **No fantasy rendering:** Planetary colors, rings, moons, glows, textures, and atmospheric effects must not be invented for spectacle.
- **Renderer-neutral:** Rules describe desired visual truth, not provider-specific syntax, image model behavior, or implementation logic.
- **Future provider compatibility:** The specification must remain reusable when Drashyam adds new visual providers or local renderers.
- **Additive to V3.1:** These rules add a canonical astronomical layer without replacing V3.1 contracts or behavior.
- **No pipeline behavior change:** This document does not change prompts, validation, scoring, Azure integration, or generation phases.

## 3. Supported Object Categories

The specification covers rendering guidance for the following categories:

- **Planets:** Solar System planets rendered with accurate geometry, colors, phase, atmosphere, scale, and lighting.
- **Moons:** Natural satellites, with detailed treatment for Earth's Moon and extensible guidance for future moons.
- **Sun:** Photosphere, corona, prominences, chromosphere, sunspots, and eclipse appearances.
- **Stars:** Color, brightness, density, magnitude hierarchy, and telescope/eye realism.
- **Meteor:** Atmospheric streaks, radiant direction, brightness, fireballs, and trains.
- **Comets:** Nucleus, coma, dust tail, ion tail, tail direction, and brightness scaling.
- **Constellations:** Stars and optional educational overlays.
- **Milky Way:** Broad galactic band, dust lanes, star clouds, and realistic night-sky integration.
- **Nebula:** Background-only contextual rendering unless a future event explicitly requires a target nebula.
- **Deep Sky Objects:** Galaxies, clusters, and nebula-like objects as observational background or documentary context.
- **Earth horizon:** Curvature, atmospheric rim, clouds, terrain silhouettes, or horizon glow when relevant.
- **Atmospheric effects:** Twilight, airglow, moonlight, haze, light pollution, and scattering.

## 4. Planet Rendering Rules

### Global Planet Requirements

All planets must render as true circular or spheroidal astronomical bodies unless perspective, limb darkening, or partial phase naturally hides part of the disk. A planet must never become oval, stretched, faceted, liquified, cartoonish, or texture-warped. Phases, shadows, and terminators must follow physically plausible illumination from the Sun.

### Mercury

- **Visual identity:** Small, airless, rocky inner planet with a cratered gray-brown surface.
- **Typical colors:** Dark gray, warm gray, muted tan, and subtle brown tones.
- **Texture expectations:** Dense cratering, scarps, rough highlands, and basaltic plains; no oceans, clouds, vegetation, or atmosphere.
- **Lighting rules:** Strong Sun-driven contrast; sharp terminator due to negligible atmosphere.
- **Shadow rules:** Crater shadows may be crisp near the terminator; global night side should be dark unless documentary fill light is explicitly used.
- **Scale guidance:** Mercury should appear smaller than Venus, Earth, Mars, Jupiter, Saturn, Uranus, and Neptune when relative scale is depicted.
- **Circular geometry requirements:** Disk must remain round; partial phases may show crescent or gibbous geometry but not elliptical distortion.
- **Atmospheric treatment:** No visible atmosphere, haze, clouds, or glow.
- **Allowed artistic flexibility:** Moderate contrast enhancement to reveal crater texture in educational visuals.
- **Forbidden rendering mistakes:** Blue Mercury, cloud-covered Mercury, ringed Mercury, Earth-like Mercury, or exaggerated volcanic/fantasy lava surfaces.

### Venus

- **Visual identity:** Bright cloud-shrouded planet with a smooth global atmosphere.
- **Typical colors:** Cream, pale yellow, ivory, beige, and sulfur-tinted white.
- **Texture expectations:** Thick cloud layers with subtle banding or mottling; surface terrain should not be visible in normal visual rendering.
- **Lighting rules:** Can show strong crescent, half, or gibbous phases from inner-planet geometry.
- **Shadow rules:** Terminator should be soft compared with Mercury because of dense atmosphere and cloud scattering.
- **Scale guidance:** Similar apparent size to Earth in relative diagrams, but presentation depends on event context and distance.
- **Circular geometry requirements:** Illuminated portion must follow a circular planetary disk; crescent horns should be smooth and symmetrical unless perspective demands otherwise.
- **Atmospheric treatment:** Dense global atmosphere; no transparent surface reveal in ordinary documentary visuals.
- **Allowed artistic flexibility:** Subtle ultraviolet-inspired cloud contrast may be used if identified as documentary enhancement.
- **Forbidden rendering mistakes:** Visible oceans, blue continents, hard cratered surface, rings, artificial neon aura, or fantasy storm eyes.

### Earth

- **Visual identity:** Blue ocean planet with white clouds, brown/green landmasses, polar ice, and atmospheric limb.
- **Typical colors:** Deep ocean blue, white clouds, green/brown land, tan deserts, and white ice.
- **Texture expectations:** Realistic cloud systems, recognizable continental/oceanic patterns when scale allows, and no arbitrary fantasy geography.
- **Lighting rules:** Sunlit side must align with the light source; night side may show city lights only if perspective and context support it.
- **Shadow rules:** Terminator should be smooth with atmospheric twilight gradient; eclipses must cast physically plausible shadows.
- **Scale guidance:** Earth scale should be consistent relative to Moon, planets, and horizon context.
- **Circular geometry requirements:** Full disk or crescent must preserve Earth's round shape; horizon curvature should be plausible.
- **Atmospheric treatment:** Thin blue limb, cloud scattering, twilight bands, and subtle horizon glow.
- **Allowed artistic flexibility:** Cinematic cloud contrast, aurora only when scientifically relevant, and gentle color grading.
- **Forbidden rendering mistakes:** Random continents, impossible cloud geometry, oversized atmosphere, fantasy rings, or global neon outlines.

### Moon

- **Visual identity:** Airless gray natural satellite with maria, highlands, craters, and phase-dependent lighting.
- **Typical colors:** Neutral gray, charcoal maria, pale gray highlands, and subtle warm/cool tones from atmospheric viewing.
- **Texture expectations:** Visible maria, crater fields, rays, and rugged terminator relief when resolution allows.
- **Lighting rules:** Phase must match Sun-Earth-Moon geometry; crescent, quarter, gibbous, full, and eclipse states must be geometrically correct.
- **Shadow rules:** Crater and mountain shadows should be sharp; terminator relief may be dramatic but physically plausible.
- **Scale guidance:** When shown with Earth, diameter should be about one quarter of Earth's unless using clear illustrative non-scale composition.
- **Circular geometry requirements:** Lunar disk must be circular; phase shadow cuts across the circular disk without stretching it.
- **Atmospheric treatment:** No lunar atmosphere; Earth atmosphere may tint the Moon near the horizon or during eclipse.
- **Allowed artistic flexibility:** Contrast enhancement for maria/craters and atmospheric color if seen from Earth.
- **Forbidden rendering mistakes:** Clouds on the Moon, blue oceans, invented continents, oval Moon, wrong phase, or wrong eclipse shadow.

### Mars

- **Visual identity:** Rust-red rocky planet with dust, darker albedo markings, polar caps, and thin atmosphere.
- **Typical colors:** Ochre, rust, orange-red, salmon, tan, and muted brown.
- **Texture expectations:** Subtle dark regions, dust haze, possible polar caps, and surface relief when close-up.
- **Lighting rules:** Terminator moderately sharp; thin atmospheric scattering may soften the limb slightly.
- **Shadow rules:** Surface shadows should be realistic, especially on close-up terrain; no Earth-like cloud shadow system unless rare thin clouds are intentionally represented.
- **Scale guidance:** Smaller than Earth and Venus, larger than Mercury and the Moon.
- **Circular geometry requirements:** Disk must remain round; global dust storms may obscure details but not shape.
- **Atmospheric treatment:** Thin dusty atmosphere, faint limb haze, occasional pale clouds or dust veils.
- **Allowed artistic flexibility:** Enhanced contrast of canyons, volcanoes, and polar caps for documentary clarity.
- **Forbidden rendering mistakes:** Bright red plastic Mars, blue oceans, green vegetation, thick Earth-like clouds, rings, or fantasy lava rivers.

### Jupiter

- **Visual identity:** Giant banded gas planet with belts, zones, storms, and the Great Red Spot when appropriate.
- **Typical colors:** Cream, white, tan, ochre, brown, orange, muted red, and beige.
- **Texture expectations:** Horizontal cloud bands, turbulent eddies, oval storms, and layered atmospheric structure.
- **Lighting rules:** Limb darkening and spherical shading should communicate gas-giant volume.
- **Shadow rules:** Moon transits may cast small circular shadows; global shadows must follow solar illumination.
- **Scale guidance:** Largest planet; should dominate relative-scale compositions.
- **Circular geometry requirements:** Disk should be circular with subtle natural oblateness acceptable; not stretched by composition.
- **Atmospheric treatment:** Entire visible surface is atmosphere; no rocky land, craters, or oceans.
- **Allowed artistic flexibility:** High-resolution storm detail and modest color enhancement.
- **Forbidden rendering mistakes:** Solid surface terrain, fake rings as primary identity, neon bands, incorrect Great Red Spot scale, or random extra moons unless event-relevant.

### Saturn

- **Visual identity:** Pale gas giant with an iconic thin ring system aligned to viewing geometry.
- **Typical colors:** Pale yellow, cream, beige, muted gold, and soft gray ring tones.
- **Texture expectations:** Subtle atmospheric banding; rings should show broad structural separation when scale allows.
- **Lighting rules:** Planet and rings must share the same Sun direction; ring plane illumination must be coherent.
- **Shadow rules:** Ring shadows on Saturn and Saturn's shadow on rings are allowed and should follow geometry.
- **Scale guidance:** Gas-giant scale; smaller than Jupiter but much larger than terrestrial planets.
- **Circular geometry requirements:** Planetary disk remains round/oblate; rings are elliptical in projection because of viewing angle, not because the planet is oval.
- **Atmospheric treatment:** Gas-giant atmosphere with soft banding and limb shading.
- **Allowed artistic flexibility:** Elegant documentary framing of ring tilt and ring detail.
- **Forbidden rendering mistakes:** Thick toy-like rings, rings intersecting incorrectly, extra fantasy rings, rocky surface, or rings around the wrong planet.

### Uranus

- **Visual identity:** Pale cyan ice giant with a calm, nearly featureless disk and extreme axial tilt when context shows orientation.
- **Typical colors:** Pale cyan, blue-green, aquamarine, and muted teal.
- **Texture expectations:** Smooth atmospheric disk with very subtle banding or haze; minimal surface-like detail.
- **Lighting rules:** Soft limb shading; phase generally subtle in distant-planet views.
- **Shadow rules:** Shadows should be understated unless moons or rings are explicitly shown.
- **Scale guidance:** Ice giant scale; smaller than Jupiter and Saturn, larger than terrestrial planets.
- **Circular geometry requirements:** Disk must be round with realistic subtle oblateness only if technically appropriate.
- **Atmospheric treatment:** Methane-tinted atmosphere; faint rings may appear only when scientifically relevant and subtle.
- **Allowed artistic flexibility:** Slight contrast enhancement to reveal atmospheric bands.
- **Forbidden rendering mistakes:** Earth-like clouds, visible ocean/land, saturated neon turquoise, large Saturn-like rings, or fantasy storms.

### Neptune

- **Visual identity:** Deep blue ice giant with subtle bands, atmospheric storms, and high-altitude clouds when appropriate.
- **Typical colors:** Deep blue, azure, cobalt, muted navy, and pale cloud accents.
- **Texture expectations:** Smooth disk with subtle bands, occasional storm features, and small bright cloud streaks.
- **Lighting rules:** Soft limb shading and distant Sun illumination; avoid excessive contrast unless documentary-enhanced.
- **Shadow rules:** Moon shadows are rare and context-specific; generic renderings should not invent them.
- **Scale guidance:** Similar ice-giant scale to Uranus, slightly smaller in diameter.
- **Circular geometry requirements:** Disk must remain circular and not become an abstract blue oval.
- **Atmospheric treatment:** Methane-rich atmosphere with controlled blue coloration.
- **Allowed artistic flexibility:** Moderate enhancement of storms or bands for educational emphasis.
- **Forbidden rendering mistakes:** Artificial neon blue, Earth-like continents, rings as dominant identity, random lightning, or fantasy ocean surface.

## 5. Moon Rendering Rules

- **Moon phases:** Phase shape must correspond to Sun-Moon-observer geometry. Crescent horns, quarter terminators, gibbous illumination, and full Moon lighting must preserve the circular lunar disk.
- **Earthshine:** On thin crescent Moons, the night side may show faint bluish-gray Earthshine; it must remain subtle and never brighter than the sunlit crescent.
- **Maria:** Dark basaltic maria should be visible on the near side when resolution allows, especially in full or gibbous phases.
- **Highlands:** Brighter highlands should contrast naturally with maria, without turning pure white or metallic.
- **Craters:** Craters, rays, and rugged relief should become more visible near the terminator; crater density should not be randomized into fantasy patterns.
- **Libration awareness:** Near-side features may shift slightly due to libration, but the Moon must not show impossible far-side/near-side combinations unless explicitly educational.
- **Lunar eclipse rendering:** Eclipsed Moon color should range from copper to dark red-brown depending on atmospheric scattering, with Earth's umbral geometry respected.
- **Observation realism:** Naked-eye Moon can be bright and smooth; binocular/telescope views may show sharper crater relief and atmospheric seeing effects.

## 6. Solar Rendering Rules

- **Photosphere:** The visible solar disk should appear warm white to pale yellow in documentary visuals, with limb darkening and texture only when scale allows.
- **Corona:** Corona appears during total solar eclipse or specialized solar imaging; it should be structured, wispy, and radially extended, not a uniform neon halo.
- **Prominences:** Prominences may appear as red/pink arcs or flame-like plasma near the limb during eclipses or hydrogen-alpha style views.
- **Sunspots:** Sunspots should be dark, irregular, and scale-appropriate; do not cover the Sun with arbitrary black dots.
- **Chromosphere:** Thin red chromospheric rim can appear during eclipse contacts or specialized solar observation.
- **Solar eclipse:** Moon must align precisely with the solar disk; totality, partial, and annular states require correct relative sizes and centers.
- **Diamond Ring:** A brief bright bead at the lunar limb may appear near totality start/end, with restrained brightness.
- **Baily's Beads:** Multiple small beads may appear where sunlight passes lunar valleys, only near totality/annularity contact.
- **Partial eclipse:** Moon silhouette must be a circular bite from the Sun, not an irregular shadow.
- **Annular eclipse:** The solar ring must remain centered according to event geometry; ring thickness must be plausible and not stylized into a logo.

## 7. Meteor Rendering Rules

- **Meteor streaks:** Meteors appear as brief linear streaks caused by atmospheric entry, with tapered ends and perspective-aware direction.
- **Radiant direction:** In meteor showers, streaks should trace backward toward the shower radiant; random directions are acceptable only for sporadic meteors.
- **Brightness:** Brightness varies by meteoroid size and speed; most meteors are thin and modest, while rare fireballs are bright.
- **Fireballs:** Fireballs may show intense cores, color hints, fragmentation, and brief flares, but should not look like missiles or comets.
- **Persistent trains:** Bright meteors may leave faint glowing trains that drift or fade; trains should not become smoke trails from aircraft.
- **Perspective:** Wide-field sky scenes should show convergence toward a radiant, while close documentary composites may emphasize one meteor.
- **Duration representation:** A still image may represent motion through streak length, but the streak must remain observationally plausible.

## 8. Comet Rendering Rules

- **Nucleus:** The nucleus is usually unresolved or very small; avoid depicting a large rocky asteroid unless the composition is a close-up educational cutaway.
- **Coma:** The coma should be diffuse, rounded, and brightest near the nucleus.
- **Ion tail:** Ion tail is typically narrow, bluish, straighter, and points away from the Sun along solar wind direction.
- **Dust tail:** Dust tail is broader, warmer/white-yellow, curved, and also generally points away from the Sun while following orbital dynamics.
- **Tail direction:** Tails must not trail behind the comet like a rocket exhaust by default; they are driven by sunlight and solar wind.
- **Brightness scaling:** Comet size and brightness should reflect event prominence, distance, and observation mode; avoid making every comet spectacular.

## 9. Star Rendering Rules

- **Star colors:** Star colors should be subtle and physically plausible: blue-white, white, yellow-white, orange, and red-orange.
- **Magnitude differences:** Bright stars should be larger/brighter than faint stars, but not represented as huge disks.
- **Density:** Star density should reflect sky location, Milky Way proximity, exposure, light pollution, and observation mode.
- **Constellation stars:** Constellation anchor stars may be emphasized slightly for education while preserving relative brightness.
- **Avoid fake diffraction spikes:** Spikes should appear only if justified by telescope optics or documentary style, and should be restrained.
- **Avoid unrealistic colorful stars:** Do not render stars as saturated rainbow dots, random neon colors, or decorative confetti.

## 10. Constellation Overlay Rules

- **Opacity:** Lines and labels should be semi-transparent and educational, typically subtle enough not to dominate the sky.
- **Line thickness:** Use thin, clean lines that connect intended stars without obscuring star fields or Milky Way structure.
- **Label placement:** Labels should sit near but not on top of key stars, avoid horizon clutter, and remain readable against the background.
- **Priority:** Event object, horizon, and major astronomical phenomenon take priority over overlays.
- **Visibility rules:** Overlays are appropriate for explanatory, educational, and sky-guide scenes, not pure cinematic beauty shots unless requested.
- **When overlays should disappear:** Remove overlays for photorealistic observation, eclipse totality drama, premium poster visuals, or when they obscure the main subject.

## 11. Atmospheric Rendering

- **Twilight:** Twilight gradients should follow the Sun's position below the horizon and transition naturally from warm horizon glow to darker upper sky.
- **Civil twilight:** Sky may remain bright blue with visible horizon and only the brightest planets/stars.
- **Nautical twilight:** Horizon remains discernible; brighter stars and planets become prominent.
- **Astronomical twilight:** Sky approaches full darkness; Milky Way and faint stars may appear if light pollution is low.
- **Airglow:** Subtle green/red airglow may appear in dark-sky astrophotography, never as aggressive neon bands.
- **Light pollution:** Urban or suburban scenes should reduce faint stars and Milky Way visibility with realistic skyglow.
- **Horizon haze:** Haze may soften low-altitude objects and warm their color; it should not invent impossible object shapes.
- **Moonlight:** Moonlight can brighten landscapes, reduce star visibility, and create cool shadows, depending on phase and altitude.

## 12. Observation Perspective Rules

- **Human eye:** Lower saturation, limited faint detail, no long-exposure Milky Way unless dark-adapted context is explicitly described, and planets usually appear as bright points except the Moon and Sun.
- **Binocular:** Slightly magnified Moon detail, star clusters, bright comet coma, and some Jupiter moon context may be visible, but planetary disks remain small.
- **Small telescope:** Planetary disks, lunar craters, Saturn's rings, Jupiter bands, and bright deep-sky objects become plausible, with limited resolution and seeing effects.
- **Large telescope:** Higher planetary detail, sharper lunar relief, fainter moons, and more structured deep-sky objects are acceptable with atmospheric seeing constraints.
- **Astrophotography:** Long exposure may reveal Milky Way, nebula color, comet tails, and faint stars beyond human-eye visibility; must not be confused with naked-eye realism.
- **Composite documentary artwork:** Scale, timing, and exposure may be combined for explanation, but any non-literal composition must remain scientifically honest and visually coherent.

Acceptable rendering differences are driven by aperture, exposure, optical field of view, atmospheric seeing, sky brightness, and documentary intent. Differences are not an excuse to change object identity, geometry, phase, or physically required lighting.

## 13. Negative Rendering Rules

The following are explicitly forbidden unless a future document marks a specific educational exception:

- Oval planets or moons caused by stretching rather than real perspective or oblateness.
- Stretched planets, smeared disks, liquified surfaces, or warped rings.
- Fake rings around planets that do not have visible rings in the event context.
- Fake moons, extra planets, or invented companion objects.
- Fantasy textures, alien continents, impossible oceans, or decorative surface patterns.
- Artificial neon glow around planets, stars, the Moon, or the Sun.
- Incorrect shadows or terminators inconsistent with the Sun's direction.
- Wrong planetary colors or over-saturated stylized palettes.
- Impossible atmospheric effects, including clouds on airless bodies.
- Incorrect phase angles for inner planets, Moon, or eclipses.
- Incorrect eclipse geometry, off-center shadows, impossible annularity, or distorted lunar silhouettes.
- Overprocessed HDR look that destroys documentary credibility.
- Cartoon rendering, toy-like planets, plastic surfaces, or children's-book simplification in documentary outputs.

## 14. `planetRenderingRules` JSON

The reusable JSON contract below describes the canonical rule structure. It is a documentation schema, not implementation code.

```json
{
  "planetRenderingRules": {
    "planet": {
      "name": "Mars",
      "category": "terrestrial_planet",
      "visualIdentity": "Rust-red rocky planet with thin dusty atmosphere, darker albedo markings, and optional polar caps."
    },
    "geometry": {
      "diskShape": "circular",
      "allowNaturalOblateness": false,
      "phaseHandling": "physically_plausible_solar_illumination",
      "forbidStretching": true
    },
    "texture": {
      "expectedFeatures": ["dusty surface", "dark albedo markings", "polar caps when visible"],
      "detailLevel": "context_and_observation_mode_dependent",
      "forbiddenTextures": ["oceans", "vegetation", "fantasy lava rivers"]
    },
    "lighting": {
      "primarySource": "Sun",
      "terminator": "moderately sharp with thin atmospheric softening",
      "limbDarkening": "subtle"
    },
    "shadow": {
      "surfaceShadows": "physically plausible",
      "nightSide": "dark unless documentary fill light is explicitly specified",
      "eclipseOrTransitShadows": "geometry-dependent only"
    },
    "atmosphere": {
      "presence": "thin",
      "appearance": ["faint limb haze", "dust tint", "occasional pale clouds"],
      "forbiddenAtmosphere": ["thick Earth-like clouds", "neon glow"]
    },
    "scale": {
      "relativeGuidance": "smaller than Earth and Venus, larger than Mercury and Moon",
      "allowIllustrativeNonScaleComposition": true,
      "mustDiscloseNonScaleIntent": true
    },
    "artisticFlexibility": {
      "allowed": ["moderate contrast enhancement", "cinematic framing", "documentary color grading"],
      "limits": "must not alter identity, phase, geometry, or physical lighting"
    },
    "forbiddenRules": [
      "no oval disk",
      "no fake rings",
      "no Earth-like oceans",
      "no invented moons",
      "no fantasy textures",
      "no incorrect Sun direction"
    ],
    "qualityTargets": {
      "scientificAccuracy": "high",
      "documentaryQuality": "premium",
      "rendererIndependence": true,
      "familyIndependentBehavior": true,
      "backwardCompatibleWithV31": true
    }
  }
}
```

## 15. Integration

- **VisualCreativeDirector:** Uses these rules as the canonical astronomical truth layer when deciding visual intent, object appearance, and acceptable artistic latitude.
- **FamilyCreativeProfiles:** May influence mood, composition, pacing, or emphasis, but must not override object identity, phase, color truth, geometry, or forbidden rules.
- **BrandDesignSystem:** Provides premium documentary tone, restraint, typography, and visual polish while respecting astronomical accuracy.
- **CreativeDirectionContract:** Can carry normalized rendering intent and object rules forward without binding the system to a specific renderer.
- **PromptComposerV2:** May reference or translate these rules into prompt language in a future implementation, but this document does not change existing prompts.
- **CreativeQualityScoringEngine:** May eventually score compliance with geometry, texture, lighting, and forbidden-rule constraints, but no validation behavior changes are introduced here.

## 16. Future Expansion

Additional object modules can be added using the same structure: visual identity, colors, texture, geometry, lighting, shadows, scale, atmosphere/environment, artistic flexibility, forbidden mistakes, and quality targets.

Potential future modules include:

- **Asteroids:** Irregular rocky bodies, low gravity, cratered surfaces, and scale-aware depiction.
- **Aurora:** Magnetic-latitude behavior, curtain structure, color physics, and exposure realism.
- **ISS:** Sunlit orbital structure, scale, transit silhouettes, and horizon passes.
- **Satellites:** Point-like motion, glints, trails in long exposure, and constellation avoidance.
- **Rocket launches:** Exhaust plume physics, staging, twilight effects, and trajectory realism.
- **Spacecraft:** Mission-specific geometry, lighting, and documentary context.
- **Galaxies:** Morphology, dust lanes, color restraint, and telescope/exposure dependence.
- **Black holes:** Accretion disk lensing, relativistic effects, and non-fantasy visualization limits.

## 17. Non-goals

This V3.2C document explicitly does not introduce:

- Implementation code.
- Azure calls or Azure Image2 changes.
- Renderer dependency or provider-specific syntax.
- Prompt replacement or prompt edits.
- Pipeline phase changes.
- Image generation behavior changes.
- Validation changes.
- Narration changes.
- Any modification to existing runtime behavior.

## 18. Acceptance Criteria

This specification is accepted when it is:

- Renderer-independent.
- Scientifically accurate.
- Reusable across future image providers and review systems.
- Compatible with V3.2A Visual Creative Director architecture.
- Compatible with V3.2B Brand Design System architecture.
- Documentation-only.
- Free of implementation ambiguity for visual rendering intent.
- Delivered without modifying code files.
- Delivered without modifying prompts, Azure integration, narration, or pipeline behavior.

## Canonical Status

V3.2C is the canonical astronomical rendering specification for all future Drashyam visual generation. It defines how astronomical subjects should look before provider-specific translation, image generation, scoring, or review occurs.
