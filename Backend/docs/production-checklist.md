# Production Checklist

Use this checklist before enabling unattended production publishing.

## Configuration and secrets
- [ ] `ConnectionStrings:Postgres` points to the production database.
- [ ] `AzureOpenAI` values are present and valid.
- [ ] `AzureSpeech` values are present and valid.
- [ ] `AzureBlob` values are present and valid.
- [ ] `YouTube` credentials are configured only if publishing is intended.
- [ ] `PlatformPublishing:InstagramReelsEnabled=false` unless the live Meta integration has been completed.
- [ ] `PlatformPublishing:FacebookEnabled=false` unless the live Meta integration has been completed.
- [ ] `Alerting` is configured with a valid webhook if enabled.
- [ ] `KeyVault` and managed identity settings are configured if used.
- [ ] all secrets are sourced from deployment secrets or Key Vault, not checked into files.

## Database and storage
- [ ] PostgreSQL schema has been applied before startup.
- [ ] the database is reachable from both API and Worker hosts.
- [ ] the Blob container exists and is writable.
- [ ] the working directory exists and has adequate disk space.

## Runtime dependencies
- [ ] `ffmpeg` is installed and reachable on the Worker host.
- [ ] the Skyfield sidecar is reachable if enabled.
- [ ] Stellarium is configured if real captures are required.
- [ ] Application Insights is configured if telemetry is required.

## Application startup
- [ ] API starts successfully in `Production`.
- [ ] Worker starts successfully in `Production`.
- [ ] `/health/live` returns success.
- [ ] `/health/ready` returns success.
- [ ] ops summary endpoints return expected data.

## Operational readiness
- [ ] alert routing has been tested.
- [ ] scheduled worker jobs are active.
- [ ] stale-job recovery procedure is documented for operators.
- [ ] retention cleanup configuration has been reviewed.
- [ ] secret rotation ownership is defined.

## Publishing verification
- [ ] a non-publishing smoke run completed.
- [ ] Blob archival was verified on a fresh run.
- [ ] YouTube publishing was verified on a controlled run if enabled.
- [ ] shorts publishing behavior was verified for enabled platforms.
- [ ] analytics fetch completed successfully after a published run.

## Final go-live decision
- [ ] configuration validated
- [ ] secrets configured
- [ ] DB migrated
- [ ] storage accessible
- [ ] health checks passing
- [ ] alerts working
- [ ] publishing verified
