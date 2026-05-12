# Repository Code Graph / Project Map

## Top-level layout

- `Backend/src/Astronomy.MediaFactory.Api` — HTTP API host, endpoint wiring, DI bootstrap.
- `Backend/src/Astronomy.MediaFactory.Core` — orchestration, domain models, pipeline contracts, ranking/planning services.
- `Backend/src/Astronomy.MediaFactory.AstroData` — astronomy data acquisition + Skyfield sidecar integration.
- `Backend/src/Astronomy.MediaFactory.ContentGen` — prompt construction + Azure OpenAI content generation.
- `Backend/src/Astronomy.MediaFactory.Rendering` — speech synthesis, Stellarium scripting/capture, FFmpeg assembly.
- `Backend/src/Astronomy.MediaFactory.Infrastructure` — persistence, operations, alerting, startup validation.
- `Backend/src/Astronomy.MediaFactory.Contracts` — strongly-typed options/config contracts.
- `Backend/python/skyfield_sidecar` — FastAPI sidecar for ephemeris/visibility APIs.

## Runtime graph (high-value path)

1. API entrypoint `POST /api/pipelines/run` calls `PipelineOrchestrator.RunAsync(...)`.
2. `PipelineOrchestrator` acquires `AstronomyContext` via `IAstronomyContextProvider`.
3. `AstronomyContextProvider` optionally calls `SkyfieldSidecarClient.GetNightVisibilityPlanAsync(...)` to build scene-ready visibility data.
4. Content pipeline builds script (`IScriptGenerationService`), audio (`ISpeechSynthesisService`), visuals (`IVisualAssetProvider`), and video (`IVideoRenderService`).
5. Publishing/archival services upload outputs and optionally publish.

## Key files by concern

### API + orchestration
- `Backend/src/Astronomy.MediaFactory.Api/Program.cs`
- `Backend/src/Astronomy.MediaFactory.Core/PipelineOrchestrator.cs`
- `Backend/src/Astronomy.MediaFactory.Core/Interfaces.cs`

### Astro data + planning
- `Backend/src/Astronomy.MediaFactory.AstroData/Services/AstronomyContextProvider.cs`
- `Backend/src/Astronomy.MediaFactory.AstroData/Clients/SkyfieldSidecarClient.cs`
- `Backend/src/Astronomy.MediaFactory.Core/NightSkyVisibilityPlanner.cs`
- `Backend/src/Astronomy.MediaFactory.Core/ObservationTimeService.cs`
- `Backend/src/Astronomy.MediaFactory.Core/Models.cs`

### NASA API usage
- `AstronomyContextProvider` actively depends on `NasaApodClient` for APOD data and `NasaNeoWsClient` for near-Earth object feed data when building `AstronomyContext`.
- `AstronomyApis:NasaBaseUrl` and `AstronomyApis:NasaApiKey` are still runtime configuration for those NASA clients and should remain configured while those clients are registered.

### Prompt/content generation
- `Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs`
- `Backend/src/Astronomy.MediaFactory.ContentGen/PromptBuilder.cs`
- `Backend/src/Astronomy.MediaFactory.Core/AstronomyPromptBuilder.cs`

### Rendering
- `Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechSynthesisService.cs`
- `Backend/src/Astronomy.MediaFactory.Rendering/StellariumScriptBuilder.cs`
- `Backend/src/Astronomy.MediaFactory.Rendering/StellariumVisualGenerationService.cs`
- `Backend/src/Astronomy.MediaFactory.Rendering/FfmpegVideoRenderService.cs`

## Debugging navigation shortcuts

- Find pipeline stage boundaries: search `RunStageAsync(` in `PipelineOrchestrator.cs`.
- Find sidecar request/response schema: `SkyfieldNightPlanRequest`, `SkyfieldNightPlanResponse` in `SkyfieldSidecarClient.cs`.
- Find scene handoff model: `SceneObservationContext` in `NightSkyVisibilityPlanner.cs` and `AstronomyContext.SceneObservationContexts` in `Models.cs`.
- Find rendering artifacts/logs emitted: look for `*.json`, `ffmpeg.log`, `*.placeholder.txt` writes in rendering services.
