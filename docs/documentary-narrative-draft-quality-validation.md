# Documentary Narrative Draft Quality Validation (O2.6)

## Architectural position

```text
Narrative Draft
        ↓
Draft Quality Validation
        ↓
Validation Result
        ↓
Future revision or narration stage
```

O2.6 reads an immutable draft, evaluates deterministic structural and text-quality rules, and reports findings. It never rewrites text and does not use an LLM.

## Result model

`DocumentaryNarrativeDraftValidationResult` identifies the draft and exposes a defensively copied, read-only ordered finding list. Validity and error/warning counts are derived: warnings allow a draft to proceed, while any error invalidates it. Each immutable finding has a stable rule code, fixed severity, deterministic message, draft ID, and optional section, passage, and field scope. Findings are emitted by numeric rule order; section findings use section number then ordinal ID, passage findings use draft order, and duplicate groups use ordinal keys.

## Rule inventory

| Code | Severity | Purpose | Scope |
|---|---|---|---|
| DND-QUALITY-001 | Error | Require a section | Draft |
| DND-QUALITY-002 | Error | Require a passage in every section | Section |
| DND-QUALITY-003 | Error | Require positive passage numbers | Passage/PassageNumber |
| DND-QUALITY-004 | Error | Match passage and source-beat numbers | Passage/PassageNumber |
| DND-QUALITY-005 | Error | Require ordinally unique passage IDs | Draft duplicate group |
| DND-QUALITY-006 | Error | Require ordinally unique source-beat IDs | Draft duplicate group |
| DND-QUALITY-007 | Error | Require source-scene IDs | Passage/SourceSceneId |
| DND-QUALITY-008 | Error | Require at least three words | Passage/Text |
| DND-QUALITY-009 | Warning | Recommend at least eight words | Passage/Text |
| DND-QUALITY-010 | Error | Limit text to 120 words | Passage/Text |
| DND-QUALITY-011 | Warning | Recommend uppercase opening letter | Opening passage/Text |
| DND-QUALITY-012 | Warning | Recommend terminal `.`, `?`, or `!` | Passage/Text |
| DND-QUALITY-013 | Error | Reject exact text repeated across beats | Draft duplicate group |
| DND-QUALITY-014 | Warning | Reject identical consecutive titles | Later passage/Title |
| DND-QUALITY-015 | Error | Require first passage to use Opening | First passage/PassageType |
| DND-QUALITY-016 | Error | Require last passage to use Closing | Last passage/PassageType |
| DND-QUALITY-017 | Error | Require positive total section duration | Draft |
| DND-QUALITY-018 | Warning | Recommend positive passage duration | Passage/EstimatedDurationSeconds |

## Word counting

A word is one contiguous sequence of non-whitespace characters. The counter recognizes Unicode whitespace, ignores empty regions caused by repeated, leading, or trailing whitespace, and performs no punctuation removal, stemming, case normalization, culture-sensitive operation, or NLP processing.

## Explicit exclusions

O2.6 provides no rewriting, grammar or spelling correction, factual or scientific validation, LLM invocation, prompts, TTS, SSML, subtitles, audio, runtime registration, dependency injection, APIs, or persistence.
