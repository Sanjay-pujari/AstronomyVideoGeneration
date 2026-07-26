# O2.11 Documentary narrative acceptance and release candidate

O2.11 is positioned immediately after convergence. Convergence describes how revision ended; acceptance separately determines whether its current draft is eligible for the immutable final narrative-domain handoff. Schema 1.0 permits automatic acceptance only for successful convergence with clean validation and no unresolved revision items.

Terminal cycle-limit, no-progress, regression, and manual-escalation outcomes may be held when the explicit policy permits manual consideration; otherwise they are rejected. Evaluation precedence is nonterminal, successful clean convergence, manual escalation, cycle limit, no progress, then regression. Supporting evidence is ordered as validation findings, unresolved items, cycle limit, no progress, regression, manual review, and policy rejection, without repeating the primary reason.

Status, primary reason, supporting reasons, and numeric evidence form one invariant. Accepted decisions are exclusively `ConvergedAndClean`, with zero findings, zero unresolved items, and no adverse supporting reasons. Held and rejected decisions accept only the terminal or policy reasons defined for their status; contradictory supporting evidence is rejected at construction time.

The accepted identity is `{DraftId}.narrative-release-candidate.{DraftVersion}`. The builder verifies value-equivalent current draft content; byte-deterministic Web JSON equivalence of the ordered final validation evidence; exact decision/convergence draft, version, finding, cycle, and latest-unresolved counts; original/current/convergence lineage; and exact ordinal correlation across convergence, acceptance, and explicitly supplied release metadata.

The summary retains cycle order and cumulative change, resolved-finding, and introduced-finding evidence. Its cycle history has exactly one entry per completed cycle, its finding history has the initial entry plus one per cycle, and its last finding count is the zero final count. Schema 1.0 summaries are necessarily clean and fully resolved. Contracts defensively copy histories and expose no public setters.

The release candidate is an immutable downstream handoff only. O2.11 has no production, persistence, approval-workflow, or media responsibility.

O2.11 does not generate or revise documentary text.

O2.11 does not invoke an external editor.

O2.11 does not call an AI model.

O2.11 does not construct prompts.

O2.11 does not publish narrative content.

O2.11 does not generate scenes, audio, subtitles, images, or video.

O2.11 does not persist acceptance results.

O2.11 does not schedule acceptance workflows.
