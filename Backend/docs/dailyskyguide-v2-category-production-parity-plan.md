# DailySkyGuide v2 Category Production Parity Plan

## Scope and invariants
- Do not modify `POST /api/pipelines/run`.
- Preserve existing DailySkyGuide production behavior and output parity.
- Disable publishing in category preview flow (`publishingEnabled=false`).
- Reuse existing pipeline services; no new FFmpeg/audio/thumbnail implementations.
- No migrations.

## Existing `/api/pipelines/run` path inventory

### 1) Controller action
- Endpoint: `POST /api/pipelines/run` in `Program.cs`; it delegates to `PipelineOrchestrator.RunAsync(request, ct)` and wraps publish-failure responses with retry/resume hints.

### 2) Request DTO
- `RunPipelineRequest` in contracts (date/content type/location/timezone/publish flags/geo/event/language).

### 3) Main pipeline service
- `PipelineOrchestrator` is the canonical orchestrator and stage coordinator.

### 4) Skyfield/night-sky services
- Context generation enters through `IAstronomyContextProvider` (in orchestrator).
- Daily planning/capture preview stack already uses:
  - `IDailySkyGuideContextBuilder`
  - `IAstronomyVisibilityService`
  - `IStellariumScenePlanner` + resolver
  - `IStellariumScriptGenerator`
  - `IStellariumImageCaptureExecutor`

### 5) Segment planning logic
- `ITopicRankingService` and (optionally) `ITopicSelectionService` are used in orchestrator for content structuring.

### 6) Script generation logic
- `IScriptGenerationService` in orchestrator.

### 7) Long narration generation
- `ISpeechSynthesisService` used for narration audio generation.

### 8) Short narration generation
- `ISpeechSynthesisService` + short script path within orchestrator.

### 9) Audio combine logic
- Existing orchestration path keeps audio generation/assembly internal to existing media services (`ISpeechSynthesisService`, render services). No new combiner needed.

### 10) Stellarium SSC generation
- `IStellariumScriptGenerator`.

### 11) Stellarium screenshot capture
- `IStellariumImageCaptureExecutor`.

### 12) Long segment video generation
- `IVideoRenderService`.

### 13) Short segment video generation
- `IShortsVideoRenderService`.

### 14) Long video combine
- Existing long-form video composition remains in orchestrator + render service path.

### 15) Short video combine
- Existing short-form composition remains in orchestrator + shorts render service path.

### 16) Final mux/merge with narration
- Existing render services perform mux/composition; keep unchanged.

### 17) Long thumbnail generation
- `IThumbnailGenerationService` with optional `ICinematicThumbnailService` and fallback `IThumbnailGeneratorService`.

### 18) Short thumbnail generation
- Same thumbnail stack, including generated short thumbnail resolution path.

### 19) Metadata generation
- `IMetadataOptimizationService` and `ISeoMetadataGeneratorService`.

### 20) Publishing services
- `IYouTubePublishingService`, `IContentPublishService`, `IMetaPublishService`, `IYouTubeThumbnailPublisher`, plus publishing options/validation gates.

## Existing category pipeline components to reuse
- `IContentCategoryPipeline` + `DailySkyGuideContentPipeline` for category-triggered runs.
- `IContentCategoryPipelineStrategy` + `DailySkyGuidePipelineStrategy` for content-planning build/preview bridge.
- `IContentPlanningService` planning and preview endpoints for plan/context/scene/script/assets.

## New interfaces (proposal)

### `ICategoryProductionPipelineStrategy`
Purpose: category-specific strategy for production-preview parity using the canonical services.

Suggested contract:
- `string CategoryCode { get; }`
- `Task<CategoryProductionPlan> BuildPlanAsync(ContentGenerationPlan plan, CategoryProductionRunRequest request, CancellationToken ct)`
- `Task<CategoryProductionPreviewResult> ExecutePreviewAsync(CategoryProductionPlan plan, CancellationToken ct)`

Notes:
- `ExecutePreviewAsync` must call existing orchestrator/service graph, not duplicate logic.
- Must force publishing disabled (internally override publish flags/options for preview run).

### `ICategoryProductionRunner`
Purpose: endpoint-facing orchestrator that resolves strategy and executes preview with standard response.

Suggested contract:
- `Task<CategoryProductionPreviewResponse> RunPreviewAsync(RunCategoryProductionPreviewRequest request, CancellationToken ct)`

Responsibilities:
- Load/validate content plan.
- Resolve `ICategoryProductionPipelineStrategy` by category code.
- Execute preview production flow.
- Return standardized DTO with artifact paths + `stepResults`.

## DailySkyGuideProductionPipelineStrategy design

### Reuse-first execution model
- Primary recommendation: invoke `PipelineOrchestrator.RunAsync` with a transformed `RunPipelineRequest` equivalent to `/api/pipelines/run` for DailySkyGuide.
- Force non-publishing mode:
  - `PublishToYouTube=false` in request.
  - Do not call manual retry/resume publish endpoints.
  - Surface `publishingEnabled=false` in response DTO.

### Dependency map
1. `IContentPlanningService` / repository -> `ContentGenerationPlan`
2. `ICategoryProductionPipelineStrategyResolver` -> `DailySkyGuideProductionPipelineStrategy`
3. Strategy uses:
   - `IDailySkyGuideContextBuilder` (plan-context parity)
   - existing strategy bridge (`DailySkyGuidePipelineStrategy`) to produce request metadata where useful
   - `PipelineOrchestrator` (single source of production truth)
4. Output harvesting:
   - read `PipelineRun.OutputFolder`
   - collect expected files (`narration`, long/short video, thumbnails, metadata json)
   - collect stage statuses via `IPipelineRecoveryService` for `stepResults`

## Adapter gaps (no duplicated core logic)
1. **Request translation adapter**
   - `ContentGenerationPlan` + category inputs -> `RunPipelineRequest` for parity run.
2. **Output artifact resolver**
   - Normalize file discovery from output folder into stable response fields.
3. **Step-results mapper**
   - Map pipeline stage records into concise API `stepResults` array.
4. **Publishing hard-disable guard**
   - Ensure preview execution cannot publish even if global config enables publishing.
   - Prefer request-level disable and explicit stage filtering at response layer.

## New endpoint design
- `POST /api/content-planning/run-category-production-preview`

Request (proposed):
- `contentGenerationPlanId: Guid`
- `contentCategoryCode?: string` (optional; defaults from plan)
- `regionId?: string`
- `language?: string`
- `runDate?: DateOnly` (optional override)
- `diagnostics?: bool`

Flow:
1. Validate plan exists and category is eligible.
2. Resolve strategy.
3. Execute preview production run with publishing disabled.
4. Return standardized artifact/status response.

## Expected response DTO
- `longAudioPath`
- `shortAudioPath`
- `longVideoPath`
- `shortVideoPath`
- `longThumbnailPath`
- `shortThumbnailPath`
- `metadata`
- `publishingEnabled=false`
- `stepResults`

Suggested shape:
- `metadata` can include title/description/tags + optimization payload if present.
- `stepResults`: array of `{ stepName, status, outputPath?, warnings?, error? }` sourced from recorded stages.

## Execution sequence for implementation
1. Add new contracts/DTOs for preview request/response and step result.
2. Add `ICategoryProductionPipelineStrategy` and `ICategoryProductionRunner` interfaces.
3. Implement `DailySkyGuideProductionPipelineStrategy` using existing orchestrator and planning context services.
4. Implement runner + resolver wiring in DI.
5. Add endpoint `POST /api/content-planning/run-category-production-preview`.
6. Add focused integration test:
   - verifies endpoint runs DailySkyGuide path
   - verifies `publishingEnabled` is always `false`
   - verifies artifact fields and `stepResults` are populated from existing output/stage data.

## Non-goals
- No edits to `/api/pipelines/run` behavior.
- No replacement of existing render/audio/thumbnail/publish internals.
- No schema/database migration.

# Category Isolation & Production Safety Rules

## Core Principle

Each category must behave like an isolated production module/strategy.

Changes in one category must not disturb:
- other categories
- DailySkyGuide v2
- existing /api/pipelines/run behavior

## Rules

1. Category-specific logic must remain inside its own folder/namespace.

Examples:
- Categories/DailySkyGuide/*
- Categories/WeeklySkyForecast/*

2. Never place category-specific if/else logic inside another category.

BAD:
if(category == "WeeklySkyForecast") inside DailySkyGuide code.

3. Shared services must remain generic and backward-compatible.

Examples:
- ThumbnailEngine
- AudioEngine
- VideoRenderer
- SkyfieldClient
- SscScriptGenerator

4. If a shared service must change:
- change must be backward-compatible
- DailySkyGuide v2 regression tests must pass

5. Each category must implement:
- its own strategy
- its own context builder
- its own segment planner
- its own SSC scene planner

6. Category registration must happen only via strategy resolver/DI registration.

7. Existing /api/pipelines/run behavior must remain untouched forever.

8. Publishing remains disabled for new categories until manually approved.

9. Every category must support:
- long narration
- short narration
- long video
- short video
- long thumbnail
- short thumbnail
- metadata

10. Output directory convention:

/media-output/{categoryName}/{date}/{regionId}/{pipelineRunId}

