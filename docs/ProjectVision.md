# Project Vision

## Purpose

Astronomy V3 exists to automate high-quality astronomy media production from real sky context, event intelligence, and reusable rendering assets. The project is not a generic video generator; it is a specialized media factory for astronomy guides, forecasts, event explainers, shorts, thumbnails, and publishing workflows.

## Overview

The current codebase shows a product moving from manually assisted content production toward an AI-first pipeline. Core services combine astronomy data, event classification, localization, prompt generation, TTS, visual asset generation, validation, publishing, and analytics feedback.

The product goal is to make astronomy content production repeatable, validated, multilingual, and platform-aware while retaining astronomical context such as visibility windows, event family, observation region, night-sky timing, and object-specific visual requirements.

## Architecture

The vision is implemented through a layered architecture:

- **Astronomy intelligence** resolves context from event stores, NASA clients, Skyfield sidecar responses, observation windows, and event-family strategies.
- **AI content intelligence** creates scripts, metadata, image prompts, question-driven narration, thumbnail hooks, and optimization recommendations.
- **Hero and visual intelligence** selects or generates hero assets, composes thumbnails, plans scenes, and validates visual fit by event family.
- **Rendering intelligence** uses Azure Speech, Stellarium-oriented assets, local celestial assets, NASA assets, ImageSharp, FFmpeg, and FFprobe.
- **Operational intelligence** tracks stage state, validation, recovery, alerting, analytics, and token health.

## Responsibilities

Astronomy V3 is responsible for:

- Producing astronomy-specific long-form and short-form content.
- Supporting event families implemented in the codebase: meteor, planet grouping, moon, eclipse, special event, and unknown fallback.
- Supporting English and Hindi output through localization, prompt instructions, fonts, SSML, voice, and prosody handling.
- Keeping rendering and publication pipelines observable and recoverable.
- Using analytics and feedback to improve hooks, metadata, thumbnails, and publishing outcomes.

## Dependencies

The product depends on:

- PostgreSQL for durable state.
- Azure OpenAI for text and metadata generation.
- Azure OpenAI image generation for cinematic image assets.
- Azure Speech for narration audio.
- Azure Blob for storage/public media.
- NASA APIs and local celestial assets for visual and astronomy context.
- Skyfield sidecar and SSC intelligence for visibility and composition.
- Stellarium, FFmpeg, and FFprobe for visual capture/rendering paths.
- Platform APIs for YouTube, Meta/Facebook, and Instagram publishing/analytics.

## Implementation Notes

The codebase already reflects key differentiators:

- Event-family abstraction with validator profiles and thumbnail composition rules.
- Hero asset intelligence and scene selection services.
- Question-driven narration and visual composition services.
- Azure GPT image integration through a dedicated cinematic image generator.
- Hindi support in prompt generation, localization, fonts, TTS prosody, and SSML voice locale handling.
- Pre-publish validation, production quality validation, media validation, and weekly asset quality validation tests.
- Scheduler, worker, maintenance, analytics collection, token-health, and alerting services.

## Target Audience

The primary audiences are:

- Operators managing automated astronomy media production.
- Developers extending backend pipeline, rendering, prompt, validation, and publishing modules.
- Content strategists creating astronomy guides, forecasts, event explainers, and social variants.
- Viewers who need clear, localized, visually guided astronomy content.

## AI-First Philosophy

Astronomy V3 uses AI as an orchestration accelerator, not as an unchecked source of truth. The codebase pairs AI generation with typed options, event-family rules, validators, fallback templates, diagnostics, and platform-aware constraints. AI is used for content generation, prompt/image generation, metadata optimization, thumbnail intelligence, narrative planning, and asset expansion while deterministic services keep event types, scene requirements, output paths, and validation controlled.

## Long-Term Roadmap

The long-term roadmap is to move from RC foundations into a stable Version 1.0 that can reliably produce multilingual, event-aware astronomy media at schedule scale. Planned work should continue hardening validation, release operations, media quality scoring, event-family coverage, AI image governance, platform publishing readiness, analytics feedback loops, and frontend/mobile operational surfaces.

See [Roadmap.md](Roadmap.md) for milestone sequencing.

## Differentiators

- Astronomy-specific event intelligence rather than generic content categories.
- Integrated Skyfield/Stellarium/FFmpeg rendering path.
- Hero engine and event-family visual rules.
- English and Hindi production support.
- Azure-native AI, speech, storage, telemetry, and managed-identity paths.
- Built-in publishing, analytics, validation, alerting, and recovery concerns.

## Future Improvements

- Expand documented release criteria for RC3, RC4, and Version 1.0.
- Add architecture diagrams generated from stable service boundaries.
- Add operator playbooks for each supported content category.
- Add explicit quality gates for every event family and supported language.
