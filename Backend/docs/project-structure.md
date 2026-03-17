# Updated project structure

- Astronomy.MediaFactory.Contracts: enums, records, strongly typed options
- Astronomy.MediaFactory.Core: entities, interfaces, prompt builder, orchestrator
- Astronomy.MediaFactory.AstroData: NASA, MPC, Skyfield sidecar clients and context builder
- Astronomy.MediaFactory.ContentGen: Azure OpenAI prompt flow and metadata generation
- Astronomy.MediaFactory.Rendering: Azure Speech, Stellarium scripts, FFmpeg render services
- Astronomy.MediaFactory.Publishing: Blob archival and YouTube publishing
- Astronomy.MediaFactory.Infrastructure: EF Core persistence and DI composition root
- Astronomy.MediaFactory.Api: manual trigger and inspection endpoints
- Astronomy.MediaFactory.Worker: scheduled pipeline execution
- python/skyfield_sidecar: Python FastAPI service for Skyfield calculations
