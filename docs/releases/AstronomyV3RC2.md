# Astronomy V3 RC2 Release Notes

## Purpose

These are the official RC2 release notes for Astronomy V3. They summarize implemented release areas visible in the repository: pipeline stability, hero engine, English/Hindi support, validation, Azure GPT image integration, architecture, known limitations, and roadmap.

## Summary

Astronomy V3 RC2 consolidates the media factory into a more event-aware, multilingual, validation-driven production system. The release strengthens event-family abstraction, hero asset intelligence, Hindi/English production paths, weekly sky forecast modules, Azure OpenAI image generation, validation services, rendering diagnostics, and production configuration hardening.

## Major Features

- Event-family support for meteor, planet grouping, moon, eclipse, special event, and unknown fallback.
- Special-event subtype support for comet, deep-sky object, constellation, occultation, and generic events.
- Hero asset intelligence and scene selection services.
- Azure OpenAI content generation and Azure OpenAI cinematic image generation.
- Hindi and English localization paths.
- Weekly sky forecast architecture, asset planning, asset expansion, asset realization, narration, timeline composition, rendering, audio, and cinematic asset modules.
- Platform publishing and analytics services for YouTube and Meta-family targets.
- Operational services for scheduling, recovery, maintenance, alerting, analytics, and token health.

## Pipeline Stability

RC2 improves pipeline stability through:

- Strongly typed configuration binding and startup validation.
- PostgreSQL connection safety guard against accidental localhost usage.
- Health checks for database, queue, and config readiness.
- Rendering diagnostics for FFmpeg and FFprobe path/executable status.
- Retry/timeout settings for publishing, Blob, sidecar, and AI image generation.
- Pipeline stage recording, recovery, monitoring, and operational alert services.

## Hero Engine

RC2 includes hero-related backend services:

- Hero asset intelligence engine.
- Hero asset scene selector.
- Hero composition engine.
- Hero asset story generator.
- Thumbnail asset intelligence service.
- Local celestial hero assets for major planets and deep-sky targets.

The hero engine supports stronger thumbnail and scene decisions by combining event context with local asset availability and composition rules.

## English Support

English remains the default language path:

- Localization fallback defaults to English where configured.
- Azure Speech defaults include English voice behavior.
- Prompt builder supports English guide and special-event generation.
- Rendering and thumbnail services use English typography defaults.

## Hindi Support

Hindi support is implemented across multiple layers:

- Localization resolver recognizes `hi` and displays `Hindi (हिन्दी)`.
- Prompt generation includes Hindi narration instructions and Hindi fallback phrases.
- Thumbnail generation includes Hindi hooks and Devanagari detection.
- Font configuration validates a Hindi font.
- Azure Speech and SSML handling support Hindi prosody and `hi-IN` locale detection.

## Validation

RC2 validation coverage includes:

- Event-family profiles with forbidden terms, required visual elements, required overlay elements, validator profiles, and diagnostics.
- Event-scene validation strategy resolver and strategies for meteor, planet pairing, conjunction, moon, eclipse, and generic events.
- Pre-publish validation service.
- Production pipeline quality validator.
- TTS package validation and alignment repair services.
- Media validation through FFmpeg/FFprobe services.
- Weekly asset quality validation tests.
- Startup validators for configuration and external dependency settings.

## Azure GPT Image

RC2 introduces Azure OpenAI cinematic image generation:

- Dedicated `AzureOpenAICinematicImageGenerator` service.
- `AzureOpenAIForImage` configuration surface.
- API-key and managed-identity authentication paths.
- Weekly AI cinematic asset queue, selection, validation, persistence, and generation services.
- Timeout and supported-size handling for image requests.

## Architecture

RC2 architecture is modular and DI-driven:

- `Api` hosts HTTP entry points and diagnostics.
- `Worker` hosts background jobs.
- `Core` owns orchestration, event family logic, hero engine, validation, planning, and weekly forecast modules.
- `Infrastructure` binds configuration, registers services, adds persistence, and configures hosted services/health checks.
- `AstroData` supplies astronomy context from NASA, MPC, Skyfield, and observation services.
- `ContentGen` owns prompt building and Azure OpenAI content/image services.
- `Rendering` owns Azure Speech, Stellarium, thumbnails, ImageSharp, FFmpeg, and assets.
- `Publishing` owns Azure Blob, YouTube, Facebook, Instagram, Meta, analytics, thumbnails, and token health.
- `Contracts` owns option and DTO contracts.
- `AIOptimization` owns optimization models and services.
- `Astronomy.SscIntelligence` owns spatial/camera/composition/scene-intent/narrative intelligence.

See [../FolderStructure.md](../FolderStructure.md) for the full folder reference.

## Known Limitations

- Production operation requires valid external credentials and configured Azure resources.
- Rendering requires FFmpeg/FFprobe and, for Stellarium paths, a valid Stellarium installation/configuration.
- Azure OpenAI image generation depends on a configured deployment and accepted image sizes.
- Publishing should be gated by mode and credentials until each platform path is verified.
- Some older docs still exist outside this official RC2 structure and should be consolidated.
- This release note is based on repository implementation rather than a tagged binary artifact; future releases should link exact tags and build outputs.

## Roadmap

After RC2, the planned release path is:

1. **RC3** — strengthen event-family/language test matrices, API documentation, validation gates, and operator runbooks.
2. **RC4** — production readiness pass across Azure, rendering, publishing, telemetry, credentials, and end-to-end regression.
3. **Version 1.0** — stable official production release with release criteria, rollback guidance, support matrix, and documented operating procedures.

See [../Roadmap.md](../Roadmap.md) for the full roadmap.

## Dependencies

RC2 depends on PostgreSQL, Azure OpenAI, Azure Speech, Azure Blob, NASA APIs, Skyfield sidecar, optional Stellarium, FFmpeg/FFprobe, YouTube/Meta platform APIs, Node/npm for clients, and .NET for backend builds/tests.

## Implementation Notes

This release note avoids claiming completed runtime certification beyond what is represented by code, service registration, assets, and tests in the repository.

## Future Improvements

- Attach CI build/test evidence to each release note.
- Add links to generated API docs.
- Add artifact locations and deployment instructions.
- Add release owner, approval, and rollback sections.
