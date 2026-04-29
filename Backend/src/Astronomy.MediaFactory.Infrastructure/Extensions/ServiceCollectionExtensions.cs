using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.AstroData.Services;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Alerting;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Astronomy.MediaFactory.Infrastructure.Operations;
using Astronomy.MediaFactory.Infrastructure.Persistence;
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
            .Validate(opt => opt.VideoWidth > 0 && opt.VideoHeight > 0 && opt.FrameRate > 0, "Rendering dimensions and frame rate must be > 0.")
            .ValidateOnStart();

        services.AddOptions<AstronomyApiOptions>()
            .Bind(configuration.GetSection(AstronomyApiOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<AzureOpenAiOptions>()
            .Bind(configuration.GetSection(AzureOpenAiOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AzureOpenAiOptions>, AzureOpenAiOptionsValidator>();

        services.AddOptions<AzureSpeechOptions>()
            .Bind(configuration.GetSection(AzureSpeechOptions.SectionName))
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
            .Validate(options => string.IsNullOrWhiteSpace(options.PrivacyStatus) || options.PrivacyStatus is "private" or "public" or "unlisted", "YouTube:PrivacyStatus must be private, public, or unlisted.")
            .Validate(options => options.UploadRetryAttempts is > 0 and <= 5 && options.RetryBaseDelaySeconds > 0 && options.MaxRetryDelaySeconds >= options.RetryBaseDelaySeconds && options.PublishRetryCooldownSeconds > 0, "YouTube retry settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<PlatformPublishingOptions>()
            .Bind(configuration.GetSection(PlatformPublishingOptions.SectionName))
            .Validate(options => options.PublishRetryAttempts is > 0 and <= 5 && options.RetryBaseDelaySeconds > 0 && options.MaxRetryDelaySeconds >= options.RetryBaseDelaySeconds && options.PublishRetryCooldownSeconds > 0, "Platform publishing retry settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<InstagramPublishingOptions>()
            .Bind(configuration.GetSection(InstagramPublishingOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<FacebookPublishingOptions>()
            .Bind(configuration.GetSection(FacebookPublishingOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<MonetizationOptions>()
            .Bind(configuration.GetSection(MonetizationOptions.SectionName))
            .Validate(options => string.IsNullOrWhiteSpace(options.AffiliateBaseUrl) || Uri.TryCreate(options.AffiliateBaseUrl, UriKind.Absolute, out _), "Monetization:AffiliateBaseUrl must be an absolute URI when provided.")
            .ValidateOnStart();

        services.AddOptions<SchedulingOptions>()
            .Bind(configuration.GetSection(SchedulingOptions.SectionName))
            .Validate(opt => opt.MaxRetryAttempts > 0 && opt.RetryBackoffSeconds > 0 && opt.QueuePollIntervalSeconds > 0, "Scheduling values must be > 0.")
            .ValidateOnStart();

        services.AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
            .Validate(opt => opt.FetchIntervalMinutes > 0 && opt.TopN > 0, "Analytics values must be > 0.")
            .ValidateOnStart();

        services.AddOptions<TopicSelectionOptions>()
            .Bind(configuration.GetSection(TopicSelectionOptions.SectionName))
            .Validate(opt => opt.RepetitionWindowDays > 0, "TopicSelection:RepetitionWindowDays must be > 0.")
            .ValidateOnStart();

        services.AddOptions<OperationsOptions>()
            .Bind(configuration.GetSection(OperationsOptions.SectionName))
            .Validate(opt => opt.RetainDays > 0 && opt.SlowStageThresholdMs > 0, "Operations values must be > 0.")
            .ValidateOnStart();

        services.AddOptions<MaintenanceOptions>()
            .Bind(configuration.GetSection(MaintenanceOptions.SectionName))
            .Validate(opt => opt.WorkingFileRetentionDays > 0 && opt.JobRetentionDays > 0 && opt.StageRetentionDays > 0 && opt.AnalyticsRetentionDays > 0 && opt.StaleJobThresholdMinutes > 0, "Maintenance values must be > 0.")
            .ValidateOnStart();

        services.AddOptions<AlertingOptions>()
            .Bind(configuration.GetSection(AlertingOptions.SectionName))
            .Validate(opt => !opt.Enabled || string.IsNullOrWhiteSpace(opt.SlackWebhookUrl) || Uri.TryCreate(opt.SlackWebhookUrl, UriKind.Absolute, out _), "Alerting:SlackWebhookUrl must be an absolute URI when provided.")
            .ValidateOnStart();

        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateOnStart();

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
        services.AddSingleton<IValidateOptions<StartupValidationOptions>, ProductionStartupValidator>();

        services.AddHttpClient<NasaApodClient>();
        services.AddHttpClient<NasaNeoWsClient>();
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

        services.AddDbContext<MediaFactoryDbContext>(o => o.UseNpgsql(cs));
        services.AddScoped<IPipelineRepository, EfPipelineRepository>();
        services.AddScoped<IAstronomyContextProvider, AstronomyContextProvider>();
        services.AddScoped<ITopicRankingService, TopicRankingService>();
        services.AddScoped<ITopicSelectionService, TopicSelectionService>();
        services.AddScoped<IVisualAssetProvider, StellariumVisualGenerationService>();
        services.AddScoped<IPromptBuilder, PromptBuilder>();
        services.AddScoped<IMetadataOptimizationService, MetadataOptimizationService>();
        services.AddScoped<IContentMonetizationService, ContentMonetizationService>();
        services.AddHttpClient<AzureOpenAiContentGenerationService>();
        services.AddScoped<IMetadataOptimizationModelClient>(sp => sp.GetRequiredService<AzureOpenAiContentGenerationService>());
        services.AddScoped<IScriptGenerationService>(sp => sp.GetRequiredService<AzureOpenAiContentGenerationService>());
        services.AddScoped<IShortsScriptGenerationService>(sp => sp.GetRequiredService<AzureOpenAiContentGenerationService>());
        services.AddScoped<IAzureSpeechClient, AzureSpeechClient>();
        services.AddScoped<IFileSystem, PhysicalFileSystem>();
        services.AddScoped<IProcessRunner, ProcessRunner>();
        services.AddScoped<RenderManifestBuilder>();
        services.AddScoped<FfmpegArgumentBuilder>();
        services.AddScoped<ISpeechSynthesisService, AzureSpeechSynthesisService>();
        services.AddScoped<IVideoRenderService, FfmpegVideoRenderService>();
        services.AddScoped<IThumbnailStrategyService, ThumbnailStrategyService>();
        services.AddScoped<IThumbnailGenerationService, ThumbnailGenerationService>();
        services.AddScoped<IAzureBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<IYouTubePublishingService, YouTubePublishingService>();
        services.AddScoped<IYouTubeThumbnailPublisher>(sp => (IYouTubeThumbnailPublisher)sp.GetRequiredService<IYouTubePublishingService>());
        services.AddScoped<IYouTubeAnalyticsService, YouTubeAnalyticsService>();
        services.AddScoped<IShortsVideoRenderService, ShortsVideoRenderService>();
        services.AddScoped<IShortFormPlatformMetadataFormatter>(sp => new PlatformMetadataFormatter(sp.GetRequiredService<IOptions<PlatformPublishingOptions>>().Value));
        services.AddScoped<IShortFormPlatformPublisher, YouTubeShortsPlatformPublisher>();
        services.AddScoped<IShortFormPlatformPublisher, InstagramReelsPlatformPublisher>();
        services.AddScoped<IShortFormPlatformPublisher, FacebookPlatformPublisher>();
        services.AddScoped<IShortFormPublishingService, ShortFormPublishingService>();
        services.AddScoped<IAnalyticsAggregationService, AnalyticsAggregationService>();
        services.AddScoped<IContentExperimentService, EfContentExperimentService>();
        services.AddScoped<IFeedbackSignalExtractor, TopKeywordSignalExtractor>();
        services.AddScoped<IFeedbackSignalExtractor, TopHookSignalExtractor>();
        services.AddScoped<IAnalyticsFeedbackProvider, AnalyticsFeedbackProvider>();
        services.AddScoped<IPromptFeedbackService, PromptFeedbackService>();
        services.AddScoped<StellariumScriptBuilder>(sp =>
            new StellariumScriptBuilder(sp.GetRequiredService<IOptions<StellariumOptions>>().Value));
        services.AddScoped<PipelineOrchestrator>();
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
        services.AddScoped<IRunOperationsService, RunOperationsService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();

        services.AddHealthChecks()
            .AddCheck<DatabaseConnectivityHealthCheck>("database", tags: ["ready"])
            .AddCheck<QueueProcessorReadinessHealthCheck>("queue", tags: ["ready"])
            .AddCheck<OperationsConfigHealthCheck>("config", tags: ["ready"]);

        return services;
    }
}
