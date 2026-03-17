# Azure Deployment Guide (Hardened Pass 16)

## Health probes
- **Liveness:** `GET /health/live`
- **Readiness:** `GET /health/ready` (checks DB + queue + config/dependency readiness)
- **General health:** `GET /health`

Use `/health/live` for platform liveness probes and `/health/ready` for startup/readiness probes.

## Required Azure resources
1. Azure Container Registry (optional but recommended)
2. Azure Database for PostgreSQL
3. Azure Storage account + blob container
4. Azure OpenAI resource + deployment
5. Azure AI Speech resource
6. Application Insights workspace
7. (Optional) Key Vault + managed identity

## Configuration strategy
- `appsettings.json`: baseline defaults for local dev.
- `appsettings.Development.json`: relaxed startup validation.
- `appsettings.Production.json`: strict startup validation.
- Environment variables override all appsettings.
- Key Vault is automatically loaded when `KeyVault__VaultUri` is provided.

## Secrets and managed identity
- Never commit secrets in appsettings.
- Preferred in Azure:
  - enable system-assigned managed identity on API/Worker
  - grant identity access to Key Vault secrets + Blob Data Contributor
  - configure `KeyVault__VaultUri`
- Blob supports either:
  - `AzureBlob__ConnectionString`, or
  - `AzureBlob__UseManagedIdentity=true` with `AzureBlob__AccountName` or `AzureBlob__ServiceUri`.
- Azure OpenAI/Speech startup validation allows either API key or managed identity flag.

## Azure Container Apps path
1. Build/push images for API and Worker.
2. Create Container App environment.
3. Deploy API app with ingress enabled (port 8080).
4. Deploy Worker app with ingress disabled.
5. Set env vars/secrets:
   - `ConnectionStrings__Postgres`
   - `AzureOpenAI__Endpoint`, `AzureOpenAI__ChatDeployment`, secret key or MI
   - `AzureSpeech__Region` (or endpoint), secret key or MI
   - `AzureBlob__ContainerName` + connection string or MI fields
   - `SkyfieldSidecar__BaseUrl` (if enabled)
   - `Telemetry__ApplicationInsightsConnectionString`
6. Configure probes:
   - liveness `/health/live`
   - readiness `/health/ready`

## Azure App Service path
1. Deploy API container to Web App for Containers.
2. Deploy Worker container to separate Web App (or Container App Job).
3. Configure the same environment variables/secrets as Container Apps.
4. Configure Health check path to `/health/ready`.

## YouTube publishing
- `YouTube__PublishingEnabled=false` by default.
- If true in production, require:
  - `YouTube__ClientId`
  - `YouTube__ClientSecret`
  - `YouTube__RefreshToken` or `YouTube__TokenFilePath`

## First deployment runbook
1. Deploy database + run schema init.
2. Deploy sidecar (if `SkyfieldSidecar__Enabled=true`).
3. Deploy API and verify:
   - `/health/live` = live
   - `/health/ready` = healthy
4. Deploy Worker and confirm scheduled jobs and queue processing logs.
5. Trigger one pipeline run via API.
6. Validate blob upload, telemetry, and pipeline summary logs.
