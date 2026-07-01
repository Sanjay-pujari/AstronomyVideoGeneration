# Changelog

All notable changes for Astronomy V3 are documented here. This changelog is based on the current repository implementation and release-sprint scope.

## Purpose

Provide a professional release history for the Astronomy V3 foundation, with explicit RC1 and RC2 notes, architecture changes, hero engine progress, validation improvements, Azure GPT image integration, Hindi support, known issues, and future work.

## Overview

Astronomy V3 has evolved from a baseline media pipeline into a modular astronomy production system with event-family intelligence, hero asset handling, multilingual support, AI image generation, validation, publishing, analytics, and operations services.

## RC2

### Added

- Event-family abstraction for meteor, planet grouping, moon, eclipse, special event, and unknown events.
- Special-event profile support for comet, deep-sky object, constellation, occultation, and generic events.
- Event-family validator profile metadata, thumbnail composition mapping, forbidden terms, required visual elements, overlay elements, and diagnostic fields.
- Azure OpenAI cinematic image generator for AI image assets.
- Weekly sky forecast pipeline modules for episode architecture, segment classification, segment diversification, visual asset planning, asset expansion, asset realization, event scoring, narration, timeline composition, rendering, audio generation, and AI cinematic assets.
- Hero asset intelligence, scene selection, hero composition, story generation, and thumbnail asset intelligence services.
- Hindi support across localization, prompt instructions, TTS/SSML, font selection, and thumbnail rendering paths.
- Expanded production validation services, media validation services, event-scene validation strategies, TTS package validation, and pre-publish validation.

### Changed

- Architecture moved further toward strongly typed options and startup validation for production dependencies.
- Rendering and thumbnail services now account for language-specific typography and event-family composition.
- AI usage is split into content generation, metadata optimization, prompt feedback, thumbnail optimization, and cinematic image generation responsibilities.
- Publishing and analytics responsibilities are separated into platform services, collectors, aggregation, and optimization feedback providers.

### Fixed / Hardened

- Startup validation and runtime diagnostics for FFmpeg/FFprobe availability.
- Safer database configuration guard against accidental localhost PostgreSQL usage unless explicitly allowed.
- Retry and timeout options for Azure Blob, YouTube, Meta publishing, Skyfield sidecar, and Azure OpenAI image generation.
- Token health startup service and token health reporting for platform credentials.

### Known Issues

- External service credentials and managed-identity configuration must be supplied per environment.
- Rendering depends on correct FFmpeg/FFprobe and optional Stellarium paths.
- AI image generation depends on configured Azure OpenAI image deployment and supported image sizes.
- Platform publishing should remain disabled, dry-run, private, or otherwise gated until credentials and compliance paths are verified.
- Some repository documents and folders predate this V3 documentation structure and should be consolidated in future documentation sprints.

## RC1

### Added

- Baseline backend media factory with API host, worker host, domain core, infrastructure, content generation, rendering, publishing, contracts, and tests.
- Initial pipeline flow: astronomy context acquisition, prompt generation, speech synthesis, visual generation, video rendering, storage, publishing, analytics, and recovery.
- PostgreSQL persistence, operational endpoints, health checks, scheduler, worker queue, maintenance, alerting, and analytics collectors.
- Azure OpenAI content generation service with fallback behavior.
- Azure Speech synthesis service.
- Azure Blob storage and public media storage services.
- YouTube, Facebook, Instagram, and Meta publishing/OAuth service surfaces.
- Frontend and mobile client structures.

### Architecture Changes

- Created modular project boundaries under `Backend/src/Astronomy.MediaFactory.*`.
- Introduced strongly typed configuration contracts under `Astronomy.MediaFactory.Contracts`.
- Centralized DI registration in `ServiceCollectionExtensions.AddMediaFactory`.
- Added Skyfield sidecar and SSC intelligence integration paths for astronomy visibility and composition.

## Hero Engine

### Added / Implemented

- Hero asset intelligence engine.
- Hero asset scene selector.
- Hero composition engine.
- Hero asset story generator.
- Thumbnail asset intelligence service.
- Celestial hero asset packs under API assets.

### Implementation Notes

The hero engine is integrated into the backend service graph and rendering/thumbnail pathways. Existing tests cover key thumbnail and asset services, and the repository includes local hero PNG assets for common celestial objects.

## Validation Improvements

### Added / Implemented

- Production pipeline quality validator.
- Pre-publish validation service.
- Event-scene validation strategy resolver and strategies for major event categories.
- TTS package validation and alignment repair services.
- Weekly asset quality validation tests.
- Media validation through FFmpeg/FFprobe-oriented services.
- Startup validation for critical options.

## Azure GPT Image Integration

### Added / Implemented

- `AzureOpenAICinematicImageGenerator` for image-generation requests.
- Configuration surface through `AzureOpenAIForImageOptions`.
- Weekly AI cinematic asset queue, selector, persister, validator, and generation services.
- Managed identity and API-key authentication paths.

## Hindi Support

### Added / Implemented

- Localization resolver with `en` and `hi` support.
- Hindi language display name.
- Hindi prompt instructions for narration.
- Hindi fallback content in content generation.
- Hindi thumbnail hooks and Devanagari detection.
- Hindi font configuration and validation.
- Hindi prosody and `hi-IN` SSML locale handling.

## Future Work

- Add release dates and tagged build references when Git tags are created.
- Add API endpoint reference documentation.
- Add test matrix documentation for event families and languages.
- Consolidate older documentation into the V3 official structure.
- Add operator runbooks for RC3/RC4 production readiness.
