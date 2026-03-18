# Backend Architecture

## System overview
Astronomy Media Factory is a .NET 10 backend split into a minimal API, a background worker, and domain-focused libraries. The backend takes a scheduled or manually requested content run, gathers astronomy context, generates long-form content and shorts, renders media assets, archives outputs, publishes to supported platforms, collects analytics, and exposes recovery and operational APIs.

At a high level the backend is responsible for:
- selecting and enriching astronomy topics for a given run date and location,
- generating scripts, metadata, thumbnails, and monetization text,
- synthesizing narration and visual assets,
- rendering final video outputs,
- archiving and publishing finished assets,
- tracking pipeline state, stage telemetry, alerts, analytics, and recovery actions.

## Runtime topology
- **API (`Astronomy.MediaFactory.Api`)**: synchronous and operator-facing minimal API for health, pipeline trigger, inspection, analytics, and operational recovery.
- **Worker (`Astronomy.MediaFactory.Worker`)**: Quartz-driven scheduler plus queue processor for unattended execution, analytics fetch, and retention cleanup.
- **PostgreSQL**: durable store for runs, jobs, scripts, assets, publications, analytics, experiments, stage telemetry, and recovery records.
- **Azure OpenAI**: script and metadata generation.
- **Azure Speech**: narration synthesis.
- **Skyfield sidecar**: ephemeris and daily sky calculations.
- **Stellarium + FFmpeg**: visual capture orchestration and final render composition.
- **Azure Blob Storage**: artifact archival and distribution storage.
- **YouTube Data API**: primary long-form and shorts publishing target.
- **Slack webhook alerting (optional)**: operational notifications.

## End-to-end pipeline flow

### 1. Generation
1. A run starts from `POST /api/pipelines/run`, `POST /api/jobs/enqueue`, or a Quartz schedule in the worker.
2. The orchestrator creates a `PipelineRun` record and marks it running.
3. The astronomy context provider gathers source data from NASA, MPC, and the Skyfield sidecar.
4. Topic ranking and optional topic planning determine which event or narrative should lead the run.
5. Prompt feedback and analytics feedback, when available, are attached before text generation.
6. Azure OpenAI generates the primary script and metadata candidates.

### 2. Optimization
1. Metadata optimization refines titles, descriptions, tags, hashtags, and thumbnail text.
2. Monetization planning optionally augments descriptions and pinned-comment text without changing the content generation flow.
3. Experiment and analytics data can influence prompt feedback and later optimization reruns.

### 3. Rendering
1. Azure Speech synthesizes narration.
2. Stellarium scene scripts and capture manifests are produced for visual generation.
3. Screenshot placeholders or captures are assembled into a render manifest.
4. FFmpeg composes the narration, visuals, and optional background assets into the final MP4.
5. Thumbnail generation produces uploadable thumbnail assets.
6. Shorts rendering derives short-form media from the parent run output.

### 4. Publishing
1. Rendered assets are archived to Azure Blob when configured.
2. The primary video is uploaded to YouTube when run-level publishing is requested and YouTube publishing is enabled.
3. Thumbnail upload is attempted separately when a YouTube video id exists.
4. Publication metadata is stored in `published_videos` and linked back to the originating run.

### 5. Multi-platform distribution
1. The short-form publishing service formats platform-specific captions and publish targets.
2. YouTube Shorts publishing is implemented through the shared YouTube upload service.
3. Instagram Reels and Facebook are present as integration points with idempotency, cooldown, and record keeping, but the live Meta Graph upload workflow is not yet wired; enabling them without completing that wiring will produce skipped or failed publication records instead of a successful upload.
4. Per-platform publication outcomes are persisted in `platform_publication_records`.

### 6. Analytics
1. The worker periodically fetches analytics using the configured interval.
2. Analytics records are stored in `video_analytics`.
3. Aggregation services build top-performing summaries for operator APIs.
4. Analytics feedback providers extract reusable signals to improve future prompts and metadata.

### 7. Monetization
1. The monetization service can produce affiliate links, CTA text, pinned comments, and sponsor copy.
2. Monetization output is persisted to `monetization_records`.
3. Monetization enriches published metadata rather than changing the render or publish control flow.

### 8. Recovery and maintenance
1. Queue retries handle transient job failures automatically.
2. Operator endpoints support replay, publish retry, archive retry, metadata reruns, stale-job recovery, and shorts regeneration.
3. Cleanup jobs remove aged working files and stale operational data according to `Maintenance` settings.
4. Alerts are emitted for stage failures, slow stages, queue backlog, health degradation, and publishing failures when alerting is enabled.

## Key components and responsibilities

### Entry points
- **`Astronomy.MediaFactory.Api`**: exposes health, pipeline, jobs, analytics, experiments, topics, platform publication, and ops endpoints.
- **`Astronomy.MediaFactory.Worker`**: schedules pipeline runs, fetches analytics, processes queued jobs, and runs retention cleanup.

### Domain and orchestration
- **`Astronomy.MediaFactory.Core`**: contracts between services, entities, orchestrators, queue processing, monitoring models, alerting models, monetization models, and recovery request types.
- **`PipelineOrchestrator`**: coordinates the main end-to-end run from context generation through publishing and short-form distribution.
- **`PipelineJobProcessor` / `PipelineJobExecutor`**: execute queued work with retry semantics.

### Data collection and topic planning
- **`Astronomy.MediaFactory.AstroData`**: NASA APOD, NASA NeoWs, MPC, and Skyfield sidecar clients plus astronomy context building and topic ranking services.

### Content generation
- **`Astronomy.MediaFactory.ContentGen`**: prompt building, Azure OpenAI-backed script generation, and prompt-feedback composition.

### Rendering
- **`Astronomy.MediaFactory.Rendering`**: Azure Speech integration, Stellarium scripting/capture manifest generation, thumbnail creation, file-system abstractions, process execution, and FFmpeg render composition.

### Publishing and distribution
- **`Astronomy.MediaFactory.Publishing`**: Azure Blob archival, YouTube publishing and analytics fetch, short-form metadata formatting, platform publication orchestration, and transient retry helpers.

### Infrastructure and operations
- **`Astronomy.MediaFactory.Infrastructure`**: EF Core persistence, DI registration, configuration validation, health checks, monitoring, alerting, and operator/recovery services.

### Supporting persistence model
Primary persisted records include:
- `pipeline_runs`
- `pipeline_jobs`
- `pipeline_stage_executions`
- `generated_scripts`
- `media_assets`
- `published_videos`
- `short_videos`
- `platform_publication_records`
- `video_analytics`
- `monetization_records`
- `recovery_operations`
- `content_experiments`
- `content_variants`

## Execution patterns

### API-triggered execution
Use this when an operator wants an immediate run or manual recovery action. The API handles the request synchronously, delegates to the orchestrator or operations service, and returns the resulting record or summary.

### Queue-driven execution
Use this for unattended generation, retries, and backlog smoothing. Jobs are persisted in PostgreSQL, picked up by the worker, and retried according to `Scheduling` values.

### Observability model
- Health endpoints provide liveness, readiness, and aggregate health.
- Pipeline stages are recorded individually for latency and failure diagnostics.
- Alerting can fan out to Slack when enabled.
- Recovery actions are audited in `recovery_operations`.

## Production-readiness notes
The architecture is feature-complete for the intended backend pipeline with one important qualifier: multi-platform distribution beyond YouTube is scaffolded but not wired to live Meta upload APIs. Production deployment should therefore treat YouTube as the currently operational publishing target and keep other platform switches disabled until their integrations are completed.
