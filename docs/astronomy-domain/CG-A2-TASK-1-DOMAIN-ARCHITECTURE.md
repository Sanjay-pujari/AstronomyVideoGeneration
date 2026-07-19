# CG-A2 Task 1 — Astronomy Domain Architecture

## Purpose
This document describes the family-neutral CG-A2 astronomy domain foundation. It is architecture-only: no Orion entry, real constellation, event family, persistence migration, narration prompt, renderer, publisher, or certification decision behavior is implemented.

## Boundaries
CG-A2 owns astronomy identity, classification, family contracts, relationships, localization, source attribution, validation, query contracts, and a testing/foundation in-memory catalog. CG-A1 remains the certification execution and reporting layer.

## Taxonomy
The foundation defines high-level enums for domain categories, family kinds, entity kinds, subject temporality, and knowledge domains. These are coarse architectural classifications, not replacements for later family-specific taxonomies.

## Entity identity
`AstronomyEntityIdentity` provides stable language-neutral IDs such as synthetic examples in tests. Localized display names never become primary identifiers. Aliases are optional and must be case-insensitively unique.

## Classification
`AstronomyClassification` combines domain category, family kind, entity kind, temporality, tags, observability flags, and optional structured `ScientificClassification`.

## Family contract and registry
`IAstronomyDomainFamily` exposes family identity, supported entity kinds, supported EventType aliases, support checks, and entity validation. `IAstronomyDomainFamilyRegistry` resolves by FamilyId, EventType, or entity identity. Resolution is deterministic, case-insensitive, trimmed, duplicate-aware, and never uses ContentStrategy.

## Relationships
`AstronomyRelationship` records typed relationships with source/target IDs, relationship type, direction, confidence, source IDs, validity range, notes, and constrained metadata. `IAstronomyRelationshipPolicy` controls self-reference and symmetry rules. This is storage-neutral and does not require a graph database.

## Localization
`AstronomyLocalizedContent` supports language, region, display names, aliases, pronunciation, cultural usage notes, machine-translation status, review status, and version. English and Hindi localizations are separate representations of the same canonical entity.

## Source attribution
`AstronomySourceReference` captures source type, publisher, title, author, URL, citation, dates, license, language, reliability, authority level, and notes. Source validation avoids hard-coded agency allowlists.

## Validation
`IAstronomyDomainValidator` validates entities, families, relationships, localization records, and sources. It aggregates stable issue codes such as `A2.DOMAIN.IDENTITY.EntityIdMissing`, `A2.DOMAIN.RELATIONSHIP.SelfReference`, and `A2.DOMAIN.SOURCE.InvalidUrl`.

## Catalog
`IAstronomyDomainCatalog` defines lightweight read/search contracts. `InMemoryAstronomyDomainCatalog` is a foundation/testing implementation only; it is not the final production repository and does not introduce persistence migrations or external search infrastructure.

## Dependency injection
`AddCgA2AstronomyDomainFoundation()` registers the family registry, relationship policy, validator, and in-memory catalog. No real astronomy families are registered.

## Serialization
Contracts are designed for `JsonSerializerDefaults.Web` with string enum converters, camelCase names, stable schema versions, and no CLR type metadata.

## Extension rules
`AstronomyDomainMetadata.ExtensionMetadata` is only for non-critical primitive JSON-compatible optional metadata. It must not contain canonical identity or required domain facts and is not a replacement for family-specific models.

## Synthetic example
Tests create synthetic family IDs and entities such as `synthetic.event.alpha` only to validate architecture. These are not astronomy catalog data.

## Future constellation integration
A later task may add a constellation family that maps to this foundation, but constellation boundaries, named stars, artwork, mythology, and observation rules remain family-specific.

## Future event-family integration
Future event families should register their stable FamilyId and EventType aliases. They should not use ContentStrategy to determine scientific identity.

## CG-A1 boundary
CG-A1 can later consume CG-A2 evidence through adapters, but certification profiles are not merged with domain families.

## Persistence boundary and migration guidance
Existing persistence entities such as event intelligence and content plans are not replaced. Future migrations should map persistence records to CG-A2 domain contracts explicitly instead of changing this foundation into an EF-specific model.

## What does not belong in the shared domain model?
- Meteor ZHR.
- Eclipse contact times.
- Constellation boundaries.
- Planetary angular separation.
- Comet orbital elements.
- Family-specific narration rules.
- Prompt instructions, rendering directives, publishing decisions, or certification decision logic.
