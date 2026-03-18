# Developer Onboarding Guide

## Purpose
This guide helps a new backend developer understand the project layout, run the services locally, test changes, debug the pipeline, and extend platform integration points without changing current production behavior.

## Project structure
- `src/Astronomy.MediaFactory.Api`: minimal API endpoints for runs, jobs, analytics, topics, and operations.
- `src/Astronomy.MediaFactory.Worker`: Quartz schedules, queue worker, analytics fetch, and maintenance execution.
- `src/Astronomy.MediaFactory.Core`: domain models, entities, orchestration, job processing, and service interfaces.
- `src/Astronomy.MediaFactory.Contracts`: shared option classes, enums, and request/response contracts.
- `src/Astronomy.MediaFactory.AstroData`: external astronomy data clients and context-building services.
- `src/Astronomy.MediaFactory.ContentGen`: prompt building and Azure OpenAI-backed generation.
- `src/Astronomy.MediaFactory.Rendering`: Azure Speech, Stellarium, FFmpeg, thumbnails, and file/process abstractions.
- `src/Astronomy.MediaFactory.Publishing`: Blob, YouTube, short-form distribution, and retry helpers.
- `src/Astronomy.MediaFactory.Infrastructure`: EF Core persistence, DI, config validation, health checks, monitoring, alerting, and run operations.
- `tests/Astronomy.MediaFactory.Tests`: automated tests across API endpoints, orchestration, publishing, alerting, recovery, and analytics.
- `docs/`: architecture, deployment, configuration, API, checklist, and runbook documentation.

## Local development prerequisites
- .NET 10 SDK
- PostgreSQL
- Python environment for the Skyfield sidecar if you want daily-sky context
- `ffmpeg` on `PATH`
- optional Stellarium for real local captures

## How to run locally

### 1. Configure local settings
Use development settings and environment variables. Typical local values:
- PostgreSQL connection string
- Azure OpenAI endpoint, deployment, and key
- Azure Speech region and key
- optional Blob connection string for archival tests
- `SkyfieldSidecar__BaseUrl=http://localhost:8010`

### 2. Start PostgreSQL
Start a local PostgreSQL instance or use the project’s compose workflow if available in your environment.

### 3. Start the sidecar
```bash
cd python/skyfield_sidecar
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app:app --host 0.0.0.0 --port 8010 --reload
```

### 4. Start the API
```bash
dotnet run --project src/Astronomy.MediaFactory.Api
```

### 5. Start the Worker
```bash
dotnet run --project src/Astronomy.MediaFactory.Worker
```

### 6. Trigger a run
Use the API overview examples or call one of:
- `POST /api/pipelines/run`
- `POST /api/jobs/enqueue`

## How to run tests
From the `Backend` directory:
```bash
dotnet test tests/Astronomy.MediaFactory.Tests/Astronomy.MediaFactory.Tests.csproj
```

If you need to target a single area, use standard `dotnet test --filter ...` patterns.

## How to debug the pipeline

### Recommended debugging sequence
1. Verify `/health/ready`.
2. Trigger one non-publishing run.
3. Watch API or Worker logs for stage progression.
4. Inspect database records for the run, jobs, stages, scripts, and assets.
5. Check the working directory under `media-output/...`.
6. Use ops endpoints to inspect failures or replay as needed.

### Useful checkpoints
- **Astronomy data stage**: confirms sidecar and source clients are reachable.
- **Prompt generation stage**: confirms Azure OpenAI configuration.
- **Speech stage**: confirms Azure Speech configuration.
- **Render stage**: confirms FFmpeg and optional Stellarium setup.
- **Archive stage**: confirms Blob access.
- **Publish stage**: confirms YouTube configuration.

### Common local failure patterns
- missing `dotnet` SDK,
- invalid Azure credentials,
- sidecar not running,
- `ffmpeg` missing from `PATH`,
- production validation accidentally enabled in a half-configured local environment.

## How to add a new platform integration safely
This codebase already models short-form platform publishing behind `IShortFormPlatformPublisher`. To add a new platform without disturbing current behavior:
1. Add or extend the platform model and configuration in `Contracts` and `Core`.
2. Implement a new publisher behind `IShortFormPlatformPublisher`.
3. Keep it disabled by default in appsettings.
4. Reuse `PlatformMetadataFormatter` patterns for captions and hashtags.
5. Preserve idempotency and cooldown behavior similar to the existing short-form service.
6. Register the publisher in DI.
7. Add endpoint or repository assertions only if the persisted publication record shape truly changes.
8. Add tests before enabling the switch in production.

The current repository already treats Instagram and Facebook as future-safe integration points: the operational pattern is in place, but the live upload workflow is intentionally not enabled by default.

## Development expectations
- Prefer configuration and documentation changes over behavioral refactors for final-polish work.
- Keep comments minimal and focused on non-obvious behavior.
- Use the existing options binding and validation pattern for any future configuration additions.
- Treat API and Worker startup validation as part of the production contract.
