# Pipeline 21-Phase Specification v1.0

## Scope

This document defines the RC2 content-planning orchestration model for:

`POST /api/content-planning/rc2/batch-generate-from-plans`

The existing V4 endpoint remains unchanged:

`POST /api/content-planning/batch-generate-from-plans`

RC2 uses a practical 21-phase orchestration model. The model preserves existing early production behavior while adding editorial phase outputs that downstream phases can consume without inventing unsupported astronomy facts.

## Early RC2 Phase Mapping

| Phase | RC2 phase name | Purpose |
| --- | --- | --- |
| 1 | Load Plan | Load and persist the selected content plan request. |
| 2 | Build ProductionEventIntelligence | Build the event intelligence artifact used by later planning phases. |
| 3 | Generate QuestionAnswerSet | Generate the question-answer set from production event intelligence. |
| 4 | Validate Questions | Validate the generated question-answer set before scene planning. |
| 5 | Generate Scene Plan | Generate `question-engine/question-driven-scene-plan.json`. |
| 6 | Scene Intent Builder | Build editorial scene intents from Phase 5 scene-plan output and upstream facts. |

## Phase 5 and Phase 6 Sequencing Decision

Scene Intent Builder was originally considered as Phase 5. RC2 preserves Phase 5 as **Generate Scene Plan** because the scene intent builder depends on the Phase 5 scene-plan artifact. Therefore Phase 6 is **Scene Intent Builder**.

This sequencing keeps the dependency order explicit:

1. Phase 5 produces `question-engine/question-driven-scene-plan.json`.
2. Phase 6 reads that scene plan plus upstream intelligence and question-answer facts.
3. Phase 6 writes editorial artifacts under the editorial workspace.

## Editorial Workspace

RC2 creates the editorial workspace under the plan output root:

`<planOutputRoot>/editorial/`

## Phase 6 Inputs

Phase 6 reads the following artifacts:

- `plan-input/production-event-intelligence.json`
- `question-engine/question-answer-set.json`
- `question-engine/question-driven-scene-plan.json`

## Phase 6 Outputs

Phase 6 writes:

- `editorial/scene-intents.json`
- `editorial/editorial-diagnostics.json`

The RC2 response phase list reports:

`Phase 6 = Scene Intent Builder`

The generated files reported for the production execution include:

- `editorial/scene-intents.json`
- `editorial/editorial-diagnostics.json`

## SceneIntent Contract

Each scene intent includes:

- `sceneId`
- `scenePurpose`
- `language`
- `eventType`
- `eventName`
- `requiredFacts`
- `observationFacts`
- `narrationIntent`
- `visualIntent`
- `scientificConstraints`
- `editorialTone`
- `missingFactWarnings`

Phase 6 must not invent missing facts. If required metadata is not available in the Phase 6 inputs, the missing value is left empty/null in the structured fact contract and a `missingFactWarnings` entry is emitted.

## Endpoint Isolation

Phase 6 registration is RC2-only. The V4 endpoint and current Phase 1-5 behavior are intentionally preserved.
