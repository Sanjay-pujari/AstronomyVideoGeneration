# ADR-006 — Relationships are typed but storage-neutral

## Status
Accepted for CG-A2 Task 1.

## Context
CG-A2 introduces astronomy-domain architecture while CG-A1 certification remains frozen. The domain must support future families without implementing real astronomy catalog entries or changing production generation behavior.

## Decision
Relationships are typed domain records with policy validation. Storage remains neutral and no graph database dependency is introduced.

## Consequences
- Future families can plug in without changing CG-A1 or production pipelines.
- Domain validation can detect ambiguity and malformed shared data early.
- Family-specific astronomy facts remain outside the shared foundation.

## Alternatives considered
- Using ContentStrategy for family selection was rejected because it is presentation-oriented.
- Embedding facts in certification was rejected because it would make CG-A1 the source of astronomy truth.
- Using generic dictionaries as the primary model was rejected because it weakens domain contracts.
