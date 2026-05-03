# Pipeline Flow

## End-to-end flow

```mermaid
flowchart LR
    A[/api/pipelines/run]
    B[AstronomyData]
    C[Skyfield /visibility/night-plan]
    D[SceneObservationContext]
    E[PromptGeneration]
    F[SpeechSynthesis]
    G[VisualGeneration]
    H[Stellarium SSC]
    I[FFmpeg render]
    J[final-video.mp4]

    A --> B --> C --> D --> E --> F --> G --> H --> I --> J
```

## Stage mapping to code

1. **`/api/pipelines/run`**
   - API route in `Backend/src/Astronomy.MediaFactory.Api/Program.cs`.
   - Invokes `PipelineOrchestrator.RunAsync`.

2. **AstronomyData**
   - Implemented by `AstronomyContextProvider.BuildContextAsync`.
   - Adds APOD/news/visual ideas and visibility context.

3. **Skyfield `/visibility/night-plan`**
   - Called through `SkyfieldSidecarClient.GetNightVisibilityPlanAsync`.
   - Request assembled in `AstronomyContextProvider` using `SkyfieldNightPlanRequest`.

4. **SceneObservationContext**
   - Built in `AstronomyContextProvider.TryApplyNightPlanResponse`.
   - Stored in `AstronomyContext.SceneObservationContexts` and consumed downstream.

5. **PromptGeneration**
   - `AzureOpenAiContentGenerationService.GenerateAsync` (or fallback template behavior after repeated failures).

6. **SpeechSynthesis**
   - `AzureSpeechSynthesisService.SynthesizeAsync` writes narration text + mp3.

7. **VisualGeneration**
   - `StellariumVisualGenerationService.PrepareVisualsAsync` composes scenes and capture manifests.

8. **Stellarium SSC**
   - `StellariumScriptBuilder.BuildSceneScript` generates per-scene Stellarium scripts.

9. **FFmpeg render**
   - `FfmpegVideoRenderService.RenderAsync` validates assets, writes diagnostics, and renders the final video.

10. **`final-video.mp4`**
   - Output path comes from orchestrator-created manifest and FFmpeg output.
