# O2.10 Documentary Narrative Revision Convergence

O2.10 is the deterministic convergence layer above O2.6 validation, O2.7 revision,
O2.8 execution, and O2.9 single-cycle orchestration. An O2.9 result describes one
completed cycle; O2.10 retains an immutable ordered chain of those results and derives
the state and permitted next action for the overall process. The external revision
boundary remains outside this layer.

The convergence identity is
`{OriginalDraftId}.revision-convergence.{OriginalDraftVersion}`. Cycle source lineage
must exactly match the preceding target lineage (or the original draft for cycle one),
and every plan, submission, binding request, result, advance request, and convergence
metadata correlation is compared ordinally.

## Outcomes and precedence

The exact evaluation order is: clean with no unresolved items; policy-enabled
regression stop; unresolved non-passage manual work; maximum cycle count; consecutive
no-progress threshold; zero-cycle not-started; in-progress. Thus cleanliness wins at
the cycle limit, regression wins over the limit, and manual escalation wins over both
the limit and no-progress.

`NotStarted` and `InProgress` plan the next cycle. `ConvergedSuccessfully` accepts the
current draft. Regression, cycle-limit, and no-progress stops terminate the process.
Manual escalation performs manual review. `ObtainExternalRevisionSubmission` is
reserved for a future model that retains an active cycle plan and is never emitted in
version 1.0.

A no-progress cycle neither improves nor regresses and has not converged. Consecutive
no-progress resets after improvement, regression, or convergence. Regression uses the
certified O2.9 comparison evidence. Manual escalation uses unresolved O2.7 items whose
action does not require passage text; it does not infer intent from words.

All successful, limit, no-progress, regression, and escalation outcomes are terminal;
only not-started and in-progress states accept another completed O2.9 result. There is
no reopen operation. Contracts defensively copy ordered collections, use caller-owned
timestamps and metadata, and require no clock or environment input. State is neither
registered as a runtime service nor persisted or scheduled.

State construction enforces the same deterministic identity, lineage, correlation,
cycle uniqueness, no-progress, status, and next-action invariants used at the starter,
advancer, and summarizer boundaries. Current-draft continuity with the final cycle is
certified by deterministic Web JSON value equivalence, which also supports reconstructed
instances during deserialization.

`TotalRemainingFindingCount` is cumulative comparison evidence: it is the sum of every
cycle's remaining-finding count (and is zero when there are no cycles). It is independent
of `CurrentFindingCount`, which describes only the latest validation result; the two
metrics are not synonymous.

Summary construction rejects negative aggregate or history counts, requires each
per-cycle history to agree with the completed-cycle count, and requires finding history
to contain the initial count followed by one entry per cycle and to end at the current
count. The improved, regressed, and clean flags must agree with those endpoint counts.

O2.10 does not generate or revise documentary text.

O2.10 does not invoke an external editor.

O2.10 does not call an AI model.

O2.10 does not construct prompts.

O2.10 does not persist convergence state.

O2.10 does not schedule revision cycles.
