# Phase 7 canonical field-path production exception

## Scope and result

No production Orion payload or captured exception diagnostic is present in this repository. The only tracked Orion evergreen knowledge payload is `Knowledge/Constellations/Orion/Orion.v1.json`, and the Phase 7 cultural mapping tests read that same file directly. Consequently, the tracked production-versus-test path difference is empty; this does **not** establish that the independently deployed production payload is identical.

Until the temporary diagnostic records a rejected entry, the requested incident fields are:

| Result | Finding |
| --- | --- |
| Offending raw path | Not recoverable from `P7KNOWLEDGE_FIELD_PATH_INVALID (Parameter 'value')` alone. It will be the `rawPath` value on the `outcome: rejected` diagnostic. |
| Offending JSON property | Not recoverable without the deployed payload or rejection diagnostic. It will be correlated by `traditionName`, `fieldName`, and `payloadSource`. |
| Offending adapter | Not recoverable from the exception alone. It will be the diagnostic's `adapter` value. |
| Rejecting rule | After separator and array-ordinal normalization, every non-empty dot-delimited segment must begin with a letter or underscore and contain only letters, digits, underscore, or hyphen. Blank paths and paths containing `..` are also rejected. |
| Is the fixture stale? | No difference exists in tracked inputs because tests consume the tracked Orion file itself. The deployed fixture status remains unproven until the deployed production JSON is exported and compared. |

## Instrumented call inventory

All runtime calls to `Phase7CanonicalFieldPathPolicy.Canonicalize` now pass through the temporary diagnostic wrapper:

1. `Phase7CulturalSourcePathMapper.Map` (exact cultural source provenance).
2. `ApprovedFieldKnowledgeAdapter.ApprovedFieldPaths` (the common schema path for every registered Phase 7 section adapter).
3. `ApprovedFieldKnowledgeAdapter.Visit` (the common payload extraction path for every registered Phase 7 section adapter).
4. Both evergreen and event candidates in `Phase7KnowledgeMergeClassifier.Classify`.
5. `Phase7SourceEligibilityPolicy.Precision`.
6. `Phase7CulturalClaimPolicy.Evaluate`.
7. Both claim and claim-resolution diagnostic construction sites in `Phase7KnowledgeResolver`.

The two remaining direct calls are unit tests of the canonical policy itself, rather than production callers.

## Diagnostic contract

Before policy execution, one JSON line is written to standard error with `rawPath`, `normalizedPath`, `caller`, `payloadSource`, `adapter`, `traditionName`, and `fieldName`. If the policy throws its `ArgumentException` for parameter `value`, a second `outcome: rejected` line repeats the exact raw input and includes the exception. The wrapper rethrows without changing production behavior.

## Production comparison procedure

Export the exact deployed evergreen JSON without transforming property names. Produce recursive JSON property paths for it and for `Knowledge/Constellations/Orion/Orion.v1.json`, sort each set ordinally, and calculate `production - fixture`. Preserve the rejected diagnostic alongside the export so its `rawPath`, adapter, tradition, and field can be joined to the production-only property. Do not infer a field from the exception message alone.
