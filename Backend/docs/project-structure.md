# Project Structure

## Solution layout
- `src/Astronomy.MediaFactory.Api`: minimal API host, health endpoints, and operator-facing routes.
- `src/Astronomy.MediaFactory.Worker`: scheduled jobs, queue processing, analytics fetch, and retention cleanup host.
- `src/Astronomy.MediaFactory.Contracts`: shared option objects, enums, and request/response records.
- `src/Astronomy.MediaFactory.Core`: domain entities, orchestration, queue logic, monitoring, alerting, monetization, and recovery models.
- `src/Astronomy.MediaFactory.AstroData`: astronomy source clients and context-building services.
- `src/Astronomy.MediaFactory.ContentGen`: prompt composition and Azure OpenAI-backed generation.
- `src/Astronomy.MediaFactory.Rendering`: Azure Speech, Stellarium, FFmpeg, thumbnails, and render support services.
- `src/Astronomy.MediaFactory.Publishing`: Blob archival, YouTube publishing and analytics, and short-form platform publishing.
- `src/Astronomy.MediaFactory.Infrastructure`: EF Core persistence, dependency injection, startup validation, health checks, alerting, and operations services.
- `tests/Astronomy.MediaFactory.Tests`: unit and integration-style coverage for API, orchestration, analytics, alerting, publishing, and recovery.
- `docs/`: architecture, deployment, configuration, API, onboarding, runbook, and checklist documentation.
- `python/skyfield_sidecar`: optional FastAPI sidecar for astronomy calculations.

## Cross-cutting patterns
- configuration is strongly typed and validated on startup,
- operational behavior is exposed through minimal APIs rather than a separate admin app,
- the worker handles unattended automation while the API remains operator-facing,
- repository persistence and health checks live in Infrastructure,
- short-form publishing is designed as a pluggable publisher model with conservative defaults.

## Future-safe extension points
- `IShortFormPlatformPublisher` for new platform delivery targets,
- options classes in `Contracts` for strongly typed configuration,
- orchestration hooks in `PipelineOrchestrator` for additional non-breaking processing stages,
- monitoring and recovery services in `Infrastructure/Operations` for new operator workflows.
