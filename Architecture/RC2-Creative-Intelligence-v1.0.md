# RC2 Creative Intelligence v1.0

RC2 Creative Intelligence is the public **Phase 7 = Creative Intelligence Foundation**. It begins the decision-making layer that describes how the audience should experience an astronomy story before any downstream media generator writes prompts or creates assets.

## Scope

Milestone C1 adds internal sub-phases:

1. **7.1 Creative Storyboard Builder** — consumes Phase 6 editorial artifacts and writes `creative/creative-storyboard.json`.
2. **7.6 Creative Diagnostics** — writes `creative/creative-diagnostics.json` with input existence, output paths, scene counts, and warnings.

This milestone does not change the existing V4 endpoint, the RC2 endpoint request body, Phase 1-6 behavior, narration, hero generation, thumbnail generation, gallery generation, motion generation, TTS, SRT, rendering, or video assembly.

## Inputs

Creative Intelligence consumes the Editorial Contract and its supporting story artifacts:

- `editorial/editorial-contract.json`
- `editorial/story-graph.json`
- `editorial/scene-intents.json`

The Editorial Contract remains the factual and editorial boundary for Creative Intelligence. Creative Intelligence must carry missing-fact warnings forward instead of inventing unsupported sky facts.

## Outputs

Creative Intelligence creates a `creative/` folder under the plan output root:

- `creative/creative-storyboard.json`
- `creative/creative-diagnostics.json`

## Responsibility Split

Creative Intelligence decides how the audience should experience the story. It sets viewer focus, emotional role, visual role, motion role, composition intent, camera intent, lighting intent, motion intent, transition intent, visual accuracy rules, and prohibited visual choices.

Generators execute intents; intelligence layers make decisions. Therefore the Creative Storyboard is not an image prompt. It is a structured creative decision contract that later phases can consume when producing prompts or assets.

## Downstream Consumption

Hero, Thumbnail, Gallery, and Motion will later consume the Creative Storyboard. Milestone C1 intentionally does not wire the storyboard into Hero, Thumbnail, Gallery, Motion, or Narration yet.

## Visual Accuracy Rules

Each storyboard scene carries astronomy visual accuracy rules, including circular planets, restrained angular separation, no false surface detail, no physical touching between planets, no daylight after-sunset visuals, respect for available direction and timing metadata, and a requirement not to visualize missing altitude, constellation, moon interference, or brightness as confirmed facts.

## Prohibited Visual Choices

Each storyboard scene prohibits fantasy skies, sci-fi spaceships, alien elements, distorted planets, unrealistic planet scale unless explicitly marked as editorial thumbnail treatment, misleading constellation labels, fake telescope detail, and overdramatic disaster-like lighting.
