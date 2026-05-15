# Configuration Guide

## Purpose
This guide documents the major configuration sections used by the backend, what each field means, and which values are safe defaults. All sections are bindable from `appsettings*.json`, environment variables, or Key Vault-backed configuration.

## General conventions
- **Required** means production startup validation or runtime logic expects a value when the feature is enabled.
- **Optional** means the system can run without the value in at least one supported mode.
- **Safe default** means the value already present in appsettings is suitable for local development or a conservative production baseline.

## AzureOpenAI
Used by the content-generation pipeline.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `Endpoint` | Yes in production | Absolute Azure OpenAI resource URI. | Empty for local placeholder only |
| `ApiKey` | Required unless `UseManagedIdentity=true` | Secret key for Azure OpenAI. | Empty |
| `ChatDeployment` | Yes in production | Deployment name used for script and metadata generation. | Empty |
| `UseManagedIdentity` | Optional | Set to `true` to avoid API keys in Azure-hosted deployments. | `false` |
| `ManagedIdentityClientId` | Optional | Needed for user-assigned managed identity. | Empty |

## AzureSpeech
Used for narration synthesis.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `Key` | Required unless `UseManagedIdentity=true` | Subscription key for Azure Speech. | Empty |
| `Region` | Required when using region-based auth or managed identity | Must align with the speech resource region. | Empty |
| `Endpoint` | Optional | Absolute endpoint alternative to region-based auth. | Empty |
| `UseManagedIdentity` | Optional | Enables managed-identity auth flow. | `false` |
| `ResourceId` | Required when `UseManagedIdentity=true` | Azure resource id for speech resource. | Empty |
| `ManagedIdentityClientId` | Optional | Needed only for user-assigned identity. | Empty |
| `Voice` | Optional | Neural voice name used for narration. | `en-US-FableMultilingualNeural` |

## AzureBlob
Used for artifact archival.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `ConnectionString` | Required unless using managed identity path | Storage account connection string. | Empty |
| `AccountName` | Required for one MI path | Use with managed identity when not using `ServiceUri`. | Empty |
| `ServiceUri` | Required for one MI path | Absolute blob service URI alternative to `AccountName`. | Empty |
| `UseManagedIdentity` | Optional | Enables MI-based blob access. | `false` |
| `ManagedIdentityClientId` | Optional | Needed for user-assigned identity. | Empty |
| `ContainerName` | Yes when Blob is enabled | Destination container for published assets. | `astronomy-videos` |
| `UploadRetryAttempts` | Optional | Retry count for transient storage failures. Keep between 1 and 5. | `3` |
| `RetryBaseDelaySeconds` | Optional | Base backoff delay. | `2` |
| `MaxRetryDelaySeconds` | Optional | Cap for retry backoff. | `20` |

## YouTube
Used for long-form and YouTube Shorts publishing.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `ClientId` | Required when `PublishingEnabled=true` | OAuth client id. | Empty |
| `ClientSecret` | Required when `PublishingEnabled=true` | OAuth client secret. | Empty |
| `ApplicationName` | Optional | Client application label. | `AstronomyVideoGenerator` |
| `PrivacyStatus` | Optional | Must be `private`, `public`, or `unlisted`. | `private` |
| `RefreshToken` | Required when publishing and not using token file | Preferred for secret-store injection. | Empty |
| `AccessToken` | Optional | Runtime convenience only; not required for startup validation. | Empty |
| `TokenFilePath` | Required when publishing and no refresh token is set | Alternative to direct token injection. Relative paths are resolved from the current process if that file exists, otherwise from `Maintenance:WorkingDirectory`/`Rendering:WorkingDirectory`. | Empty |
| `PublishingEnabled` | Optional | Master enable switch for YouTube publishing. | `false` |
| `UploadRetryAttempts` | Optional | Retry count for publish operations. | `3` |
| `RetryBaseDelaySeconds` | Optional | Retry base delay. | `2` |
| `MaxRetryDelaySeconds` | Optional | Retry cap. | `20` |
| `PublishRetryCooldownSeconds` | Optional | Anti-storm cooldown between retries. | `30` |

## PlatformPublishing
Controls short-form distribution behavior.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `YouTubeShortsEnabled` | Optional | Enables Shorts publication path when requested. | `true` |
| `InstagramReelsEnabled` | Optional | Keep `false` unless the Meta upload workflow is implemented and configured. | `false` |
| `FacebookEnabled` | Optional | Keep `false` unless the Meta upload workflow is implemented and configured. | `false` |
| `YouTubeShortsPreferredPublishLocalTime` | Optional | Metadata hint only. | `19:30` |
| `InstagramReelsPreferredPublishLocalTime` | Optional | Metadata hint only. | `21:00` |
| `FacebookPreferredPublishLocalTime` | Optional | Metadata hint only. | `20:30` |
| `PublishRetryAttempts` | Optional | Platform publish retry count. | `3` |
| `RetryBaseDelaySeconds` | Optional | Retry base delay. | `2` |
| `MaxRetryDelaySeconds` | Optional | Retry cap. | `20` |
| `PublishRetryCooldownSeconds` | Optional | Prevents rapid duplicate attempts. | `30` |

## Scheduling
Controls worker-triggered automation and queue retry behavior.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `DailySkyGuideCron` | Optional | Quartz cron for daily sky guide job enqueue. | `0 0 18 * * ?` |
| `TelescopeTargetsCron` | Optional | Quartz cron for telescope targets job enqueue. | `0 0 19 * * ?` |
| `SpaceNewsCron` | Optional | Quartz cron for space news job enqueue. | `0 0 20 * * ?` |
| `AstrophotographyTipsCron` | Optional | Quartz cron for astrophotography tips job enqueue. | `0 0 21 * * ?` |
| `MaxRetryAttempts` | Optional | Max queue-job attempts before permanent failure. Must be greater than 0. | `3` |
| `RetryBackoffSeconds` | Optional | Retry delay multiplier for queue jobs. | `60` |
| `QueuePollIntervalSeconds` | Optional | Worker polling delay when no jobs are runnable. | `10` |

## Operations
Controls monitoring and startup-validation posture.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `RetainDays` | Optional | General operational retention value used by monitoring summaries. | `30` |
| `SlowStageThresholdMs` | Optional | Stage latency threshold for slow-stage warnings. | `10000` |
| `EnableDetailedStageMetadata` | Optional | Keeps rich stage metadata for troubleshooting. | `true` |
| `EnforceProductionValidation` | Optional | Intended to keep production startup requirements strict. | `true` |

## Alerting
Controls operational notifications.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `Enabled` | Optional | Master switch for alert routing. | `false` |
| `NotifyOnStageFailed` | Optional | Emit alerts on stage failures. | `true` |
| `NotifyOnStageSlow` | Optional | Emit alerts on slow stages. | `true` |
| `NotifyOnPublishFailed` | Optional | Emit alerts on publishing failures. | `true` |
| `NotifyOnPipelineFailed` | Optional | Emit alerts on full run failures. | `true` |
| `NotifyOnQueueBacklogHigh` | Optional | Emit alerts when backlog grows. | `true` |
| `NotifyOnHealthDegraded` | Optional | Emit alerts on degraded readiness. | `true` |
| `NotifyOnPublishSucceeded` | Optional | Usually left off to reduce noise. | `false` |
| `SlowStageThresholdMs` | Optional | Alert-specific slow threshold. | `10000` |
| `QueueBacklogThreshold` | Optional | Backlog threshold for alerting. | `25` |
| `DedupWindowSeconds` | Optional | Prevents duplicate alert storms. | `120` |
| `SlackWebhookUrl` | Required only when `Enabled=true` and Slack is the target | Must be absolute when provided. | Empty |

## Maintenance
Controls retention and stale-job handling.

| Field | Required | Notes | Safe default |
|---|---|---|---|
| `WorkingFileRetentionDays` | Optional | Retention for on-disk run artifacts. | `14` |
| `JobRetentionDays` | Optional | Retention for historical job records. | `30` |
| `StageRetentionDays` | Optional | Retention for pipeline stage records. | `30` |
| `AnalyticsRetentionDays` | Optional | Retention for analytics snapshots. | `90` |
| `StaleJobThresholdMinutes` | Optional | Threshold for stale-job recovery checks. | `60` |
| `WorkingDirectory` | Optional | Base directory for generated outputs and cleanup scope. | `./media-output` |

## Related sections worth reviewing
- **`StartupValidation`**: toggles production validation requirements.
- **`KeyVault`**: adds secure secret sourcing.
- **`Telemetry`**: controls Application Insights hookup.
- **`Rendering`**: FFmpeg and working-directory settings.
- **`SkyfieldSidecar`** and **`Stellarium`**: optional astronomy and visual-generation dependencies.
- **`Monetization`**: affiliate and sponsor copy behavior.

## Recommended safe production baseline
- keep `StartupValidation` strict,
- keep `YouTube:PublishingEnabled=false` until credentials are confirmed,
- keep `PlatformPublishing:InstagramReelsEnabled=false` and `PlatformPublishing:FacebookEnabled=false`,
- enable Blob archival,
- enable alerting only after verifying the webhook target,
- use managed identity and Key Vault where possible.
