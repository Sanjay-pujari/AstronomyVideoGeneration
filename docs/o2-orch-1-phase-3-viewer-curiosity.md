# O2.ORCH.1 — Phase 3 Viewer Curiosity

## Implementation summary

The existing RC2 endpoint, batch orchestrator, 20-phase production runner, and
`AstronomyQuestionEngine` remain in place. Phase 3 calls the question engine once and
deterministically projects its returned `QuestionAnswerSetDto` plus Phase 2
`ProductionEventIntelligence` into the Viewer Curiosity contract. No prompt or provider
request was added.

## Repository audit

| Concern | Existing file/type | Existing method | Minimal change |
| --- | --- | --- | --- |
| Endpoint/controller | `ContentPlanningRc2Controller` | `BatchGenerateFromPlans` | None |
| Batch orchestration | `Rc2ContentPlanningBatchOrchestrator` | `GenerateFromPlansAsync` | None |
| Production runner | `ProductionPipelineExecutionService` | `RunAsync` | Phase 3 projection and artifact persistence |
| Phase registration | `ProductionPipelineExecutionService` | `PhaseDefinitions` | Phase number remains 3 |
| Question provider | `IQuestionEngine` / `AstronomyQuestionEngine` | `GenerateQuestionAnswersAsync` | None |
| Phase 3 | `ProductionPipelineExecutionService` | `PhaseGenerateQuestionsAsync` | Project, validate, and write four canonical files |
| Production composition | `ServiceCollectionExtensions.AddMediaFactory` | `TryAddSingleton<IViewerCuriosityArtifactProjector, ViewerCuriosityArtifactProjector>` | Stateless projector is mandatory in the runner constructor; there is no runner fallback |
| Serialization/writer | Pipeline `JsonOptions` and phase writers | `PhaseGenerateQuestionsAsync` / `WritePhaseManifestAsync` | Complete set is written to staging and installed by directory rename; manifest roles added |
| Checksum | SHA-256 content identity used throughout production | `ViewerCuriosityChecksum` | Canonical payload-only SHA-256 for new artifacts |
| Validation | Phase execution validation | `ViewerCuriosityArtifactValidator.Validate` | Validate metadata, identity, references, counts, and checksums before success |
| Resume/state | `phase-XX-validation.json` | `PreviousPhaseSucceeded` | Existing phase-state mechanism retained |
| Overwrite | Pipeline cleanup | `ClearPhaseRangeOutputsForOverwrite` | Delete Phase 3-owned files and invalidate downstream validation state |

## Canonical Phase 3 artifacts

The established `question-engine/question-answer-set.json` path remains available to
legacy consumers. Phase 3 additionally owns `03-questions/`:

| Artifact | Role |
| --- | --- |
| `viewer-question-bank.json` | Authoritative |
| `question-answer-set.json` | Compatibility copy |
| `learning-objectives.json` | Supporting |
| `question-plan.json` | Supporting |

The new artifacts carry execution ID (the stable plan execution ID), language, profile,
schema version `1.1`, UTC creation time, and a payload checksum. IDs and checksums are
deterministic; creation time is deliberately excluded from checksums. The phase manifest
records each path and role.

## Grounding, identity, and variant scope

Each typed `ViewerKnowledgeReference` identifies a concrete JSON field in the Phase 2
`plan-input/production-event-intelligence.json` artifact. Its reference type is
`ProductionIntelligenceField`, its source artifact is explicit, and accepted references
have `Resolved` status. Recognition, timing/location, observing, and scientific questions
select only corresponding non-empty Phase 2 fields. Scientific and cultural/historical
categories are rejected by validation without a resolved reference; unsupported source
types map deliberately to `Other`, produce a warning, and are listed for editorial
attention rather than being mislabeled as comparisons.

Applicable variants come only from explicit `RequestedOutputs` (`long`/`longVideo` and
`short`/`shortVideo`), not profile-name substring inference. Question identity includes
normalized profile, language, question text, category, sorted typed references, and sorted
variant scope. Provider display order is excluded from identity, retained as
`SourceDisplayOrder`, and canonical `Order` is normalized to `1..N` after deduplication.
Priority uses category importance first and source display order as its deterministic
fallback because the current Question Engine DTO has no source-priority field.

Checksums serialize payloads without creation timestamps or checksum fields and key-sort
every plan dictionary. Semantically unordered identity inputs are sorted.

## Invocation

Replace the base URL and plan ID; no secret is shown. The request fields are from
`BatchGenerateFromPlansRequest`.

Initial Phases 1–3:

```bash
curl -X POST http://localhost:5000/api/content-planning/rc2/batch-generate-from-plans \
  -H 'Content-Type: application/json' \
  -d '{"year":2026,"regionId":"US","language":"en","planId":"<PLAN_ID>","useProductionPipeline":true,"dryRun":false,"startPhaseNo":1,"endPhaseNo":3,"overwriteExisting":false,"retryFailedOnly":false}'
```

Phase 3 resume, overwrite, and failed-only retry respectively use the same body with:

```json
{"startPhaseNo":3,"endPhaseNo":3,"overwriteExisting":false,"retryFailedOnly":false}
{"startPhaseNo":3,"endPhaseNo":3,"overwriteExisting":true,"retryFailedOnly":false}
{"startPhaseNo":3,"endPhaseNo":3,"overwriteExisting":false,"retryFailedOnly":true}
```

PowerShell example (change the three phase flags as above):

```powershell
$body = @{
  year = 2026; regionId = 'US'; language = 'en'; planId = '<PLAN_ID>'
  useProductionPipeline = $true; dryRun = $false
  startPhaseNo = 1; endPhaseNo = 3
  overwriteExisting = $false; retryFailedOnly = $false
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri 'http://localhost:5000/api/content-planning/rc2/batch-generate-from-plans' -ContentType 'application/json' -Body $body
```

The response's `outputRoot` is the generated workspace. Inspect it with:

```bash
find '<OUTPUT_ROOT>' -maxdepth 2 -type f | sort
cat '<OUTPUT_ROOT>/phase-manifest.json'
cat '<OUTPUT_ROOT>/validation/phase-03-validation.json'
```

There is no separate execution-status endpoint on this controller; the synchronous
response, manifest, and phase validation files are the current status contract.

## Compatibility and recovery

Dry-run still avoids all provider calls and authoritative writes. A failed-only retry
skips Phase 3 only when its previous validation says `Succeeded` and required outputs
are valid. Resume deserializes the compatibility DTO and checks event identity, language,
collection/count reconciliation, supporting checksums, and the authoritative bank. It
also parses `phase-manifest.json`, checks plan identity, all required paths and roles,
workspace containment, uniqueness, and exactly one Phase 3 authority. Missing, malformed,
unrelated, or role-inconsistent state is not reused. Overwrite removes Phase 3-owned outputs and downstream Phase 4–20 validation
records while preserving Phase 1–2. Legacy consumers continue to read the unchanged
`question-engine/question-answer-set.json`.

Current Phase 7 code has pre-existing reads of the legacy question artifact in its
semantic-context preparation. O2.ORCH.1 adds no new Phase 3-to-Phase 7 dependency and
does not migrate Phase 7.

## Inspection checklist

Confirm Phase 1 `plan-input/content-plan-production-request.json`, Phase 2
`plan-input/production-event-intelligence.json`, all four Phase 3 files, the phase
manifest, and `validation/phase-03-validation.json`. Verify one language/profile,
resolved Phase 2 field knowledge references, nonduplicate questions, objective references,
reconciled plan counts, and checksums. With `endPhaseNo=3`, no blueprint, narration,
media, or publishing output should be generated.

Focused tests cover type mapping, stable semantic IDs across source reordering, typed
knowledge references, dictionary-order-independent and round-trip checksums, creation-time
exclusion, and production DI registration. Existing pipeline tests cover overwrite cleanup
and the public orchestration contract.

## Deferred O2.ORCH.2 work

`DocumentaryBlueprintIntegrationService`, Phase 4 blueprint consumption, and Phase 5
certification are intentionally deferred. O2.ORCH.2 may consume the authoritative bank
and supporting objectives/plan without changing the compatibility artifact.
