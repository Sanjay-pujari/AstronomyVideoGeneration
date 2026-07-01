# Astronomy V3 Roadmap

## Purpose

This roadmap documents the milestone path visible from the repository and sprint requirements. It distinguishes completed/implemented areas from planned release stabilization work.

## Overview

Milestone sequence:

```text
Completed
  ↓
RC1
  ↓
Hero Tested
  ↓
Hero HI/EN Tested
  ↓
RC2
  ↓
RC3
  ↓
RC4
  ↓
Version 1.0
```

## Architecture

The roadmap follows architecture maturity rather than isolated feature delivery: pipeline foundation, hero asset validation, multilingual validation, RC2 architecture consolidation, then release-candidate hardening toward Version 1.0.

## Responsibilities

- Track implemented foundation work.
- Track RC2 release scope.
- Identify planned hardening without inventing features beyond codebase direction.
- Keep milestone language consistent with [CHANGELOG.md](CHANGELOG.md) and [releases/AstronomyV3RC2.md](releases/AstronomyV3RC2.md).

## Completed Milestones

### Completed

- Modular .NET backend projects for API, Worker, Core, Contracts, Infrastructure, AstroData, ContentGen, Rendering, Publishing, and AIOptimization.
- Frontend and mobile TypeScript app structures.
- PostgreSQL persistence and EF-backed repositories.
- Skyfield sidecar integration and SSC intelligence components.
- Azure OpenAI content generation and Azure Speech rendering path.
- FFmpeg rendering services and render diagnostics.
- Publishing integrations for YouTube and Meta-family targets.
- Scheduler, worker, maintenance, analytics, alerting, and token-health services.

### RC1

- Baseline media factory pipeline for context acquisition, script generation, TTS, visuals, rendering, storage, publishing, and analytics.
- Initial thumbnail and visual composition services.
- Initial category/event planning and operational endpoints.
- Foundational documentation and debugging references already present in `docs`.

### Hero Tested

- Hero asset intelligence services, scene selection, hero composition, story generation, and thumbnail asset intelligence are registered in the backend service graph.
- Celestial hero asset packs are present for major objects such as planets, Sun, Earth, Milky Way, Andromeda, and Orion Nebula.
- Local asset collage thumbnail service and cinematic thumbnail services are backed by tests.

### Hero HI/EN Tested

- Localization resolver supports English and Hindi language selection.
- Prompt generation includes Hindi-specific narration instructions.
- Rendering includes Hindi text detection, Hindi font selection, and Hindi prosody handling.
- SSML locale handling supports `hi-IN` and defaults English voice behavior.

### RC2

- Event-family abstraction for meteor, planet grouping, moon, eclipse, special event, and unknown fallback.
- Special-event subtypes for comet, deep-sky object, constellation, occultation, and generic events.
- Azure OpenAI cinematic image generation service and weekly AI cinematic asset pipeline registration.
- Expanded validation and quality services for pre-publish, production quality, media validation, event-scene validation, TTS package validation, and weekly asset validation.
- Weekly sky forecast architecture with episode architecture, classification, diversification, visual planning, asset expansion, asset realization, narration, timeline composition, rendering, audio generation, and cinematic assets.

## Planned Milestones

### RC3

- Stabilize release criteria and operational runbooks for every production category.
- Increase automated coverage across event families, localizations, and rendering outcomes.
- Tighten validation gates around AI-generated images and platform publishing readiness.
- Improve documentation coverage for API endpoints and operational workflows.

### RC4

- Production readiness pass across credentials, managed identity, Key Vault, Blob, Speech, OpenAI, publishing, and telemetry.
- End-to-end regression matrix for API, Worker, sidecar, frontend, mobile, rendering, and publishing dry runs.
- Finalize public/admin UX documentation and operator dashboards.
- Validate analytics feedback loop and optimization safety thresholds.

### Version 1.0

- Stable, documented, repeatable production pipeline.
- Release process with changelog, release notes, testing evidence, rollback guidance, and known limitations.
- Official support matrix for event families, languages, Azure dependencies, publishing targets, and rendering modes.

## Dependencies

Roadmap execution depends on continued stability of PostgreSQL, Azure OpenAI, Azure Speech, Azure Blob, NASA clients, Skyfield sidecar, Stellarium, FFmpeg/FFprobe, platform APIs, and repository test coverage.

## Implementation Notes

This roadmap intentionally avoids claiming that planned RC3/RC4 work is already complete. It records completed work only where corresponding implementation folders, service registrations, assets, or tests exist.

## Future Improvements

- Add dates, owners, and acceptance criteria to each planned milestone.
- Link each milestone to automated test evidence and release artifacts.
