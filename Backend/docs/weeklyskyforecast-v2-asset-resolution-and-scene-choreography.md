# WeeklySkyForecast v2 — Phase 5: Asset Resolution + Scene Choreography

## Purpose
Phase 5 transforms `hybridScenePlanPackage` into a deterministic `sceneChoreographyPackage` that fully specifies *what* to render, *when* to render it, and *how* to compose it. The renderer is intentionally a dumb executor.

## Scene choreography philosophy
- Narrative segments map to a compact 4–6 scene plan.
- Reuse is preferred over scene explosion.
- Calm, premium, documentary-style pacing is mandatory.

## Render abstraction philosophy
- Rendering engines do not decide editorial intent.
- All timing, overlays, transitions, camera, motion, and asset fallback choices are precomputed.

## Asset resolution strategy
- Resolve celestial hero, utility overlays, and atmospheric backgrounds.
- Moon and Jupiter prefer transparent hero PNGs.
- Venus can use glow/light treatment.
- Background primitives: twilight gradient, starfield, horizon glow.
- Viewing-tip primitives: tripod overlay, phone framing overlay.
- Thumbnail uses high-contrast hero composition assets.

## Stellarium orchestration strategy
- Stellarium is selective and scene-purpose driven.
- `best_night_wide_scene` requires Stellarium orientation realism.
- Hero scenes may optionally consume Stellarium-style background context.
- Moon/Jupiter emotional close-up scenes should not require Stellarium.
- No SSC generation in this phase; only future SSC metadata markers.

## Hybrid composition strategy
- Hybrid scenes combine layered celestial assets + optional realistic sky layers.
- Scene composition is narration-led, with reuse priority and explicit render contracts.

## Overlay choreography rules
- Overlays are sparse and legible.
- Best-night scenes: west arrow (~+2s), time annotation (~+4s), gentle object labels.
- Hero grouping scenes: minimal labels only.
- Viewing tip scenes: tripod/phone framing overlays appear late.

## Motion rules
- Motion style: subtle, cinematic, emotionally calm.
- Avoid aggressive movement and hyper-editing.

## Camera behavior rules
- Supported behaviors: `Static`, `SlowPushIn`, `SlowPullOut`, `GentlePanLeft`, `GentlePanRight`, `ParallaxDepth`, `CinematicFloat`.
- Each scene must define primary and optional secondary camera behavior.

## Transition timing rules
- Timelines include overlap lead seconds and narration alignment.
- Transition cadence preserves breathing room and visual continuity.

## Asset fallback strategy
If preferred local assets are unavailable, never fail planning. Fall back in this order:
1. GeneratedImage
2. PublicImage
3. StockFootage

## Render contract philosophy
Render contracts are explicit and deterministic for:
- Hybrid compositor
- Stellarium renderer
- Celestial asset compositor
- Thumbnail compositor

Each contract includes expected inputs/outputs, reuse support, and compositing requirements.
