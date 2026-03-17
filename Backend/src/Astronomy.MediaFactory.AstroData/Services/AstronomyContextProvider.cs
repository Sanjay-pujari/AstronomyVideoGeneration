using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.AstroData.Services;

public sealed class AstronomyContextProvider : IAstronomyContextProvider
{
    private readonly NasaApodClient _apodClient;
    private readonly NasaNeoWsClient _neoWsClient;
    private readonly ISkyfieldSidecarClient _skyfieldSidecarClient;
    private readonly ILogger<AstronomyContextProvider> _logger;

    public AstronomyContextProvider(NasaApodClient apodClient, NasaNeoWsClient neoWsClient, ISkyfieldSidecarClient skyfieldSidecarClient, ILogger<AstronomyContextProvider> logger)
    {
        _apodClient = apodClient;
        _neoWsClient = neoWsClient;
        _skyfieldSidecarClient = skyfieldSidecarClient;
        _logger = logger;
    }

    public async Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
    {
        var context = new AstronomyContext { Date = date, LocationName = locationName, TimeZone = timeZone };
        await AddNasaContextAsync(context, date, cancellationToken);

        if (contentType == ContentType.DailySkyGuide)
        {
            var (latitude, longitude) = ResolveCoordinates(locationName);
            var sidecarResponse = await _skyfieldSidecarClient.GetDailySkyAsync(new SkyfieldDailySkyRequest
            {
                Date = date.ToString("yyyy-MM-dd"),
                LocationName = locationName,
                Latitude = latitude,
                Longitude = longitude,
                Timezone = timeZone
            }, cancellationToken);

            if (sidecarResponse is not null && sidecarResponse.Events.Count > 0)
            {
                foreach (var item in sidecarResponse.Events)
                {
                    context.Events.Add(new AstronomyEventModel
                    {
                        Category = item.Category,
                        ObjectName = item.ObjectName,
                        VisibilityWindow = item.VisibilityWindow,
                        Direction = item.Direction,
                        ObservationTool = item.ObservationTool,
                        Details = item.Details,
                        Score = item.Category.Equals("Planet", StringComparison.OrdinalIgnoreCase) ? 0.95 : 0.90
                    });
                }

                foreach (var item in sidecarResponse.VisualIdeas)
                {
                    context.VisualIdeas.Add(new VisualIdeaModel { Title = item.Title, Description = item.Description });
                }

                return context;
            }

            _logger.LogWarning("Skyfield sidecar returned no events for {Date} at {LocationName}. Falling back to demo astronomy context.", date, locationName);
        }

        AddFallbackEvents(context);
        return context;
    }

    private async Task AddNasaContextAsync(AstronomyContext context, DateOnly date, CancellationToken cancellationToken)
    {
        var apod = await _apodClient.GetAsync(date, cancellationToken);
        if (apod is not null)
        {
            context.NewsItems.Add(new NewsItemModel { Headline = apod.Title ?? "NASA APOD", Summary = apod.Explanation ?? "No summary available.", SourceName = "NASA APOD", PublishedDate = date, SourceUrl = apod.Hdurl ?? apod.Url });
            context.VisualIdeas.Add(new VisualIdeaModel { Title = apod.Title ?? "APOD visual", Description = "NASA APOD visual anchor for the video.", SourcePathOrUrl = apod.Hdurl ?? apod.Url });
        }

        _ = await _neoWsClient.GetFeedAsync(date, date.AddDays(2), cancellationToken);
    }

    private static void AddFallbackEvents(AstronomyContext context)
    {
        context.Events.AddRange(new[]
        {
            new AstronomyEventModel { Category = "Moon", ObjectName = "Waxing Gibbous Moon", VisibilityWindow = "After sunset until after midnight", Direction = "East to south", ObservationTool = "Naked eye / binoculars", Details = "Strong crater contrast near the terminator.", Score = 0.88 },
            new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", VisibilityWindow = "Evening hours", Direction = "South-west", ObservationTool = "Binoculars / telescope", Details = "Good target for Galilean moon observation.", Score = 0.94 },
            new AstronomyEventModel { Category = "Deep Sky", ObjectName = "Orion Nebula", VisibilityWindow = "Early evening", Direction = "South-west", ObservationTool = "Binoculars / small telescope", Details = "Bright beginner nebula when skies are dark enough.", Score = 0.90 }
        });
    }

    private static (double Latitude, double Longitude) ResolveCoordinates(string locationName)
    {
        return locationName.Trim().ToLowerInvariant() switch
        {
            "udaipur, india" => (24.5854, 73.7125),
            _ => (24.5854, 73.7125)
        };
    }
}
