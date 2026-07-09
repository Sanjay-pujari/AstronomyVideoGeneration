# RC2 Story Intelligence v1.0

## Scope

RC2 Story Intelligence is an internal milestone inside public **Phase 6 = Editorial Intelligence Foundation**. The public phase name, endpoint request body, and earlier Phase 1–5 behavior remain stable; Story Intelligence only expands the internal editorial artifacts produced by Phase 6.

## Phase 6 Internal Sub-phases

1. **6.1 Observation Metadata Builder** — extracts supported observation facts from production inputs into `editorial/observation-metadata.json`.
2. **6.2A Story Graph Builder** — creates `editorial/story-graph.json` from the question-engine scene plan, question-answer set, and observation metadata.
3. **6.2B Scene Intent Builder** — creates `editorial/scene-intents.json` from the story graph and observation metadata.
4. **6.3 Editorial Contract Builder** — creates `editorial/editorial-contract.json`, including the story graph summary, scene intents, narration facts, and visual facts.
5. **6.4 Editorial Diagnostics** — creates `editorial/editorial-diagnostics.json` with file, count, and warning diagnostics.

## Story Graph Builder Responsibility

The Story Graph Builder translates question-engine story structure into a factual editorial graph. It uses:

- `question-engine/question-driven-scene-plan.json` as the story structure source.
- `question-engine/question-answer-set.json` as a loaded input for provenance diagnostics.
- `editorial/observation-metadata.json` as the factual source.

The builder must not invent astronomy facts. Required observation facts are copied from observation metadata when available, and missing facts are surfaced as warnings.

`editorial/story-graph.json` includes:

- `storyGraphVersion: "AstroPulse-StoryGraph-v1"`
- `orchestrationVersion: "RC2"`
- event, language, and region metadata
- `storyArc`
- `scenes`
- `transitions`
- `requiredObservationFacts`
- `missingFactWarnings`

## Scene Purpose Mapping

When a source scene already provides a specific purpose, the source purpose is preserved. If the source purpose is missing, generic, or unknown, the fallback mapping is used:

| Scene order | Fallback purpose |
| --- | --- |
| 1 | Hook |
| 2 | Discovery |
| 3 | Science |
| 4 | Observation |
| 5 | Takeaway |
| 6+ | SupportingDetail |

If the scene plan contains fewer than five scenes, Phase 6 creates only the available scenes and records a warning.

## SceneIntent Dependency

The Scene Intent Builder now consumes:

- `editorial/story-graph.json`
- `editorial/observation-metadata.json`

It emits one `SceneIntent` per story graph scene and preserves story purposes such as `Hook`, `Discovery`, `Science`, `Observation`, `Takeaway`, and `SupportingDetail`. It must not default scene purposes to `Editorial`.

## Editorial Contract

The Editorial Contract Builder includes:

- a `storyGraph` summary
- updated multi-scene `sceneIntents`
- `requiredNarrationFacts` from observation metadata-backed intent facts
- `requiredVisualFacts` from story graph and observation metadata

## Stability

Public **Phase 6 remains Editorial Intelligence Foundation**. Story Intelligence is an internal foundation within that phase and does not change the RC2 request body, the existing V4 endpoint, Phase 1–5 behavior, or TTS/SRT/rendering/video logic.
