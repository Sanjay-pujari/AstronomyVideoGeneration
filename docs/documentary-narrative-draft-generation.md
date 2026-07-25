# Documentary Narrative Draft Generation Foundation (O2.5)

## Architectural position

```text
Certified Narrative Composition + Externally supplied narrative text
        ↓
Narrative Draft Assembler
        ↓
Immutable Narrative Draft
        ↓
Future narration validation and TTS
```

## Responsibility

O2.5 binds externally supplied text to composition beats, preserves the certified composition structure, emits passages in deterministic composition order, and packages them as an immutable draft. It does not author, normalize, or rewrite text.

## Contracts

`DocumentaryNarrativeDraft` is the aggregate; `DocumentaryNarrativeDraftMetadata` carries caller-supplied provenance; `DocumentaryNarrativeDraftSection` and `DocumentaryNarrativePassage` preserve composition structure; `DocumentaryNarrativePassageInput` contains only a source beat ID and text; `DocumentaryNarrativeDraftRequest` supplies the draft identity, version, metadata, composition, and inputs. `DocumentaryNarrativeDraftAssembler` is a parameterless, synchronous, stateless assembler. `DocumentaryNarrativePassageType` is the fixed narrative-function inventory.

## Mapping

Each composition beat produces exactly one passage. Passage IDs are `{SourceBeatId}.passage`; section IDs are `{DraftId}.section.{SectionNumber}` using invariant formatting. The explicit beat-type mapping converts Hook to Opening and Closure to Closing while preserving like-for-like intermediate functions. All semantic fields, duration values, nested references, visual opportunities, and externally supplied text are preserved exactly.

## Structural gate

Ordinal source-beat matching requires exactly one input per composition beat. Duplicate inputs are rejected by the request; missing, unknown, and extra inputs are rejected by the assembler. Input order is irrelevant: section and passage output always follows composition order.

## Determinism

IDs, versions, timestamps, provenance, and correlations are supplied externally. Matching is ordinal, mappings are fixed, ID formatting is invariant, and ordering comes exclusively from the composition. The assembler uses no clock, randomness, environment state, or mutable static state.

## Explicit exclusions

O2.5 implements no LLM invocation, prompt construction, language-model provider abstraction, retries, automatic rewriting, TTS, SSML, audio, subtitles, runtime registration, dependency injection, APIs, or persistence.
