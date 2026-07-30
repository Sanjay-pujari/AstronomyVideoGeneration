# O2.ORCH.ALIGN.2A — Phase 2 Production Event Intelligence

## Ownership and contracts

Phase 2 owns normalization, family/capability selection, strategy enrichment, semantic validation, certification, and publication. Its canonical authority is `02-intelligence/production-event-intelligence.json`; `plan-input/production-event-intelligence.json` is a legacy projection of the committed authority's inner `ProductionEventIntelligence` only. Phase 1 creates the bootstrap compatibility artifact and inventories that required publication in its six-entry manifest. When Phase 2 replaces the projection, the shared phase manifest is refreshed after the Phase 2 commit so the Phase 1 entry continues to checksum the active physical compatibility artifact while the Phase 2 entry records its derivation from Phase 2 authority.

The versioned authority envelope retains `ProductionEventIntelligence` as the downstream compatibility model while adding event identity, capability resolution, typed family payload, artifact references, validation, certification, and Phase 1/request lineage. Plan, execution, and publication transaction identities are distinct. The semantic checksum excludes its own checksum field.

## Taxonomy and capabilities

Alias normalization is deterministic and recognizes constellation, meteor shower, conjunction, pairing/close approach, grouping, named full moon, new moon, lunar/solar eclipse, comet, and deep-sky object families. Registered capabilities reuse the existing `IMediaEventStrategyResolver` and strategy definitions. A known family without a capability fails with `P2_KNOWN_FAMILY_CAPABILITY_MISSING`; only an unknown family can select the configured generic fallback, and that decision is recorded. Orion therefore selects `Constellation`, never `GenericAstronomy`.

Family payloads model constellation, meteor, planetary alignment/grouping, eclipse, lunar, and generic observations without requiring irrelevant fields. Policies classify required, recommended, optional, conditional, and not-applicable fields. Coverage excludes not-applicable requirements; solar-eclipse safety is blocking. Scores retain their declared scale (`70/100` or `7/10`) and reject out-of-range values.

## Artifact set and publication

The atomic canonical directory contains:

* `production-event-intelligence.json` — sole authority;
* `certified-knowledge-context.json` — typed, category-separated claims;
* `observation-context.json` — temporal, geographic, visibility, safety, and calculation context;
* `source-registry.json` — typed plan/provider provenance;
* `production-intelligence-diagnostics.json` — capability and family-policy diagnostics.

All five files are written into `.02-intelligence-staging-<transaction-id>`, parsed and checksum-validated, then committed by directory rename with `.02-intelligence-backup-<transaction-id>` rollback protection. Startup removes only abandoned staging and restores a valid backup when canonical authority is absent. Compatibility is written after canonical commit. The phase manifest records exactly one authoritative role plus all supporting and compatibility roles with raw SHA-256.

Reuse requires the complete set, successful deserialization/certification, and a valid semantic checksum; it returns `P2_REUSED` without rewriting canonical files. Replacement is the integration point for transactional Phase 3–20 invalidation. Phase 3 is refreshed from the committed envelope immediately after Phase 2, preventing use of bootstrap intelligence.

## Test and certification matrix

Focused tests cover taxonomy aliases, unknown-family behavior, deterministic resolution, score scales, and invalid score ranges. The service validates complete staging, corrupt/missing artifacts, semantic checksums, authority identity, compatibility projection, and backup recovery. Physical endpoint certification remains environment-dependent and Phase 2 is **not frozen** until the RC2 API, representative stored plan fixtures, artifact ZIP, and downstream invalidation fault-injection suite are run in a configured production-like environment.

## Known limitations / technical debt

* Recovery currently restores a valid backup and removes aged staging; richer operator classification/preservation of every corrupt evidence set remains to be added.
* Downstream directory/manifest invalidation must be moved behind the repository's shared transactional invalidator rather than only reporting the replacement boundary.
* Provider-specific constellation star geometry and IAU identity depend on expanding the existing knowledge provider projection; no Orion facts are hardcoded here.
* Full endpoint certification requires the database fixture, Azure configuration, FFmpeg, and a runnable .NET SDK.
