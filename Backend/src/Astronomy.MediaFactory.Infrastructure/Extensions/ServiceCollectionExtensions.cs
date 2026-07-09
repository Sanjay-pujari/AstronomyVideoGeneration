using System.Net.Http;
using Astronomy.MediaFactory.AIOptimization;
using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.AstroData.Services;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure;
using Astronomy.MediaFactory.Infrastructure.Alerting;
using Astronomy.MediaFactory.Infrastructure.Analytics;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Astronomy.MediaFactory.Infrastructure.Operations;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Optimization;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Infrastructure.Scheduling;
using Astronomy.MediaFactory.Publishing;
using Astronomy.MediaFactory.Rendering;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NasaAssets;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Configuration;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Services;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Confidence;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EventScoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Astronomy.MediaFactory.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediaFactory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VisualIntelligenceOptions>()
            .Bind(configuration.GetSection(VisualIntelligenceOptions.SectionName));
        services.AddOptions<EditorialIntelligenceOptions>()
            .Bind(configuration.GetSection(EditorialIntelligenceOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<OutputArtifactsOptions>()
            .Bind(configuration.GetSection(OutputArtifactsOptions.SectionName));
        services.AddVisualIntelligenceOrchestration();

        services.AddOptions<ProductionPipelineOptions>()
            .Bind(configuration.GetSection(ProductionPipelineOptions.SectionName))
            .Validate(options => options.StaleRunningThresholdMinutes > 0, "ProductionPipeline:StaleRunningThresholdMinutes must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<RenderingOptions>()
            .Bind(configuration.GetSection(RenderingOptions.SectionName))
            .Configure(options => configuration.GetSection(RenderingOptions.VideoRenderSectionName).Bind(options))
            .Configure(options => configuration.GetSection(RenderingOptions.VideoEncodingSectionName).Bind(options))
            .Validate(opt => opt.VideoWidth > 0 && opt.VideoHeight > 0 && opt.FrameRate > 0, "Rendering dimensions and frame rate must be > 0.")
            .ValidateOnStart();

        services.AddOptions<TypographyOptions>()
            .Bind(configuration.GetSection(TypographyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<ITypographyResolver, TypographyResolver>();

        services.AddOptions<VideoAssemblyOptions>()
            .Bind(configuration.GetSection(VideoAssemblyOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<SubtitleTtsOptions>()
            .Bind(configuration.GetSection(SubtitleTtsOptions.SectionName))
            .Validate(options => options.SubtitleMaxWordsPerCue > 0, "SubtitleTtsOptions:SubtitleMaxWordsPerCue must be greater than zero.")
            .Validate(options => options.SubtitleMaxLines > 0, "SubtitleTtsOptions:SubtitleMaxLines must be greater than zero.")
            .Validate(options => options.SubtitleMaxCharsPerLine > 0, "SubtitleTtsOptions:SubtitleMaxCharsPerLine must be greater than zero.")
            .Validate(options => options.SubtitleMinCueDurationMs >= 0 && options.SubtitleMaxCueDurationMs > 0 && options.SubtitleMaxCueDurationMs >= options.SubtitleMinCueDurationMs, "SubtitleTtsOptions subtitle duration settings are invalid.")
            .Validate(options => options.CueGapMs >= 0 && options.SentenceBreakPauseMs >= 0, "SubtitleTtsOptions pause/gap settings must be zero or greater.")
            .ValidateOnStart();

        services.AddOptions<AstronomyApiOptions>()
            .Bind(configuration.GetSection(AstronomyApiOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CelestialAssetsOptions>()
            .Bind(configuration.GetSection(CelestialAssetsOptions.SectionName))
            .Validate(options => options.MaxImagesPerObject > 0, "CelestialAssets:MaxImagesPerObject must be greater than 0.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "CelestialAssets:RootPath is required.")
            .Validate(options => options.AllowedExtensions.Count > 0, "CelestialAssets:AllowedExtensions must not be empty.")
            .ValidateOnStart();

        services.AddOptions<NasaImagesOptions>()
            .Bind(configuration.GetSection(NasaImagesOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.SearchBaseUrl, UriKind.Absolute, out _), "NasaImages:SearchBaseUrl must be an absolute URI.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.SearchEndpoint), "NasaImages:SearchEndpoint is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.AssetEndpoint), "NasaImages:AssetEndpoint is required.")
            .ValidateOnStart();

        services.AddOptions<AzureOpenAiOptions>()
            .Bind(configuration.GetSection(AzureOpenAiOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<AzureOpenAIForImageOptions>()
            .Bind(configuration.GetSection(AzureOpenAIForImageOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<WeeklySkyForecastAICinematicAssetsOptions>()
            .Bind(configuration.GetSection(WeeklySkyForecastAICinematicAssetsOptions.SectionName))
            .Validate(options => options.MaxAssetsPerRun >= 0, "WeeklySkyForecast:AICinematicAssets:MaxAssetsPerRun must be zero or greater.")
            .Validate(options => options.EffectiveMaxGenerationSeconds > 0, "WeeklySkyForecast:AICinematicAssets:MaxGenerationSeconds must be greater than zero.")
            .Validate(options => options.SingleImageTimeoutSeconds > 0, "WeeklySkyForecast:AICinematicAssets:SingleImageTimeoutSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<WeeklySkyForecastAssetExpansionOptions>()
            .Bind(configuration.GetSection(WeeklySkyForecastAssetExpansionOptions.SectionName))
            .Validate(options => string.Equals(options.Mode, "PlanningOnly", StringComparison.OrdinalIgnoreCase) || string.Equals(options.Mode, "ExecuteExpandedScenes", StringComparison.OrdinalIgnoreCase), "WeeklySkyForecast:AssetExpansion:Mode must be PlanningOnly or ExecuteExpandedScenes.")
            .Validate(options => options.MaxExpandedScenesPerRun >= 0, "WeeklySkyForecast:AssetExpansion:MaxExpandedScenesPerRun must be zero or greater.")
            .Validate(options => options.MaxFramesPerExpandedScene > 0, "WeeklySkyForecast:AssetExpansion:MaxFramesPerExpandedScene must be greater than zero.")
            .Validate(options => options.ExpandedExecutionTimeoutSeconds > 0, "WeeklySkyForecast:AssetExpansion:ExpandedExecutionTimeoutSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AzureOpenAiOptions>, AzureOpenAiOptionsValidator>();

        services.AddOptions<AzureSpeechOptions>()
            .Bind(configuration.GetSection(AzureSpeechOptions.SectionName))
            .Configure(options => ApplySpeechSpeedOptions(configuration.GetSection(SpeechOptions.SectionName).Get<SpeechOptions>(), options))
            .Validate(options => !AzureConfigurationValidation.ValidateSpeech(options, requireConfiguration: false).Any(), "AzureSpeech settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<AzureBlobOptions>()
            .Bind(configuration.GetSection(AzureBlobOptions.SectionName))
            .Validate(options => !AzureConfigurationValidation.ValidateBlob(options, requireConfiguration: false).Any(), "AzureBlob settings are invalid.")
            .Validate(options => options.UploadRetryAttempts is > 0 and <= 5 && options.RetryBaseDelaySeconds > 0 && options.MaxRetryDelaySeconds >= options.RetryBaseDelaySeconds, "AzureBlob retry settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<KeyVaultOptions>()
            .Bind(configuration.GetSection(KeyVaultOptions.SectionName))
            .Validate(options => !AzureConfigurationValidation.ValidateKeyVault(options).Any(), "KeyVault settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<YouTubeOptions>()
            .Bind(configuration.GetSection(YouTubeOptions.SectionName))
            .PostConfigure(options => ResolveRelativeYouTubeTokenFilePath(configuration, options))
            .Validate(options => string.IsNullOrWhiteSpace(options.PrivacyStatus) || options.PrivacyStatus is "private" or "public" or "unlisted", "YouTube:PrivacyStatus must be private, public, or unlisted.")
            .Validate(options => string.IsNullOrWhiteSpace(options.DefaultPrivacyStatus) || options.DefaultPrivacyStatus is "private" or "public" or "unlisted", "YouTube:DefaultPrivacyStatus must be private, public, or unlisted.")
            .Validate(options => options.UploadRetryAttempts is > 0 and <= 5 && options.RetryBaseDelaySeconds > 0 && options.MaxRetryDelaySeconds >= options.RetryBaseDelaySeconds && options.PublishRetryCooldownSeconds > 0, "YouTube retry settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<MetaOptions>()
            .Bind(configuration.GetSection(MetaOptions.SectionName))
            .Validate(options => options.Scopes is { Count: > 0 }, "Meta:Scopes must include at least one OAuth scope.")
            .ValidateOnStart();

        services.AddOptions<MetaPublishingOptions>()
            .Bind(configuration.GetSection(MetaPublishingOptions.SectionName))
            .Validate(options => options.Mode is null || options.Mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) || options.Mode.Equals("DryRun", StringComparison.OrdinalIgnoreCase) || options.Mode.Equals("Private", StringComparison.OrdinalIgnoreCase) || options.Mode.Equals("Public", StringComparison.OrdinalIgnoreCase), "MetaPublishing:Mode must be Disabled, DryRun, Private, or Public.")
            .Validate(options => options.FacebookSimpleUploadMaxBytes >= 0 && options.FacebookUploadChunkSizeBytes > 0, "MetaPublishing Facebook full-video upload size settings are invalid.")
            .Validate(options => options.GraphRetryMaxAttempts is > 0 and <= 10 && options.GraphRetryBaseDelaySeconds >= 0, "MetaPublishing Graph retry settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<PublishingTargetsOptions>()
            .Bind(configuration.GetSection(PublishingTargetsOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<PublicMediaStorageOptions>()
            .Bind(configuration.GetSection(PublicMediaStorageOptions.SectionName))
            .Validate(options => !options.Enabled || options.Provider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase), "PublicMediaStorage:Provider must be AzureBlob when public media storage is enabled.")
            .Validate(options => options.SasExpiryHours > 0, "PublicMediaStorage:SasExpiryHours must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<PlatformPublishingOptions>()
            .Bind(configuration.GetSection(PlatformPublishingOptions.SectionName))
            .Validate(options => options.PublishRetryAttempts is > 0 and <= 5 && options.RetryBaseDelaySeconds > 0 && options.MaxRetryDelaySeconds >= options.RetryBaseDelaySeconds && options.PublishRetryCooldownSeconds > 0, "Platform publishing retry settings are invalid.")
            .ValidateOnStart();


        services.AddOptions<MonetizationOptions>()
            .Bind(configuration.GetSection(MonetizationOptions.SectionName))
            .Validate(options => string.IsNullOrWhiteSpace(options.AffiliateBaseUrl) || Uri.TryCreate(options.AffiliateBaseUrl, UriKind.Absolute, out _), "Monetization:AffiliateBaseUrl must be an absolute URI when provided.")
            .ValidateOnStart();

        services.AddOptions<GrowthOptions>()
            .Bind(configuration.GetSection(GrowthOptions.SectionName))
            .Validate(options => string.IsNullOrWhiteSpace(options.WebsiteUrl) || Uri.TryCreate(options.WebsiteUrl, UriKind.Absolute, out _), "Growth:WebsiteUrl must be an absolute URI when provided.")
            .Validate(options => string.IsNullOrWhiteSpace(options.NewsletterUrl) || Uri.TryCreate(options.NewsletterUrl, UriKind.Absolute, out _), "Growth:NewsletterUrl must be an absolute URI when provided.")
            .Validate(options => string.IsNullOrWhiteSpace(options.AppDownloadUrl) || Uri.TryCreate(options.AppDownloadUrl, UriKind.Absolute, out _), "Growth:AppDownloadUrl must be an absolute URI when provided.")
            .ValidateOnStart();

        services.AddOptions<SchedulingOptions>()
            .Bind(configuration.GetSection(SchedulingOptions.SectionName))
            .Validate(opt => opt.MaxRetryAttempts > 0 && opt.RetryBackoffSeconds > 0 && opt.QueuePollIntervalSeconds > 0, "Scheduling values must be > 0.")
            .ValidateOnStart();

        services.AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
            .Validate(opt => opt.FetchIntervalMinutes > 0 && opt.TopN > 0, "Analytics values must be > 0.")
            .ValidateOnStart();

        services.AddOptions<AIOptimizationOptions>()
            .Bind(configuration.GetSection(AIOptimizationOptions.SectionName))
            .Validate(opt => opt.MinimumAnalyticsRows > 0 && !string.IsNullOrWhiteSpace(opt.OutputFileName), "AIOptimization minimum rows and output file name are required.")
            .ValidateOnStart();

        services.AddOptions<AstronomyEventsOptions>()
            .Bind(configuration.GetSection(AstronomyEventsOptions.SectionName))
            .Validate(opt => opt.LookAheadDays > 0 && opt.RefreshEveryHours > 0 && opt.MinimumContentOpportunityScore is >= 0 and <= 1 && opt.MediumEventThreshold is >= 0 and <= 1 && opt.MajorEventThreshold is >= 0 and <= 1 && opt.MaxInjectedEventsPerDailyGuide >= 0 && opt.MaxSpecialEventVideosPerDay >= 0, "AstronomyEvents values are invalid.")
            .ValidateOnStart();

        services.AddOptions<TopicSelectionOptions>()
            .Bind(configuration.GetSection(TopicSelectionOptions.SectionName))
            .Validate(opt => opt.RepetitionWindowDays > 0, "TopicSelection:RepetitionWindowDays must be > 0.")
            .ValidateOnStart();

        services.AddOptions<OperationsOptions>()
            .Bind(configuration.GetSection(OperationsOptions.SectionName))
            .Validate(opt => opt.RetainDays > 0 && opt.SlowStageThresholdMs > 0, "Operations values must be > 0.")
            .ValidateOnStart();

        services.AddOptions<PublishingValidationOptions>()
            .Bind(configuration.GetSection(PublishingValidationOptions.SectionName));

        services.AddOptions<TokenHealthOptions>()
            .Bind(configuration.GetSection(TokenHealthOptions.SectionName))
            .Validate(options => options.RefreshBeforeExpiryDays >= 0, "TokenHealth:RefreshBeforeExpiryDays must be >= 0.")
            .ValidateOnStart();

        services.AddOptions<PublishingOptions>()
            .Bind(configuration.GetSection(PublishingOptions.SectionName))
            .Validate(options => options.Mode is "Disabled" or "DryRun" or "Private" or "Public", "Publishing:Mode must be Disabled, DryRun, Private, or Public.")
            .Validate(options => string.IsNullOrWhiteSpace(options.DefaultPrivacyStatus) || options.DefaultPrivacyStatus is "private" or "public" or "unlisted", "Publishing:DefaultPrivacyStatus must be private, public, or unlisted.")
            .ValidateOnStart();

        services.AddOptions<MaintenanceOptions>()
            .Bind(configuration.GetSection(MaintenanceOptions.SectionName))
            .Validate(opt => opt.WorkingFileRetentionDays > 0 && opt.JobRetentionDays > 0 && opt.StageRetentionDays > 0 && opt.AnalyticsRetentionDays > 0 && opt.StaleJobThresholdMinutes > 0, "Maintenance values must be > 0.")
            .ValidateOnStart();

        services.AddOptions<AlertingOptions>()
            .Bind(configuration.GetSection(AlertingOptions.SectionName))
            .Validate(opt => !opt.Enabled || string.IsNullOrWhiteSpace(opt.SlackWebhookUrl) || Uri.TryCreate(opt.SlackWebhookUrl, UriKind.Absolute, out _), "Alerting:SlackWebhookUrl must be an absolute URI when provided.")
            .ValidateOnStart();

        services.AddOptions<AlertsOptions>()
            .Bind(configuration.GetSection(AlertsOptions.SectionName))
            .Validate(opt => opt.GenerateEveryMinutes > 0 && opt.SendEveryMinutes > 0 && opt.DefaultMinimumEventScore is >= 0 and <= 1 && opt.MaxAlertsPerSubscriberPerDay > 0, "Alerts settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(opt => opt.SmtpPort > 0, "Email:SmtpPort must be > 0.")
            .ValidateOnStart();

        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
            .Validate(options => options.CollectEveryMinutes > 0 && options.CollectForRecentDays > 0, "Analytics collection values must be > 0.")
            .ValidateOnStart();


        services.AddOptions<ContentDurationOptions>()
            .Bind(configuration.GetSection(ContentDurationOptions.SectionName))
            .Validate(options => options.DailySkyGuideMinutes > 0 && options.SpecialEventGuideMinutes > 0, "ContentDuration long-form targets must be greater than 0.")
            .Validate(options => options.YouTubeShortSeconds > 0 && options.InstagramReelSeconds > 0 && options.FacebookReelSeconds > 0, "ContentDuration short-form targets must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<ContentExpansionOptions>()
            .Bind(configuration.GetSection(ContentExpansionOptions.SectionName))
            .Validate(options => options.MinObjectsPerGuide > 0 && options.MaxObjectsPerGuide >= options.MinObjectsPerGuide, "ContentExpansion object bounds are invalid.")
            .Validate(options => options.MinimumVisibilityScore is >= 0 and <= 1, "ContentExpansion:MinimumVisibilityScore must be between 0 and 1.")
            .ValidateOnStart();

        services.AddOptions<LocalizationOptions>()
            .Bind(configuration.GetSection(LocalizationOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.DefaultLanguage), "Localization:DefaultLanguage is required.")
            .Validate(options => options.SupportedLanguages.Count > 0, "Localization:SupportedLanguages must include at least one language.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.FallbackLanguage), "Localization:FallbackLanguage is required.")
            .ValidateOnStart();

        services.AddOptions<SchedulerOptions>()
            .Bind(configuration.GetSection(SchedulerOptions.SectionName))
            .Validate(options => options.MaxConcurrentRuns > 0, "Scheduler:MaxConcurrentRuns must be greater than 0.")
            .Validate(options => options.Schedules.All(schedule => !string.IsNullOrWhiteSpace(schedule.Name)), "Scheduler schedules must have names.")
            .Validate(options => options.Schedules.All(schedule => schedule.Latitude is >= -90 and <= 90), "Scheduler schedule Latitude must be between -90 and 90.")
            .Validate(options => options.Schedules.All(schedule => schedule.Longitude is >= -180 and <= 180), "Scheduler schedule Longitude must be between -180 and 180.")
            .Validate(options => options.Schedules.All(schedule => TimeOnly.TryParse(schedule.LocalRunTime, out _)), "Scheduler schedule LocalRunTime must use HH:mm format.")
            .Validate(options => options.Regions.Items.All(region => !string.IsNullOrWhiteSpace(region.RegionId) && !string.IsNullOrWhiteSpace(region.DisplayName)), "Regions must have RegionId and DisplayName.")
            .Validate(options => options.Regions.Items.All(region => region.Latitude is >= -90 and <= 90), "Region Latitude must be between -90 and 90.")
            .Validate(options => options.Regions.Items.All(region => region.Longitude is >= -180 and <= 180), "Region Longitude must be between -180 and 180.")
            .Validate(options => options.Regions.Items.All(region => TimeOnly.TryParse(region.LocalRunTime, out _)), "Region LocalRunTime must use HH:mm format.")
            .Validate(options => options.Regions.Items.All(region => !string.IsNullOrWhiteSpace(region.Language)), "Region Language is required.")
            .ValidateOnStart();


        services.AddOptions<ObservationOptions>()
            .Bind(configuration.GetSection(ObservationOptions.SectionName))
            .Validate(options => options.Latitude is >= -90 and <= 90, "Observation:Latitude must be between -90 and 90.")
            .Validate(options => options.Longitude is >= -180 and <= 180, "Observation:Longitude must be between -180 and 180.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Timezone), "Observation:Timezone is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.LocationName), "Observation:LocationName is required.")
            .Validate(options => options.DefaultObservationHour is >= 0 and <= 23, "Observation:DefaultObservationHour must be between 0 and 23.")
            .Validate(options => options.SkyOverviewMinutesAfterSunset >= 0, "Observation:SkyOverviewMinutesAfterSunset must be >= 0.")
            .Validate(options => options.Overview.Mode is "AttractiveOnly" or "PolarisOnly" or "Hybrid", "Observation:Overview:Mode must be AttractiveOnly, PolarisOnly, or Hybrid.")
            .ValidateOnStart();

        services.AddOptions<ThumbnailAIOptimizationOptions>()
            .Bind(configuration.GetSection(ThumbnailAIOptimizationOptions.SectionName));

        services.AddOptions<ThumbnailOptions>()
            .Bind(configuration.GetSection(ThumbnailOptions.SectionName))
            .Configure(options => configuration.GetSection("Thumbnail").Bind(options))
            .Validate(opt => opt.LongThumbnailWidth > 0 && opt.LongThumbnailHeight > 0 && opt.ShortThumbnailWidth > 0 && opt.ShortThumbnailHeight > 0, "Thumbnail dimensions must be > 0.")
            .Validate(opt => opt.MaxSupportObjectsLong is >= 0 and <= 2, "ThumbnailGeneration:MaxSupportObjectsLong must be between 0 and 2.")
            .Validate(opt => opt.MaxSupportObjectsShort is >= 0 and <= 1, "ThumbnailGeneration:MaxSupportObjectsShort must be between 0 and 1.")
            .Validate(opt => opt.JpegQuality is > 0 and <= 100, "ThumbnailGeneration:JpegQuality must be between 1 and 100.")
            .ValidateOnStart();

        services.AddOptions<ThumbnailFontOptions>()
            .Bind(configuration.GetSection(ThumbnailFontOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.DefaultEnglishFont), "ThumbnailFonts:DefaultEnglishFont is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.HindiFont), "ThumbnailFonts:HindiFont is required.")
            .ValidateOnStart();

        services.AddOptions<CelestialAssetPackOptions>()
            .Bind(configuration.GetSection(CelestialAssetPackOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<ThumbnailCinematicAIOptions>()
            .Bind(configuration.GetSection(ThumbnailCinematicAIOptions.SectionName));

        services.AddOptions<StellariumOptions>()
            .Bind(configuration.GetSection(StellariumOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => string.IsNullOrWhiteSpace(options.ExecutablePath) || Path.IsPathRooted(options.ExecutablePath), "Stellarium:ExecutablePath must be an absolute path when provided.")
            .Validate(options => string.IsNullOrWhiteSpace(options.ScriptsDirectory) || Path.IsPathRooted(options.ScriptsDirectory), "Stellarium:ScriptsDirectory must be an absolute path when provided.")
            .Validate(options => string.IsNullOrWhiteSpace(options.CaptureDirectory) || Path.IsPathRooted(options.CaptureDirectory), "Stellarium:CaptureDirectory must be an absolute path when provided.")
            .ValidateOnStart();

        services.AddOptions<SkyfieldSidecarOptions>()
            .Bind(configuration.GetSection(SkyfieldSidecarOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !options.Enabled || Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "SkyfieldSidecar:BaseUrl must be an absolute URI when enabled.")
            .Validate(options => options.TimeoutSeconds > 0, "SkyfieldSidecar:TimeoutSeconds must be > 0.")
            .Validate(options => options.YearlyAccuracyTimeoutSeconds > 0, "SkyfieldSidecar:YearlyAccuracyTimeoutSeconds must be > 0.")
            .ValidateOnStart();

        services.AddOptions<StartupValidationOptions>()
            .Bind(configuration.GetSection(StartupValidationOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<OptimizationOptions>()
            .Bind(configuration.GetSection(OptimizationOptions.SectionName))
            .Validate(options => options.MinimumDataPoints > 0, "Optimization:MinimumDataPoints must be greater than 0.")
            .Validate(options => options.ConfidenceThreshold is >= 0 and <= 1, "Optimization:ConfidenceThreshold must be between 0 and 1.")
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StartupValidationOptions>, ProductionStartupValidator>();
        services.AddHostedService<ObsoleteConfigurationWarningHostedService>();
        services.AddSingleton<IRuntimeAssetPathResolver, RuntimeAssetPathResolver>();
        services.AddHostedService<RuntimeAssetValidationHostedService>();
        services.AddHostedService<CelestialAssetWarmupHostedService>();

        services.AddHttpClient<NasaApodClient>();
        services.AddHttpClient<NasaNeoWsClient>();
        services.AddHttpClient<ICelestialAssetIngestionService, CelestialAssetIngestionService>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<INasaImagesClient, NasaImagesClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<INasaAssetDownloader, NasaAssetDownloader>(client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped<INasaAssetSelector, NasaAssetSelector>();
        services.AddScoped<INasaAssetRealizationService, NasaAssetRealizationService>();
        services.AddScoped<ICelestialAssetProvider, CelestialAssetProvider>();
        services.AddHttpClient<MinorPlanetCenterClient>();
        services.AddHttpClient<ISkyfieldSidecarClient, SkyfieldSidecarClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SkyfieldSidecarOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(120, options.TimeoutSeconds));
        });
        services.AddHttpClient<ISkyfieldVisibilityClient, SkyfieldVisibilityClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SkyfieldSidecarOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        var cs = configuration.GetConnectionString("Postgres")
                 ?? configuration["ConnectionStrings:Postgres"];

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Missing Postgres connection string. Set ConnectionStrings:Postgres to your Azure Postgres connection string.");

        // Safety guard: block localhost unless explicitly allowed.
        // Set one of the following to true to allow localhost:
        // - Env var: ALLOW_LOCALHOST_POSTGRES=true
        // - Config: DatabaseSafety:AllowLocalhostPostgres=true
        var allowLocalhost = configuration.GetValue<bool>("DatabaseSafety:AllowLocalhostPostgres")
                             || string.Equals(Environment.GetEnvironmentVariable("ALLOW_LOCALHOST_POSTGRES"), "true", StringComparison.OrdinalIgnoreCase);

        if (!allowLocalhost)
        {
            var csb = new NpgsqlConnectionStringBuilder(cs);
            var host = (csb.Host ?? "").Trim();
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || host.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to use a localhost Postgres connection. Either set DatabaseSafety:AllowLocalhostPostgres=true (or env ALLOW_LOCALHOST_POSTGRES=true), " +
                    $"or update ConnectionStrings:Postgres to your Azure Postgres host. Current Host='{host}'.");
            }
        }

        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<MediaFactoryDbContext>(o => o.UseNpgsql(cs));
        services.AddScoped<IPipelineRepository, EfPipelineRepository>();
        services.AddScoped<IAstronomyContextProvider, AstronomyContextProvider>();
        services.AddScoped<IAstronomyEventStore, EfAstronomyEventStore>();
        services.AddScoped<IAstronomyEventScoringService, AstronomyEventScoringService>();
        services.AddScoped<IAstronomyEventDiscoveryService, AstronomyEventDiscoveryService>();
        services.AddScoped<IAstronomyEventDecisionService, AstronomyEventDecisionService>();
        services.AddScoped<IObservationWindowService, ObservationWindowService>();
        services.AddScoped<ITopicRankingService, TopicRankingService>();
        services.AddScoped<ITopicSelectionService, TopicSelectionService>();
        services.AddScoped<IObservationTimeService, ObservationTimeService>();
        services.AddScoped<IVisualAssetProvider, StellariumVisualGenerationService>();
        services.AddScoped<IPromptBuilder, PromptBuilder>();
        services.AddScoped<IMetadataOptimizationService, MetadataOptimizationService>();
        services.AddScoped<IContentMonetizationService, ContentMonetizationService>();
        services.AddHttpClient<AzureOpenAiContentGenerationService>();
        services.AddHttpClient<AzureOpenAICinematicImageGenerator>();
        services.AddScoped<IMetadataOptimizationModelClient>(sp => sp.GetRequiredService<AzureOpenAiContentGenerationService>());
        services.AddScoped<IScriptGenerationService>(sp => sp.GetRequiredService<AzureOpenAiContentGenerationService>());
        services.AddScoped<IShortsScriptGenerationService>(sp => sp.GetRequiredService<AzureOpenAiContentGenerationService>());
        services.AddScoped<ISsmlBuilder, SsmlBuilder>();
        services.AddScoped<IAzureSpeechClient, AzureSpeechClient>();
        services.AddScoped<IFileSystem, PhysicalFileSystem>();
        services.AddScoped<IProcessRunner, ProcessRunner>();
        services.AddScoped<RenderManifestBuilder>();
        services.AddScoped<FfmpegArgumentBuilder>();
        services.AddScoped<ISpeechSynthesisService, AzureSpeechSynthesisService>();
        services.AddScoped<IVideoRenderService, FfmpegVideoRenderService>();
        services.AddScoped<IThumbnailStrategyService, ThumbnailStrategyService>();
        services.AddScoped<IThumbnailScoringService, ThumbnailScoringService>();
        services.AddScoped<IThumbnailCtrScoringService, ThumbnailCtrScoringService>();
        services.AddScoped<IThumbnailAiOptimizationService, ThumbnailAiOptimizationService>();
        services.AddScoped<IThumbnailMoodGradingService, ThumbnailMoodGradingService>();
        services.AddScoped<IThumbnailVisualHierarchyService, ThumbnailVisualHierarchyService>();
        services.AddScoped<ICinematicThumbnailAiService, CinematicThumbnailAiService>();
        services.AddScoped<IThumbnailHookService, ThumbnailHookService>();
        services.AddScoped<IThumbnailCandidateSelector, ThumbnailCandidateSelector>();
        services.AddScoped<IThumbnailCompositionService, ThumbnailCompositionService>();
        services.AddScoped<ICinematicCollageComposer, CinematicCollageComposer>();
        services.AddScoped<ICelestialAssetPackExtractor, CelestialAssetPackExtractor>();
        services.AddScoped<ICinematicThumbnailService, LocalAssetCollageThumbnailService>();
        services.AddScoped<IThumbnailGenerationService, LocalAssetCollageThumbnailService>();
        services.AddScoped<IThumbnailRenderer, ThumbnailRenderer>();
        services.AddScoped<IThumbnailGeneratorService, ThumbnailGeneratorService>();
        services.AddScoped<ISeoMetadataGeneratorService, SeoMetadataGeneratorService>();
        services.AddScoped<IAzureBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<IPublicMediaStorageService, AzureBlobPublicMediaStorageService>();
        services.AddScoped<IMetaThumbnailAssetPublisher, Astronomy.MediaFactory.Publishing.MetaThumbnailAssetPublisher>();
        services.AddScoped<IYouTubePublishingService, YouTubePublishingService>();
        services.AddScoped<IYouTubeThumbnailPublisher>(sp => (IYouTubeThumbnailPublisher)sp.GetRequiredService<IYouTubePublishingService>());
        services.AddHttpClient<IYouTubeAuthService, YouTubeAuthService>();
        services.AddHttpClient<IYouTubeOAuthService, YouTubeOAuthService>();
        services.AddHttpClient<IMetaOAuthService, MetaOAuthService>();
        services.AddHttpClient<IFacebookReelPublishService, FacebookReelPublishService>(client => client.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.AddHttpClient<IFacebookVideoPublishService, FacebookVideoPublishService>();
        services.AddHttpClient<IInstagramReelPublishService, InstagramReelPublishService>(client => client.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.AddScoped<IMetaPosterFrameFallbackService, MetaPosterFrameFallbackService>();
        services.AddScoped<IMetaPublishService, MetaPublishService>();
        services.AddHttpClient<ITokenHealthService, TokenHealthService>();
        services.AddScoped<ITokenHealthReportWriter, TokenHealthReportWriter>();
        services.AddHostedService<TokenHealthStartupHostedService>();
        services.AddScoped<IYouTubeApiClient, GoogleYouTubeApiClient>();
        services.AddScoped<IPlatformThumbnailResolver, PlatformThumbnailResolver>();
        services.AddScoped<IYouTubePublishService, YouTubePublishService>();
        services.AddScoped<IContentPublishService, ContentPublishService>();
        services.AddScoped<IYouTubeAnalyticsService, YouTubeAnalyticsService>();
        services.AddScoped<IYouTubeAnalyticsCollector, YouTubeAnalyticsCollector>();
        services.AddScoped<IPlatformAnalyticsCollector>(sp => sp.GetRequiredService<IYouTubeAnalyticsCollector>());
        services.AddHttpClient<IFacebookAnalyticsCollector, FacebookAnalyticsCollector>();
        services.AddScoped<IPlatformAnalyticsCollector>(sp => sp.GetRequiredService<IFacebookAnalyticsCollector>());
        services.AddHttpClient<IInstagramAnalyticsCollector, InstagramAnalyticsCollector>();
        services.AddScoped<IPlatformAnalyticsCollector>(sp => sp.GetRequiredService<IInstagramAnalyticsCollector>());
        services.AddScoped<IAnalyticsCollectionService, AnalyticsCollectionService>();
        services.AddHostedService<AnalyticsCollectionBackgroundService>();
        services.AddScoped<IShortsVideoRenderService, ShortsVideoRenderService>();
        services.AddScoped<IShortFormPlatformMetadataFormatter>(sp => new PlatformMetadataFormatter(sp.GetRequiredService<IOptions<PlatformPublishingOptions>>().Value, sp.GetRequiredService<IOptions<GrowthOptions>>().Value));
        services.AddScoped<IShortFormPlatformPublisher, YouTubeShortsPlatformPublisher>();
        services.AddScoped<IShortFormPlatformPublisher, InstagramReelsPlatformPublisher>();
        services.AddScoped<IShortFormPlatformPublisher, FacebookPlatformPublisher>();
        services.AddScoped<IShortFormPublishingService, ShortFormPublishingService>();
        services.AddScoped<IAnalyticsAggregationService, AnalyticsAggregationService>();
        services.AddScoped<IAnalyticsIntelligenceService, AnalyticsIntelligenceService>();
        services.AddScoped<IAnalyticsIngestionService, ManualAnalyticsIngestionService>();
        services.AddScoped<ISafeAnalyticsExecutor, SafeAnalyticsExecutor>();
        services.AddScoped<IOptimizationService, RuleBasedOptimizationService>();
        services.AddScoped<IHookOptimizationService, HookOptimizationService>();
        services.AddScoped<ITrendSignalProvider, StaticTrendSignalProvider>();
        services.AddScoped<IPublishingOptimizationService, PublishingOptimizationService>();
        services.AddScoped<IAIOptimizationPipelineService, AIOptimizationPipelineService>();
        services.AddHttpClient<IAIOptimizationService, AIOptimizationService>();
        services.AddScoped<IContentExperimentService, EfContentExperimentService>();
        services.AddScoped<IFeedbackSignalExtractor, TopKeywordSignalExtractor>();
        services.AddScoped<IFeedbackSignalExtractor, TopHookSignalExtractor>();
        services.AddScoped<IAnalyticsFeedbackProvider, AnalyticsFeedbackProvider>();
        services.AddScoped<IPromptFeedbackService, PromptFeedbackService>();
        services.AddScoped<StellariumScriptBuilder>(sp =>
            new StellariumScriptBuilder(sp.GetRequiredService<IOptions<StellariumOptions>>().Value));
        services.AddScoped<IPrePublishValidationService, PrePublishValidationService>();
        services.AddScoped<IPipelineStageExecutor, PipelineStageExecutor>();
        services.AddScoped<IPipelineRecoveryService, PipelineRecoveryService>();
        services.AddSingleton<ISchedulerAuditStore, JsonSchedulerAuditStore>();
        services.AddSingleton<IPipelineRunQueue, PipelineRunQueue>();
        services.AddSingleton<PipelineSchedulerService>();
        services.AddSingleton<IPipelineSchedulerService>(sp => sp.GetRequiredService<PipelineSchedulerService>());
        services.AddHostedService(sp => sp.GetRequiredService<PipelineSchedulerService>());
        services.AddScoped<IContentCategorySettingsService, ContentCategorySettingsService>();
        services.AddScoped<IContentVarietyGuard, ContentVarietyGuard>();
        services.AddScoped<IContentPlanningService, ContentPlanningService>();
        services.AddScoped<IContentPlanProductionRequestMapper, ContentPlanProductionRequestMapper>();
        services.AddScoped<ProductionPipelineExecutionService>();
        services.AddScoped<IProductionPipelineExecutionService>(sp => sp.GetRequiredService<ProductionPipelineExecutionService>());
        services.AddScoped<IProductionPhaseRunner>(sp => sp.GetRequiredService<ProductionPipelineExecutionService>());
        services.AddScoped<IContentPlanProductionExecutionService, ContentPlanProductionExecutionService>();
        services.AddScoped<IProductionRunningRecoveryService, ProductionRunningRecoveryService>();
        services.AddScoped<ContentPlanBatchGenerationService>();
        services.AddScoped<IContentPlanBatchGenerationService>(sp => sp.GetRequiredService<ContentPlanBatchGenerationService>());
        services.AddScoped<IContentPlanGenerationReadinessService>(sp => sp.GetRequiredService<ContentPlanBatchGenerationService>());
        services.AddScoped<Rc2PipelinePhaseRegistry>();
        services.AddScoped<SceneIntentBuilder>();
        services.AddScoped<CreativeStoryboardBuilder>();
        services.AddScoped<NarrationPromptComposer>();
        services.AddScoped<Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries.DocumentaryVocabulary>();
        services.AddScoped<Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries.DocumentaryTransitionLibrary>();
        services.AddScoped<Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries.DocumentaryFactTransformer>();
        services.AddScoped<Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Directors.DocumentaryStyleDirector>();
        services.AddScoped<Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Directors.IDocumentaryStyleDirector>(sp => sp.GetRequiredService<Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Directors.DocumentaryStyleDirector>());
        services.AddScoped<IPromptComposer<NarrationPromptComposerInput, NarrationPromptComposerOutput>>(sp => sp.GetRequiredService<NarrationPromptComposer>());
        services.AddScoped<NarrationGeneratorV5>();
        services.AddScoped<Rc2ContentPlanningBatchOrchestrator>();
        services.AddScoped<IManualCategoryPreparationOrchestrator, ManualCategoryPreparationOrchestrator>();
        services.AddScoped<ICategoryProductionPipelineStrategy, DailySkyGuideProductionPipelineStrategy>();
        services.AddScoped<ICategoryProductionPipelineStrategy, WeeklySkyForecastProductionPipelineStrategy>();
        services.AddScoped<ICategoryProductionRunner, CategoryProductionRunner>();
        services.AddScoped<IProductionPreviewOutputValidator, ProductionPreviewOutputValidator>();
        services.AddScoped<IAssetAwareManualRunPreparationService, AssetAwareManualRunPreparationService>();
        services.AddScoped<ICategoryRequirementResolver, CategoryRequirementResolver>();
        services.AddScoped<IVisualStrategyResolver, VisualStrategyResolver>();
        services.AddScoped<IAstronomyVisibilityService, AstronomyVisibilityService>();
        services.AddScoped<IAstronomyEventConsolidationService, AstronomyEventConsolidationService>();
        services.AddScoped<IAstronomyEventDetectionService, AstronomyEventDetectionService>();
        services.AddScoped<IAstronomyContentOpportunityService, AstronomyContentOpportunityService>();
        services.AddScoped<IAstronomyCategoryReadinessService, AstronomyCategoryReadinessService>();
        services.AddScoped<IAstronomyVideoPlanningService, AstronomyVideoPlanningService>();
        services.AddScoped<IAstronomyAssetPlanningService, AstronomyAssetPlanningService>();
        services.AddScoped<IEventProductionIntelligenceAdapter, AstronomyEventProductionIntelligenceAdapter>();
        services.AddScoped<IMediaEventStrategyResolver, MediaEventStrategyResolver>();
        services.AddScoped<IVisualSourceResolver, DefaultVisualSourceResolver>();
        services.AddScoped<IMediaEventStrategy, MeteorShowerStrategy>();
        services.AddScoped<IMediaEventStrategy, PlanetPairingStrategy>();
        services.AddScoped<IMediaEventStrategy, PlanetGroupingStrategy>();
        services.AddScoped<IMediaEventStrategy, ConjunctionStrategy>();
        services.AddScoped<IMediaEventStrategy, NamedFullMoonStrategy>();
        services.AddScoped<IMediaEventStrategy, NewMoonStrategy>();
        services.AddScoped<IMediaEventStrategy, LunarEclipseStrategy>();
        services.AddScoped<IMediaEventStrategy, SolarEclipseStrategy>();
        services.AddScoped<IMediaEventStrategy, GenericAstronomyEventStrategy>();
        services.AddScoped<IEventSceneValidationStrategyResolver, EventSceneValidationStrategyResolver>();
        services.AddScoped<IEventSceneValidationStrategy, MeteorShowerSceneValidationStrategy>();
        services.AddScoped<IEventSceneValidationStrategy, PlanetPairingSceneValidationStrategy>();
        services.AddScoped<IEventSceneValidationStrategy, ConjunctionSceneValidationStrategy>();
        services.AddScoped<IEventSceneValidationStrategy, NamedFullMoonSceneValidationStrategy>();
        services.AddScoped<IEventSceneValidationStrategy, NewMoonSceneValidationStrategy>();
        services.AddScoped<IEventSceneValidationStrategy, LunarEclipseSceneValidationStrategy>();
        services.AddScoped<IEventSceneValidationStrategy, SolarEclipseSceneValidationStrategy>();
        services.AddScoped<IEventSceneValidationStrategy, GenericEventSceneValidationStrategy>();
        services.AddScoped<IProductionPipelineQualityValidator, ProductionPipelineQualityValidator>();
        services.AddScoped<IQuestionEngine, AstronomyQuestionEngine>();
        services.AddScoped<IHeroAssetIntelligenceEngine, HeroAssetIntelligenceEngine>();
        services.AddScoped<IHeroAssetSceneSelector, HeroAssetSceneSelector>();
        services.AddScoped<IHeroCompositionEngine, HeroCompositionEngine>();
        services.AddScoped<IHeroAssetStoryGenerator, HeroAssetStoryGenerator>();
        services.AddScoped(_ => new ThumbnailV7CinematicOverlayRenderer());
        services.AddScoped<IThumbnailAssetIntelligenceService, ThumbnailAssetIntelligenceService>();
        services.AddScoped<IVideoAssemblyIntelligenceService, VideoAssemblyIntelligenceService>();
        services.AddScoped<IQuestionScenePlanner, QuestionScenePlanner>();
        services.AddScoped<IQuestionSceneIntentEnricher, QuestionSceneIntentEnricher>();
        services.AddScoped<IQuestionDrivenNarrationGenerator, QuestionDrivenNarrationGenerator>();
        services.AddScoped<IQuestionDrivenImagePromptGenerator, QuestionDrivenImagePromptGenerator>();
        services.AddScoped<IAstronomyInfographicDesignSystem, AstronomyInfographicDesignSystem>();
        services.AddScoped<AstronomyBackgroundLayerRenderer>();
        services.AddScoped<CelestialObjectLayerRenderer>();
        services.AddScoped<SkyGuidanceLayerRenderer>();
        services.AddScoped<EducationalLayerRenderer>();
        services.AddScoped<AnnotationLayerRenderer>();
        services.AddScoped<IAstronomyInfographicRenderer, AstronomyInfographicRenderer>();
        services.AddScoped<QuestionDrivenVisualComposer>();
        services.AddScoped<IQuestionDrivenVisualComposer>(sp => sp.GetRequiredService<QuestionDrivenVisualComposer>());
        services.AddScoped<IEditorialAstronomyInfographicComposer, EditorialAstronomyInfographicComposer>();
        services.AddScoped<IAstronomyVisualAssetStrategyService, AstronomyVisualAssetStrategyService>();
        services.AddScoped<IInfographicLayoutBlueprintGenerator, InfographicLayoutBlueprintGenerator>();
        services.AddScoped<INarrationPlanningService, NarrationPlanningService>();
        services.AddScoped<IDirectorNarrationService, DirectorNarrationService>();
        services.AddScoped<IFinalNarrationService, FinalNarrationService>();
        services.AddScoped<IPolishedNarrationService, PolishedNarrationService>();
        services.AddScoped<ITtsPackagePlanningService, TtsPackagePlanningService>();
        services.AddScoped<ITtsPackageValidationService, TtsPackageValidationService>();
        services.AddScoped<ITtsAlignmentRepairService, TtsAlignmentRepairService>();
        services.AddScoped<ITtsAudioGenerationService, AzureTtsAudioGenerationService>();
        services.AddScoped<IDirectorTimelineService, DirectorTimelineService>();
        services.AddScoped<ISceneAssemblyPlanService, SceneAssemblyPlanService>();
        services.AddScoped<IRenderRecipeGenerator, RenderRecipeGenerator>();
        services.AddScoped<IRenderCapabilityMatrixService, RenderCapabilityMatrixService>();
        services.AddScoped<ISceneRenderer, FfmpegSceneRenderer>();
        services.AddScoped<IVisualAssetGenerationService, VisualAssetGenerationService>();
        services.AddScoped<ISceneAssetsV3Service, SceneAssetsV3Service>();
        services.AddScoped<IProductionVisualComposerService, ProductionVisualComposerService>();
        services.AddScoped<ISceneEditorialPreviewService, SceneEditorialPreviewService>();
        services.AddScoped<IAstronomyAssetProductionJobService, AstronomyAssetProductionJobService>();
        services.AddScoped<IAstronomyProductionMonitoringService, AstronomyProductionMonitoringService>();
        services.AddScoped<IAssetExecutionService, AssetExecutionService>();
        services.AddScoped<ISkyMapCardExecutionService, SkyMapCardExecutionService>();
        services.AddScoped<IConstellationGuideExecutionService, ConstellationGuideExecutionService>();
        services.AddScoped<IStellariumScreenshotExecutionService, StellariumScreenshotExecutionService>();
        services.AddScoped<INasaAssetExecutionService, NasaAssetExecutionService>();
        services.AddScoped<IAiImagePromptExecutionService, AiImagePromptExecutionService>();
        services.AddScoped<IStellariumCapturePreviewService, StellariumCapturePreviewService>();
        services.AddScoped<IStellariumCaptureExecutionService, StellariumCaptureExecutionService>();
        services.AddScoped<IAstronomyAssetProducer, TextOverlayAssetProducer>();
        services.AddScoped<IAstronomyAssetProducer, ThumbnailConceptAssetProducer>();
        services.AddScoped<IAstronomyAssetProducer, StellariumScreenshotAssetProducer>();
        services.AddScoped<IAstronomyAssetProducer, ConstellationGuideAssetProducer>();
        services.AddScoped<IAstronomyAssetProducer, SkyMapCardAssetProducer>();
        services.AddScoped<IAstronomyAssetProducer, NasaAssetProducer>();
        services.AddScoped<IAstronomyAssetProducer, AiImageAssetProducer>();
        services.AddScoped<IAstronomyAssetProducerPreviewService, AstronomyAssetProducerPreviewService>();
        services.AddScoped<IAstronomyEventDiscoveryPreviewService, AstronomyEventDiscoveryPreviewService>();
        services.AddHttpClient<ISkyfieldAccuracyProvider, SkyfieldSidecarAccuracyProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SkyfieldSidecarOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.YearlyAccuracyTimeoutSeconds);
        });
        services.AddScoped<IAstronomyEventVerificationService, AstronomyEventVerificationService>();
        services.AddScoped<IAstronomyEventVerifiedImportService, AstronomyEventVerifiedImportService>();
        services.AddScoped<IStellariumScriptGenerator, StellariumScriptGenerator>();
        services.AddScoped<IStellariumImageCaptureExecutor, StellariumImageCaptureExecutor>();
        services.AddScoped<IDailySkyGuideVisualAssetPackager, DailySkyGuideVisualAssetPackager>();
        services.AddScoped<IDailySkyGuideVisualAssetProvider, CapturedDailySkyGuideVisualAssetProvider>();
        services.AddScoped<IDailySkyGuideAssetAwareContextService, DailySkyGuideAssetAwareContextService>();
        services.AddScoped<IDailySkyGuideAssetAwareCompositionPlanner, DailySkyGuideAssetAwareCompositionPlanner>();
        services.AddScoped<IAssetAwareCompositionPlanner, DailySkyGuideAssetAwareCompositionPlanner>();
        services.AddScoped<IAssetAwarePreviewVideoComposer, FfmpegAssetAwarePreviewVideoComposer>();
        services.AddScoped<IDailySkyGuidePreviewVideoGenerator, DailySkyGuidePreviewVideoGenerator>();
        services.AddScoped<IAssetAwareCompositionPlannerResolver, AssetAwareCompositionPlannerResolver>();
        services.AddScoped<IDailySkyGuideVisualAssetConsumer, NoOpDailySkyGuideVisualAssetConsumer>();
        services.AddScoped<IStellariumScenePlanner, DailySkyGuideStellariumScenePlanner>();
        services.AddScoped<IStellariumScenePlannerResolver, StellariumScenePlannerResolver>();
        services.AddScoped<IRegionResolutionService, RegionResolutionService>();
        services.AddScoped<IDailySkyGuideContextBuilder, DailySkyGuideContextBuilder>();
        services.AddScoped<IWeeklySkyForecastContextBuilder, WeeklySkyForecastContextBuilder>();
        services.AddScoped<IWeeklySkyForecastContextBuilderV2, WeeklySkyForecastContextBuilder>();
        services.AddScoped<IWeeklySkyForecastSegmentPlanner, WeeklySkyForecastSegmentPlanner>();
        services.AddScoped<IWeeklySkyForecastSscScenePlanner, LegacyWeeklyVisualAssetGenerator>();
        services.AddScoped<ICategoryOutputPathResolver, CategoryOutputPathResolver>();
        services.AddScoped<IWeeklySkyForecastMetadataBuilder, WeeklySkyForecastMetadataBuilder>();
        services.AddScoped<IWeeklySkyForecastPreparationOrchestrator, WeeklySkyForecastPreparationOrchestrator>();
        services.AddScoped<IWeeklySkyForecastSceneRenderingOrchestrator, WeeklySkyForecastSceneRenderingOrchestrator>();
        services.AddScoped<IAstroPulseGalleryService, AstroPulseGalleryService>();
        services.AddScoped<IWeeklySkyForecastTimelineCompositionOrchestrator, WeeklySkyForecastTimelineCompositionOrchestrator>();
        services.AddScoped<IWeeklySkyForecastFinalMediaOrchestrator, WeeklySkyForecastFinalMediaOrchestrator>();
        services.AddScoped<IWeeklySkyForecastVisualAssetGenerationService, WeeklySkyForecastVisualAssetGenerationService>();
        services.AddScoped<IWeeklySkyForecastSegmentVideoRenderer, WeeklySkyForecastSegmentVideoRenderer>();
        services.AddScoped<IWeeklySkyForecastV2EventIntelligenceBuilder, WeeklySkyForecastV2EventIntelligenceBuilder>();
        services.AddScoped<IWeeklyAstronomyEventExtractor, WeeklyAstronomyEventExtractor>();
        services.AddScoped<IWeeklySkyForecastV2EditorialIntelligenceBuilder, WeeklySkyForecastV2EditorialIntelligenceBuilder>();
        services.AddScoped<IWeeklySkyForecastV2CinematicEditorialRefiner, WeeklySkyForecastV2CinematicEditorialRefiner>();
        services.AddScoped<IWeeklySkyForecastV2NarrativeAbstractionBuilder, WeeklySkyForecastV2NarrativeAbstractionBuilder>();
        services.AddScoped<IWeeklySkyForecastV2NarrationPlanner, WeeklySkyForecastV2NarrationPlanner>();
        services.AddScoped<IWeeklySkyForecastV2NarrationTextGenerator, WeeklySkyForecastV2NarrationTextGenerator>();
        services.AddScoped<INarrationV31Composer, NarrationV31Composer>();
        services.AddScoped<IObservationConsistencyEngine, ObservationConsistencyEngine>();
        services.AddScoped<IObservationConfidenceEngine, ObservationConfidenceEngine>();
        services.AddScoped<IEditorialIntelligenceService, EditorialIntelligenceService>();
        services.AddScoped<INarrationGenerationService, NarrationGenerationService>();
        services.AddScoped<NarrationTimeFormatter>();
        services.AddScoped<IWeeklySkyForecastV2AssetResolver, WeeklySkyForecastV2AssetResolver>();
        services.AddScoped<IWeeklySkyForecastV2EditorialNormalizer, WeeklySkyForecastV2EditorialNormalizer>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture.WeeklyEpisodeStructurePolicy>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture.WeeklyEpisodeDurationPolicy>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture.WeeklyEpisodeVisualPolicy>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture.WeeklyEpisodeNarrationPolicy>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture.WeeklyEpisodePlanPersister>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture.WeeklyEpisodeArchitectureService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification.WeeklySegmentClassificationPolicy>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification.WeeklySegmentClassifier>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification.WeeklySegmentClassificationPersister>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification.WeeklySegmentClassificationService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification.SegmentDiversificationPolicy>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification.SegmentVisualDiversityAnalyzer>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification.SegmentPacingDiversityAnalyzer>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification.SegmentDiversificationPersister>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification.WeeklySegmentDiversificationService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning.VisualAssetPriorityScorer>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning.VisualAssetMixAnalyzer>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning.VisualAssetPlanningPersister>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning.WeeklyVisualAssetPlanningService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion.AssetExpansionPolicy>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion.SegmentCoverageAnalyzer>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion.UniqueSceneRequirementBuilder>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion.AssetExpansionPersister>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion.AssetExpansionValidator>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion.WeeklyAssetExpansionService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization.WeeklyAssetRealizationPersister>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization.WeeklyAssetRealizationValidator>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization.WeeklyAssetRealizationService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization.WeeklyNarrationVisualTimelineComposer>();
        services.AddScoped<IWeeklyEventPriorityScoringEngine, WeeklyEventPriorityScoringEngine>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine.IWeeklyNarrationEngineV2, Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine.WeeklyNarrationEngineV2>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition.IWeeklyTimelineCompositionEngine, Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition.WeeklyTimelineCompositionEngine>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.IWeeklyFfmpegRenderPreparationEngine, Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.WeeklyFfmpegRenderPreparationEngine>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.IWeeklyPipelineRunDirectoryResolver, WeeklyPipelineRunDirectoryResolver>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.IWeeklyExistingRunVideoRenderer, Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.WeeklyExistingRunVideoRenderer>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.IWeeklyAudioDrivenTimelineReconciliationService, Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.WeeklyAudioDrivenTimelineReconciliationService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.IWeeklyVisualIntentEngine, Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering.WeeklyVisualIntentEngine>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration.IWeeklySkyForecastAudioGenerationService, Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration.WeeklySkyForecastAudioGenerationService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration.IWeeklySkyForecastTtsSynthesizer, WeeklySkyForecastAzureTtsSynthesizer>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.AICinematicStylePolicy>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.AICinematicPromptBuilder>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.IAICinematicAssetQueueBuilder, Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.AICinematicAssetQueueBuilder>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.IAICinematicAssetSelector, Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.AICinematicAssetSelector>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.IAICinematicAssetPersister, Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.AICinematicAssetPersister>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.AICinematicAssetPersister>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.IAICinematicAssetValidator, Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.AICinematicAssetValidator>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.AICinematicAssetValidator>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.IAICinematicAssetGenerator>(sp => sp.GetRequiredService<AzureOpenAICinematicImageGenerator>());
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.IAICinematicImageGenerator>(sp => sp.GetRequiredService<AzureOpenAICinematicImageGenerator>());
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.IAICinematicAssetRealizationService, Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.WeeklyAICinematicAssetGenerationService>();
        services.AddScoped<Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets.WeeklyAICinematicAssetGenerationService>();
        services.AddScoped<IWeeklyStoryboardComposer, WeeklyStoryboardComposer>();
        services.AddScoped<IWeeklyConjunctionFramingEngine, WeeklyConjunctionFramingEngine>();
        services.AddScoped<IAstronomicalGroupingComposer, AstronomicalGroupingComposer>();
        services.AddScoped<IWeeklyDynamicFovCalculator, WeeklyDynamicFovCalculator>();
        services.AddScoped<IWeeklySscSceneBuilder, WeeklySscSceneBuilder>();
        services.AddScoped<IWeeklySkySceneComposer, WeeklySkySceneComposer>();
        services.AddScoped<IWeeklyScreenshotQualityValidator, WeeklyScreenshotQualityValidator>();
        services.AddScoped<IWeeklyCinematicShotExpansionEngine, WeeklyCinematicShotExpansionEngine>();
        services.AddScoped<IWeeklyCameraPathEngine, WeeklyCameraPathEngine>();
        services.AddScoped<IWeeklyCinematicCompositionEngine, WeeklyCinematicCompositionEngine>();
        services.AddScoped<IWeeklyMotionClipRenderer, WeeklyMotionClipRenderer>();
        services.AddScoped<IWeeklyMotionRenderManifestBuilder, WeeklyMotionRenderManifestBuilder>();
        services.AddScoped<IWeeklySkyForecastV2IntelligenceService, WeeklySkyForecastV2IntelligenceService>();
        services.AddScoped<IWeeklyStellariumScriptWriter, WeeklyStellariumScriptWriter>();
        services.AddScoped<IWeeklyStellariumScriptExecutor, WeeklyStellariumScriptExecutor>();
        services.AddScoped<IStellariumScriptExecutionService>(sp => (IStellariumScriptExecutionService)sp.GetRequiredService<IWeeklyStellariumScriptExecutor>());
        services.AddScoped<IWeeklyStellariumScreenshotGenerator, WeeklyStellariumScreenshotGenerator>();
        services.AddScoped<IExternalProcessRunner, ExternalProcessRunner>();
        services.AddScoped<IFFmpegService, FFmpegService>();
        services.AddScoped<IFFprobeService, FFprobeService>();
        services.AddScoped<IMediaValidationService, MediaValidationService>();

        services.AddScoped<IContentCategoryPipelineStrategy, DailySkyGuidePipelineStrategy>();
        services.AddScoped<IContentCategoryPipelineStrategyResolver, ContentCategoryPipelineStrategyResolver>();
        services.AddScoped<IContentCategoryPipeline, DailySkyGuideContentPipeline>();
        services.AddScoped<PipelineOrchestrator>();
        services.AddScoped<IPipelineRunExecutor, OrchestratorPipelineRunExecutor>();
        services.AddScoped<IPipelineJobQueue, PipelineJobQueue>();
        services.AddScoped<IPipelineJobExecutor, PipelineJobExecutor>();
        services.AddScoped<PipelineJobProcessor>();
        services.AddScoped<IPipelineStageRecorder, PipelineStageRecorder>();
        services.AddScoped<AlertingRouter>();
        services.AddScoped<AlertMessageFormatter>();
        services.AddSingleton<AlertNoiseSuppressor>();
        services.AddHttpClient<SlackWebhookOperationalAlertPublisher>();
        services.AddScoped<IOperationalAlertChannel>(sp => sp.GetRequiredService<SlackWebhookOperationalAlertPublisher>());
        services.AddScoped<IOperationalAlertPublisher>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AlertingOptions>>().Value;
            if (!options.Enabled)
                return new NoOpOperationalAlertPublisher();

            var channels = sp.GetServices<IOperationalAlertChannel>().ToArray();
            return channels.Length == 0
                ? new NoOpOperationalAlertPublisher()
                : new ChannelFanOutOperationalAlertPublisher(channels);
        });
        services.AddScoped<IOperationalAlertNotifier, SafeOperationalAlertNotifier>();
        services.AddScoped<IStageAlertPublisher, RoutingStageAlertPublisher>();
        services.AddScoped<IPipelineMonitoringService, PipelineMonitoringService>();
        services.AddHttpClient<IOpsDashboardService, OpsDashboardService>();
        services.AddScoped<IRunOperationsService, RunOperationsService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<ISkyAlertService, SkyAlertService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<SkyAlertGenerationService>();
        services.AddSingleton<ISkyAlertGenerationService>(sp => sp.GetRequiredService<SkyAlertGenerationService>());
        services.AddHostedService(sp => sp.GetRequiredService<SkyAlertGenerationService>());

        services.AddHealthChecks()
            .AddCheck<DatabaseConnectivityHealthCheck>("database", tags: ["ready"])
            .AddCheck<QueueProcessorReadinessHealthCheck>("queue", tags: ["ready"])
            .AddCheck<OperationsConfigHealthCheck>("config", tags: ["ready"]);

        return services;
    }

    private static void ResolveRelativeYouTubeTokenFilePath(IConfiguration configuration, YouTubeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TokenFilePath) || Path.IsPathRooted(options.TokenFilePath))
        {
            return;
        }

        var serviceExecutablePath = YouTubeTokenResolver.ResolveTokenFilePath(options);
        if (File.Exists(serviceExecutablePath))
        {
            options.TokenFilePath = serviceExecutablePath;
            return;
        }

        var configuredPath = Path.GetFullPath(options.TokenFilePath);
        if (File.Exists(configuredPath))
        {
            options.TokenFilePath = configuredPath;
            return;
        }

        var workingDirectoryCandidates = new[]
            {
                configuration.GetSection(MaintenanceOptions.SectionName).Get<MaintenanceOptions>()?.WorkingDirectory,
                configuration.GetSection(RenderingOptions.SectionName).Get<RenderingOptions>()?.WorkingDirectory
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(Path.GetFullPath(path!), options.TokenFilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existingTokenPath = workingDirectoryCandidates.FirstOrDefault(File.Exists);
        options.TokenFilePath = existingTokenPath ?? serviceExecutablePath;
    }

    private static void ApplySpeechSpeedOptions(SpeechOptions? speechOptions, AzureSpeechOptions azureSpeechOptions)
    {
        if (speechOptions is null)
        {
            return;
        }

        azureSpeechOptions.UseSsml = speechOptions.UseSsml;
        azureSpeechOptions.DefaultLanguage = speechOptions.DefaultLanguage;
        azureSpeechOptions.Voices = new Dictionary<string, string>(speechOptions.Voices, StringComparer.OrdinalIgnoreCase);
        azureSpeechOptions.ProsodyRate = new Dictionary<string, string>(speechOptions.ProsodyRate, StringComparer.OrdinalIgnoreCase);
        azureSpeechOptions.DefaultProsodyRate = speechOptions.DefaultProsodyRate;
        azureSpeechOptions.HindiProsodyRate = speechOptions.HindiProsodyRate;
        azureSpeechOptions.EnglishProsodyRate = speechOptions.EnglishProsodyRate;
        azureSpeechOptions.AllowAudioTempoCompression = speechOptions.AllowAudioTempoCompression;
        azureSpeechOptions.MaxAudioTempo = speechOptions.MaxAudioTempo;
        azureSpeechOptions.MinAudioTempo = speechOptions.MinAudioTempo;
    }
}
