# Configuration migration proposal

Audit date: 2026-10-21. Branch: `RecoveryBrach-18-05-26`. Source commit: `0e5ba735191036fdfac8c35c789805b83f03c785`. Audit only; no migration, model installation, generation, publication, or certification run was performed.

## Current state and why configuration alone is insufficient

API and Worker appsettings.json select Azure chat/image/Speech, set PublicMediaStorage.Enabled=true and Provider=AzureBlob, and require Azure OpenAI/Speech/Blob for production startup. Checked-in API publishing is enabled in Public mode; Worker defaults differ. Do not start these workflows casually during migration testing. No credentials were exposed by this audit.

Development bypasses ProductionStartupValidator, but AzureConfigurationValidation still checks partially populated sections. Nonempty Azure endpoint/deployment/region/account settings with empty credentials may remain invalid. OperationsConfigHealthCheck always calls ValidateBlob(requireConfiguration:true) and can stay unhealthy outside Development even after generation is local. Public storage DI validates Provider=AzureBlob; PrepareInstagramImageAsync repeats an AzureBlob/connection-string gate. These are **required code changes**, not settings tricks.

The recovery branch already removes the AIOptimization project's old net9.0 override and uses shared net10.0 props. The earlier main-branch onboarding framework mismatch is not evidence of a current-branch build failure. No current-branch build or test result is claimed here.

## Proposed additive configuration (illustration only)

This JSON is **not an existing supported configuration** and was not written into appsettings or environment settings. Reuse existing Speech, rendering, language, subtitle and public-media policy fields; add only provider selection/neutral endpoint/model metadata where needed.

```json
{
  "AIProviders": {
    "Text": {
      "Provider": "Ollama",
      "BaseUrl": "http://127.0.0.1:11434",
      "Model": "Qwen2.5-7B-Instruct-Q4_K_M@<reviewed-digest>",
      "ContextTokens": 8192,
      "MaxConcurrentRequests": 1,
      "AllowPaidFallback": false
    },
    "Speech": {
      "Provider": "LocalTts",
      "BaseUrl": "http://127.0.0.1:8011",
      "Engine": "Kokoro",
      "ModelDigest": "<required-reviewed-digest>",
      "VoiceMap": {
        "en": "<reviewed-English-voice>",
        "hi": "<reviewed-Hindi-voice>"
      }
    },
    "Image": {
      "Provider": "LocalDiffusion",
      "BaseUrl": "http://127.0.0.1:8188",
      "ModelDigest": "<required-reviewed-digest>",
      "MaxConcurrentRequests": 1
    }
  },
  "PublicMediaStorage": {
    "Enabled": true,
    "Provider": "FileSystemHttps",
    "RootPath": "<private-durable-staging-volume>",
    "PublicBaseUrl": "https://media.example.invalid",
    "SignedUrlLifetimeHours": 24
  }
}
```

Placeholders intentionally prevent accidental activation. No provider was installed and no model digest exists yet. Do not set Speech/Images to a provider that cannot load reviewed weights. No STT/Embedding section is needed for removing current Azure dependencies; add optional QA/alignment config only after capability approval. Defaults must fail clearly for missing models or invalid voices, not switch to a paid provider.

## Migration map

| Existing setting / owner | Proposed treatment | Validation required |
|---|---|---|
| AzureOpenAI section / typed service registrations | Retain as optional legacy provider; add selected text transport and adapters to existing typed contracts | Model reachable and pinned; context/output budgets; exact schema + semantic validation; no Azure calls when local selected |
| AzureOpenAIForImage section and four HTTP implementations | Retain historical/provider option; make shared provider selection and validation capability-specific | Cinematic/native aspect support, forbidden-science prompts, thumbnail/gallery no-fallback behavior, model license/digest |
| AzureSpeech + Speech options | Preserve language, rate, voice-profile intent; add reviewed local voice mapping | Remove hardcoded hi- naming dependency by validating language metadata; correctly interpret supported SSML, pauses, sample/codec formats |
| IWeeklySkyForecastTtsSynthesizer + bulk packages | Separate implementation selection while preserving public contracts | Weekly 96kbps MP3; bulk RIFF PCM; AzureSpeech package-name gate replaced with explicit version-compatible provider metadata for new packages |
| StartupValidationOptions / ProductionStartupValidator | Validate chosen required capabilities, not mandatory Azure resources | Test Production with no Azure keys and valid local models; missing local dependency produces explicit failure |
| AzureConfigurationValidation / options ValidateOnStart | Run provider-specific validation only for selected capabilities while retaining strict checks on selected Azure mode | Do not “fix” by globally disabling validation or inventing dummy keys |
| OperationsConfigHealthCheck | Selected storage/provider readiness, not unconditional AzureBlob validation | Selected archive + public staging reachable; database/queue health preserved |
| PublicMediaStorage.Provider=AzureBlob, ConnectionString, SasExpiryHours | Preserve legacy Azure mode, introduce filesystem HTTPS mode after code support; equivalent TTL/renewal semantics | Both DI and RC2 PrepareInstagramImageAsync gates must accept selected capability; signed URL checks/expiry and stable object keys |
| AzureBlob archival options | Retain optional legacy mode; select durable filesystem/S3 adapter | Empty upload result must not masquerade as persisted archive; explicit health/restore verification |
| KeyVault:VaultUri / KEYVAULT__VAULTURI | No mandatory vault; use secure bindings/mounted secrets where required | Empty/disabled only after confirming no required secrets are sourced there; keep social OAuth safe |
| Telemetry ApplicationInsights connection string | Optional; existing Serilog output can remain, optional OTEL later | Correlation and security redaction retained; no bootstrap dependency |
| ConnectionStrings:Postgres and DatabaseSafety:AllowLocalhostPostgres | Keep normal PostgreSQL; local host allowed only in intended dev deployment | Schema continuity, backups and OAuth/approval/publication records unchanged; no EF migration for provider swap |
| Rendering/Stellarium/fonts/output roots | Environment-specific paths remain under current contracts | Linux paths vs Windows defaults; valid ffmpeg/ffprobe/fonts; no mutation of certified outputs |

## Secret and network inventory for the proposed deployment

Local inference needs no API subscription key. Bind LLM/TTS/image endpoints privately and allow only worker/API callers. Initial model downloads need authoritative model hosts, artifact downloads and checksum/license snapshots; Hugging Face card access did not succeed in this audit. Do not change network policy as part of audit-only work.

Social publishing still needs actual OAuth client credentials/tokens, YouTube/Google and Meta API access, plus public HTTPS media DNS/TLS. Put URL signing keys, database credentials and social secrets in secure settings/mounts, never this JSON or chat. Existing platform Git authentication is unrelated to social OAuth or Azure resource authorization.

## Activation, historical compatibility and rollback

Future order: review model/license → implement and test adapter/config guards → test in isolated non-publishing workspace with fresh PlanId → read-only legacy compatibility checks → explicit canary provider selection → certify fresh media. Keep historical Azure settings/schema readers until existing artifacts can be read. Pin provider/engine/model/voice/prompt/codec versions for new identities. If canary fails, disable new generation or switch to another already certified local candidate; Azure is unavailable and cannot be assumed as a functioning rollback. Serving already approved immutable artifacts may continue if platform/approval policy permits; never replay posts automatically.

No configuration draft, appsettings, credential requirement, deployment workflow or network setting was changed by this audit.

## Repository evidence

- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs): Typed option validation, provider DI, hardcoded AzureBlob public storage restriction.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/ProductionStartupValidator.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/ProductionStartupValidator.cs): Global production startup gates and Development exception.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureConfigurationValidation.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/AzureConfigurationValidation.cs): Partial configuration validation.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/SecureConfigurationExtensions.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Configuration/SecureConfigurationExtensions.cs): Environment variables then optional Key Vault.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/OperationsHealthChecks.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Operations/OperationsHealthChecks.cs): Unconditional Blob validation in readiness.
- [`Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs`](../../Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/Rc2PublishingExecutionService.cs): PrepareInstagramImageAsync Azure-only gate.
- [`Backend/src/Astronomy.MediaFactory.Contracts/AzureSpeechOptions.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/AzureSpeechOptions.cs): Voice/rate resolution.
- [`Backend/src/Astronomy.MediaFactory.Contracts/PublicMediaStorageOptions.cs`](../../Backend/src/Astronomy.MediaFactory.Contracts/PublicMediaStorageOptions.cs): Actual existing storage configuration fields.
- [`Backend/src/Astronomy.MediaFactory.Api/appsettings.json`](../../Backend/src/Astronomy.MediaFactory.Api/appsettings.json): API current settings; credential values intentionally omitted.
- [`Backend/src/Astronomy.MediaFactory.Worker/appsettings.json`](../../Backend/src/Astronomy.MediaFactory.Worker/appsettings.json): Worker settings differ; must migrate both hosts.
- [`Backend/src/Astronomy.MediaFactory.AIOptimization/Astronomy.MediaFactory.AIOptimization.csproj`](../../Backend/src/Astronomy.MediaFactory.AIOptimization/Astronomy.MediaFactory.AIOptimization.csproj): No net9 override on this recovery branch.
- [`Backend/Directory.Build.props`](../../Backend/Directory.Build.props): Shared net10.0 target.
