# Phase 10B Mobile API Gaps

The mobile foundation uses only the approved existing API surface. The following mobile views are scaffolded, but the current endpoint list does not guarantee dedicated payloads for every card.

## Content payload gaps

- **Tonight's sky visible objects**: no dedicated endpoint returns a region-specific visible-object list for the mobile UI. The foundation derives this from `GET /api/events/upcoming` event visibility text when present.
- **Latest DailySkyGuide video**: no dedicated endpoint is listed for the latest DailySkyGuide media item. The foundation reads `latestDailySkyGuide` or `latestVideos` from `GET /api/ops/dashboard` if that dashboard payload includes those fields.
- **Latest YouTube long videos, Shorts, Facebook Reels, and Instagram Reels**: no dedicated media listing endpoint is listed. The foundation groups `latestVideos` and `latestShorts` from `GET /api/ops/dashboard` when present.

## Operations payload gaps

- **Latest pipeline runs list**: `GET /api/pipeline/status/{runId}` requires a known run ID, but no approved endpoint lists recent run IDs. The foundation displays `pipelineRuns` from `GET /api/ops/dashboard` when available and otherwise shows an empty state.
- **Platform publish status list**: no dedicated publish-status endpoint is listed. The foundation displays `platformStatuses` from `GET /api/ops/dashboard` when available and otherwise shows an empty state.

## No-secret rule

If future endpoints include token, secret, connection string, API key, signature, SAS, or refresh-token shaped fields, the mobile API client strips those fields before screen models are created.
