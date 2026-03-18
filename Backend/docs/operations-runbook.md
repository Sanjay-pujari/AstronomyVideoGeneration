# Operations Runbook

## Purpose
This runbook covers operator-safe recovery and maintenance actions for the Astronomy Media Factory backend.

## Health checks before and after recovery
- API liveness: `GET /health/live`
- API readiness: `GET /health/ready`
- Ops summary: `GET /api/ops/summary`
- Queue status: `GET /api/ops/jobs/summary`
- Recent failures: `GET /api/ops/failures/recent`

Validate the following after any recovery:
1. The target run is no longer `Running` unexpectedly.
2. No duplicate main-video jobs are queued for the same date/content type.
3. `published_videos`, `media_assets`, and `recovery_operations` reflect the action you took.
4. Logs contain a recovery operation id and completion or failure entry.

## Replay a failed pipeline run
Use this when the full run failed before completion.

```http
POST /api/ops/runs/{runId}/replay
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Replay after transient OpenAI outage.",
  "allowReplayOfSucceededRun": false,
  "publishToYouTubeOverride": true,
  "useTopicPlannerOverride": false
}
```

Safety notes:
- Successful runs are rejected unless `allowReplayOfSucceededRun=true`.
- Running runs are rejected.
- Replay creates a new main-video job instead of mutating the original run.

## Retry publish only
Use this when rendering succeeded but YouTube upload failed or the thumbnail upload failed.

Retry full publish using stored assets:

```http
POST /api/ops/runs/{runId}/retry-publish
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Retry after YouTube outage resolved.",
  "retryThumbnailOnly": false,
  "forceRepublish": false,
  "publishToYouTube": true
}
```

Retry thumbnail only for an already uploaded video:

```http
POST /api/ops/runs/{runId}/retry-publish
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Thumbnail upload failed previously.",
  "retryThumbnailOnly": true,
  "forceRepublish": false,
  "publishToYouTube": true
}
```

## Retry blob archival only
Use this when Azure Blob was unavailable but the rendered files still exist locally.

```http
POST /api/ops/runs/{runId}/retry-archive
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Blob service outage resolved.",
  "force": true
}
```

## Regenerate shorts from a completed parent video
Use this when the main run succeeded but shorts need a clean rerender.

```http
POST /api/ops/runs/{runId}/regenerate-shorts
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Shorts hook underperformed.",
  "publishToYouTube": false,
  "force": false
}
```

## Rerun metadata optimization
Use this when titles, descriptions, or tags performed poorly and you want updated metadata before a republish.

```http
POST /api/ops/runs/{runId}/rerun-metadata
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Optimize metadata using latest heuristics.",
  "applyToPublishedVideo": true
}
```

Recommended sequence for poor metadata:
1. Run metadata optimization.
2. Review the updated script/published record.
3. Trigger `retry-publish` if a new upload is desired.

## Recover stale or stuck jobs
Use this when jobs remain in `Running`, `Pending`, or `Retrying` longer than expected.

```http
POST /api/ops/jobs/recover-stale
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Worker pod restarted unexpectedly.",
  "thresholdMinutes": 90,
  "requeueRecoveredJobs": true,
  "recoverIncompleteRuns": true
}
```

Behavior:
- The service marks matching jobs as stale, records recovery notes, and optionally requeues them.
- Succeeded runs with missing publish/archive outcomes queue targeted `PublishVideo` or `ArchiveAssets` jobs.
- No successful run is replayed automatically.

## Requeue a single failed or stale job
```http
POST /api/ops/jobs/{jobId}/requeue
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Retry a single failed archive job.",
  "force": false
}
```

Use `force=true` only when requeueing a previously successful job intentionally.

## Retention and cleanup
Manual cleanup endpoint:

```http
POST /api/ops/maintenance/cleanup
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Monthly cleanup.",
  "deleteWorkingFiles": true,
  "deleteDbRecords": true,
  "deleteAnalytics": true
}
```

Default retention configuration:

```json
"Maintenance": {
  "WorkingFileRetentionDays": 14,
  "JobRetentionDays": 30,
  "StageRetentionDays": 30,
  "AnalyticsRetentionDays": 90,
  "StaleJobThresholdMinutes": 60,
  "WorkingDirectory": "./media-output"
}
```

Scheduled cleanup runs daily at 03:00 UTC in the worker.

## Secret rotation
1. Update secrets in the configured source of truth (Key Vault, environment variables, or deployment secrets).
2. Restart API and worker processes.
3. Confirm `/health/ready` is healthy.
4. Run a non-publishing pipeline test if possible.
5. If YouTube credentials changed, verify a publish retry on a non-critical run.

## Recovering after a YouTube outage
1. Pause manual publish attempts until the external outage is resolved.
2. Review failed runs from `/api/ops/failures/recent`.
3. For each rendered run with local assets intact, call `retry-publish`.
4. If the upload succeeded but thumbnail failed, call `retry-publish` with `retryThumbnailOnly=true`.
5. Validate `YouTubeVideoId` and published status after recovery.

## Recovering after a Blob outage
1. Confirm rendered video and audio files still exist in `media-output`.
2. Call `retry-archive` for affected runs.
3. Verify `media_assets.public_url` or `published_videos.blob_url` were updated.
4. If files were deleted locally, do not retry archival until assets are restored from backup.

## Failed publish recovery strategy
- If upload failed and no `YouTubeVideoId` exists, run `retry-publish`.
- If thumbnail upload failed, run `retry-publish` with `retryThumbnailOnly=true`.
- If metadata is poor, run `rerun-metadata` first and then `retry-publish`.

## Audit trail
Every recovery and cleanup action writes a `recovery_operations` record containing:
- operation type
- requested time
- requested by
- notes
- completion status
- result summary

Use this table together with application logs to reconstruct who initiated recovery and what changed.
