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
| Serialization/writer | Pipeline `JsonOptions` and phase writers | `WritePhaseManifestAsync` | Atomic JSON helper for the four related writes; manifest roles added |
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
schema version `1.0`, UTC creation time, and a payload checksum. IDs and checksums are
deterministic; creation time is deliberately excluded from checksums. The phase manifest
records each path and role.

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
are valid; partial or corrupt authority must be regenerated under pipeline retry
semantics. Overwrite removes Phase 3-owned outputs and downstream Phase 4–20 validation
records while preserving Phase 1–2. Legacy consumers continue to read the unchanged
`question-engine/question-answer-set.json`.

Current Phase 7 code has pre-existing reads of the legacy question artifact in its
semantic-context preparation. O2.ORCH.1 adds no new Phase 3-to-Phase 7 dependency and
does not migrate Phase 7.

## Inspection checklist

Confirm Phase 1 `plan-input/content-plan-production-request.json`, Phase 2
`plan-input/production-event-intelligence.json`, all four Phase 3 files, the phase
manifest, and `validation/phase-03-validation.json`. Verify one language/profile,
resolved event knowledge references, nonduplicate questions, objective references,
reconciled plan counts, and checksums. With `endPhaseNo=3`, no blueprint, narration,
media, or publishing output should be generated.

## Deferred O2.ORCH.2 work

`DocumentaryBlueprintIntegrationService`, Phase 4 blueprint consumption, and Phase 5
certification are intentionally deferred. O2.ORCH.2 may consume the authoritative bank
and supporting objectives/plan without changing the compatibility artifact.
