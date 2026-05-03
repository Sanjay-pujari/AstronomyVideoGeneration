# Key Services Reference

## PipelineOrchestrator
- **File path:** `Backend/src/Astronomy.MediaFactory.Core/PipelineOrchestrator.cs`
- **Responsibility:** Owns end-to-end pipeline execution, stage tracking, fallback behavior, and run lifecycle persistence.
- **Main methods:** `RunAsync(...)` (+ internal `RunStageAsync<T>(...)` wrapper).
- **Input models:** `RunPipelineRequest`.
- **Output models:** `PipelineRun` (persisted + updated through repository).
- **Dependencies:** `IAstronomyContextProvider`, ranking/prompt/render/publish services, repository, metadata/alerting/operations options.
- **Diagnostics generated:** stage logs, stage failure/slow alerts, stage status persistence via `IPipelineStageRecorder`.
- **Common failure symptoms:** run status stuck/failed, stage timeout alerts, missing artifacts when downstream stage fails.

## AstronomyContextProvider
- **File path:** `Backend/src/Astronomy.MediaFactory.AstroData/Services/AstronomyContextProvider.cs`
- **Responsibility:** Builds `AstronomyContext` by combining NASA enrichment and sidecar-based night visibility planning.
- **Main methods:** `BuildContextAsync(...)`, `AddNasaContextAsync(...)`, `TryApplyNightPlanResponse(...)`.
- **Input models:** `DateOnly`, `ContentType`, location/timezone inputs + `ObservationOptions`.
- **Output models:** `AstronomyContext` containing `Events`, `NewsItems`, `VisualIdeas`, `SceneObservationContexts`.
- **Dependencies:** `NasaApodClient`, `NasaNeoWsClient`, `ISkyfieldSidecarClient`, options + logger.
- **Diagnostics generated:** structured visual-idea debug payloads (request/response/scene context), warning logs for sidecar fallbacks.
- **Common failure symptoms:** empty `SceneObservationContexts`, fallback-only scenes, no visible objects due to altitude thresholds/timezone issues.

## SkyfieldSidecarClient
- **File path:** `Backend/src/Astronomy.MediaFactory.AstroData/Clients/SkyfieldSidecarClient.cs`
- **Responsibility:** HTTP contract client for sidecar endpoints (`/ephemeris/daily-sky`, `/visibility/night-plan`).
- **Main methods:** `GetDailySkyAsync(...)`, `GetNightVisibilityPlanAsync(...)`.
- **Input models:** `SkyfieldDailySkyRequest`, `SkyfieldNightPlanRequest`.
- **Output models:** `SkyfieldDailySkyResponse?`, `SkyfieldNightPlanResponse?`.
- **Dependencies:** `HttpClient`, logger, JSON serialization contract.
- **Diagnostics generated:** warnings for non-success status, invalid payload shape, empty responses; error logs on exceptions.
- **Common failure symptoms:** null response from sidecar, schema mismatch, location/date validation failures, endpoint connectivity errors.

## NightSkyVisibilityPlanner
- **File path:** `Backend/src/Astronomy.MediaFactory.Core/NightSkyVisibilityPlanner.cs`
- **Responsibility:** Computes candidate visibility, ranks targets, and constructs balanced scene selections.
- **Main methods:** `BuildPlan(...)`, internal `Evaluate(...)`, `ScoreAndRank(...)`, `BuildScenes(...)`.
- **Input models:** `ObservationOptions`, `DateOnly`, optional `NightSkyCandidateObject[]`.
- **Output models:** `NightSkyPlan` with visible/not-visible objects and `SelectedScenes` (`SceneObservationContext`).
- **Dependencies:** `ObservationTimeService.BuildSamples(...)`, default candidate catalog.
- **Diagnostics generated:** console ranking lines (`[DailySkyGuideRanking] ...`).
- **Common failure symptoms:** rejected candidates due to low altitude/short visibility window; repetitive/low-diversity selections if candidate pool is constrained.

## ObservationTimeService
- **File path:** `Backend/src/Astronomy.MediaFactory.Core/ObservationTimeService.cs`
- **Responsibility:** Selects final scene observation times, either from precomputed scene contexts or fallback synthetic sampling.
- **Main methods:** `SelectSceneTimes(...)`, `BuildSamples(...)`.
- **Input models:** `AstronomyContext`, `DateOnly`, `ObservationOptions`.
- **Output models:** `IReadOnlyList<SceneObservationTime>`.
- **Dependencies:** `NightSkyVisibilityPlanner.DefaultCandidates` (friendliness/type hints), timezone conversion.
- **Diagnostics generated:** indirect via selected reason/visibility fields in returned scene timing model.
- **Common failure symptoms:** filler scenes dominating output, object times clustered, unexpected invisibility when altitude threshold is high.

## SceneObservationContext
- **File path:** `Backend/src/Astronomy.MediaFactory.Core/NightSkyVisibilityPlanner.cs` (type definition), consumed in `Models.cs`.
- **Responsibility:** Canonical scene-level astronomy context handoff for narration + visuals.
- **Main methods:** N/A (data model).
- **Input models:** populated from sidecar visibility and observation selection logic.
- **Output models:** mapped to `SceneObservationTime`, `StellariumScene` inputs, narration context payloads.
- **Dependencies:** used by context provider, observation time service, and visual generation.
- **Diagnostics generated:** serialized into context visual ideas (`scene-observation-context`, `narration-context`).
- **Common failure symptoms:** null/zero altitude/azimuth causing weak pointing cues; wrong timezone causing observation mismatch.

## PromptGeneration
- **File path:** `Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs`
- **Responsibility:** Converts astronomy context into strict JSON script payloads (long + short form) via Azure OpenAI.
- **Main methods:** `GenerateAsync(...)`, `GenerateShortAsync(...)`, `RequestCompletionAsync(...)`.
- **Input models:** `ContentType`, `AstronomyContext`, prompt-feedback context.
- **Output models:** `ScriptResult`, `ShortScriptResult`.
- **Dependencies:** `IPromptBuilder`, Azure OpenAI options, managed identity/API key auth.
- **Diagnostics generated:** retry attempt logs, parse/validation warnings, unsupported deployment errors, fallback usage logs.
- **Common failure symptoms:** fallback scripts due to invalid JSON completion, wrong deployment type (embeddings), missing endpoint/key.

## AzureSpeechSynthesisService
- **File path:** `Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechSynthesisService.cs`
- **Responsibility:** Produces narration MP3 and persists raw narration text.
- **Main methods:** `SynthesizeAsync(...)`.
- **Input models:** raw script text + output directory + `AzureSpeechOptions`.
- **Output models:** narration audio file path.
- **Dependencies:** `IAzureSpeechClient`, `IFileSystem`, speech options.
- **Diagnostics generated:** voice-order info logs, configuration validation exceptions, synthesis failure logs.
- **Common failure symptoms:** missing key/region/endpoint/resourceId, no narration.mp3 produced, downstream FFmpeg failure due to absent audio.

## StellariumScriptBuilder
- **File path:** `Backend/src/Astronomy.MediaFactory.Rendering/StellariumScriptBuilder.cs`
- **Responsibility:** Converts a `StellariumScene` into executable Stellarium startup script content.
- **Main methods:** `BuildSceneScript(...)`.
- **Input models:** `StellariumScene` (with `SceneObservationContext`).
- **Output models:** SSC/JS script string for Stellarium automation.
- **Dependencies:** `StellariumOptions` and scene metadata (object/time/location/altitude).
- **Diagnostics generated:** script includes defensive `safeCall` and label-failure `core.output(...)` path.
- **Common failure symptoms:** object not selected/labelled due to name mismatch, compatibility differences in Stellarium scripting API.

## StellariumVisualGenerationService
- **File path:** `Backend/src/Astronomy.MediaFactory.Rendering/StellariumVisualGenerationService.cs`
- **Responsibility:** Builds per-scene scripts/manifests, runs Stellarium capture, and guarantees image outputs via placeholder fallback.
- **Main methods:** `PrepareVisualsAsync(...)`.
- **Input models:** `AstronomyContext`, output directory.
- **Output models:** `IReadOnlyCollection<string>` visual file paths.
- **Dependencies:** `StellariumScriptBuilder`, `IObservationTimeService`, `StellariumOptions`, `ObservationOptions`.
- **Diagnostics generated:** capture-manifest JSON, scene metadata JSON, generated SSC context JSON, render warning JSON, placeholder marker text files, logs.
- **Common failure symptoms:** missing Stellarium executable, empty/black screenshots, placeholder images substituted for real captures.

## FfmpegVideoRenderService
- **File path:** `Backend/src/Astronomy.MediaFactory.Rendering/FfmpegVideoRenderService.cs`
- **Responsibility:** Validates render inputs, emits FFmpeg plans/commands/diagnostics, and composes final MP4.
- **Main methods:** `RenderAsync(...)`, `RenderFromSegmentsAsync(...)`, `RenderFromImageSegmentsAsync(...)`.
- **Input models:** `RenderManifest`.
- **Output models:** final video output path (`manifest.OutputPath`).
- **Dependencies:** `RenderManifestBuilder`, `FfmpegArgumentBuilder`, `IProcessRunner`, `IFileSystem`, rendering options.
- **Diagnostics generated:** `render-manifest.json`, `ffmpeg-input.txt`, `ffmpeg-segments.txt`, `caption-metadata.json`, `subtitles.scaffold.srt`, `ffmpeg-command.txt`, `ffmpeg.log`, per-segment command files.
- **Common failure symptoms:** missing asset validation errors, FFmpeg segment timeout, concat failure, narration duration probe failures.
