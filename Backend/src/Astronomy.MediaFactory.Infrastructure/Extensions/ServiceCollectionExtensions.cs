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
namespace Astronomy.MediaFactory.Infrastructure.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediaFactory(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RenderingOptions>(configuration.GetSection(RenderingOptions.SectionName));
        services.Configure<AstronomyApiOptions>(configuration.GetSection(AstronomyApiOptions.SectionName));
        services.Configure<AzureOpenAiOptions>(configuration.GetSection(AzureOpenAiOptions.SectionName));
        services.Configure<AzureSpeechOptions>(configuration.GetSection(AzureSpeechOptions.SectionName));
        services.Configure<AzureStorageOptions>(configuration.GetSection(AzureStorageOptions.SectionName));
        services.Configure<YouTubeOptions>(configuration.GetSection(YouTubeOptions.SectionName));
        services.AddHttpClient<NasaApodClient>();
        services.AddHttpClient<NasaNeoWsClient>();
        services.AddHttpClient<MinorPlanetCenterClient>();
        services.AddHttpClient<SkyfieldSidecarClient>();
        var cs = configuration.GetConnectionString("Postgres") ?? configuration["ConnectionStrings:Postgres"] ?? "Host=localhost;Port=5432;Database=astronomy_media_factory;Username=postgres;Password=postgres";
        services.AddDbContext<MediaFactoryDbContext>(o => o.UseNpgsql(cs));
        services.AddScoped<IPipelineRepository, EfPipelineRepository>();
        services.AddScoped<IAstronomyContextProvider, AstronomyContextProvider>();
        services.AddScoped<ITopicRankingService, TopicRankingService>();
        services.AddScoped<IVisualAssetProvider, FileVisualAssetProvider>();
        services.AddScoped<IPromptBuilder, PromptBuilder>();
        services.AddHttpClient<IScriptGenerationService, AzureOpenAiContentGenerationService>();
        services.AddScoped<IAzureSpeechClient, AzureSpeechClient>();
        services.AddScoped<ISpeechSynthesisService, AzureSpeechSynthesisService>();
        services.AddScoped<IVideoRenderService, FfmpegVideoRenderService>();
        services.AddScoped<IArchivalService, AzureBlobArchivalService>();
        services.AddScoped<IYouTubePublishingService, YouTubePublishingService>();
        services.AddScoped<StellariumScriptService>();
        services.AddScoped<PipelineOrchestrator>();
        return services;
    }
}
