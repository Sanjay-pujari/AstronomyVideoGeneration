# Astronomy Media Factory v6

v6 upgrades the solution toward a real automated astronomy video pipeline.

## New in v6
- service separation for AstroData, ContentGen, Rendering, Publishing
- scaffolds for NASA APOD, NASA NeoWs, MPC, Skyfield sidecar, Stellarium scripts
- Azure OpenAI, Azure Speech, Azure Blob, and YouTube integration points
- detailed architecture docs and updated project structure

## Skyfield sidecar (daily sky)
The Skyfield sidecar now enforces a stricter request/response contract:
- `date` must be `yyyy-MM-dd`
- `locationName` is required
- `latitude` must be between `-90` and `90`
- `longitude` must be between `-180` and `180`
- `timezone` must be a valid IANA timezone (for example `Asia/Kolkata`)

### Local developer setup

#### 1) Start the sidecar directly (recommended for local development)
```bash
cd python/skyfield_sidecar
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app:app --host 0.0.0.0 --port 8010 --reload
```

#### 2) Verify sidecar is healthy
```bash
curl http://localhost:8010/health
```

#### 3) Exercise the daily-sky endpoint
```bash
curl -X POST http://localhost:8010/ephemeris/daily-sky \
  -H "Content-Type: application/json" \
  -d '{
    "date": "2026-03-17",
    "locationName": "Udaipur, India",
    "latitude": 24.5854,
    "longitude": 73.7125,
    "timezone": "Asia/Kolkata"
  }'
```

### Run infrastructure with Docker Compose
```bash
docker compose up --build postgres skyfield-sidecar
```

### Wiring sidecar into .NET services
`SkyfieldSidecar:BaseUrl` defaults to `http://localhost:8010`.

Set it in `src/Astronomy.MediaFactory.Worker/appsettings.json` or by environment variable:
```bash
export SkyfieldSidecar__BaseUrl=http://localhost:8010
```

## Important note
This environment did not include the .NET SDK, so the package is not build-verified here.
Use it as the next implementation scaffold on top of your local working solution.

## Stellarium local visual generation (PASS 4)
The daily sky guide pipeline now generates Stellarium scene scripts and screenshot manifests under the run output directory:

```text
media-output/<ContentType>/<yyyy-MM-dd>/<run-id>/visuals/
  001-sky-overview.ssc
  001-sky-overview.json
  002-moon.ssc
  002-moon.json
  003-jupiter.ssc
  003-jupiter.json
  004-orion-nebula.ssc
  004-orion-nebula.json
  005-wide-sky-close.ssc
  005-wide-sky-close.json
  capture-manifest.json
  screenshots/
    001-sky-overview.png
    ...
```

### Configure Stellarium
Set the `Stellarium` section in `appsettings.json` (API/Worker) or environment variables:

```json
"Stellarium": {
  "ExecutablePath": "",
  "ScriptsDirectory": "",
  "CaptureDirectory": "",
  "DefaultLandscape": "guereins",
  "DefaultProjection": "ProjectionPerspective"
}
```

- `ExecutablePath`: optional full path to Stellarium executable.
- `ScriptsDirectory`: optional override directory for generated `.ssc` and scene metadata `.json`.
- `CaptureDirectory`: optional override for screenshot output folder.

If `ExecutablePath` is empty or missing, the pipeline logs a warning and writes placeholder PNGs so FFmpeg rendering still proceeds.

### Local execution workflow
1. Run the daily pipeline as usual.
2. Open generated `.ssc` files from the run's `visuals` directory.
3. If Stellarium executable is configured, the service attempts optional startup-script invocation for each scene.
4. `capture-manifest.json` lists expected screenshot paths and scene metadata for renderer consumption.

## PASS 6: Publishing (Azure Blob + YouTube)

### Configuration
Update appsettings (API/Worker):

```json
"AzureBlob": {
  "ConnectionString": "<azure-storage-connection-string>",
  "ContainerName": "astronomy-videos"
},
"YouTube": {
  "ClientId": "<google-oauth-client-id>",
  "ClientSecret": "<google-oauth-client-secret>",
  "ApplicationName": "AstronomyVideoGenerator",
  "PrivacyStatus": "private",
  "RefreshToken": "<oauth-refresh-token>",
  "TokenFilePath": "./secrets/youtube-token.json"
}
```

### Generate YouTube OAuth refresh token (manual)
1. Create OAuth Desktop App credentials in Google Cloud.
2. Enable YouTube Data API v3.
3. Use OAuth playground or local OAuth helper to authorize `https://www.googleapis.com/auth/youtube.upload`.
4. Save the refresh token in `YouTube:RefreshToken` or put this JSON in `YouTube:TokenFilePath`:

```json
{
  "access_token": "<optional-access-token>",
  "refresh_token": "<required-refresh-token>"
}
```

### Pipeline behavior
After FFmpeg render completes, pipeline now does:
1. Upload `final-video.mp4`, `narration.mp3`, and optional thumbnail to Azure Blob.
2. Upload video to YouTube (when `PublishToYouTube=true`).
3. Persist publishing metadata in `published_videos` table.

Fallbacks:
- Blob upload failure logs error and continues YouTube upload from local file.
- YouTube upload failure logs error, preserves blob artifact, and stores `Status=UploadFailed`.

## PASS 16: Azure deployment hardening

- Production startup validation now fails fast for missing critical cloud settings.
- Development profile keeps local fallback behavior available.
- Managed identity-ready paths are supported for Blob and secure configuration loading.
- Optional Azure Key Vault loading is enabled via `KeyVault__VaultUri`.
- Application Insights hooks are wired for API and Worker when `Telemetry:ApplicationInsightsConnectionString` is set.
- Use `docs/azure-deployment-guide.md` for first deployment runbook and Azure setup steps.
