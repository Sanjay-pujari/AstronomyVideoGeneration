# Documentary Narrative Revision Execution Foundation (O2.8)

## Objective and architectural position

O2.8 is the deterministic, provider-neutral boundary between the certified O2.7 revision request and an external editor. It turns a draft, revision request, and externally supplied execution provenance into an immutable work package. It then validates an external submission and converts it into the existing O2.7 binding request. The existing binder remains the sole owner of final revision binding.

O2.8 does not author revision text.

O2.8 does not call an AI model.

O2.8 does not construct prompts.

O2.8 does not bind the revised draft.

There are no provider, prompt, network, storage, runtime-registration, speech, subtitle, or audio dependencies in this boundary. A future human, offline tool, batch process, service, or AI adapter may consume and produce these contracts without changing O2.8.

## Work-package construction

The stateless builder validates the draft/request identity and version with ordinal comparisons. Text-required request items are grouped by exact passage identity in first-applicable request order. Every finding for a passage stays in request order across the aligned ID, rule, severity, action, and message collections. IDs use invariant, one-based sequences and the deterministic forms `<request>.passage-work.<sequence>`, `<request>.manual-work.<sequence>`, and `<request>.work-package.v1`.

Non-text findings become individual manual-review work items and never enter passage replacement work. Clean and manual-only packages therefore do not require external passage editing. The represented item count must equal the request item count.

Each passage work item snapshots its exact original text and the immediately previous and next passages in full section-then-passage reading order. Boundary contexts are absent. Context snapshots are informational and cannot be submitted as replacement work.

## External submissions and validation

Submission provenance—including timestamp, editor type/name, schema version, and correlation—is externally supplied. Editor type describes provenance only and triggers no behavior. Passage submissions preserve text exactly, identify the source work item and passage, and resolve the complete ordered finding group. Collections are defensively copied and expose no public setters.

The assembler validates exact ordinal lineage among draft, request, package, submission, and O2.7 revision metadata. For every submitted passage it independently compares the source-draft passage text with both the work-package original text and the submitted original text using exact ordinal comparison. It rejects unknown or case-mismatched work and passage identities, stale original text (including case-only and whitespace-only changes), work/passage mismatches, manual findings, missing, reordered, duplicated, or extra finding IDs, and conflicting submission coverage.

Partial submission **across passages** is supported; partial submission **within a passage group** is rejected. An empty submission is valid for clean and manual-review-only requests. Manual findings remain unresolved for the O2.7 binder, which consequently owns `NoChangesRequired`, `PartiallyRevised`, and all other final status decisions.

## Conversion, determinism, and immutability

For each submitted passage, in submission order, the assembler creates one existing `DocumentaryNarrativePassageRevisionInput`, mapping resolved IDs, passage ID, original text, and revised text without normalization. It does not invoke or duplicate binder logic and does not create a revised draft.

No clock, randomness, culture-sensitive formatting, environment setting, mutable static state, or service affects output. All timestamps and identities are inputs; all collections preserve order and are defensively copied. Equivalent reconstructed values therefore serialize identically with `JsonSerializerOptions(JsonSerializerDefaults.Web)`.

## Lineage and future extension boundary

Work-package identity ties the external edit to one revision request, draft ID, and draft version. Submission identity ties the response back to that exact package. O2.7 revision metadata independently confirms source draft ID and version. Future editor adapters belong outside this foundation: they may translate a work package for their editor and return a submission, but may not introduce provider behavior into these contracts, the builder, or the assembler.
