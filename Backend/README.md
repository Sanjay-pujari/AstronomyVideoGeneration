# Astronomy Media Factory v6

v6 upgrades the solution toward a real automated astronomy video pipeline.

## New in v6
- service separation for AstroData, ContentGen, Rendering, Publishing
- scaffolds for NASA APOD, NASA NeoWs, MPC, Skyfield sidecar, Stellarium scripts
- Azure OpenAI, Azure Speech, Azure Blob, and YouTube integration points
- detailed architecture docs and updated project structure

## Skyfield sidecar (daily sky)
Run the sidecar locally:

```bash
cd python/skyfield_sidecar
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app:app --host 0.0.0.0 --port 8010
```

Run infrastructure with Docker Compose:

```bash
docker compose up --build postgres skyfield-sidecar
```

Example call:

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

## Important note
This environment did not include the .NET SDK, so the package is not build-verified here.
Use it as the next implementation scaffold on top of your local working solution.
