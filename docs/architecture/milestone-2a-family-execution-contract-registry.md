# Milestone 2A Family Execution Contract Registry

## Purpose
Milestone 2A introduces a dormant, domain-neutral execution-contract vocabulary and immutable in-memory registry for later execution validation, artifact validation, family certification, and production cutover.

Milestone 2A does not alter production execution.

## Contract model hierarchy

```text
DomainExecutionContract
    ↓ contains
FamilyExecutionContract
    ↓ declares
Input / Semantic / Projection / Artifact / Validation Requirements
    ↓ indexed by
ExecutionContractRegistry
    ↓ resolves
Canonical family contract or structured NotFound result
```

`DomainExecutionContract` defines a domain identifier, opaque contract version, display metadata, immutable family contracts, and immutable metadata. `FamilyExecutionContract` defines a canonical family identifier, opaque contract version, aliases, declarative requirement collections, metadata, and lifecycle status.

## Immutable design
Contract records normalize default immutable arrays to empty arrays and null metadata dictionaries to empty immutable dictionaries. The registry snapshots the supplied domain sequence during construction and exposes immutable domain contracts through a read-only collection view.

## Registry identity policy
Family identities are compared with `StringComparer.OrdinalIgnoreCase` after trimming incoming requests. Canonical family ID matches are reported as `CanonicalFamilyId`; alias matches are reported as `Alias`; failures are returned as structured `FamilyContractResolution` values.

## Canonical ID and alias rules
Aliases are trimmed, empty aliases are discarded, duplicate aliases are removed case-insensitively within a family, and aliases matching the canonical family ID are removed. Registry construction rejects aliases that conflict with another alias or another canonical family ID.

## Domain-qualified resolution
The same canonical family ID may appear in multiple domains. Unqualified resolution of such an identity is ambiguous and returns `NotFound` with a diagnostic message. Supplying `domainId` restricts resolution to that domain and resolves deterministically.

## Duplicate and conflict rules
The registry rejects duplicate domain IDs, duplicate canonical family IDs within a domain, alias conflicts within a domain, cross-domain alias conflicts, and cross-domain alias-to-canonical conflicts. These errors indicate invalid static contract definitions.

## Versioning rules
`ContractVersion` is required and treated as an opaque stable identifier. The registry does not parse semantic versions, select a latest version, or collapse multiple versions for the same domain/family identity. Conflicting versions for the same canonical identity in one domain are rejected.

## Current dormant status
`AstronomyExecutionContractCatalog` returns an empty Astronomy domain shell with `frameworkStatus=dormant`, `runtimeWiring=none`, and `milestone=2A`. It is not registered in dependency injection and is not consumed by production runtime code.

## Explicit non-goals
Milestone 2A does not define family-specific contracts, validate runtime values, access files, invoke semantic adapters, generate artifacts, register services, change family resolution, or alter narration/orchestration behavior.

## Planned use in Milestone 2B and 2C
Milestone 2B can add concrete dormant family contract definitions. Milestone 2C can use the registry and structured resolution results as a validation and certification boundary before any production cutover decision.
