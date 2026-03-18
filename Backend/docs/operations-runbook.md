# Operations Runbook

## Purpose
This runbook documents common operator actions for the Astronomy Media Factory backend, especially around recovery, publishing retries, stale jobs, dependency failures, and ongoing health validation.

## Primary health checks
Run these before and after any manual intervention:
- `GET /health/live`
- `GET /health/ready`
- `GET /api/ops/summary`
- `GET /api/ops/jobs/summary`
- `GET /api/ops/failures/recent`

Confirm all of the following:
1. The API is live and ready.
2. The queue is processing or idle as expected.
3. The target run is not stuck in `Running`.
4. No duplicate jobs were introduced accidentally.
5. Recovery actions were written to `recovery_operations`.

## Replay a failed run
Use this when the end-to-end run failed and you want a new main-video attempt.

```http
POST /api/ops/runs/{runId}/replay
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Replay after transient dependency outage.",
  "allowReplayOfSucceededRun": false,
  "publishToYouTubeOverride": false,
  "useTopicPlannerOverride": false
}
```

### When to use it
- Azure OpenAI or Azure Speech failed during the run.
- Astronomy context generation failed early.
- Rendering failed before usable output was produced.

### Safety notes
- Running runs are rejected.
- Successful runs are rejected unless explicitly allowed.
- Replay creates new queued work; it does not mutate the original run into a new attempt.

## Retry publish
Use this when render output exists and publish failed or only partially succeeded.

### Retry full publish
```http
POST /api/ops/runs/{runId}/retry-publish
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Retry after YouTube recovered.",
  "retryThumbnailOnly": false,
  "forceRepublish": false,
  "publishToYouTube": true
}
```

### Retry thumbnail only
```http
POST /api/ops/runs/{runId}/retry-publish
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Retry only the YouTube thumbnail upload.",
  "retryThumbnailOnly": true,
  "forceRepublish": false,
  "publishToYouTube": true
}
```

### When to use it
- YouTube upload failed but render succeeded.
- The video uploaded but thumbnail upload failed.
- YouTube was disabled during the original run and is now intentionally being retried.

## Retry archive
Use this when Azure Blob failed but local artifacts still exist.

```http
POST /api/ops/runs/{runId}/retry-archive
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Blob outage resolved.",
  "force": true
}
```

### Preconditions
- The run output files still exist under the working directory.
- Storage credentials and container access are fixed.

## Recover stale jobs
Use this when jobs remain `Running`, `Pending`, or `Retrying` beyond the acceptable threshold.

```http
POST /api/ops/jobs/recover-stale
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Worker instance restarted unexpectedly.",
  "thresholdMinutes": 90,
  "requeueRecoveredJobs": true,
  "recoverIncompleteRuns": true
}
```

### Expected behavior
- stale jobs are marked and audited,
- optionally requeued jobs return to the runnable queue,
- incomplete but otherwise recoverable runs may queue targeted publish or archive jobs,
- successful runs are not automatically replayed.

## Requeue a single job
Use this when one known job should be retried without broader stale-job recovery.

```http
POST /api/ops/jobs/{jobId}/requeue
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Retry one failed archive job.",
  "force": false
}
```

Use `force=true` only for an intentional rerun of a job that already succeeded.

## Regenerate shorts
Use this when the main long-form run succeeded but short-form outputs need to be re-rendered or re-published.

```http
POST /api/ops/runs/{runId}/regenerate-shorts
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Refresh shorts after poor performance.",
  "publishToYouTube": false,
  "force": false
}
```

### Typical reasons
- short-form hook or formatting underperformed,
- a short render failed while the parent run succeeded,
- you want fresh short assets before a later publish attempt.

## Rerun metadata optimization
Use this when a video exists but titles, descriptions, or tags need another pass.

```http
POST /api/ops/runs/{runId}/rerun-metadata
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Refresh metadata using latest analytics feedback.",
  "applyToPublishedVideo": true
}
```

### Recommended sequence
1. Rerun metadata optimization.
2. Inspect the updated generated script or published record.
3. Retry publish only if you intentionally want a republish.

## Handle YouTube failures

### Symptoms
- `published_videos.Status` reflects upload failure.
- `YouTubeVideoId` is missing.
- ops failure feeds show recent publish failures.

### Response
1. Confirm whether the failure is credential-related or an external YouTube outage.
2. Verify `YouTube:PublishingEnabled=true` only if credentials are complete.
3. If the run rendered successfully, use `retry-publish`.
4. If only the thumbnail failed, set `retryThumbnailOnly=true`.
5. Validate `YouTubeVideoId`, thumbnail upload state, and resulting publication record.

## Handle Blob failures

### Symptoms
- asset upload logs fail,
- blob URLs are missing from media assets or published records,
- render files remain on local disk only.

### Response
1. Verify storage credentials, container existence, and network access.
2. Confirm local output files still exist.
3. Use `retry-archive` for the affected run.
4. Confirm blob URL fields are populated after recovery.
5. Do not retry archival if local source files have already been deleted and no backup exists.

## Handle sidecar issues

### Symptoms
- astronomy context or daily-sky requests fail,
- production startup validation fails while Skyfield is enabled,
- local daily sky content lacks expected context.

### Response
1. Check sidecar health directly.
2. Confirm `SkyfieldSidecar:BaseUrl` is correct and absolute.
3. If the sidecar is intentionally unavailable, disable it by setting `SkyfieldSidecar:Enabled=false` only if the deployment can tolerate reduced context behavior.
4. Replay failed runs after the sidecar is restored.

## Handle Stellarium or visual-capture issues

### Symptoms
- screenshots are missing,
- render stage falls back to placeholder images,
- script files are generated but captures are not usable.

### Response
1. Confirm `Stellarium:ExecutablePath`, `ScriptsDirectory`, and `CaptureDirectory` if real capture is expected.
2. Verify the host has access to the configured directories.
3. Re-run a non-publishing pipeline to validate visuals before retrying a public publish.
4. If placeholders are acceptable for the incident window, proceed with retry logic once the render completes successfully.

## Rotate secrets
1. Update secrets in Key Vault, deployment secret store, or environment-variable source of truth.
2. Restart both API and Worker so the new values are loaded.
3. Verify `/health/ready`.
4. Run a non-publishing test.
5. If YouTube secrets changed, validate a controlled publish or publish retry.
6. If Blob authentication changed, verify archival on a fresh or replayed run.

## Validate system health
Use this checklist during normal operations or after incident recovery:
1. `GET /health/live`
2. `GET /health/ready`
3. `GET /api/ops/summary`
4. `GET /api/ops/jobs/summary`
5. inspect recent failures
6. confirm the worker is polling and scheduled jobs are active
7. confirm alerts arrive when expected
8. confirm one recent run moved through all expected states

## Cleanup and retention
Manual cleanup endpoint:

```http
POST /api/ops/maintenance/cleanup
Content-Type: application/json

{
  "requestedBy": "manual",
  "notes": "Monthly retention cleanup.",
  "deleteWorkingFiles": true,
  "deleteDbRecords": true,
  "deleteAnalytics": true
}
```

The worker also schedules daily cleanup at 03:00 UTC.

## Audit trail
Every recovery or cleanup action should be traceable through:
- `recovery_operations`,
- pipeline and worker logs,
- stage execution records,
- job status transitions.
