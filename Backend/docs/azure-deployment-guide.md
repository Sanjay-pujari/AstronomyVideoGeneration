# Deployment Guide

## Purpose
This guide describes how to deploy the backend safely without changing application behavior. It covers local development, production deployment, required services, configuration, secure secret handling, and startup verification.

## Required services

### Required for the full production pipeline
- **PostgreSQL** for pipeline, analytics, and operational state.
- **Azure OpenAI** for script and metadata generation.
- **Azure Speech** for narration synthesis.
- **Azure Blob Storage** for archival and durable asset access.

### Required when enabled by configuration
- **Skyfield sidecar** for astronomy calculations when `SkyfieldSidecar:Enabled=true`.
- **Stellarium** when you want real screenshot capture rather than placeholder images.
- **YouTube Data API** when `YouTube:PublishingEnabled=true` or when runs request YouTube publishing.
- **Slack webhook** when `Alerting:Enabled=true` and Slack notifications are desired.
- **Application Insights** when telemetry is required in production.

## Deployment artifacts
Deploy the following processes separately:
- **API**: `src/Astronomy.MediaFactory.Api`
- **Worker**: `src/Astronomy.MediaFactory.Worker`
- **Optional sidecar**: `python/skyfield_sidecar`

The API and Worker share the same database and most configuration sections. They should be deployed with the same secret source and broadly aligned environment values.

## Configuration precedence
1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. environment variables
4. Key Vault-loaded secrets when `KeyVault:VaultUri` is configured

Environment variables use double underscores, for example:
- `ConnectionStrings__Postgres`
- `AzureOpenAI__Endpoint`
- `AzureSpeech__Region`
- `AzureBlob__ContainerName`

## Environment variables

### Minimum baseline
- `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT`
- `ConnectionStrings__Postgres`

### Azure OpenAI
Required for production startup validation unless explicitly relaxed:
- `AzureOpenAI__Endpoint`
- `AzureOpenAI__ChatDeployment`
- either `AzureOpenAI__ApiKey` or `AzureOpenAI__UseManagedIdentity=true`
- optional `AzureOpenAI__ManagedIdentityClientId`

### Azure Speech
Required for production startup validation unless explicitly relaxed:
- either `AzureSpeech__Region` or `AzureSpeech__Endpoint`
- either `AzureSpeech__Key` or `AzureSpeech__UseManagedIdentity=true`
- required for managed identity: `AzureSpeech__ResourceId`
- optional: `AzureSpeech__ManagedIdentityClientId`
- optional voice override: `AzureSpeech__Voice`

### Azure Blob
Required for production startup validation unless explicitly relaxed:
- `AzureBlob__ContainerName`
- either `AzureBlob__ConnectionString`
- or `AzureBlob__UseManagedIdentity=true` with `AzureBlob__AccountName` or `AzureBlob__ServiceUri`
- optional `AzureBlob__ManagedIdentityClientId`

### YouTube API
Only required when publishing is enabled:
- `YouTube__PublishingEnabled=true`
- `YouTube__ClientId`
- `YouTube__ClientSecret`
- either `YouTube__RefreshToken` or `YouTube__TokenFilePath`
- optional `YouTube__PrivacyStatus`

### Optional but commonly used
- `SkyfieldSidecar__Enabled`
- `SkyfieldSidecar__BaseUrl`
- `Telemetry__ApplicationInsightsConnectionString`
- `Alerting__Enabled`
- `Alerting__SlackWebhookUrl`
- `KeyVault__VaultUri`
- `KeyVault__ManagedIdentityClientId`

## Key Vault and managed identity

### Recommended production approach
1. Enable a system-assigned or user-assigned managed identity on the API and Worker.
2. Grant the identity permission to read Key Vault secrets.
3. Grant the identity Blob data access if Blob uses managed identity.
4. Store secrets in Key Vault rather than appsettings files.
5. Set `KeyVault__VaultUri` and optionally `KeyVault__ManagedIdentityClientId`.

### Service-specific notes
- **Azure OpenAI**: supports API key auth or managed identity.
- **Azure Speech**: supports key auth or managed identity, but managed identity requires both `Region` and `ResourceId`.
- **Azure Blob**: supports connection string or managed identity using `AccountName` or `ServiceUri`.
- **YouTube**: still requires OAuth client credentials and refresh token or token file path; this should be treated as secret material and stored securely.

## Local setup

### 1. Prepare dependencies
- Start PostgreSQL.
- Start the Skyfield sidecar if `SkyfieldSidecar:Enabled=true`.
- Ensure `ffmpeg` is on `PATH`.
- Optionally configure Stellarium for real captures.

### 2. Configure local settings
Use `appsettings.Development.json` plus local environment variables. A typical local environment keeps production startup validation relaxed and leaves YouTube publishing disabled.

Example shell variables:
```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=astronomy_media_factory;Username=postgres;Password=postgres'
export AzureOpenAI__Endpoint='https://<resource>.openai.azure.com/'
export AzureOpenAI__ChatDeployment='<deployment-name>'
export AzureOpenAI__ApiKey='<api-key>'
export AzureSpeech__Region='<speech-region>'
export AzureSpeech__Key='<speech-key>'
export AzureBlob__ConnectionString='<storage-connection-string>'
export AzureBlob__ContainerName='astronomy-videos'
export SkyfieldSidecar__BaseUrl='http://localhost:8010'
```

### 3. Run the API
```bash
dotnet run --project src/Astronomy.MediaFactory.Api
```

### 4. Run the Worker
```bash
dotnet run --project src/Astronomy.MediaFactory.Worker
```

### 5. Verify locally
- `GET /health/live`
- `GET /health/ready`
- queue one run through `POST /api/jobs/enqueue` or `POST /api/pipelines/run`

## Production setup

### 1. Provision infrastructure
Create:
- PostgreSQL database
- Azure OpenAI resource and deployment
- Azure Speech resource
- Azure Storage account and blob container
- API host
- Worker host
- optional Key Vault
- optional Application Insights
- optional Skyfield sidecar host

### 2. Apply database schema
Run the project’s standard EF Core migration or schema initialization process before starting the API and Worker. This repository maps all required tables in `MediaFactoryDbContext`, and production readiness assumes the target database already contains the corresponding schema.

### 3. Deploy the API
- set environment and secrets,
- configure readiness probe to `/health/ready`,
- configure liveness probe to `/health/live`,
- expose ingress only for the API.

### 4. Deploy the Worker
- use the same configuration source,
- do not expose public ingress,
- keep Quartz and queue processing active,
- verify scheduled jobs and backlog processing through logs and ops endpoints.

### 5. Deploy optional sidecar
If Skyfield is enabled in production, deploy the sidecar and set `SkyfieldSidecar__BaseUrl` to its reachable internal URL.

### 6. Smoke test the platform
1. Confirm API health endpoints respond.
2. Confirm Worker starts without validation failures.
3. Trigger a non-publishing run.
4. Confirm pipeline stages, generated scripts, and media assets are persisted.
5. Confirm Blob archival works.
6. If YouTube is enabled, validate a controlled publish.

## Local versus production expectations

### Local
- Development validation can relax cloud requirements.
- Placeholder visuals are acceptable if Stellarium is not configured.
- Publishing should usually remain disabled.
- Secrets may come from user-level environment variables.

### Production
- Startup validation should remain enabled.
- Managed identity and Key Vault are preferred.
- Blob archival should be mandatory.
- Health probes, alerts, and telemetry should be active.
- Only enable YouTube or other platforms after credential validation.

## How to run API and Worker together in production-like mode
1. Set `DOTNET_ENVIRONMENT=Production` for both processes.
2. Provide all required secrets and cloud endpoints.
3. Start the API first and confirm `/health/ready`.
4. Start the Worker and confirm it begins polling the queue.
5. Trigger a run and verify end-to-end persistence, rendering, archival, and any intended publishing.

## Post-deploy validation
- readiness health is green,
- queue summary is reachable,
- recent pipelines show expected status transitions,
- alerts flow to their destination,
- Blob output paths are valid,
- YouTube publishing is disabled or verified,
- retention cleanup and analytics fetch jobs are scheduled.
