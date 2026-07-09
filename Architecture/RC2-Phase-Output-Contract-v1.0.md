# RC2 Phase Output Contract v1.0

## 1. Purpose

This contract freezes the expected responsibility, inputs, outputs, diagnostics, validation, failure behavior, and downstream consumers for every RC2 / Platform V2 phase.

Every phase must define:

- Responsibility
- Inputs
- Outputs
- Diagnostics
- Validation file
- Failure behavior
- Downstream consumers

This document is the source of truth for validating RC2 phase execution.

## 2. Global Rules

Every phase must:

1. Write `validation/phase-XX-validation.json`.
2. Write diagnostics when applicable.
3. Register output files in the phase result.
4. Register output files in `generatedFiles`.
5. Update the phase manifest.
6. Never count stale files as current outputs.
7. Respect `executionMode=RebuildOutputs` and `overwriteExisting=true`.
8. Fail clearly if required inputs are missing.
9. Preserve production endpoint behavior.
10. Keep the RC2 request body unchanged.

## 3. Phase Validation Contract

Every phase validation file must include:

```json
{
  "phaseNo": 0,
  "phaseName": "",
  "status": "Succeeded | Failed | Skipped",
  "startedUtc": "",
  "finishedUtc": "",
  "durationMs": 0,
  "inputFiles": [],
  "outputFiles": [],
  "diagnosticFiles": [],
  "warnings": [],
  "errors": [],
  "exceptionType": "",
  "exceptionMessage": "",
  "canRetry": true,
  "reason": ""
}
```

The validation file must be written for:

- Success
- Failure
- Timeout
- Exception
- Skipped phase

## 4. Phase Manifest Contract

The phase manifest must include:

- `requestedStartPhaseNo`
- `requestedEndPhaseNo`
- `executedPhases`
- `skippedPhases`
- `failedPhases`
- `lastCompletedPhaseNo`
- `lastFailedPhaseNo`
- `outputRoot`
- `generatedFiles`
- `validationFiles`
- `diagnosticsFiles`
- `createdUtc`
- `updatedUtc`

## 5. Phase-by-Phase Output Contract

### Phase 1 — Run Setup / Plan Selection

**Responsibility:**
Resolve exact content plan, output root, execution mode, and initial run state.

**Inputs:**

- Request body
- `content_generation_plans` table
- `astronomy_event_intelligence` linkage

**Outputs:**

- Output root
- Run manifest / phase manifest initialized
- `validation/phase-01-validation.json`

**Diagnostics:**

- Plan selection diagnostics when selection, linkage, or output-root resolution is non-trivial.

**Validation:**

- Plan selected
- `outputRoot` resolved
- Manifest initialized

**Failure:**

- If manual `planId` is provided but no plan is selected, fail.
- Do not return `Success=True` with `SelectedPlanCount=0`.

**Consumers:**

- All later phases.

### Phase 2 — Domain Intelligence

**Responsibility:**
Ensure event/domain facts are available and normalized.

**Inputs:**

- Selected plan
- `astronomy_event_intelligence`
- Event objects

**Outputs:**

- Domain/event intelligence artifacts if applicable
- `validation/phase-02-validation.json`

**Diagnostics:**

- Domain/event normalization diagnostics when facts are created, repaired, or inferred.

**Validation:**

- Primary/secondary objects resolved where applicable
- Event facts available

**Failure:**

- Fail clearly when required event facts or object references cannot be resolved.

**Consumers:**

- Question Engine
- Editorial Intelligence
- Creative Intelligence

### Phase 3 — Question / Story Planning

**Responsibility:**
Generate or resolve question-answer set and question-driven scene plan.

**Inputs:**

- Domain intelligence
- Selected plan
- Language
- Region

**Outputs:**

- `question-engine/question-answer-set.json`
- `question-engine/question-driven-scene-plan.json`
- `validation/phase-03-validation.json`

**Diagnostics:**

- Question engine diagnostics when questions, answers, or scene plans are generated or repaired.

**Validation:**

- QA set exists
- Scene plan exists
- Scene count > 0

**Failure:**

- Fail clearly when the QA set or scene plan cannot be produced.

**Consumers:**

- Story Intelligence
- Narration
- Story Frames

### Phase 4 — Story Intelligence

**Responsibility:**
Convert scene plan into story graph.

**Inputs:**

- `question-driven-scene-plan.json`
- `question-answer-set.json`
- Observation metadata if available

**Outputs:**

- `editorial/story-graph.json`
- `validation/phase-04-validation.json`

**Diagnostics:**

- Story graph diagnostics when scene ordering, purpose assignment, or metadata alignment requires explanation.

**Validation:**

- Story graph exists
- Scenes exist
- Scene purposes assigned

**Failure:**

- Fail clearly when the story graph cannot be generated or has no valid scenes.

**Consumers:**

- Editorial Contract
- Creative Storyboard
- Narration

### Phase 5 — Editorial Intelligence

**Responsibility:**
Create observation metadata, scene intents, and editorial contract.

**Inputs:**

- Domain intelligence
- Story graph
- Question/scene plan

**Outputs:**

- `editorial/observation-metadata.json`
- `editorial/scene-intents.json`
- `editorial/editorial-contract.json`
- `editorial/editorial-diagnostics.json`
- `validation/phase-05-validation.json`

**Diagnostics:**

- `editorial/editorial-diagnostics.json`

**Validation:**

- Editorial contract exists
- Scene intents exist
- Observation metadata exists

**Failure:**

- Fail clearly when the editorial contract, scene intents, or observation metadata cannot be produced.

**Consumers:**

- Creative Intelligence
- Narration Studio
- Visual/Scene Assets

### Phase 6 — Creative Intelligence / Story Frames

**Responsibility:**
Create creative storyboard and long/short story-frame planning artifacts.

**Inputs:**

- `editorial/editorial-contract.json`
- `editorial/story-graph.json`
- `editorial/scene-intents.json`

**Outputs:**

- `creative/creative-storyboard.json`
- `creative/creative-diagnostics.json`
- `long-story-frames/`
- `short-story-frames/`
- `validation/phase-06-validation.json`

**Diagnostics:**

- `creative/creative-diagnostics.json`

**Validation:**

- Creative storyboard exists
- `long-story-frames/` exist when `LongVideo` requested
- `short-story-frames/` exist when `ShortVideo` requested
- Dimensions are correct per format

**Failure:**

- Fail clearly when required storyboard or requested story-frame outputs cannot be produced.

**Consumers:**

- Scene Asset Generation
- Hero
- Thumbnail
- Gallery
- Motion
- Narration

### Phase 7 — Narration Studio V5

**Responsibility:**
Generate narration planning, style contract, prompt, LLM request, narration, and diagnostics.

**Inputs:**

- `editorial/editorial-contract.json`
- `creative/creative-storyboard.json`
- Narration briefs / story graph
- Documentary-style-contract if already created

**Outputs:**

- `narration-v5/narration-plan.json`
- `narration-v5/narration-briefs.json`
- `narration-v5/style/documentary-style-contract.json`
- `narration-v5/style/documentary-style-diagnostics.json`
- `narration-v5/prompt-preview.md`
- `narration-v5/prompt-quality.json`
- `narration-v5/llm-request.json`
- `narration-v5/narration.json`
- `narration-v5/narration-diagnostics.json`
- `validation/phase-07-validation.json`

**Diagnostics:**

- `narration-v5/style/documentary-style-diagnostics.json`
- `narration-v5/prompt-quality.json`
- `narration-v5/narration-diagnostics.json`

**Validation:**

- Narration scenes match expected scene count
- No engineering leakage
- Prompt quality passes
- Narration diagnostics exist
- Channel ending exists exactly once

**Failure:**

- Fail clearly when narration, prompt quality, diagnostics, or channel-ending requirements are not satisfied.

**Consumers:**

- TTS
- SRT
- Video
- QA

### Phase 8 — Scene Asset Generation

**Responsibility:**
Generate scene image assets for every requested output format.

**Inputs:**

- `creative/creative-storyboard.json`
- `editorial/editorial-contract.json`
- `long-story-frames/`
- `short-story-frames/`

**Outputs:**

- `scene-assets-v3/long/`
- `scene-assets-v3/short/`
- `validation/phase-08-validation.json`

**Long output:**

`scene-assets-v3/long/`

- `scene-manifest-v3.json`
- `scene-assets-v3-diagnostics.json`
- `visual-timeline-v3.json`
- Generated images

**Short output:**

`scene-assets-v3/short/`

- `scene-manifest-v3.json`
- `scene-assets-v3-diagnostics.json`
- `visual-timeline-v3.json`
- Generated images

**Diagnostics:**

- `scene-assets-v3/long/scene-assets-v3-diagnostics.json` when long assets are requested
- `scene-assets-v3/short/scene-assets-v3-diagnostics.json` when short assets are requested

**Validation:**

- Long assets generated when `LongVideo` requested
- Short assets generated when `ShortVideo` requested
- Long dimensions match long format
- Short dimensions match portrait format
- Generated images match story-frame scene IDs
- No stale files counted as current outputs

**Failure:**

- Fail clearly when requested scene assets cannot be generated, dimensions are invalid, or generated images do not match story-frame scene IDs.

**Consumers:**

- Gallery
- Motion
- Video Assembly
- QA

### Phase 9 — Hero Asset

**Responsibility:**
Generate hero image / hero prompt package.

**Inputs:**

- Creative storyboard
- Editorial contract
- Story frames if needed

**Outputs:**

- `hero/`
- `validation/phase-09-validation.json`

**Diagnostics:**

- Hero diagnostics when image generation, prompt construction, or format alignment requires explanation.

**Validation:**

- Hero package exists when requested or required by downstream publishing.
- Hero outputs are registered in the phase result and `generatedFiles`.

**Failure:**

- Fail clearly when required hero assets cannot be generated.

**Consumers:**

- Publishing
- QA

### Phase 10 — Thumbnail

**Responsibility:**
Generate long/short thumbnail assets.

**Inputs:**

- Creative storyboard
- Editorial contract
- Hero/scene assets if needed

**Outputs:**

- `thumbnail/`
- `validation/phase-10-validation.json`

**Diagnostics:**

- Thumbnail diagnostics when prompt construction, source-asset selection, or format alignment requires explanation.

**Validation:**

- Thumbnail assets exist for each requested output format.
- Thumbnail outputs are registered in the phase result and `generatedFiles`.

**Failure:**

- Fail clearly when required thumbnail assets cannot be generated.

**Consumers:**

- Publishing
- QA

### Phase 11 — Gallery

**Responsibility:**
Generate gallery assets aligned to story.

**Inputs:**

- Creative storyboard
- Scene assets
- Editorial contract

**Outputs:**

- `gallery/`
- `validation/phase-11-validation.json`

**Diagnostics:**

- Gallery diagnostics when gallery selection, alignment, or generated image decisions require explanation.

**Validation:**

- Gallery assets exist when requested.
- Gallery assets align to story and requested formats.

**Failure:**

- Fail clearly when required gallery assets cannot be generated or aligned to story.

**Consumers:**

- Publishing
- QA

### Phase 12 — TTS

**Responsibility:**
Generate scene-based audio.

**Inputs:**

- `narration-v5/narration.json`

**Outputs:**

- `tts/`
- `validation/phase-12-validation.json`

**Diagnostics:**

- TTS diagnostics when audio generation, voice selection, or timing metadata requires explanation.

**Validation:**

- Scene-based audio exists for narration scenes.
- TTS outputs are registered in the phase result and `generatedFiles`.

**Failure:**

- Fail clearly when required narration input is missing or scene audio cannot be generated.

**Consumers:**

- SRT
- Video

### Phase 13 — Subtitles / SRT

**Responsibility:**
Generate scene-aligned subtitles.

**Inputs:**

- `narration-v5/narration.json`
- TTS timing/audio

**Outputs:**

- `srt/`
- `subtitles/`
- `validation/phase-13-validation.json`

**Diagnostics:**

- Subtitle diagnostics when timing, segmentation, or scene alignment requires explanation.

**Validation:**

- Scene-aligned subtitles exist.
- Subtitle outputs are registered in the phase result and `generatedFiles`.

**Failure:**

- Fail clearly when narration, TTS timing/audio, or subtitle generation requirements are missing.

**Consumers:**

- Video

### Phase 14 — Motion Planning

**Responsibility:**
Create motion plan for generated assets.

**Inputs:**

- Creative storyboard
- Scene assets
- Narration timing

**Outputs:**

- `motion/`
- `validation/phase-14-validation.json`

**Diagnostics:**

- Motion diagnostics when scene timing, image selection, or camera-movement planning requires explanation.

**Validation:**

- Motion plan exists for generated scene assets.
- Motion outputs are registered in the phase result and `generatedFiles`.

**Failure:**

- Fail clearly when required assets or timing inputs are missing, or when motion planning cannot be completed.

**Consumers:**

- Video

### Phase 15 — Video Assembly

**Responsibility:**
Render short and long videos.

**Inputs:**

- Scene assets
- Narration audio
- Subtitles
- Motion plan

**Outputs:**

- `video/short/`
- `video/long/`
- `validation/phase-15-validation.json`

**Diagnostics:**

- Video assembly diagnostics when render settings, timing, format decisions, or encoder output require explanation.

**Validation:**

- Short video exists when `ShortVideo` requested.
- Long video exists when `LongVideo` requested.
- Render outputs are registered in the phase result and `generatedFiles`.

**Failure:**

- Fail clearly when required render inputs are missing or requested videos cannot be rendered.

**Consumers:**

- QA
- Publishing

### Phase 16 — Manifest / Packaging

**Responsibility:**
Create final production manifest and publishing package.

**Inputs:**

- All generated assets

**Outputs:**

- Final manifest
- Publishing package
- `validation/phase-16-validation.json`

**Diagnostics:**

- Packaging diagnostics when asset inclusion, manifest generation, or publishing package decisions require explanation.

**Validation:**

- Final manifest exists.
- Publishing package exists.
- Package references only current outputs.

**Failure:**

- Fail clearly when generated assets are incomplete or the package cannot be created.

**Consumers:**

- Publishing

### Phase 17 — Production QA

**Responsibility:**
Validate scientific, editorial, creative, and technical quality.

**Inputs:**

- All phase outputs

**Outputs:**

- `qa/`
- `comparison/`
- `validation/phase-17-validation.json`

**Diagnostics:**

- QA diagnostics and comparison artifacts for scientific, editorial, creative, and technical checks.

**Validation:**

- QA outputs exist.
- Scientific, editorial, creative, and technical checks are recorded.
- QA failures are reported as explicit errors or warnings according to severity.

**Failure:**

- Fail clearly when blocking QA checks fail or required phase outputs are unavailable.

**Consumers:**

- Final completion

### Phase 18 — Completion

**Responsibility:**
Mark run/plan completion and final status.

**Inputs:**

- QA result
- Generated outputs

**Outputs:**

- Completion marker
- Final status update
- `validation/phase-18-validation.json`

**Diagnostics:**

- Completion diagnostics when final status, plan state, or portal/API/publishing handoff requires explanation.

**Validation:**

- Completion marker exists.
- Final status update is recorded.
- Final status reflects QA result and generated outputs.

**Failure:**

- Fail clearly when QA result is missing, generated outputs are incomplete, or final status cannot be updated.

**Consumers:**

- Portal/API/publishing

## 6. Failure Behavior

On any failure:

- Write validation file.
- Write diagnostics if available.
- Update phase manifest.
- Mark phase result failed.
- Do not mark stale files as current outputs.
- Stop execution unless phase is explicitly skippable.

## 7. Format-Specific Output Rules

### Long format

- Landscape
- Default target: `1920x1080` or configured long resolution
- Output root: `scene-assets-v3/long`

### Short format

- Portrait
- Default target: `2160x3840` or configured short resolution
- Output root: `scene-assets-v3/short`

Provider image size may differ from final render size, but diagnostics must record:

- Requested final target size
- Provider requested size
- Post-processing resize/crop size

## 8. Implementation Checklist

Before changing any phase:

- Check this contract.
- Update diagnostics.
- Update validation.
- Update manifest.
- Run phase-specific test.
- Confirm production endpoint unchanged.
