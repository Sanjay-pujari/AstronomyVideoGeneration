# O2.ORCH.4 Task 3A — Documentary Intent Planner Hardening Audit

## Scope and frozen boundaries

Task 3A hardens the single, pure `DocumentaryIntentPlanner`; it does not introduce filesystem, AI, publication, or cross-variant allocation. `DocumentaryBlueprintBuilder`, `ProductionPipelineExecutionService`, and Phase 1–3 production code are unchanged. The only composition-root change registers the intent planner.

## Contract corrections

The intent contract now carries structured supporting coverage and structured deferrals. A deferral records question, variant, evidence status, stable reason and permission codes, optional consolidation target, and notes code. The planning result exposes Long and Short question and knowledge summaries separately plus aggregate coverage. Slots explicitly own visual intent and resolved editorial-outcome value, distinct from the outcome template code.

## Allocation and evidence certification

Candidate selection has no permissive fallback: variant, allowed category, editorial permission, reuse, and required-knowledge rules remain mandatory. Every Phase 3 reference claimed as `Resolved` is checked against a unique certified authority with an allowed artifact, source pointer, and semantic checksum; missing claims return `DI_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID`. Evidence status also requires Phase 3 `Resolved`/`Certified` metadata, certified references, no grounding warnings, and category grounding. Absence of editorial attention alone never establishes grounding.

Supporting selection is disabled unless its slot explicitly permits consolidation. When enabled it repeats eligibility and evidence checks, sorts deterministically, and emits a coverage record. Required High coverage is evaluated per variant and accepts only actual structured coverage or a stable profile-authorized deferral.

## Duration certification

The allocator reserves every minimum, distributes the remainder by normalized positive weights, floors shares, assigns fractional remainders by fraction then slot order and ID, enforces maximums, and deterministically repeats redistribution after caps. Results are keyed by `SlotId`; no order-to-array indexing occurs. Invalid bounds and impossible budgets return `DI_DURATION_ALLOCATION_FAILED`/typed profile issues.

## Input and failure certification

Pre-allocation validation covers non-empty and agreeing identities, checksums, language, question/objective/reference uniqueness and reconciliation, required objectives, question-plan reconciliation, certified authority shape, variant/profile identity, null collections, unique and contiguous slots, exactly one last terminal slot, and count/duration/weight bounds. Expected authority and allocation errors become `DocumentaryPlanningIssue` results; programmer defects are not broadly swallowed.

## Determinism

IDs and checksums remain SHA-256 based and contain no time, random, environment, or filesystem input. Ordering uses ordinal stable keys. Long and Short allocation still starts independently from the normalized request.

## Verification status

The focused test suite is intended to cover fallback policy, evidence/reference certification, required knowledge, supporting coverage records, High coverage/deferrals, profile-owned visuals/outcomes, weighted duration reconciliation, typed malformed-input failures, independent summaries, and repeat equality. In this execution container the .NET SDK is unavailable, so compilation and Orion Gold execution could not be run locally; this is an environment limitation rather than a claimed passing certification.

## Verdict

The implementation boundaries and requested hardening are complete, but runtime certification must be executed in a .NET-enabled build environment before Phase 4 Task 4 begins.

NOT_READY_FOR_PHASE_4_TASK_4
