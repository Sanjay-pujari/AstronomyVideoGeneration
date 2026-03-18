# Astronomy Media Factory Backend

## Overview
Astronomy Media Factory is the backend for an AI-assisted astronomy video generation pipeline. The backend is feature-complete and focuses on long-form generation, short-form derivatives, operational recovery, publishing, analytics, monetization metadata, and deployment hardening.

## Documentation index
- `docs/architecture.md`: system overview, pipeline flow, and component responsibilities.
- `docs/azure-deployment-guide.md`: local and production deployment steps, required services, and secure configuration guidance.
- `docs/configuration-guide.md`: configuration sections, required fields, optional fields, and safe defaults.
- `docs/api-overview.md`: health, pipeline, jobs, analytics, ops, and platform publication endpoints.
- `docs/operations-runbook.md`: operator recovery procedures and incident handling.
- `docs/production-checklist.md`: pre-go-live and verification checklist.
- `docs/developer-onboarding.md`: project structure, local run steps, tests, debugging, and future-safe extension guidance.
- `docs/project-structure.md`: repository layout and extension points.

## High-level flow
1. Collect astronomy context from source services.
2. Generate scripts and metadata with Azure OpenAI.
3. Synthesize narration with Azure Speech.
4. Build visuals through Stellarium-oriented artifacts and render final video with FFmpeg.
5. Archive assets to Azure Blob.
6. Publish long-form and supported short-form outputs.
7. Collect analytics and feed them back into optimization.
8. Recover, replay, or maintain runs through ops endpoints and worker jobs.

## Core runtime components
- **API**: health checks, inspection, manual run triggers, analytics, and operational endpoints.
- **Worker**: scheduled queues, retries, analytics fetch, and maintenance jobs.
- **PostgreSQL**: durable pipeline state.
- **Azure OpenAI / Azure Speech / Azure Blob**: generation, narration, and storage services.
- **YouTube**: active publishing target.
- **Skyfield sidecar / Stellarium / FFmpeg**: astronomy calculations and rendering support.

## Local developer setup

### Start the Skyfield sidecar
```bash
cd python/skyfield_sidecar
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app:app --host 0.0.0.0 --port 8010 --reload
```

### Verify sidecar health
```bash
curl http://localhost:8010/health
```

### Run infrastructure helpers with Docker Compose
```bash
docker compose up --build postgres skyfield-sidecar
```

### Run the API
```bash
dotnet run --project src/Astronomy.MediaFactory.Api
```

### Run the Worker
```bash
dotnet run --project src/Astronomy.MediaFactory.Worker
```

## Configuration notes
- `SkyfieldSidecar:BaseUrl` defaults to `http://localhost:8010`.
- Development settings relax startup validation for cloud dependencies.
- Production should keep strict startup validation enabled and prefer Key Vault plus managed identity.
- Leave YouTube and non-YouTube short-form platforms disabled until credentials and publish paths are verified.

## Important delivery note
The backend is production-polished from a documentation and operational perspective. In this execution environment the .NET SDK was not installed, so build and test verification must still be performed in a standard .NET 10 toolchain before release.
