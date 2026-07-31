# RC2 certified Phase 1–4 API

## Request

The synchronous production route is `POST /api/content-planning/rc2/batch-generate-from-plans`.
No separate run-status endpoint is required because the request returns terminal execution status.

```json
{
  "year": 2026,
  "regionId": "US",
  "language": "en",
  "maxPlans": 1,
  "dryRun": false,
  "useProductionPipeline": true,
  "startPhaseNo": 1,
  "endPhaseNo": 4,
  "planId": "<ORION_GOLD_PLAN_UUID>",
  "executionMode": "Normal"
}
```

## Success response

HTTP 200 returns the existing batch response plus `rc2CertifiedExecution`. That object reports the
execution ID, terminal Phase 1–4 statuses, the certified Phase 4 integration-service identity,
physical and committed-state flags, aggregate identity/checksum, Long/Short scene and duration
totals, validation and commit status, idempotency status, and relative artifact paths.

Phase 4 creates `04-blueprint/documentary-blueprint.json`, the Long and Short projections,
`knowledge-selection.json`, both scene indexes, and `blueprint-build-report.json`. The execution
root also contains `phase-manifest.json` and `phase-04-validation.json`. A Phase 4 resume returns
`alreadyPublished: true` when the identical authority was already committed. The former
`editorial/story-graph.json` Phase 4 authority is not produced.
