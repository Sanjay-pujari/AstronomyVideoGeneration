# ADR-013: No Persistence In Knowledge Foundation

Status: Accepted

Milestone: CG-A2 Task 2.6

## Context
The Knowledge Foundation needs deterministic contracts that match production code under `Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation`.

## Decision
Use the production contracts referenced by this ADR: `KnowledgeId`, `KnowledgeVersion`, `IAstronomyKnowledgeStatement`, `ITypedAstronomyKnowledgePayload`, `AstronomyTypedPayloadDescriptor`, validation rule registries, graph validation contracts, catalog contracts, query contracts, registration extensions, capabilities, and compatibility verifier as applicable.

## Rationale
The selected approach keeps knowledge independent from prompts, media generation, persistence, external services, reflection discovery, and runtime inference.

## Consequences
Future work must preserve frozen IDs, ordering, lifetimes, immutability, and validation ownership or provide migration documentation.

## Alternatives considered
Reflection discovery, repositories, database-backed query translation, hosted certification services, graph repair, and inference-based validation were considered.

## Rejected alternatives
Rejected alternatives conflict with deterministic Task 2 boundaries or introduce Task 3/business capability concerns.

## Extension implications
Extensions must be explicit, tested, documented, deterministic, and non-mutating.

## References to actual production contracts
`Astronomy.MediaFactory.Core.KnowledgeFoundation`, `Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration`, `Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation`, `Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain`, `Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph`, `Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog`, `Astronomy.MediaFactory.Core.KnowledgeFoundation.Query`, `Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution`, and `Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration`.
