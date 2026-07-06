# Astronomy V3 Technical Reference

## Purpose

This documentation set is the foundation reference for Astronomy V3, an AI-assisted astronomy media generation system. It is based on the repository implementation: the ASP.NET Core API and worker, .NET service modules, TypeScript frontends, Skyfield sidecar, rendering stack, publishing integrations, and operational infrastructure.

## Project Overview

Astronomy V3 generates astronomy-focused video content from event, visibility, and asset intelligence. The backend coordinates astronomy data acquisition, content planning, prompt generation, narration, visual asset production, FFmpeg rendering, validation, storage, publishing, analytics, and operational recovery.

The implementation is centered in `Backend/src/Astronomy.MediaFactory.*`, with supporting web clients in `frontend`, mobile client code in `mobile`, a Skyfield Python sidecar under `Backend/python/skyfield_sidecar`, and image/render assets under `Backend/src/Astronomy.MediaFactory.Api/assets` and `render`.

## Product Vision

Astronomy V3 exists to turn astronomy events and night-sky visibility context into production-ready educational and social video assets. Its product direction is described in [ProjectVision.md](ProjectVision.md): AI-first media production, reusable astronomy intelligence, multilingual output, validated hero visuals, and platform-aware publishing.

## Technology Stack

| Layer | Implementation |
| --- | --- |
| Backend host | ASP.NET Core minimal API and controllers in `Astronomy.MediaFactory.Api` |
| Worker | .NET worker host in `Astronomy.MediaFactory.Worker` |
| Domain and orchestration | `Astronomy.MediaFactory.Core` |
| Persistence | PostgreSQL via Entity Framework Core/Npgsql |
| Frontend | Vite/TypeScript public and admin UI in `frontend` |
| Mobile | TypeScript mobile app structure in `mobile` |
| Astronomy calculations | Skyfield sidecar and SSC intelligence modules |
| Rendering | Azure Speech, Stellarium-oriented scripts/captures, ImageSharp, FFmpeg/FFprobe |
| AI generation | Azure OpenAI chat/content and Azure OpenAI image generation |
| Storage | Azure Blob/public blob storage services |
| Publishing | YouTube, Facebook, Instagram, Meta OAuth services |
| Operations | health checks, scheduler, analytics collectors, maintenance, Slack alerting, token health |

## High-Level Architecture

Astronomy V3 is organized as a modular media factory:

1. **API host** exposes health, diagnostics, operational, OAuth, asset, and production endpoints.
2. **Infrastructure bootstrap** registers options, validators, database access, external clients, orchestration services, renderers, publishers, analytics collectors, and hosted services.
3. **Core orchestration** selects categories and events, creates plans, executes pipeline stages, validates output, and records run state.
4. **AstroData and SSC intelligence** provide astronomy context, object visibility, spatial composition, and Stellarium scene planning.
5. **ContentGen and PromptEngine** build prompts and use Azure OpenAI for narration/script/metadata content.
6. **Rendering** creates TTS audio, thumbnails, Stellarium/scene assets, and FFmpeg output.
7. **Publishing and analytics** store artifacts, publish to configured platforms, collect metrics, and feed optimization loops.

See [FolderStructure.md](FolderStructure.md) for folder-level responsibilities.

## Pipeline Overview

The implemented flow maps to the existing pipeline documentation and services:

1. Trigger a pipeline/category run from API, scheduler, worker, or operations flow.
2. Build astronomy context using NASA clients, event stores, observation options, Skyfield sidecar, and region/localization inputs.
3. Select or plan content categories such as daily guides, weekly forecasts, or special event outputs.
4. Resolve event family and strategy for meteor, planet grouping, moon, eclipse, special event, or unknown events.
5. Generate prompt-driven script, metadata, narration plan, image prompts, and thumbnails.
6. Synthesize audio with Azure Speech, including English and Hindi voice handling.
7. Produce visual assets through local celestial assets, Stellarium scripts/screenshots, NASA assets, sky maps, thumbnails, and Azure OpenAI cinematic images where enabled.
8. Assemble and validate video with FFmpeg/FFprobe and validation services.
9. Upload/archive assets to Azure Blob and optionally publish to YouTube, Instagram, or Facebook.
10. Collect analytics, run optimization services, and support recovery/maintenance jobs.

## Supported Event Families

The repository implements `EventFamily` as:

- Meteor
- PlanetGrouping
- Moon
- Eclipse
- SpecialEvent
- Unknown

Special-event subtypes include Comet, DeepSkyObject, Constellation, Occultation, and Generic. Event family profiles define validator profiles, thumbnail composition types, forbidden terms, required visual elements, required overlay elements, and diagnostic fields.

## Supported Languages

The implemented localization resolver supports normalized language selection with default/fallback behavior. Current explicit language handling includes:

- English (`en`)
- Hindi (`hi`), including Hindi narration instructions, Devanagari detection, Hindi font selection, Hindi prosody handling, and `hi-IN` SSML voice locale support.

## Azure Services Used

- **Azure OpenAI** for chat/content generation and metadata optimization.
- **Azure OpenAI image generation** for cinematic AI assets.
- **Azure Speech** for narration synthesis and SSML/audio generation.
- **Azure Blob Storage** for artifact archival and public media storage.
- **Azure Key Vault / secure configuration** support through configuration options.
- **Application Insights** when telemetry connection string is configured.
- **Managed identity** paths for Azure OpenAI, Azure Speech, and Blob access where options enable them.

## AI Models Used

The code is deployment-driven rather than hard-coded to public model names. The main configured AI model/deployment surfaces are:

- `AzureOpenAI:ChatDeployment` for chat/completion content generation.
- `AzureOpenAIForImage:ImageDeployment` for cinematic image generation.
- Azure Speech voice settings for TTS voices and prosody.

## Folder Structure

See [FolderStructure.md](FolderStructure.md) for the reverse-engineered repository map, including Backend, Frontend, MediaFactory, Rendering, PromptEngine, Validation, Assets, Configuration, Azure, API, and Infrastructure.

## Documentation Index

- [ProjectVision.md](ProjectVision.md) — product purpose, goals, audience, AI-first principles, differentiators.
- [ContentIntelligencePlatformV4Specification.md](ContentIntelligencePlatformV4Specification.md) — master V4 specification for the astronomy-first Content Intelligence Platform roadmap and current development handoff.
- [Roadmap.md](Roadmap.md) — completed and planned RC milestones.
- [CHANGELOG.md](CHANGELOG.md) — professional changelog for RC1 and RC2 areas.
- [FolderStructure.md](FolderStructure.md) — reverse-engineered folder responsibilities.
- [releases/AstronomyV3RC2.md](releases/AstronomyV3RC2.md) — official RC2 release notes.

## Quick Start

### Backend API

```bash
cd Backend
dotnet run --project src/Astronomy.MediaFactory.Api
```

### Worker

```bash
cd Backend
dotnet run --project src/Astronomy.MediaFactory.Worker
```

### Skyfield Sidecar

```bash
cd Backend/python/skyfield_sidecar
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app:app --host 0.0.0.0 --port 8010 --reload
```

### Frontend

```bash
cd frontend
npm install
npm run dev
```

## Build Instructions

```bash
cd Backend
dotnet build
```

```bash
cd frontend
npm install
npm test
```

```bash
cd mobile
npm install
npm test
```

Docker support exists for API, Worker, Postgres, and Skyfield sidecar through backend Dockerfiles and `Backend/docker-compose.yml`.

## Configuration Overview

Configuration is strongly typed and validated at startup. Major sections include production pipeline, rendering, typography, Azure OpenAI, Azure OpenAI image, Azure Speech, Azure Blob, Key Vault, YouTube, Meta, public media storage, platform publishing, monetization, growth, scheduling, analytics, AI optimization, astronomy events, operations, publishing validation, maintenance, alerting, localization, scheduler/regions, observation, thumbnail generation, Stellarium, Skyfield sidecar, and startup validation.

Production deployments should provide a PostgreSQL connection string, avoid localhost database targets unless explicitly allowed, configure Azure credentials or managed identity, validate FFmpeg/FFprobe and Stellarium paths, and enable only publishing targets that have verified credentials.
