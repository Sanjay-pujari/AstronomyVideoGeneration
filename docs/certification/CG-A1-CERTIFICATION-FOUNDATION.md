# CG-A1 Certification Foundation

This document describes Task 1 of the CG-1A Phase 1–7 Certification Framework: the shared contracts, models, registry, and dependency-injection foundation only.

## Purpose

The foundation defines common certification language for future phase certifiers without changing runtime media generation behavior. It separates execution, semantic, and publication readiness so later tasks can certify artifacts, semantic evidence, and quality independently.

## Certification levels

- **L1 Structural / Execution**: verifies required phase artifacts exist, are non-empty, and meet basic structural expectations.
- **L2 Semantic**: verifies canonical identity, family semantic values, required facts, retention, projection, beat coverage, and narration evidence.
- **L3 Quality / Publication**: reserves publication-readiness and content-quality status separately from structural and semantic checks.

## EventType versus ContentStrategy

`ProductionPipelineRequest.EventType` defines the astronomy family. `ContentStrategy` is editorial/presentation strategy only. A request with `EventType = MeteorShower` and `ContentStrategy = LocalViewingGuide` must still resolve as Meteor Shower.

The certification profile registry accepts only an `eventType` string. It has no `ContentStrategy` parameter, so presentation strategy cannot influence family selection.

## Contracts added

- Certification enums and result records.
- `FamilyCertificationContext`.
- `IPhaseArtifactRegistry` and `PhaseArtifactDefinition`.
- Family profile contracts and profile requirement records.
- `IFamilyCertificationProfileRegistry`.
- `IPhaseCertifier`.
- `ISemanticCertificationEvidenceReader`.
- `ICertificationCoordinator`.
- `ICertificationReportWriter`.

## Registry behavior

The generic family profile registry receives `IFamilyCertificationProfile` instances through DI, indexes each profile by `FamilyId` and supported event type aliases, compares keys case-insensitively, rejects null or empty event types, detects duplicate aliases during construction, and supports zero registered profiles.

## Extension points

Later tasks can add real family profiles, artifact definitions, phase certifiers, evidence readers, coordinators, and report writers by implementing the contracts defined here and registering those concrete services.

## Not implemented in Task 1

Task 1 intentionally does not implement Phase 1–7 certifiers, real Meteor Shower or Planet Conjunction profiles, phase artifact definitions, semantic diagnostic parsing, JSON or Markdown report generation, pipeline hooks, dashboard generation, quality evaluation, or runtime semantic behavior changes.

## Next task

The next task should add the first concrete family/profile or phase-specific certification implementation requested by the multi-step plan, using these foundation contracts without changing the EventType-only family rule.
