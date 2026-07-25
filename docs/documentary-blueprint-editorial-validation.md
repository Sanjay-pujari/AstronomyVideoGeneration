# Documentary Blueprint Editorial Validation

## Architectural position

```text
DocumentaryBlueprint
        ↓
Editorial Validation
        ↓
Validation Result
```

O2.3 reads a complete immutable blueprint and reports editorial-structure findings. It neither mutates nor repairs the blueprint, generates content, nor queries the Knowledge Foundation.

## Validation result

`DocumentaryBlueprintValidationResult` identifies the blueprint and defensively exposes findings in execution order. `IsValid`, `ErrorCount`, and `WarningCount` are derived from those findings; warnings alone are valid. Each immutable finding has a stable rule code, fixed `Error` or `Warning` severity, deterministic message, blueprint identifier, and optional scene, scene number, and field.

## Rule inventory

| Order | Code | Severity | Purpose | Scope |
|---:|---|---|---|---|
| 1 | DBP-EDITORIAL-001 | Error | Require at least one scene | Blueprint |
| 2 | DBP-EDITORIAL-002 | Error | Require positive scene numbers | Scene |
| 3 | DBP-EDITORIAL-003 | Error | Require continuous numbering from one | Blueprint |
| 4 | DBP-EDITORIAL-004 | Error | Require collection/number order agreement | Blueprint |
| 5 | DBP-EDITORIAL-005 | Error | Require scene knowledge | Scene |
| 6 | DBP-EDITORIAL-006 | Error | Require exactly one primary reference | Scene |
| 7 | DBP-EDITORIAL-007 | Error | Require ordinally unique knowledge IDs | Scene |
| 8 | DBP-EDITORIAL-008 | Warning | Identify repeated viewer questions | Duplicate group |
| 9 | DBP-EDITORIAL-009 | Warning | Prefer an opening role for scene one | Scene |
| 10 | DBP-EDITORIAL-010 | Warning | Prefer closure in the last scene | Scene |
| 11 | DBP-EDITORIAL-011 | Warning | Require critical scenes to introduce or deepen | Scene |
| 12 | DBP-EDITORIAL-012 | Error | Require practical guidance in practical scenes | Scene |
| 13 | DBP-EDITORIAL-013 | Warning | Prefer emotional payoff in reflective closings | Scene |
| 14 | DBP-EDITORIAL-014 | Error | Require knowledge for scientifically required visuals | Visual/scene |
| 15 | DBP-EDITORIAL-015 | Error | Require positive total estimated duration | Blueprint |
| 16 | DBP-EDITORIAL-016 | Warning | Identify zero-duration scenes | Scene |

## Determinism

Rules execute through explicit sequential statements in the table order. Scene findings use scene number then ordinal scene ID; visual findings preserve visual collection order. Codes and messages are stable. Validation has no clock, randomness, configuration, I/O, service provider, or other runtime dependency.

## Explicit exclusions

O2.3 provides no automatic fixes or rewriting, Knowledge Selection, scientific fact verification, narrative composition, narration, LLM integration, prompts, runtime registration, dependency injection, APIs, or persistence.
