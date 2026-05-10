# Frontend API gaps

Phase 10C Public User Portal is implemented using these available public-safe APIs:

- `GET /api/regions`
- `GET /api/events/upcoming`
- `GET /api/events/top`
- `GET /api/analytics/top-content`
- `GET /api/analytics/dashboard`

The frontend intentionally does not call admin-only diagnostics on public routes and does not render pipeline runs, stages, token health, output paths, stack traces, or SAS query strings in the public portal.

## Missing public APIs

### Public DailySkyGuide details

Needed for the Tonight's Sky page to show the exact latest `DailySkyGuide` for a selected region/date with a visible objects summary.

Suggested endpoint:

```http
GET /api/public/daily-sky-guides/latest?regionId={regionId}&date={yyyy-mm-dd}
```

Suggested safe response fields:

- `id`
- `regionId`
- `regionName`
- `targetDate`
- `title`
- `summary`
- `visibleObjects[]` with public fields such as `name`, `type`, `bestViewingWindow`, `direction`, `altitude`, and `viewerNotes`
- `longVideoUrl`
- `shortVideoUrls[]`
- `reelUrls[]`

Do not include internal run IDs, stage names, file system paths, storage blob paths, stack traces, secrets, or signed query strings.

### Public content search/filter

Needed for the Videos page to perform real server-side filtering by region, platform, event, content type, and date.

Suggested endpoint:

```http
GET /api/public/content?regionId={regionId}&platform={platform}&eventId={eventId}&contentType={video|short|reel}&from={yyyy-mm-dd}&to={yyyy-mm-dd}
```

Suggested safe response fields:

- `items[]`
- `id`
- `title`
- `regionId`
- `regionName`
- `eventId`
- `eventTitle`
- `platform`
- `contentType`
- `publishedAt`
- `durationSeconds`
- `watchUrl`
- `thumbnailUrl`

### Public event details

Needed for event detail pages and better special-event video matching.

Suggested endpoint:

```http
GET /api/public/events/{eventId}
```

Suggested safe response fields:

- `id`
- `title`
- `eventType`
- `startsAt`
- `regionId`
- `regionName`
- `visibilitySummary`
- `educationalSummary`
- `relatedVideos[]`

## Existing internal dashboard API gaps retained from earlier frontend phases

### Settings readonly safe configuration summary

No dedicated read-only endpoint exists for a sanitized production configuration summary. The admin frontend displays only client-side safe settings such as API base URL, timeout, environment inference, and the dashboard secret-redaction policy.

### Recent pipeline runs list

No dedicated scoped endpoint exists for recent runs. The admin dashboard uses `GET /api/ops/dashboard` `recentPipelineRuns` and `GET /api/scheduler/status` `recentRuns` when present.

### Pipeline run details require a known run ID

`GET /api/pipeline/status/{runId}` returns stage timeline and published URLs for a single run, but no scoped endpoint returns details for all recent runs at once. The admin dashboard loads per-run status only when an operator selects a run.
