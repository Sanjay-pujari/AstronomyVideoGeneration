# Documentary Blueprint Domain

## Architectural position

```text
Knowledge
    ↓
Documentary Blueprint
    ↓
Narration
```

The blueprint is the deterministic contract between knowledge and later narration. It references knowledge entries rather than duplicating facts, and describes scene intent and editorial structure. It contains neither narration nor LLM prompts. It is not a runtime service, persistence model, Blueprint Builder, or Editorial Validator.

## Contracts

- `DocumentaryBlueprint` is the aggregate and retains identity, subject, publication, language, version, metadata, and ordered scenes.
- `DocumentarySceneBlueprint` captures an ordered scene's narrative stage, editorial role and priority, question, objective, outcome, references, visual opportunities, transition intent, and estimated duration.
- `ViewerQuestion` holds the one question explored by a scene.
- `SceneObjective` holds summary, learning, curiosity, and emotional goals.
- `EditorialOutcome` holds viewer takeaway, narrative contribution, and future-analysis outcome flags.
- `KnowledgeReference` identifies an existing entry, section, purpose, and primary status; it never embeds knowledge content.
- `SceneTransition` holds transition intent, next-question seed, and editorial direction—not finished prose.
- `VisualOpportunity` holds visual guidance and optional knowledge/asset references—not image-generation instructions.
- `DocumentaryBlueprintMetadata` holds externally supplied creation, author, model, knowledge, schema, and correlation values. `CreatedUtc` must be non-default and is preserved exactly; the schema version is `1.0`.

The enum inventories are intentionally closed: `DocumentaryNarrativeStage` describes the narrative progression; `DocumentarySceneRole` the scene function; `EditorialPriority` its importance; and `BlueprintPublicationFormat` its target format.

## Invariants and immutability

Required identifiers and text cannot be blank. Required objects and collections cannot be null, collections cannot contain nulls, durations cannot be negative, and scene IDs and scene numbers are unique within an aggregate. Enum values must be defined.

Contracts expose get-only state. Every supplied collection is copied into a read-only collection, preserving caller order and preventing mutation both through the original collection and the exposed property. Nested values are immutable.

## Determinism and serialization

IDs, timestamps, versions, correlation IDs, numbers, and ordering are supplied by callers. Default or ambient timestamps are never generated. Contracts use no clock, random source, current culture, environment state, or static mutable state. Tests reuse the repository's established `System.Text.Json` web defaults (`JsonSerializerDefaults.Web`), which reconstruct contracts through their public constructors. Round trips preserve every approved value and caller-provided scene, knowledge-reference, and visual-opportunity order; equivalent inputs serialize identically.

## Explicit exclusions

This domain adds no knowledge selection, builder, validation service, narrative composition, narration, LLM integration, prompts, runtime registration, dependency injection, API, or persistence behavior. Later stages may consume the contracts, but their implementation is outside this scope.
