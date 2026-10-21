# Azure dependency inventory

Audit date: 2026-10-21. Branch: `RecoveryBrach-18-05-26`. Source commit: `0e5ba735191036fdfac8c35c789805b83f03c785`. Audit only; no migration, model installation, generation, publication, or certification run was performed.

## Decision and counting rules

**13 confirmed implementation entries, representing 8 Azure-dependent capabilities across 6 Azure service families.** Entries include separate HTTP/SDK implementations, not every wrapper call. Families: Azure OpenAI, Azure AI Speech, Azure Blob Storage, Microsoft Entra identity, Azure Key Vault, Application Insights. Capability count: text, images, speech, archival, public staging, identity, secrets bootstrap, monitoring. This avoids treating every package, setting, documentation mention, or wrapper as another service.

Three AI capabilities (text/images/speech) are wired into reachable production code. **No successful Azure production request was observed in this audit.** Runtime active is Unknown unless inspected settings prove that an optional integration is unconfigured here. Checked-in Enabled=true is not deployment telemetry. Cloud metadata had no application secret/runtime-variable/outbound-identity bindings; the seven checked application environment names were unset. Do not infer that other deployments lack user secrets or vault bindings.

Mandatory means mandatory for the stated workflow, not for every API route. An API health endpoint working would not prove narration or publishing works.

## Implementation inventory

### D01 — Azure OpenAI — text

- **File:** [`Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs`](../../Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs)
- **Class/method:** `AzureOpenAiContentGenerationService.RequestCompletionAsync; InvokeAsync; GenerateAsync; GenerateShortAsync`
- **Phase/subsystem:** RC2 7; legacy scripts/shorts; metadata optimization
- **Purpose / actual call:** HTTP POST /openai/deployments/{deployment}/chat/completions; JSON-object mode, temperature 0.2, default max_tokens 4096. RC2 InvokeAsync classifies failures and does not use legacy template success. Legacy GenerateAsync/GenerateShortAsync can return deterministic fallback after retries.
- **Input:** NarrationProviderCall (system/user prompts, checksum, scene/token limits), or content type + AstronomyContext
- **Output:** NarrationProviderCallResult/raw JSON; ScriptResult; ShortScriptResult
- **Configuration names:** AzureOpenAI:Endpoint, ChatDeployment, ApiKey, UseManagedIdentity, ManagedIdentityClientId
- **Mandatory:** Yes for newly generated governed Phase 7 narration
- **Runtime active:** Unknown; DI-reachable, credentials absent in this machine
- **Replacement needed:** Yes
- **Proposed alternative:** Ollama + Qwen2.5-7B-Instruct Q4_K_M, provisional
- **Migration risk:** Critical

### D02 — Azure OpenAI — optimization

- **File:** [`Backend/src/Astronomy.MediaFactory.Infrastructure/Optimization/AIOptimizationService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Optimization/AIOptimizationService.cs)
- **Class/method:** `AIOptimizationService.GenerateWithAzureOpenAIAsync; GenerateRecommendationsAsync`
- **Phase/subsystem:** Analytics/optimization outside canonical RC2 phases
- **Purpose / actual call:** Separate raw HTTP implementation, api-key header; changing D01 DI alone will not migrate this path. Safe-apply and human-approval policy must remain.
- **Input:** AIOptimizationContext: locally collected analytics and rule recommendations
- **Output:** AIOptimizationRecommendations JSON / deterministic recommendations
- **Configuration names:** AIOptimization options; AzureOpenAI:Endpoint, ChatDeployment, ApiKey
- **Mandatory:** No; deterministic rule-based fallback exists
- **Runtime active:** Unknown; code reachable, request not observed
- **Replacement needed:** Yes for retaining optional AI recommendations; no for baseline deterministic mode
- **Proposed alternative:** Reuse local text provider through IAIOptimizationService
- **Migration risk:** Medium

### D03 — Azure OpenAI — image

- **File:** [`Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAICinematicImageGenerator.cs`](../../Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAICinematicImageGenerator.cs)
- **Class/method:** `AzureOpenAICinematicImageGenerator.GenerateAsync; RequestImageBytesAsync`
- **Phase/subsystem:** RC2 8; weekly cinematic images; documentary visual adapters
- **Purpose / actual call:** POST images/generations; supports base64 or downloaded URL responses. Missing configuration yields unconfigured result; scene policy decides whether fallback is permitted. Transport errors propagate after bounded retries.
- **Input:** AICinematicAssetRequest: prompt, negative prompt, style, target dimensions, planned path
- **Output:** AICinematicProviderResult with status, path, ProviderConfigured, warnings; image bytes
- **Configuration names:** AzureOpenAIForImage:Endpoint, ImageDeployment, ApiKey, UseManagedIdentity; AICinematicAssets timeout
- **Mandatory:** Yes for selected cinematic scene routes; not deterministic scientific routes
- **Runtime active:** Unknown; IAICinematicImageGenerator is DI-bound to Azure
- **Replacement needed:** Yes
- **Proposed alternative:** Local diffusion adapter via IAICinematicImageGenerator; factual rendering retained
- **Migration risk:** High

### D04 — Azure OpenAI — image

- **File:** [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/HeroAssetIntelligenceEngine.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/HeroAssetIntelligenceEngine.cs)
- **Class/method:** `HeroAssetIntelligenceEngine.GenerateHeroWithAzureImage2Async; GenerateAzureHeroImageFilesAsync`
- **Phase/subsystem:** Legacy/non-governed Hero endpoints; not current governed Phase 11
- **Purpose / actual call:** A separate raw HTTP images/generations implementation. Governed Phase 11 instead invokes ResponsiveHeroAuthorityService; do not count this legacy method as a Phase 11 production Azure call.
- **Input:** AzureOpenAIForImageOptions, hero prompt, image path
- **Output:** AzureImage2GenerationResult and rendered Hero variants
- **Configuration names:** AzureOpenAIForImage:*; Hero/output-artifact policies
- **Mandatory:** No for governed Phase 11; conditional for legacy Hero route
- **Runtime active:** Unknown; legacy route reachable, no request observed
- **Replacement needed:** Yes if preserving that generation feature
- **Proposed alternative:** Shared local image boundary plus existing deterministic overlays
- **Migration risk:** High

### D05 — Azure OpenAI — image

- **File:** [`Backend/src/Astronomy.MediaFactory.Rendering/AstroPulseGalleryService.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/AstroPulseGalleryService.cs)
- **Class/method:** `AstroPulseGalleryService.GenerateBackgroundWithAzureImage2Async; Phase13GalleryAuthority.PublishAsync`
- **Phase/subsystem:** RC2 13 and gallery service
- **Purpose / actual call:** GenerateGalleryAsync immediately returns Phase13GalleryAuthority.PublishAsync. Older code after that return is unreachable. The comment describing the new route as deterministic/no Azure is stale: Phase13GalleryAuthority itself calls the Azure image helper. Phase13 calls this separate HTTP helper: 2024-10-21 API, 300s timeout, bounded retries. It must be migrated independently of D03.
- **Input:** AzureOpenAIForImageOptions, page prompt, output path, AstroPulseGalleryAspect
- **Output:** AzureImage2GenerationResult; six separately generated gallery pages and gallery-manifest.json
- **Configuration names:** AzureOpenAIForImage:*
- **Mandatory:** Yes for fresh requested Gallery output on this branch
- **Runtime active:** Unknown; active code route confirmed, no live request
- **Replacement needed:** Yes
- **Proposed alternative:** Refactor static HTTP boundary into existing image abstraction; local diffusion backgrounds
- **Migration risk:** High

### D06 — Azure OpenAI — image

- **File:** [`Backend/src/Astronomy.MediaFactory.Api/Program.cs`](../../Backend/src/Astronomy.MediaFactory.Api/Program.cs)
- **Class/method:** `GenerateVisualLabBackgroundWithAzureImageAsync; /api/visual-lab/generate-background`
- **Phase/subsystem:** Visual Lab outside canonical phase execution
- **Purpose / actual call:** Direct HTTP implementation with api-key or cognitive-services bearer token. Explicit canvas fallback may be allowed; it is not evidence that governed Phase12/13 accept a fallback.
- **Input:** VisualLabBackgroundGenerateRequest, prompt, width/height, planned image path
- **Output:** VisualLabAzureImageGenerationResult / image and diagnostics
- **Configuration names:** AzureOpenAIForImage:*; request fallbackAllowed
- **Mandatory:** No for RC2; conditional for optional Visual Lab Azure generation
- **Runtime active:** Unknown; endpoint is mapped, not invoked
- **Replacement needed:** Yes if retaining AI lab generation
- **Proposed alternative:** Local image adapter; keep explicit canvas fallback semantics
- **Migration risk:** Medium

### D07 — Azure AI Speech — shared SDK boundary

- **File:** [`Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechClient.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechClient.cs)
- **Class/method:** `AzureSpeechClient.SynthesizeMp3Async; SynthesizeWavSsmlAsync`
- **Phase/subsystem:** RC2 15; legacy narration; bulk TTS; documentary narration adapter
- **Purpose / actual call:** Microsoft.CognitiveServices.Speech SpeechSynthesizer.SpeakSsmlAsync/SpeakTextAsync. No SpeechRecognizer or WordBoundary subscription found. Language detection is a Devanagari character heuristic, not cloud detection. See consumer table below.
- **Input:** Plain narration or SSML plus AzureSpeechOptions
- **Output:** byte[]: MP3 24kHz/160kbps mono; WAV RIFF 24kHz/16-bit mono PCM
- **Configuration names:** AzureSpeech:Key, Region, Endpoint, UseManagedIdentity, ResourceId, ManagedIdentityClientId, voices/rates; Speech settings
- **Mandatory:** Yes for new audio in these registered implementations
- **Runtime active:** Unknown; registered, required keys absent, no synthesis attempted
- **Replacement needed:** Yes
- **Proposed alternative:** Local TTS engine behind neutral synthesis boundary with explicit codec normalization
- **Migration risk:** Critical

### D08 — Azure AI Speech — weekly SDK boundary

- **File:** [`Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs)
- **Class/method:** `WeeklySkyForecastAzureTtsSynthesizer.SynthesizeSsmlToFileAsync`
- **Phase/subsystem:** WeeklySkyForecast audio Phase 6.3 (separate numbering from RC2)
- **Purpose / actual call:** Independent SpeechSynthesizer call; replacing AzureSpeechClient alone does not remove this dependency.
- **Input:** SSML, outputPath, voiceName, audioFormat
- **Output:** File: MP3 24kHz/96kbps mono; non-mp3 rejected
- **Configuration names:** AzureSpeech:*
- **Mandatory:** Yes for new weekly forecast audio
- **Runtime active:** Unknown; IWeeklySkyForecastTtsSynthesizer registered
- **Replacement needed:** Yes
- **Proposed alternative:** Local implementation of IWeeklySkyForecastTtsSynthesizer
- **Migration risk:** High

### D09 — Azure Blob Storage — archival

- **File:** [`Backend/src/Astronomy.MediaFactory.Publishing/AzureBlobStorageService.cs`](../../Backend/src/Astronomy.MediaFactory.Publishing/AzureBlobStorageService.cs)
- **Class/method:** `AzureBlobStorageService.UploadAsync; BuildContainerClient`
- **Phase/subsystem:** Legacy PipelineOrchestrator, ShortsVideoRenderService, RunOperationsService archival/recovery
- **Purpose / actual call:** BlobContainerClient/BlobServiceClient CreateIfNotExistsAsync and UploadAsync. Unconfigured client skips upload and returns empty result; do not describe this as successful durable archival.
- **Input:** BlobUploadRequest (video/audio/thumbnail paths, base path)
- **Output:** BlobUploadResult (VideoUrl, AudioUrl, ThumbnailUrl)
- **Configuration names:** AzureBlob:ConnectionString, ContainerName, UseManagedIdentity, AccountName, ServiceUri; retry/transfer options
- **Mandatory:** No for local artifact generation; required if archival URLs are demanded; production startup can separately require it
- **Runtime active:** Unknown; registered, no usable binding observed
- **Replacement needed:** Yes for preserving archival feature
- **Proposed alternative:** Filesystem archival implementation first; optional S3-compatible object storage
- **Migration risk:** High

### D10 — Azure Blob Storage — public staging

- **File:** [`Backend/src/Astronomy.MediaFactory.Publishing/AzureBlobPublicMediaStorageService.cs`](../../Backend/src/Astronomy.MediaFactory.Publishing/AzureBlobPublicMediaStorageService.cs)
- **Class/method:** `AzureBlobPublicMediaStorageService.UploadPublicAssetAsync; UploadForInstagramAsync; CreateReadAccessAsync`
- **Phase/subsystem:** RC2 InstagramPost; Instagram reels; Meta thumbnails
- **Purpose / actual call:** Blob upload plus BlobSasBuilder/GenerateSasUri. Both DI options validation and Rc2PublishingExecutionService.PrepareInstagramImageAsync explicitly require AzureBlob, connection string and positive expiry: a DI swap alone is insufficient.
- **Input:** Local file, plan/run Guid, asset filename/content type; opaque blob key for renewal
- **Output:** PublicMediaUploadResult: Success, PublicUrl, BlobName, ExpiresUtc, Error
- **Configuration names:** PublicMediaStorage:Enabled, Provider, ConnectionString, ContainerName, BlobPrefix, UseSasUrl, SasExpiryHours, PublicBaseUrl; Meta flags
- **Mandatory:** Yes for current governed Instagram image staging; not for RC2 direct YouTube/Facebook uploads
- **Runtime active:** Unknown; Enabled=true in files, no credential binding, not exercised
- **Replacement needed:** Yes
- **Proposed alternative:** IPublicMediaStorageService filesystem staging + public HTTPS signed GET service
- **Migration risk:** Critical

### D11 — Microsoft Entra ID / Azure managed identity

- **File:** [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/SecureConfigurationExtensions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/SecureConfigurationExtensions.cs)
- **Class/method:** `CreateCredential; plus managed-identity branches of D01,D03–D05,D07–D09 and Visual Lab`
- **Phase/subsystem:** Optional Azure authentication across services
- **Purpose / actual call:** DefaultAzureCredential and https://cognitiveservices.azure.com/.default scopes. Current cloud-tool Azure-related variable names alone do not prove application resource authorization.
- **Input:** ManagedIdentityClientId/resource scope; host identity
- **Output:** TokenCredential/access token; Speech aad#resource#token authorization
- **Configuration names:** UseManagedIdentity, ManagedIdentityClientId, AzureSpeech:ResourceId, KeyVault:VaultUri
- **Mandatory:** No for independent deployment; conditional Azure auth only
- **Runtime active:** No configured cloud outbound identity; checked-in managed-identity flags false; live Azure host unknown
- **Replacement needed:** No replacement needed for offline inference; replace secret handling where external APIs still need it
- **Proposed alternative:** Local OS secrets/files or existing secure environment bindings; preserve social OAuth
- **Migration risk:** Medium

### D12 — Azure Key Vault

- **File:** [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/SecureConfigurationExtensions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/SecureConfigurationExtensions.cs)
- **Class/method:** `AddMediaFactorySecureConfiguration; CreateCredential`
- **Phase/subsystem:** API and Worker configuration bootstrap
- **Purpose / actual call:** Environment variables are loaded first. A configured but unavailable vault can fail bootstrap; do not merely change AI providers while leaving a dead vault URI.
- **Input:** KeyVault:VaultUri and optional ManagedIdentityClientId
- **Output:** Configuration values loaded via AddAzureKeyVault
- **Configuration names:** KeyVault:VaultUri; KeyVault__VaultUri; KEYVAULT__VAULTURI
- **Mandatory:** No; enabled only with a valid nonempty URI
- **Runtime active:** No in inspected config/machine; deployed instance Unknown
- **Replacement needed:** No mandatory replacement; non-Azure secret source needed if vault currently used elsewhere
- **Proposed alternative:** Secure mounted files/environment; optional self-hosted secret manager
- **Migration risk:** Medium

### D13 — Azure Application Insights

- **File:** [`Backend/src/Astronomy.MediaFactory.Api/Program.cs`](../../Backend/src/Astronomy.MediaFactory.Api/Program.cs)
- **Class/method:** `API AddApplicationInsightsTelemetry; Worker AddApplicationInsightsTelemetryWorkerService`
- **Phase/subsystem:** Monitoring, not RC2 generation authority
- **Purpose / actual call:** Worker source: Backend/src/Astronomy.MediaFactory.Worker/Program.cs. Keep correlation IDs and redact media URL tokens and OAuth credentials.
- **Input:** Telemetry:ApplicationInsightsConnectionString; runtime instrumentation
- **Output:** Telemetry export, not production media artifacts
- **Configuration names:** Telemetry:ApplicationInsightsConnectionString
- **Mandatory:** No; registration conditional on nonempty string
- **Runtime active:** No in inspected files/bindings; deployed instance Unknown
- **Replacement needed:** No mandatory replacement; retain observability through local logs
- **Proposed alternative:** Existing Serilog console + optional OpenTelemetry collector
- **Migration risk:** Low

## Speech consumers that must all be accounted for

| File | Consumer / input → output | Migration hazard |
|---|---|---|
| [`Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechSynthesisService.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechSynthesisService.cs) | AzureSpeechSynthesisService.SynthesizeAsync/SynthesizeToFileAsync: script → narration.txt + narration.mp3 | Uses ISpeechSynthesisService but depends on IAzureSpeechClient/options internally; failure throws. |
| [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs) | Phase15RealTtsV2Async → GenerateAndValidateTtsAudioAsync: Phase14 SceneAudioUnit → per-scene MP3 + timeline/checksum authority | Direct Azure config gate, hi- voice prefix gate, hardcoded provider/voice policy identity; not fixed by replacing ISpeechSynthesisService. |
| [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AzureTtsAudioGenerationService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AzureTtsAudioGenerationService.cs) | GenerateTtsAudioAsync/GenerateTtsAudioBulkAsync: final-tts-package.json SSML → WAV segments/combined WAV and TtsAudioManifest | Validates TtsProvider == AzureSpeech; pilot category gate; >10KB and >1 second validation; unsupported package is skipped. |
| [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/NarrationAdapter.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/NarrationAdapter.cs) | AzureSpeechDocumentaryNarrationProviderBinding.SynthesizeAsync: DocumentaryNarrationProviderRequest → registered WAV/MP3 artifact descriptor | ProviderId AzureSpeech, Azure voice resolver, normalization and physical inspection; bind another provider without replacing production artifact governance. |
| [`Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs) | SynthesizeSsmlToFileAsync: forecast SSML → 96kbps MP3 | Independent SDK construction; maintain weekly contract separately. |

## Negative findings and infrastructure distinctions

- No executable `SpeechRecognizer`, Azure word-boundary event handler, Azure Translator client, Azure AI Search SDK/client, embedding request/vector store, Azure Vision client, Content Safety, AzureML, Functions, SignalR or Communication Services implementation was found in the scanned source. This is a source-scope negative finding, not proof about external deployed services. `LooksLikeEmbeddingDeployment` rejects embedding deployments for chat; it is not embedding usage.
- PostgreSQL is used through EF/Npgsql and has a local Docker service. “Azure Postgres” in an error/document does not mandate an Azure database. Preserve the existing schema and connection-string contract; no vector migration is currently justified.
- `.github/workflows/deploy-vm.yml` deploys to a self-hosted Windows runner with IIS/NSSM. It does not provision Azure or prove the VM is hosted there. Its automatic trigger targets main; this audit branch does not change deployment behavior.
- API/Worker Dockerfiles use Microsoft .NET container images; MCR distribution is not an Azure subscription requirement. `Backend/docker-compose.yml` defines PostgreSQL, optional Azurite, sidecar and API. Azurite is an emulator, not evidence of actual Azure access, and is not a public Meta media host.
- `Backend/python/skyfield_sidecar/app.py`, `test_weekly_forecast.py`, and `tools/five_category_phase7_endpoint_runner.py` were included in discovery; no Azure implementation match was found. Do not run the endpoint tool in an audit: it can generate artifacts.
- Eight Azure-related entries exist in `Backend/Directory.Packages.props`: Azure.Storage.Blobs, Azure.Core, Azure.AI.OpenAI, Azure.Identity, Azure.Extensions.AspNetCore.Configuration.Secrets, both ApplicationInsights packages, Microsoft.CognitiveServices.Speech. D01 uses raw HTTP despite the Azure.AI.OpenAI package. Package existence does not establish SDK invocation.
- Local ImageSharp processing is not Azure AI. Its own commercial/license terms need independent review; preserving the current library does not establish free commercial rights for every operator.

## Discovery method and scope

Searched source/configuration/projects/workflows/docs/Python/tools using Azure/OpenAI/CognitiveServices/identity/Blob/embedding/TTS/image terms, then inspected DI, actual HTTP/SDK calls, consumers and current phase dispatch. DOCX word/document.xml text was also extracted and searched read-only (25 documents); their references were not treated as executable behavior. Architecture Markdown plus executable phase dispatch govern the findings. Initial text scan (excluding .git, bin, obj, node_modules, dist and test-build outputs) matched **261 files**: 149 C#, 101 Markdown, 5 csproj, 4 JSON, 1 props, 1 workflow. These counts are search hits, not dependency counts. No credential values were printed or copied. `Backend/.env.example` was inspected by variable names only and confirms the documented AzureOpenAI__, AzureSpeech__, AzureBlob__, KeyVault__ and Telemetry__ configuration families. No `UserSecretsId` or `AddUserSecrets` reference was found in the searched C#/project files; private secret files were not read.

Supporting registration and guards:

- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs): AddMediaFactory registrations and Azure-only public storage validation.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/ProductionStartupValidator.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/ProductionStartupValidator.cs): Validate returns early for Development; otherwise RequireAzureOpenAi/RequireAzureSpeech/RequireBlobStorage gates remain.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureConfigurationValidation.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureConfigurationValidation.cs): Partially populated Azure settings may still fail option validation, even if a startup requirement flag is disabled.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/OperationsHealthChecks.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/OperationsHealthChecks.cs): OperationsConfigHealthCheck still validates Blob; Degraded in Development, Unhealthy otherwise.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs): PrepareInstagramImageAsync has an AzureBlob configuration gate; YouTube file uploads and Facebook photo binary upload are separate paths.
- [`Backend/Directory.Packages.props`](../../Backend/Directory.Packages.props): Package versions only; do not remove during this audit.
- [`Backend/docker-compose.yml`](../../Backend/docker-compose.yml): Local infrastructure options.
- [`.github/workflows/deploy-vm.yml`](../../.github/workflows/deploy-vm.yml): Actual deployment workflow, not inferred hosting vendor.

## Search-hit appendix — executable sources/projects

These are discovery candidates; the entries above classify the actual service boundaries. Names or diagnostics alone are not confirmed Azure calls.

- [`Backend/Directory.Packages.props`](../../Backend/Directory.Packages.props)
- [`Backend/src/Astronomy.MediaFactory.Api/Astronomy.MediaFactory.Api.csproj`](../../Backend/src/Astronomy.MediaFactory.Api/Astronomy.MediaFactory.Api.csproj)
- [`Backend/src/Astronomy.MediaFactory.Api/Program.cs`](../../Backend/src/Astronomy.MediaFactory.Api/Program.cs)
- [`Backend/src/Astronomy.MediaFactory.ContentGen/Astronomy.MediaFactory.ContentGen.csproj`](../../Backend/src/Astronomy.MediaFactory.ContentGen/Astronomy.MediaFactory.ContentGen.csproj)
- [`Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAICinematicImageGenerator.cs`](../../Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAICinematicImageGenerator.cs)
- [`Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs`](../../Backend/src/Astronomy.MediaFactory.ContentGen/AzureOpenAiContentGenerationService.cs)
- [`Backend/src/Astronomy.MediaFactory.Contracts/AzureBlobOptions.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/AzureBlobOptions.cs)
- [`Backend/src/Astronomy.MediaFactory.Contracts/AzureSpeechOptions.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/AzureSpeechOptions.cs)
- [`Backend/src/Astronomy.MediaFactory.Contracts/Contracts.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/Contracts.cs)
- [`Backend/src/Astronomy.MediaFactory.Contracts/PublicMediaStorageOptions.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/PublicMediaStorageOptions.cs)
- [`Backend/src/Astronomy.MediaFactory.Contracts/ThumbnailAIOptimizationOptions.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/ThumbnailAIOptimizationOptions.cs)
- [`Backend/src/Astronomy.MediaFactory.Contracts/VisualIntelligenceContracts.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/VisualIntelligenceContracts.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/Interfaces.cs`](../../Backend/src/Astronomy.MediaFactory.Core/Interfaces.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/OpsDashboardModels.cs`](../../Backend/src/Astronomy.MediaFactory.Core/OpsDashboardModels.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/PipelineOrchestrator.cs`](../../Backend/src/Astronomy.MediaFactory.Core/PipelineOrchestrator.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/PipelineStageModel.cs`](../../Backend/src/Astronomy.MediaFactory.Core/PipelineStageModel.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/SceneAssetsV3.cs`](../../Backend/src/Astronomy.MediaFactory.Core/SceneAssetsV3.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/ShortsVideoRenderService.cs`](../../Backend/src/Astronomy.MediaFactory.Core/ShortsVideoRenderService.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/ThumbnailPromptContract.cs`](../../Backend/src/Astronomy.MediaFactory.Core/ThumbnailPromptContract.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Astronomy.MediaFactory.Infrastructure.csproj`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Astronomy.MediaFactory.Infrastructure.csproj)
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/ExecutionHost.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/ExecutionHost.cs)
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/ExistingVisualProviderBindings.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/ExistingVisualProviderBindings.cs)
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/Hosting.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/Hosting.cs)
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/NarrationAdapter.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/NarrationAdapter.cs)
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/RealProviderSmoke.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/RealProviderSmoke.cs)
- [`Backend/src/Astronomy.MediaFactory.ProductionAdapters/VisualAdapter.cs`](../../Backend/src/Astronomy.MediaFactory.ProductionAdapters/VisualAdapter.cs)
- [`Backend/src/Astronomy.MediaFactory.Publishing/Astronomy.MediaFactory.Publishing.csproj`](../../Backend/src/Astronomy.MediaFactory.Publishing/Astronomy.MediaFactory.Publishing.csproj)
- [`Backend/src/Astronomy.MediaFactory.Publishing/AzureBlobPublicMediaStorageService.cs`](../../Backend/src/Astronomy.MediaFactory.Publishing/AzureBlobPublicMediaStorageService.cs)
- [`Backend/src/Astronomy.MediaFactory.Publishing/AzureBlobStorageService.cs`](../../Backend/src/Astronomy.MediaFactory.Publishing/AzureBlobStorageService.cs)
- [`Backend/src/Astronomy.MediaFactory.Publishing/TransientRetryHelper.cs`](../../Backend/src/Astronomy.MediaFactory.Publishing/TransientRetryHelper.cs)
- [`Backend/src/Astronomy.MediaFactory.Rendering/AstroPulseGalleryService.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/AstroPulseGalleryService.cs)
- [`Backend/src/Astronomy.MediaFactory.Rendering/Astronomy.MediaFactory.Rendering.csproj`](../../Backend/src/Astronomy.MediaFactory.Rendering/Astronomy.MediaFactory.Rendering.csproj)
- [`Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechClient.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechClient.cs)
- [`Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechSynthesisService.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/AzureSpeechSynthesisService.cs)
- [`Backend/src/Astronomy.MediaFactory.Rendering/Phase13GalleryAuthority.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/Phase13GalleryAuthority.cs)
- [`Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs`](../../Backend/src/Astronomy.MediaFactory.Rendering/WeeklySkyForecastAzureTtsSynthesizer.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryMediaPipelineInventory.cs`](../../Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryMediaPipelineInventory.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryMediaPipelinePlanner.cs`](../../Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryMediaPipelinePlanner.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/Phase7NarrationAuthorityOrchestrationContracts.cs`](../../Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/Phase7NarrationAuthorityOrchestrationContracts.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/CreativeQualityScoringEngine.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/CreativeQualityScoringEngine.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/EditorialProductContract.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/EditorialProductContract.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/GalleryIntelligenceAlignment.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/GalleryIntelligenceAlignment.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/HeroPromptMigrationService.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/HeroPromptMigrationService.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/ImageProviderProfiles.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/ImageProviderProfiles.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/LongStoryFramePlanner.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/LongStoryFramePlanner.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/PromptCompositionV2.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/PromptCompositionV2.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/ShortStoryFramePlanner.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/ShortStoryFramePlanner.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/StoryFrameGenerator.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/StoryFrameGenerator.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/StoryFramePromptPackage.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/StoryFramePromptPackage.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/VisualIntelligenceOrchestration.cs`](../../Backend/src/Astronomy.MediaFactory.Core/VisualIntelligence/VisualIntelligenceOrchestration.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/WeeklySkyForecast/AICinematicAssets/WeeklyAICinematicAssets.cs`](../../Backend/src/Astronomy.MediaFactory.Core/WeeklySkyForecast/AICinematicAssets/WeeklyAICinematicAssets.cs)
- [`Backend/src/Astronomy.MediaFactory.Core/WeeklySkyForecast/AudioGeneration/WeeklySkyForecastAudioGeneration.cs`](../../Backend/src/Astronomy.MediaFactory.Core/WeeklySkyForecast/AudioGeneration/WeeklySkyForecastAudioGeneration.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureConfigurationValidation.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureConfigurationValidation.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureOpenAiOptionsValidator.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureOpenAiOptionsValidator.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/ProductionStartupValidator.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/ProductionStartupValidator.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/SecureConfigurationExtensions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/SecureConfigurationExtensions.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/StartupValidationOptions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/StartupValidationOptions.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/DocumentaryBlueprint/Phase7ProviderIsolationAudit.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/DocumentaryBlueprint/Phase7ProviderIsolationAudit.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/OperationsHealthChecks.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/OperationsHealthChecks.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/OpsDashboardService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/OpsDashboardService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/RunOperationsService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/RunOperationsService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Optimization/AIOptimizationService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Optimization/AIOptimizationService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AstronomyVisualAssetStrategyService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AstronomyVisualAssetStrategyService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AzureTtsAudioGenerationService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/AzureTtsAudioGenerationService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/CategoryRequirementResolver.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/CategoryRequirementResolver.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/DailySkyGuideContextBuilder.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/DailySkyGuideContextBuilder.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/HeroAssetIntelligenceEngine.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/HeroAssetIntelligenceEngine.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/LongSceneImagePublicationService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/LongSceneImagePublicationService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/MatureThumbnailCandidatePublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/MatureThumbnailCandidatePublisher.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase14AudioSyncPublisher.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase14AudioSyncPublisher.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase8SceneAssetManifestValidator.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/Phase8SceneAssetManifestValidator.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ResponsiveHeroAuthorityService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ResponsiveHeroAuthorityService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ResponsiveThumbnailAuthorityService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ResponsiveThumbnailAuthorityService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/SceneAssetCertificationService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/SceneAssetCertificationService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/SceneAssetsV3Service.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/SceneAssetsV3Service.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ThumbnailAssetIntelligenceService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ThumbnailAssetIntelligenceService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ThumbnailV7Engine.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ThumbnailV7Engine.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/TtsPackagePlanningService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/TtsPackagePlanningService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/VideoAssemblyIntelligenceService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/VideoAssemblyIntelligenceService.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/WeeklySkyForecastV2FinalMedia.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/WeeklySkyForecastV2FinalMedia.cs)
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AIOptimizationServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AIOptimizationServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AssetExecutionServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AssetExecutionServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AstroPulseGalleryServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AstroPulseGalleryServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AstronomyAssetProducerPreviewServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AstronomyAssetProducerPreviewServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AzureOpenAiAICinematicImageGeneratorTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AzureOpenAiAICinematicImageGeneratorTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AzureOpenAiContentGenerationServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AzureOpenAiContentGenerationServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/AzureSpeechSynthesisServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/AzureSpeechSynthesisServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ContentPlanBatchGenerationServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ContentPlanBatchGenerationServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ContentPlanningGeneratePlanTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ContentPlanningGeneratePlanTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/CreativeQualityScoringEngineTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/CreativeQualityScoringEngineTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/DirectorTimelineServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/DirectorTimelineServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/EditorialProductContractTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/EditorialProductContractTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/FinalNarrationServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/FinalNarrationServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/HeroAssetStoryGeneratorTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/HeroAssetStoryGeneratorTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/HeroImageV4ComparisonTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/HeroImageV4ComparisonTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/HeroPromptMigrationServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/HeroPromptMigrationServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ImageProviderProfileRegistryTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ImageProviderProfileRegistryTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/LongStoryFramePlannerTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/LongStoryFramePlannerTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/MetaPublishingTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/MetaPublishingTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/NarrationPlanningAuthorityTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/NarrationPlanningAuthorityTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/OpsDashboardServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/OpsDashboardServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase12Phase13MatureVisualRestorationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase12Phase13MatureVisualRestorationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase13AzureImage2RetryTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase13AzureImage2RetryTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase7KnowledgePipelineIsolationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase7KnowledgePipelineIsolationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase7NarrationAuthorityOrchestrationStaticTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase7NarrationAuthorityOrchestrationStaticTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase8AuthorityArchitectureTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase8AuthorityArchitectureTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Phase9CertificationPropagationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Phase9CertificationPropagationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/PipelineAlertEmissionTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/PipelineAlertEmissionTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/PipelineOrchestratorSceneNarrationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/PipelineOrchestratorSceneNarrationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/PipelineStageInstrumentationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/PipelineStageInstrumentationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionPipelineExecutionServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/PromptComposerV2Tests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/PromptComposerV2Tests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/PublicMediaStorageServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/PublicMediaStorageServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/PublishingFlowTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/PublishingFlowTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2Phase6ValidationStateTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2Phase6ValidationStateTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/Rc2RealPipelineCertificationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/Rc2RealPipelineCertificationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/RecoveryOperationsTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/RecoveryOperationsTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ResponsiveHeroAuthorityServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ResponsiveHeroAuthorityServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ResponsiveHeroPositiveExecutionTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ResponsiveHeroPositiveExecutionTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ResponsiveThumbnailAuthorityServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ResponsiveThumbnailAuthorityServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/SceneAssetsV3ServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/SceneAssetsV3ServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ShortStoryFramePlannerTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ShortStoryFramePlannerTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ShortsVideoRenderServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ShortsVideoRenderServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/StartupValidationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/StartupValidationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/StoryFrameV4ComparisonTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/StoryFrameV4ComparisonTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ThumbnailAssetIntelligenceServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ThumbnailAssetIntelligenceServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ThumbnailV7CinematicOverlayRendererTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ThumbnailV7CinematicOverlayRendererTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/TtsAlignmentRepairServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/TtsAlignmentRepairServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/TtsAudioGenerationServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/TtsAudioGenerationServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/TtsPackageValidationServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/TtsPackageValidationServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/VideoAssemblyIntelligenceServiceTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/VideoAssemblyIntelligenceServiceTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/VisualIntelligenceContractsTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/VisualIntelligenceContractsTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/VisualIntelligenceOrchestratorTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/VisualIntelligenceOrchestratorTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/VisualQualityFrameworkTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/VisualQualityFrameworkTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/WeeklySkyForecastAudioGenerationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/WeeklySkyForecastAudioGenerationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/YouTubePublishingIntegrationTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/YouTubePublishingIntegrationTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/DocumentaryBlueprint/DocumentaryMediaPipelineArchitectureTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/DocumentaryBlueprint/DocumentaryMediaPipelineArchitectureTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/DocumentaryBlueprint/DocumentaryMediaPipelineInventoryTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/DocumentaryBlueprint/DocumentaryMediaPipelineInventoryTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/KnowledgeFoundation/ConfidenceAssessmentContractsTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/KnowledgeFoundation/ConfidenceAssessmentContractsTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/AzureSpeechDocumentaryNarrationProviderBindingTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/AzureSpeechDocumentaryNarrationProviderBindingTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryMediaVerificationDiTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryMediaVerificationDiTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationArchitectureTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationArchitectureTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationSynthesisResultMapperTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationSynthesisResultMapperTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationTestFixtures.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryNarrationTestFixtures.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionHostArchitectureTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionHostArchitectureTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionHostDiTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionHostDiTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionHostTestFixtures.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionHostTestFixtures.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionRequestBuilderTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionRequestBuilderTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryRealProviderSmokeTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryRealProviderSmokeTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryVariantCompositionDiTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryVariantCompositionDiTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryVisualAdapterTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryVisualAdapterTests.cs)
- [`Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/ExistingAzureSpeechDocumentaryNarrationAdapterTests.cs`](../../Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/ExistingAzureSpeechDocumentaryNarrationAdapterTests.cs)

## DOCX specification discovery

All 25 repository DOCX documents were parsed successfully using ZIP/XML; no Office macros or embedded binaries were executed. Matches below are documentation references, not additional service dependencies. No embedding/AI Search mention was found in these extracted documents. Exact runtime ownership remains determined by the source inventory and phase matrix.

| Repository-relative document | Azure mentions | OpenAI mentions | Speech mentions | Blob mentions |
|---|---:|---:|---:|---:|
| `docs/Astornomy/AI Future plan.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/AI Optimization Process.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/Category wise pipeline Plan.docx` | 8 | 0 | 8 | 0 |
| `docs/Astornomy/Exact flow.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/Features.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/My Evaluation by chatgpt.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/PHASES AUDIT.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/Platform Architecture Evolution.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/Platform Comletion.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/Roadmap.docx` | 1 | 1 | 0 | 0 |
| `docs/Astornomy/Run Local.docx` | 1 | 0 | 2 | 2 |
| `docs/Astornomy/Schedule Section.docx` | 3 | 3 | 0 | 0 |
| `docs/Astornomy/Testing Plan for AI Optimization and UI.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/Thumbnail Testing.docx` | 0 | 0 | 0 | 0 |
| `docs/Astornomy/YouTub eIntegration.docx` | 0 | 0 | 0 | 0 |
| `docs/Implementation Guide/Astronomy_Media_Factory_Phase_1_20_Master_Roadmap_v1.0.docx` | 12 | 2 | 29 | 0 |
| `docs/Implementation Guide/Astronomy_Phase_8_20_Existing_Implementation_Alignment_Plan_v1.0.docx` | 17 | 1 | 38 | 1 |
| `docs/implementation/Drashyam_Artifact_Contract_Reference_v1.0.docx` | 2 | 0 | 6 | 0 |
| `docs/implementation/Drashyam_Configuration_Operations_Guide_v1.0.docx` | 7 | 3 | 4 | 0 |
| `docs/implementation/Drashyam_RC2_Implementation_Specification_Library_v1.0.docx` | 5 | 1 | 10 | 0 |
| `docs/implementation/Drashyam_RC2_Orchestration_Code_Integration_Guide_v1.0.docx` | 4 | 1 | 5 | 0 |
| `docs/implementation/Drashyam_RC2_Pipeline_Development_Guide_v1.1_Final.docx` | 8 | 3 | 20 | 0 |
| `docs/implementation/Drashyam_RC2_Pipeline_Implementation_Guide_v1.0.docx` | 1 | 1 | 2 | 0 |
| `docs/implementation/RC2_Pipeline_Implementation_Guide_RPIG_v1.1.docx` | 1 | 1 | 2 | 0 |
| `docs/implementation/RPIG_v1.1_Review_Draft.docx` | 3 | 1 | 7 | 0 |
