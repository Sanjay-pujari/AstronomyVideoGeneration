# Architecture summary

Ingestion -> normalize/rank -> generate script -> generate narration -> prepare visuals -> render -> archive -> publish.

Use Skyfield for astronomy calculations, Stellarium for visual capture, Azure OpenAI for text generation, Azure Speech for narration, FFmpeg for final MP4 composition, Azure Blob for archival, and YouTube Data API for publishing.
