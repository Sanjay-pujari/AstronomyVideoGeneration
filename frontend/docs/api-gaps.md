# Phase 10A Web Dashboard API Gaps

Scope reviewed from the required Phase 10A dashboard pages while modifying only `/frontend`.

## Missing or partial APIs

1. **Settings Readonly safe configuration summary**
   - No dedicated read-only endpoint exists for a sanitized production configuration summary.
   - Frontend fallback: displays only client-side safe settings: API base URL, timeout, environment inference, and the dashboard secret-redaction policy.
   - Required backend shape, if added later: safe booleans and non-secret labels only; never tokens, refresh tokens, app secrets, connection strings, storage keys, or SAS query strings.

2. **Recent pipeline runs list**
   - No dedicated endpoint was included in Phase 10A scope for recent runs.
   - Frontend fallback: uses `GET /api/ops/dashboard` `recentPipelineRuns` and `GET /api/scheduler/status` `recentRuns` when present.

3. **Pipeline run details require a known run id**
   - `GET /api/pipeline/status/{runId}` returns stage timeline and published URLs for a single run, but no scoped endpoint in Phase 10A returns details for all recent runs at once.
   - Frontend fallback: each recent run has a “Details” button that loads the existing per-run status endpoint.
