# Documentary Narrative Revision Domain (O2.7)

## Architectural Position

```text
Draft Validation
        ↓
Revision Request
        ↓
External Editor / Future Provider
        ↓
Revision Binding
        ↓
Revised Draft
```

## Responsibility and Contracts

O2.7 converts ordered quality findings into immutable revision items, accepts externally supplied final passage text, binds it deterministically, preserves other content, and reports lineage and unresolved work. Request metadata, revision items and requests describe work; grouped passage inputs and revision metadata describe an edit; binding requests, grouped changes, and results describe its outcome. `DocumentaryNarrativeRevisionRequestBuilder` performs translation and `DocumentaryNarrativeRevisionBinder` performs validation and binding. Actions describe the ten approved kinds of work; statuses are `NoChangesRequired`, `Revised`, `PartiallyRevised`, and `Rejected` (structural failures throw rather than emitting the last status).

## Finding Mapping

| Rules | Action |
|---|---|
| 001, 002 | `ReviewDraftStructure` |
| 003, 004 | `CorrectPassageNumber` |
| 005, 006, 007 | `CorrectSourceIdentity` |
| 008, 009, 010 | `RevisePassageText` |
| 011 | `RevisePassageOpening` |
| 012 | `AddTerminalPunctuation` |
| 013 | `DifferentiatePassageText` |
| 014 | `DifferentiatePassageTitle` |
| 015, 016 | `CorrectPassageType` |
| 017, 018 | `ReviewDuration` |

All codes use the `DND-QUALITY-` prefix. Unknown codes are rejected by an exhaustive switch.

## Multiple Findings Per Passage

One grouped input contains the ordered identities of **all** text-required items applicable to one passage and one final replacement. Duplicate passages and duplicate item identities are rejected. A grouped input creates one change; no sequential transformations occur.

## Structural Gate

The binder matches item and passage identities ordinally, permits text-required items only, requires the grouped identity list to equal applicable request order, requires exactly one draft passage, and compares original text exactly. Valid partial coverage is applied; omitted text items and every manual-review item remain unresolved in request order.

## Revised Draft, Lineage, and Determinism

The target ID is `{SourceDraftId}.revision.{TargetDraftVersion}`. Only target draft ID, version, and supplied passage text change. Aggregate metadata, section structure, and every other passage property are copied exactly. Source/target IDs and versions plus request identity remain in the result. Metadata is supplied; IDs use invariant formatting; comparisons are ordinal; request, change, unresolved, section, passage, and nested collection order is stable. No clock, randomness, trimming, rewriting, or generated text is used. A clean request returns the original draft instance and identity.

## Explicit Exclusions

O2.7 implements no text authoring, automatic rewriting or acceptance, grammar or spelling correction, LLM invocation, prompts, model providers, TTS, SSML, subtitles, audio, runtime registration, dependency injection, API, or persistence behavior.
