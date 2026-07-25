# Documentary narrative revision-cycle orchestration (O2.9)

## Objective and architectural position

O2.9 is the deterministic orchestration layer above O2.6 draft validation, O2.7 revision-domain operations, and O2.8 revision-execution contracts. It coordinates one cycle while leaving all editorial work outside the process. It neither changes nor reimplements the rules owned by those upstream layers.

## Planning lifecycle and identity

The planner validates source lineage and the single correlation chain, delegates finding-to-item translation to `DocumentaryNarrativeRevisionRequestBuilder`, and delegates work grouping to `DocumentaryNarrativeRevisionWorkPackageBuilder`. It retains the exact source draft and validation result in the plan. The stable cycle identity is:

```text
{SourceDraftId}.revision-cycle.{SourceDraftVersion}.{RevisionRequestId}
```

It contains no time, random, culture, or environment component. A clean source produces `NoRevisionRequired`; any request items—including manual-only work—produce `AwaitingExternalRevision`. `RequiresExternalRevision` therefore means that the request contains at least one item, rather than merely a passage-text item.

## External boundary and completion lifecycle

An external human or future adapter supplies an O2.8 submission and O2.7 revision metadata. O2.9 does not create revised prose. The completer validates package, request, draft, version, revision metadata, and correlation lineage. It delegates submission conversion to `DocumentaryNarrativeRevisionSubmissionAssembler`, binding to `DocumentaryNarrativeRevisionBinder`, and revised-draft inspection to `DocumentaryNarrativeDraftValidator`.

Completion always compares the new validation result, including during partial completion. Unresolved O2.7 items take precedence and produce `PartiallyCompleted`. With none unresolved, remaining O2.6 findings produce `CompletedWithRemainingFindings`, while a clean revised draft produces `CompletedSuccessfully`. A clean empty cycle remains `NoRevisionRequired`, retains its source identity and version, and has no applied changes. `AwaitingExternalRevision` is never emitted by completion.

## Validation comparison and finding identity

Findings are matched as an ordered multiset, not as rule-code sets. Identity uses the rule code, draft-level-versus-nested scope, section ID and number, passage ID and number, field, message, and severity, with ordinal string semantics. Counts allow duplicate identities, while resolved and remaining summaries preserve source order and introduced summaries preserve revised order. Draft identity itself changes upon binding and is deliberately represented by its scope category, so the same scoped issue can remain across source and target drafts.

Improvement requires a lower total and no introduced finding. Regression means a greater total or any introduced finding; consequently an equal-count replacement is a regression, not an improvement. A zero revised count is clean.

Comparison construction enforces both multiset decompositions: resolved plus remaining equals the source count, and remaining plus introduced equals the revised count. The improvement, regression, and clean flags must exactly equal the values derived from those counts, so callers cannot construct a contradictory comparison.

## Determinism, immutability, and correlation

All timestamps and provenance are caller supplied. Contracts expose no public setters, and direct summary collections are defensively copied in order. Operations are sealed, synchronous, parameterless, stateless, and instantiate only the certified deterministic upstream operations. Equivalent inputs consequently preserve cycle IDs, ordering, lineage, and Web JSON.

O2.9 uses one correlation chain. Exact ordinal equality is required across cycle metadata, request metadata, execution/work-package metadata, submission metadata, revision metadata, and completion input. A mismatch is a structural error and throws; it is never ignored.

Plans enforce their correlation chain at construction, including direct or deserialized construction. Completed results likewise enforce exact submission, binding, revision-result, validation, and comparison lineage; their supplied status must equal the status derived from the plan, unresolved-item count, and revised finding count.

## Responsibility and future-extension boundary

O2.9 does not invoke an external editor.

O2.9 does not call an AI model.

O2.9 does not construct prompts.

O2.9 does not persist revision cycles.

O2.9 does not schedule revision cycles.

It also performs no provider selection, retrying, networking, API exposure, runtime registration, dependency injection, TTS, SSML, subtitle, or audio work. Future adapters may operate beyond the external-submission boundary, but O2.9 remains deterministic coordination only. O2.10 is outside this implementation.
