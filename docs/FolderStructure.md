# Folder Structure

## Purpose

This document reverse engineers the repository structure for Astronomy V3 and explains the purpose, responsibilities, dependencies, implementation notes, and future improvements for every major folder requested in Documentation Sprint 1.

## Overview

The repository is organized around a .NET backend media factory, TypeScript frontend/mobile clients, Python sidecar services, render assets, database scripts, tests, and documentation.

## Architecture

Top-level structure:

```text
Backend/      .NET backend, infrastructure, Python sidecar, database scripts, tests, Docker
frontend/     Vite/TypeScript public and admin frontend
Frontend/     Legacy or placeholder frontend notes
mobile/       TypeScript mobile app screens, services, navigation, tests
docs/         Official and historical documentation
render/       Render-time semantic asset registry
youtube_titles.txt  Supporting content/title data
```

## Backend

### Purpose

`Backend` contains the main server-side implementation for Astronomy V3.

### Responsibilities

- API host and controller/endpoints.
- Worker host and background jobs.
- Core domain models, orchestration, production pipeline, event intelligence, hero engine, validation, rendering interfaces, analytics, and scheduling models.
- Infrastructure registration, persistence, operations, scheduling, analytics, alerting, and configuration validation.
- Rendering, content generation, publishing, AstroData, AI optimization, contracts, and tests.
- Docker and database initialization/manual scripts.

### Dependencies

- .NET SDK/runtime.
- PostgreSQL/Npgsql/Entity Framework Core.
- Azure OpenAI, Azure Speech, Azure Blob, Key Vault/managed identity options.
- NASA APIs, Skyfield sidecar, Stellarium, FFmpeg/FFprobe.
- Platform APIs for YouTube, Meta/Facebook/Instagram.

### Implementation Notes

Important subfolders include:

- `Backend/src/Astronomy.MediaFactory.Api` — HTTP host, endpoint mapping, health checks, OAuth controllers, static asset folder, Dockerfile, appsettings.
- `Backend/src/Astronomy.MediaFactory.Worker` — background worker host, analytics fetch job, Dockerfile, appsettings.
- `Backend/src/Astronomy.MediaFactory.Core` — orchestration, event family logic, production pipeline, hero engine, validation, weekly forecast modules, models, service contracts.
- `Backend/src/Astronomy.MediaFactory.Infrastructure` — DI registration, EF persistence, operations, alerting, analytics, scheduling, configuration validation.
- `Backend/src/Astronomy.MediaFactory.Contracts` — typed option/config contracts and cross-module DTOs.
- `Backend/src/Astronomy.MediaFactory.AstroData` — astronomy context providers, observation windows, NASA/MPC/Skyfield clients.
- `Backend/src/Astronomy.MediaFactory.ContentGen` — Azure OpenAI content/image generation, prompt builder, templates.
- `Backend/src/Astronomy.MediaFactory.Rendering` — Azure Speech, FFmpeg, Stellarium, thumbnails, celestial assets, typography, visual composition.
- `Backend/src/Astronomy.MediaFactory.Publishing` — Azure Blob, YouTube, Meta, Facebook, Instagram publishing, thumbnails, analytics, token health.
- `Backend/src/Astronomy.MediaFactory.AIOptimization` — AI/rule-based optimization services and models.
- `Backend/Astronomy.SscIntelligence` — SSC/spatial/camera/composition/scene-intent/narrative intelligence.
- `Backend/python/skyfield_sidecar` — Python sidecar for Skyfield-based astronomy calculations.
- `Backend/db` — database initialization and manual migration/seed scripts.
- `Backend/tests` — .NET test project and service tests.

### Future Improvements

Add a generated project-dependency graph and API endpoint matrix.

## Frontend

### Purpose

`frontend` contains the TypeScript web application for public/admin experiences and frontend API integration tests.

### Responsibilities

- Public portal routes and UI.
- Admin dashboard routes and navigation.
- Shared HTTP client, config, types, formatting, diagnostics.
- Vite build configuration, SPA server script, static copy script, and tests.

### Dependencies

- Node/npm.
- Vite and TypeScript.
- Backend API endpoints.

### Implementation Notes

The folder uses lowercase `frontend` for the active web app. The uppercase `Frontend` folder currently contains `Frontend.txt` and appears to be a placeholder or legacy note folder.

### Future Improvements

Document route-to-endpoint coverage and align frontend docs with the official V3 docs.

## MediaFactory

### Purpose

The `Astronomy.MediaFactory.*` projects are the main application modules.

### Responsibilities

- `Api` hosts HTTP endpoints and bootstraps the app.
- `Worker` runs scheduled/background workloads.
- `Core` owns domain orchestration and production logic.
- `Contracts` defines shared configuration/options contracts.
- `Infrastructure` wires dependencies and persistence.
- `AstroData` acquires astronomy context.
- `ContentGen` generates prompts/content/images.
- `Rendering` builds audio/visual/video artifacts.
- `Publishing` stores and publishes outputs.
- `AIOptimization` provides optimization models/services.

### Dependencies

Cross-project dependencies are registered centrally through the infrastructure service collection extension.

### Implementation Notes

The service graph shows a modular, DI-driven architecture with typed options and many scoped services for production phases, category strategies, event strategies, validators, renderers, asset producers, publishers, and analytics collectors.

### Future Improvements

Add a stable architecture diagram after RC2 documentation is accepted.

## Rendering

### Purpose

`Backend/src/Astronomy.MediaFactory.Rendering` produces audio, thumbnails, Stellarium artifacts, visual compositions, and FFmpeg output.

### Responsibilities

- Azure Speech synthesis and SSML building.
- FFmpeg argument building, process execution, video rendering, and diagnostics.
- Stellarium visual generation, script building, script service, templates, and capture manifests.
- Thumbnail generation, scoring, composition, cinematic collage, local asset collage, and typography.
- Celestial asset providers, ingestion, pack extraction, runtime asset path resolution, and procedural fallbacks.
- Weekly sky forecast audio TTS synthesis and gallery support.

### Dependencies

- Azure Speech.
- FFmpeg/FFprobe.
- Stellarium where configured.
- ImageSharp/SixLabors fonts and drawing.
- Local celestial assets and configured fonts.

### Implementation Notes

Rendering includes specific Hindi support through Devanagari detection, Hindi font selection, Hindi prosody, and `hi-IN` voice locale detection.

### Future Improvements

Document supported rendering modes, output artifacts, and media validation gates.

## PromptEngine

### Purpose

PromptEngine responsibilities are implemented mainly in `ContentGen` and prompt-related `Core` services.

### Responsibilities

- Build structured prompts for daily sky guides and special event guides.
- Generate scripts, shorts scripts, and metadata with Azure OpenAI.
- Enforce prompt rules for duration, language, scene labels, and output structure.
- Compose prompt feedback from analytics.
- Generate question-driven narration and image prompts.

### Dependencies

- Azure OpenAI chat deployment.
- Localization options.
- Astronomy context, event context, and analytics feedback.

### Implementation Notes

Prompt generation contains explicit Hindi narration instructions and fallback content. Azure OpenAI calls use a configured deployment and include timeout, retry/fallback, strict JSON validation, and managed identity/API-key paths.

### Future Improvements

Add prompt contract documentation and sample validated prompt/response schemas.

## Validation

### Purpose

Validation protects pipeline quality, rendering correctness, platform readiness, and event-family correctness.

### Responsibilities

- Production pipeline quality validation.
- Pre-publish validation.
- Event-scene validation strategy resolution and family/category-specific validation.
- TTS package validation and alignment repair.
- Media validation through FFmpeg/FFprobe services.
- Startup validation for configuration and external dependencies.
- Weekly asset quality validation.

### Dependencies

- Core domain models and event family profiles.
- Rendering outputs and media probes.
- Publishing/platform configuration.
- Typed options validation.

### Implementation Notes

Validation is not centralized in one folder; it appears across Core, Infrastructure startup validators, Rendering media services, and tests.

### Future Improvements

Create a validation matrix by pipeline stage, event family, and publishing target.

## Assets

### Purpose

Assets provide local visual material and render-time semantic metadata.

### Responsibilities

- Store celestial hero and transparent PNG assets.
- Store source asset sheets and extraction reports.
- Provide a render semantic registry.
- Support thumbnail and scene composition.

### Dependencies

- Runtime asset path resolver.
- Celestial asset provider/extractor services.
- Rendering and thumbnail services.

### Implementation Notes

Assets are present under `Backend/src/Astronomy.MediaFactory.Api/assets/celestial` for objects including planets, Sun, Earth, Milky Way, Andromeda Galaxy, and Orion Nebula. `render/production-asset-semantic-registry.json` provides semantic registry data.

### Future Improvements

Document asset naming, licensing/source metadata, and regeneration workflow.

## Configuration

### Purpose

Configuration is strongly typed and validated so production runs fail early when required settings are missing or unsafe.

### Responsibilities

- Define option classes in `Contracts` and `Core`.
- Bind configuration sections in infrastructure.
- Validate rendering dimensions, database safety, Azure settings, publishing settings, scheduling, localization, observation, thumbnail, Stellarium, Skyfield sidecar, and startup behavior.
- Load secure configuration through infrastructure configuration extensions.

### Dependencies

- `appsettings*.json`, environment variables, secure configuration, and optional Key Vault/managed identity.

### Implementation Notes

`AddMediaFactory` binds and validates many sections, including Azure OpenAI, Azure OpenAI image, Azure Speech, Azure Blob, Key Vault, YouTube, Meta, publishing, scheduling, analytics, localization, observation, thumbnails, Stellarium, and Skyfield sidecar.

### Future Improvements

Create a complete configuration reference table with defaults, required fields, and environment-specific guidance.

## Azure

### Purpose

Azure services provide AI generation, speech synthesis, storage, telemetry, and secure configuration support.

### Responsibilities

- Generate scripts/metadata through Azure OpenAI.
- Generate cinematic images through Azure OpenAI image deployment.
- Synthesize narration through Azure Speech.
- Store/private-public artifacts through Azure Blob.
- Support Key Vault and managed identity.
- Emit Application Insights telemetry when configured.

### Dependencies

- Azure endpoints, deployment names, API keys or managed identity, resource IDs, connection strings, Blob containers, and telemetry connection strings.

### Implementation Notes

Azure clients include managed-identity and API-key paths. Azure Speech validates against region/endpoint/key/managed-identity requirements. Azure Blob services support archive and public-media workflows.

### Future Improvements

Add Azure deployment guide references and least-privilege managed identity permissions.

## API

### Purpose

`Astronomy.MediaFactory.Api` is the HTTP entry point for the backend.

### Responsibilities

- Configure Serilog, CORS, controllers, OpenAPI/Swagger, health checks, DI, and app startup diagnostics.
- Expose root health status, liveness/readiness checks, rendering diagnostics, asset extraction, OAuth controllers, and production/operations endpoints defined in Program/controller files.
- Register SSC intelligence and Skyfield temporal resolver.

### Dependencies

- Infrastructure `AddMediaFactory` registrations.
- ASP.NET Core, health checks, controllers, appsettings, and external services.

### Implementation Notes

The API checks FFmpeg and FFprobe paths at startup and exposes rendering diagnostics. Development enables Swagger UI.

### Future Improvements

Generate an official endpoint catalog from route mappings and controllers.

## Infrastructure

### Purpose

`Astronomy.MediaFactory.Infrastructure` owns dependency registration and production-support services.

### Responsibilities

- Bind and validate options.
- Configure HttpClients and external clients.
- Register EF Core/PostgreSQL repositories.
- Register pipeline, scheduling, category, rendering, publishing, analytics, optimization, alerting, maintenance, and sky-alert services.
- Register hosted services and health checks.

### Dependencies

- All MediaFactory modules, PostgreSQL, Azure services, platform APIs, Skyfield sidecar, NASA APIs, and operational options.

### Implementation Notes

Infrastructure contains the central service graph. It also guards against accidental localhost PostgreSQL usage unless explicitly allowed.

### Future Improvements

Split service registration by domain as the graph grows and document each registration group.
