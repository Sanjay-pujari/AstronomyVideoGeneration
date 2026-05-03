# Debug Playbook

## 1) Fast triage checklist

1. Confirm API received run request (`/api/pipelines/run`).
2. Inspect orchestrator stage logs for first failing stage.
3. Check context diagnostics (`skyfield-night-plan-*`, `scene-observation-context`) for upstream data quality.
4. Validate narration/visual artifact existence before FFmpeg stage.
5. Read generated `ffmpeg.log` and per-segment command files.

## 2) Artifact-first debugging (most useful files)

- `visuals/capture-manifest.json` — expected Stellarium scene/script/capture mapping.
- `visuals/scripts/*.generated-ssc-context.json` — exact scene time/object context given to Stellarium.
- `visuals/scripts/object-scene-render-warning.json` — object-scene quality warnings.
- `narration.txt` + `narration.mp3` — speech stage outputs.
- `render-manifest.json`, `ffmpeg-command.txt`, `ffmpeg.log` — render stage source of truth.

## 3) Symptom → likely source

- **Pipeline fails in AstronomyData stage**
  - Likely sidecar or timezone/config mismatch in `AstronomyContextProvider` or `SkyfieldSidecarClient`.
- **Only fallback/overview scenes generated**
  - Sidecar returned empty/low-altitude visibility set, or filtering rejected all targets.
- **Script generation repeatedly falls back**
  - Azure OpenAI response format invalid JSON or deployment misconfigured.
- **No narration audio**
  - Azure Speech credentials/region/endpoint issues.
- **Placeholder screenshots instead of real captures**
  - Stellarium executable path missing/invalid, or capture mapping failed.
- **FFmpeg fails after visual generation succeeds**
  - Missing scene files/audio, probe duration failure, timeout, or concat mismatch.

## 4) Stage-specific diagnostics and where they come from

- **PipelineOrchestrator**
  - Stage start/complete/fail logs + stage alerts from `RunStageAsync`.
- **AstronomyContextProvider**
  - Adds serialized request/response/scene debug payloads into `context.VisualIdeas`.
- **SkyfieldSidecarClient**
  - Non-success HTTP status and payload validation warnings.
- **NightSkyVisibilityPlanner**
  - Console ranking lines for candidate inclusion/rejection.
- **ObservationTimeService / SceneObservationContext**
  - Selected reason/visibility/time diagnostics embedded in scene models.
- **PromptGeneration**
  - Retry + parse failure logs, unsupported deployment logs, fallback logs.
- **AzureSpeechSynthesisService**
  - Config validation errors and synthesis failure logs.
- **StellariumScriptBuilder**
  - Generated script is inspectable per scene; label API fallback info output in script.
- **StellariumVisualGenerationService**
  - Capture manifest, per-scene metadata, render warnings, placeholders.
- **FfmpegVideoRenderService**
  - Full render plan + command + stderr/stdout diagnostics in log files.

## 5) Minimal reproducible debug sequence

1. Trigger one run with fixed `date`, `locationName`, `timeZone`.
2. Save full pipeline output directory.
3. Verify `SceneObservationContexts` correctness (times/object ordering/timezone).
4. Replay only render stage using produced narration + visuals.
5. Compare failing run `ffmpeg.log` to successful baseline run.

## 6) Recommended breakpoints / code entrypoints

- `PipelineOrchestrator.RunAsync` (stage transitions + run state).
- `AstronomyContextProvider.BuildContextAsync` (fallback decisions).
- `AstronomyContextProvider.TryApplyNightPlanResponse` (scene synthesis).
- `SkyfieldSidecarClient.GetNightVisibilityPlanAsync` (HTTP failure conditions).
- `AzureOpenAiContentGenerationService.RequestCompletionAsync` (OpenAI request/response).
- `AzureSpeechSynthesisService.SynthesizeAsync` (audio generation path).
- `StellariumVisualGenerationService.PrepareVisualsAsync` (scene creation and capture fallback).
- `FfmpegVideoRenderService.RenderAsync` (input validation and render branch selection).
