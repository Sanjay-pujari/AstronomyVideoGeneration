# API Overview

## Purpose
The API is a minimal ASP.NET backend exposing health, pipeline, analytics, topics, experiments, platform publication, and operational recovery endpoints.

Base path examples below assume the API is running locally at `http://localhost:5000` or behind your deployed host URL.

## Health endpoints

### `GET /`
Simple service banner.

**Example response**
```json
{
  "service": "Astronomy.MediaFactory.Api",
  "status": "ok"
}
```

### `GET /health`
Aggregate health endpoint.

### `GET /health/live`
Liveness probe.

### `GET /health/ready`
Readiness probe including tagged checks for database, queue, and configuration.

## Pipeline endpoints

### `GET /api/pipelines/recent`
Returns the 20 most recent pipeline runs.

### `GET /api/pipelines/{id}`
Returns one pipeline run by id.

### `POST /api/pipelines/run`
Triggers an immediate pipeline run.

**Example request**
```json
{
  "date": "2026-03-18",
  "contentType": 1,
  "locationName": "Udaipur, India",
  "timeZone": "Asia/Kolkata",
  "publishToYouTube": false,
  "useTopicPlanner": true
}
```

**Example response**
```json
{
  "pipelineRunId": "00000000-0000-0000-0000-000000000000",
  "status": 3,
  "message": "Completed."
}
```

### `GET /api/scripts/recent`
Returns recent generated script records.

## Job endpoints

### `POST /api/jobs/enqueue`
Queues asynchronous work for the worker.

**Example request**
```json
{
  "jobType": 1,
  "runDate": "2026-03-18",
  "contentType": 1,
  "locationName": "Udaipur, India",
  "timeZone": "Asia/Kolkata",
  "publishToYouTube": false,
  "useTopicPlanner": false,
  "scheduledAt": null,
  "parentPipelineRunId": null
}
```

### `GET /api/jobs/recent`
Returns the 50 most recent jobs.

### `GET /api/jobs/{id}`
Returns a single job by id.

## Analytics endpoints

### `GET /api/analytics/recent`
Returns recent analytics snapshots.

### `GET /api/analytics/top-performing?topN=10`
Returns an aggregated recent top-performing summary.

### `GET /api/analytics/{videoId}`
Returns analytics records for the provided external video id.

## Ops endpoints

### Summary and inspection
- `GET /api/ops/summary`
- `GET /api/ops/pipelines/recent?take=20`
- `GET /api/ops/pipelines/{id}/stages`
- `GET /api/ops/failures/recent?take=20`
- `GET /api/ops/jobs/summary`

### Recovery actions
- `POST /api/ops/runs/{id}/replay`
- `POST /api/ops/runs/{id}/retry-publish`
- `POST /api/ops/runs/{id}/retry-archive`
- `POST /api/ops/runs/{id}/regenerate-shorts`
- `POST /api/ops/runs/{id}/rerun-metadata`
- `POST /api/ops/jobs/{id}/requeue`
- `POST /api/ops/jobs/recover-stale`
- `POST /api/ops/maintenance/cleanup`

**Example replay request**
```json
{
  "requestedBy": "manual",
  "notes": "Replay after a transient outage.",
  "allowReplayOfSucceededRun": false,
  "publishToYouTubeOverride": false,
  "useTopicPlannerOverride": false
}
```

## Platform publication endpoints

### `GET /api/platform-publications/recent?take=20`
Returns recent short-form platform publication records.

### `GET /api/platform-publications/{id}`
Returns one platform publication record.

### `GET /api/platform-publications/by-short/{shortId}`
Returns all publication records for a short video.

## Topic and prompt endpoints

### `GET /api/topics/recommended`
Builds a lightweight recommendation plan from query parameters.

### `POST /api/topics/plan`
Builds a full topic-selection plan.

### `POST /api/prompts/feedback-preview`
Returns prompt-feedback context before generation.

## Experiment endpoints
- `GET /api/experiments/recent`
- `GET /api/experiments/{id}`
- `GET /api/experiments/top-performing?take=10`

## Status-code behavior notes
- missing resources typically return `404 Not Found`,
- invalid recovery operations return `400 Bad Request`,
- conflicting queue submissions return `409 Conflict`,
- successful reads and writes return `200 OK`.

## Enum reference used by requests
- `ContentType`: `1=DailySkyGuide`, `2=TelescopeTargets`, `3=SpaceNews`, `4=AstrophotographyTips`
- `PipelineJobType`: `1=GenerateMainVideo`, `2=GenerateShorts`, `3=PublishVideo`, `4=ArchiveAssets`
- `PipelineRunStatus`: `1=Queued`, `2=Running`, `3=Succeeded`, `4=Failed`
