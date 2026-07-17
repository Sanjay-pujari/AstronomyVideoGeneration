# Milestone 1.5 MeteorActivity Duplication Inventory

## 1. Executive Summary

The repository currently contains **12 MeteorActivity-related implementations or equivalent logic paths** found by inspecting production source, tests, and existing discovery notes.

- **Production runtime:** 9 paths: Phase 7 presence snapshots, two duplicated Phase 7 `ReadMeteorActivity`/`BuildMeteorActivityFromRequest` helper sets, adapter-context construction, source adapter extraction, catalog lookup/normalization, compatibility projection, adapter registration, and lifecycle diagnostics.
- **Diagnostics-only:** 4 paths: presence snapshot, context/write diagnostics, adapter/resolution/projection/beat diagnostics, and failure classification. Some diagnostics are invoked during runtime but only write diagnostics or classify exception details.
- **Test-only:** 4 paths: direct `MeteorActivityValue` fixtures, production-parity characterization, executable-family coverage, and adapter/projection regression tests.
- **Semantic-result-affecting:** 6 paths: `ReadMeteorActivity`, `BuildMeteorActivityFromRequest`, `CreateAdapterContext`, `MeteorActivitySourceAdapterV1.TryExtract`, `MeteorShowerKnowledgeCatalogV1.FindByCanonicalShowerIdentity`, and `LegacyRequiredSemanticFactCompatibilityMapper.Map`.

Milestone 1.5 did **not** consolidate or move any MeteorActivity construction, extraction, normalization, projection, adapter, or source-policy behavior.

## 2. Duplicate Inventory Table

| ID | File | Type/Method | Responsibility | Called By | Runtime Role | Changes Semantic Result? |
|---|---|---|---|---|---|---|
| M1 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | `BuildPhase7SourceContextPresenceSnapshot` / first `BuildMeteorActivityPresence` | Presence diagnostics for Phase 7 source context, using `ReadMeteorActivity` then `BuildMeteorActivityFromRequest` | Phase 7 diagnostics writing | Diagnostics-only runtime path | No |
| M2 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | first `ReadMeteorActivity` | Extracts root `zhr` from `ProductionEventIntelligence` JSON into a partial `MeteorActivityValue` | First presence helper | Diagnostics-only extraction | No |
| M3 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | first `BuildMeteorActivityFromRequest` | Derives annual meteor-shower metadata from production request plus event window | First presence helper | Diagnostics-only derivation | No |
| M4 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | second `BuildPhase7SourceContextPresenceSnapshot` / second `BuildMeteorActivityPresence` | Duplicated presence diagnostics block | Phase 7 diagnostics writing in later partial region | Diagnostics-only runtime path | No |
| M5 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | `CreateAdapterContext` | Populates `SemanticSourceAdapterContextV1.ProductionEventIntelligence.MeteorActivity` using `ReadMeteorActivity` then `BuildMeteorActivityFromRequest` | Required semantic fact resolver | Production runtime context population | Yes |
| M6 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | second `ReadMeteorActivity` | Extracts root `zhr` from `ProductionEventIntelligence` JSON into a partial `MeteorActivityValue` | `CreateAdapterContext` | Production runtime extraction | Yes |
| M7 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | second `BuildMeteorActivityFromRequest` | Derives annual meteor-shower metadata from request, gated by `ContentStrategy == "MeteorShower"` | `CreateAdapterContext` | Production runtime derivation | Yes |
| M8 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Catalog/MeteorShowerKnowledgeCatalogV1.cs` | `FindByCanonicalShowerIdentity` / `Normalize` | Normalizes shower identity and returns known shower metadata | `BuildMeteorActivityFromRequest`, diagnostics | Production metadata lookup | Yes |
| M9 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters/Event/SemanticSourceAdaptersV1.cs` | `MeteorActivitySourceAdapterV1.TryExtract` | Extracts `MeteorActivityValue` from typed adapter context and records adapter diagnostics | Semantic resolution engine via registry | Production source adapter | Yes |
| M10 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/LegacyRequiredSemanticFactCompatibilityMapper.cs` | `Map` handling for `MeteorActivityValue` | Projects canonical MeteorActivity into legacy `Radiant`, `PeakWindow`, and related fields | Phase 7 projection | Production compatibility projection | Yes |
| M11 | `Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Diagnostics/MeteorActivityLifecycleDiagnostics.cs` | `WriteContext`, `RecordAdapter`, `RecordResolution`, `RecordProjection`, `RecordBeat`, `ClassifyMeteorActivityFailure` | Meteor-specific diagnostics and failure classification inputs | Adapter, resolver, projection, Phase 7 exception path | Diagnostics-only runtime path | No, except exception classification text |
| M12 | `Backend/tests/Astronomy.MediaFactory.Tests/*Meteor*`, `SemanticSourceAdaptersV1Tests.cs`, `ExecutableFamilySemanticCoverageV1Tests.cs`, `StructuredFieldProjectionRegressionTests.cs` | Inline `MeteorActivityValue` fixtures and reflection-driven production-parity tests | Test construction and characterization of MeteorActivity behavior | xUnit tests | Test-only | No production effect |

## 3. Logic Comparison

### M2/M6: `ReadMeteorActivity`

- **Accepted input sources:** nullable `JsonElement` for `ProductionEventIntelligence`.
- **Gating conditions:** source must be a JSON object.
- **Event/family checks:** none.
- **ContentStrategy checks:** none.
- **Shower identity source:** none.
- **Catalog lookup key:** none.
- **Event-window source:** none.
- **Radiant source:** none.
- **Peak-window source:** none.
- **ZHR source:** root property named `zhr`, either a scalar number or object with `value`.
- **Parent-body source:** none.
- **Provenance:** none on the constructed value.
- **Null behavior:** returns `null` when source is missing/non-object or `zhr` is absent/unparseable.
- **Fallback behavior:** no fallback after failed `zhr` parse inside the method; callers may fall back to request derivation.
- **Divergence:** the two implementations are equivalent in responsibility but not text-identical; the later production version uses `TryGetRootString` while the earlier presence version enumerates properties.

### M3/M7: `BuildMeteorActivityFromRequest`

- **Accepted input sources:** `ContentPlanProductionPipelineRequest` and an optional `EventWindowValue`.
- **Gating conditions:** request must be non-null; `ContentStrategy` must equal `MeteorShower`; catalog record must exist; catalog record must support family `MeteorShower`.
- **Event/family checks:** checks catalog record `SupportedFamilyId`, not request `EventType`.
- **ContentStrategy checks:** requires `request.ContentStrategy == "MeteorShower"` using ordinal-ignore-case comparison.
- **Shower identity source:** first non-empty of `request.PrimaryObjects.FirstOrDefault()` and `request.ShortTitle`.
- **Catalog lookup key:** shower identity above, normalized by `MeteorShowerKnowledgeCatalogV1`.
- **Event-window source:** caller-provided request/intelligence event window.
- **Radiant source:** catalog `RadiantConstellation`.
- **Peak-window source:** caller-provided event window.
- **ZHR source:** catalog `ZenithalHourlyRate`.
- **Parent-body source:** catalog `ParentBody`, only when `ParentBodyAuthoritative`.
- **Provenance:** string containing catalog ID and catalog provenance.
- **Null behavior:** returns `null` for non-MeteorShower content strategy, missing catalog record, or unsupported catalog family.
- **Fallback behavior:** caller attempts `ReadMeteorActivity` before this method.
- **Divergence:** this gate can block realistic `LocalViewingGuide` production requests even when `EventType` is `MeteorShower`; Milestone 1.5 did not change it.

### M1/M4: `BuildMeteorActivityPresence`

- **Accepted input sources:** complete `RequiredSemanticFactResolutionInput`.
- **Gating conditions:** same helper sequence as production path but only for diagnostics.
- **Event/family checks:** inherited from `BuildMeteorActivityFromRequest`.
- **ContentStrategy checks:** inherited from `BuildMeteorActivityFromRequest`.
- **Shower identity source:** inherited from derived activity's `ShowerName` for catalog record reporting.
- **Catalog lookup key:** `MeteorShowerKnowledgeCatalogV1.Normalize(activity?.ShowerName)`.
- **Event-window/radiant/peak/ZHR/parent source:** reported from the derived or extracted activity.
- **Provenance:** not surfaced directly.
- **Null/fallback behavior:** reports boolean presence flags; does not alter resolution.
- **Divergence:** duplicates production extraction logic for a diagnostics-only snapshot.

### M5/M9/M10: Context, adapter, and projection

- **Context population:** `CreateAdapterContext` is the semantic-result-affecting owner of typed `ProductionEventIntelligenceSourceV1.MeteorActivity` population.
- **Adapter extraction:** `MeteorActivitySourceAdapterV1.TryExtract` only reads typed context; it does not normalize raw JSON or requests.
- **Projection:** `LegacyRequiredSemanticFactCompatibilityMapper.Map` projects existing `MeteorActivityValue` to legacy facts; it does not populate missing MeteorActivity.
- **Divergence:** population and projection are separate responsibilities, but missing population prevents adapter candidate emission and therefore projection.

## 4. Ownership Recommendation

| Responsibility | Recommended owner | Reason | Current owner | Migration risk | Move in Milestone 2? | Remain temporarily? |
|---|---|---|---|---|---|---|
| MeteorActivity request normalization | Shared MeteorActivity Normalizer | One place should apply request/event/family/source rules before semantic context population | `NarrationGeneratorV5.BuildMeteorActivityFromRequest` duplicates | High: changes could alter production failure behavior | Yes, with characterization tests | Yes until Milestone 2 |
| ProductionEventIntelligence extraction | Phase 2 Production Event Intelligence | Raw JSON-to-typed extraction should happen before Phase 7 when possible | `NarrationGeneratorV5.ReadMeteorActivity` | Medium: schema assumptions are narrow | Yes | Yes |
| Meteor shower catalog lookup | Shared MeteorActivity Normalizer | Catalog lookup feeds derived typed value and should be central to normalization | `BuildMeteorActivityFromRequest` plus catalog | Medium | Yes | Yes |
| Adapter-context construction | Semantic Adapter Context Factory | Context factory should assemble typed sources without owning family-specific normalization rules | `NarrationGeneratorV5.CreateAdapterContext` | Medium | Yes | Yes |
| Semantic adapter extraction | Semantic Source Adapter | Adapter should remain a typed source extractor, not a raw normalizer | `MeteorActivitySourceAdapterV1` | Low | No, keep there | Yes |
| Compatibility projection | Compatibility Projection | Legacy field projection belongs at compatibility boundary | `LegacyRequiredSemanticFactCompatibilityMapper` | Medium | No unless mapper architecture changes | Yes |
| Diagnostics | Diagnostics | Presence, lifecycle, adapter, resolution, projection, and beat diagnostics are observation paths | `MeteorActivityLifecycleDiagnostics` and Phase 7 presence helpers | Low | Maybe only after normalizer exists | Yes |
| Family execution requirements | Execution Contract | Required/optional family facts should be explicit contract data | Family profiles and resolver requirement enumeration | Medium | Yes, if Milestone 2 covers execution contracts | Yes |
| Tests | Tests | Fixtures should remain in tests but should follow public/intentional builders once introduced | Inline xUnit construction | Low | No production migration | Yes |

## 5. Proposed Future Flow

Recommendation only; this flow is **not implemented** in Milestone 1.5:

```text
Production request / Phase 2 intelligence
    ↓
Shared MeteorActivity normalizer
    ↓
Typed MeteorActivityValue
    ↓
SemanticSourceAdapterContextV1
    ↓
MeteorActivity source adapter
    ↓
SemanticResolutionEngineV1
    ↓
Compatibility projection
    ↓
Resolver beats
```

## 6. Known Production Divergence

The condition `ContentStrategy == "MeteorShower"` exists in production helper logic.

| File | Method | Whether it blocks LocalViewingGuide | Affects runtime context population | Duplicated | Changed in Milestone 1.5 |
|---|---|---|---|---|---|
| `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | first `BuildMeteorActivityFromRequest` | Yes for diagnostics-derived presence | No; diagnostics-only presence path | Yes | No |
| `Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs` | second `BuildMeteorActivityFromRequest` | Yes | Yes; used by `CreateAdapterContext` | Yes | No |

Milestone 1.5 did not modify this condition.
