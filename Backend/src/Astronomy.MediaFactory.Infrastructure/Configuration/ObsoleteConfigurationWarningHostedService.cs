using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Configuration;

public sealed class ObsoleteConfigurationWarningHostedService : IHostedService
{
    private static readonly string[] ObsoleteSections = ["InstagramPublishing", "FacebookPublishing", "ThumbnailAIOptimization", "ThumbnailCinematicAI"];

    private readonly IConfiguration _configuration;
    private readonly ILogger<ObsoleteConfigurationWarningHostedService> _logger;

    public ObsoleteConfigurationWarningHostedService(
        IConfiguration configuration,
        ILogger<ObsoleteConfigurationWarningHostedService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var sectionName in ObsoleteSections)
        {
            if (_configuration.GetSection(sectionName).Exists())
            {
                var guidance = sectionName.StartsWith("Thumbnail", StringComparison.OrdinalIgnoreCase)
                    ? "Thumbnail generation now uses ThumbnailGeneration:Mode=LocalAssetCollage; this section is deprecated and ignored by the active thumbnail flow."
                    : "Configure Meta publishing only under MetaPublishing.";
                _logger.LogWarning(
                    "Obsolete configuration section {SectionName} is present and ignored. {Guidance}",
                    sectionName,
                    guidance);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
