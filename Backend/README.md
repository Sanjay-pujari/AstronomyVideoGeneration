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
