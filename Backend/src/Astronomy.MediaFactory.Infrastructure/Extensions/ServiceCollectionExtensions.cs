using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.AstroData.Services;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Publishing;
using Astronomy.MediaFactory.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.Infrastructure.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediaFactory(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RenderingOptions>(configuration.GetSection(RenderingOptions.SectionName));
        services.Configure<AstronomyApiOptions>(configuration.GetSection(AstronomyApiOptions.SectionName));
        services.Configure<AzureOpenAiOptions>(configuration.GetSection(AzureOpenAiOptions.SectionName));
        services.Configure<AzureSpeechOptions>(configuration.GetSection(AzureSpeechOptions.SectionName));
        services.Configure<AzureBlobOptions>(configuration.GetSection(AzureBlobOptions.SectionName));
        services.Configure<YouTubeOptions>(configuration.GetSection(YouTubeOptions.SectionName));
        services.Configure<SchedulingOptions>(configuration.GetSection(SchedulingOptions.SectionName));
        services.Configure<AnalyticsOptions>(configuration.GetSection(AnalyticsOptions.SectionName));
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
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "SkyfieldSidecar:BaseUrl must be an absolute URI.")
            .ValidateOnStart();
        services.AddHttpClient<NasaApodClient>();
        services.AddHttpClient<NasaNeoWsClient>();
        services.AddHttpClient<MinorPlanetCenterClient>();
        services.AddHttpClient<ISkyfieldSidecarClient, SkyfieldSidecarClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SkyfieldSidecarOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });
        var cs = configuration.GetConnectionString("Postgres") ?? configuration["ConnectionStrings:Postgres"] ?? "Host=localhost;Port=5432;Database=astronomy_media_factory;Username=postgres;Password=postgres";
        services.AddDbContext<MediaFactoryDbContext>(o => o.UseNpgsql(cs));
        services.AddScoped<IPipelineRepository, EfPipelineRepository>();
        services.AddScoped<IAstronomyContextProvider, AstronomyContextProvider>();
        services.AddScoped<ITopicRankingService, TopicRankingService>();
        services.AddScoped<IVisualAssetProvider, StellariumVisualGenerationService>();
        services.AddScoped<IPromptBuilder, PromptBuilder>();
        services.AddScoped<IMetadataOptimizationService, MetadataOptimizationService>();
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
        services.AddScoped<IAzureBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<IYouTubePublishingService, YouTubePublishingService>();
        services.AddScoped<IYouTubeAnalyticsService, YouTubeAnalyticsService>();
        services.AddScoped<IShortsVideoRenderService, ShortsVideoRenderService>();
        services.AddScoped<IAnalyticsAggregationService, AnalyticsAggregationService>();
        services.AddScoped<IFeedbackSignalExtractor, TopKeywordSignalExtractor>();
        services.AddScoped<IFeedbackSignalExtractor, TopHookSignalExtractor>();
        services.AddScoped<IAnalyticsFeedbackProvider, AnalyticsFeedbackProvider>();
        services.AddScoped<StellariumScriptBuilder>(sp =>
            new StellariumScriptBuilder(sp.GetRequiredService<IOptions<StellariumOptions>>().Value));
        services.AddScoped<PipelineOrchestrator>();
        services.AddScoped<IPipelineJobQueue, PipelineJobQueue>();
        services.AddScoped<IPipelineJobExecutor, PipelineJobExecutor>();
        services.AddScoped<PipelineJobProcessor>();
        return services;
    }
}
