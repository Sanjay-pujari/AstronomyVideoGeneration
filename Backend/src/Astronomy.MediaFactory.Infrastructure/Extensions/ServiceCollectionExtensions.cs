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
using Astronomy.MediaFactory.Infrastructure.Optimization;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Infrastructure.Scheduling;
using Astronomy.MediaFactory.Publishing;
using Astronomy.MediaFactory.Rendering;
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
        services.AddOptions<RenderingOptions>()
            .Bind(configuration.GetSection(RenderingOptions.SectionName))
            .Configure(options => configuration.GetSection(RenderingOptions.VideoRenderSectionName).Bind(options))
            .Configure(options => configuration.GetSection(RenderingOptions.VideoEncodingSectionName).Bind(options))
            .Validate(opt => opt.VideoWidth > 0 && opt.VideoHeight > 0 && opt.FrameRate > 0, "Rendering dimensions and frame rate must be > 0.")
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
        services.AddScoped<ICelestialAssetProvider, CelestialAssetProvider>();
        services.AddHttpClient<MinorPlanetCenterClient>();
        services.AddHttpClient<ISkyfieldSidecarClient, SkyfieldSidecarClient>((sp, client) =>
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
