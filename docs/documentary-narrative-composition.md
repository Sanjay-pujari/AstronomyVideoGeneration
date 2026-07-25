# Documentary Narrative Composition (O2.4)

## Architectural position

```text
Validated DocumentaryBlueprint
        ↓
Narrative Composition
        ↓
Narrative Composition Model
        ↓
Future Narration Stage
```

O2.4 structures a validated blueprint, maps every scene to one beat, groups consecutive beats into sections, and preserves knowledge references and editorial intent. It does not generate narration.

## Contracts

`DocumentaryNarrativeComposition` is the aggregate; `NarrativeCompositionMetadata` carries externally supplied provenance; `DocumentaryNarrativeSection` and `DocumentaryNarrativeBeat` are ordered immutable planning units. `DocumentaryNarrativeSectionRole` and `DocumentaryNarrativeBeatType` provide the closed inventories. `DocumentaryNarrativeCompositionRequest` supplies identity, version, metadata, blueprint, and its O2.3 result to the stateless `DocumentaryNarrativeComposer`.

## Mapping

Each scene becomes exactly one beat, in source order. Beat IDs are `{SceneId}.beat`, numbers are scene numbers, and purpose is the unchanged `SceneObjective.Summary`. Explicit scene-role switches determine beat type and section role. Consecutive equal section roles are grouped; nonconsecutive roles are not merged. Sections use continuous numbers and `{CompositionId}.section.{number}` IDs with invariant ordinal formatting. Titles and purposes are fixed by role, and duration is the checked sum of beat durations.

## Validation gate

The supplied O2.3 result must identify the blueprint and be valid. Warning-only results are accepted. The composer trusts this certification and neither constructs nor invokes the validator or duplicates its rules.

## Determinism

All identity, timestamp, version, and correlation values are supplied. Fixed mappings, grouping, ordering, IDs, titles, purposes, and duration arithmetic make equivalent inputs stable. The composer uses no clock, randomness, mutable static state, or runtime service.

## Explicit exclusions

O2.4 does not implement narration, scripts, generated prose, prompt construction, LLM integration, TTS, subtitle generation, scene rendering, automatic repair, Knowledge Selection, runtime registration, dependency injection, APIs, or persistence.
