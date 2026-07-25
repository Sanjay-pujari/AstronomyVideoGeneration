# Documentary Blueprint Builder

## Architectural position

```text
Complete deterministic planning input
        ↓
Documentary Blueprint Builder
        ↓
DocumentaryBlueprint
```

The O2.2 builder is a synchronous, dependency-free mapper. It accepts editorial decisions that have already been made, creates the O2.1 contracts, and preserves every supplied semantic value and collection order. It performs no runtime discovery or orchestration.

## Input model

`DocumentaryBlueprintBuildRequest` supplies aggregate identity, subject, publication format, language, version, immutable O2.1 metadata, and ordered scene inputs. `DocumentarySceneBlueprintInput` supplies every scene field and reuses the immutable O2.1 viewer question, objective, outcome, knowledge reference, visual opportunity, and transition value objects. Separate metadata and nested input duplicates are therefore unnecessary.

Both inputs expose get-only properties. Scene, knowledge-reference, and visual-opportunity collections are copied into read-only collections at construction time.

## Builder responsibility and mapping

`DocumentaryBlueprintBuilder.Build(DocumentaryBlueprintBuildRequest)` maps aggregate and scene properties directly into a new `DocumentaryBlueprint`. The builder does not trim or rewrite strings, sort collections, add scenes, choose references, or infer editorial meaning. It has no constructor dependencies.

## Validation

The input constructors reject blank required values, null metadata, null collections or collection elements, undefined enum values, negative durations, and duplicate scene IDs or numbers. Reused O2.1 value objects and metadata retain their existing validation, including a non-default `CreatedUtc`, required text, optional-identifier rules, and the exact `1.0` schema version. `Build` rejects a null request, while construction of the returned O2.1 aggregate remains the final domain-invariant boundary.

## Determinism

All IDs, timestamps, correlation values, versions, scene decisions, and ordering are externally supplied. The builder uses no clock, random source, environment or machine state, culture, static mutable state, I/O, or service lookup. Repeated builds and separately constructed equivalent requests therefore produce equivalent values and identical JSON with the repository's `JsonSerializerDefaults.Web` configuration.

## Explicit exclusions

O2.2 does not implement Knowledge Selection, editorial planning heuristics, editorial validation, narration, narrative composition, LLM integration, prompts, runtime registration, dependency injection, APIs, or persistence.
