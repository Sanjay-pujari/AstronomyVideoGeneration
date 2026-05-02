using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.AstroData.Services;

public sealed class AstronomyContextProvider : IAstronomyContextProvider
{
    private readonly NasaApodClient _apodClient;
    private readonly NasaNeoWsClient _neoWsClient;
    private readonly ISkyfieldSidecarClient _skyfieldSidecarClient;
    private readonly ILogger<AstronomyContextProvider> _logger;
    private readonly SkyfieldSidecarOptions _sidecarOptions;
    private readonly ObservationOptions _observationOptions;

    public AstronomyContextProvider(NasaApodClient apodClient, NasaNeoWsClient neoWsClient, ISkyfieldSidecarClient skyfieldSidecarClient, ILogger<AstronomyContextProvider> logger, IOptions<SkyfieldSidecarOptions> sidecarOptions, IOptions<ObservationOptions> observationOptions)
    {
        _apodClient = apodClient;
        _neoWsClient = neoWsClient;
        _skyfieldSidecarClient = skyfieldSidecarClient;
        _logger = logger;
        _sidecarOptions = sidecarOptions.Value;
        _observationOptions = observationOptions.Value;
    }

    public async Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
    {
        var context = new AstronomyContext { Date = date, LocationName = string.IsNullOrWhiteSpace(locationName) ? _observationOptions.LocationName : locationName, TimeZone = string.IsNullOrWhiteSpace(timeZone) ? _observationOptions.Timezone : timeZone, Latitude = _observationOptions.Latitude, Longitude = _observationOptions.Longitude };
        await AddNasaContextAsync(context, date, cancellationToken);

        if (contentType == ContentType.DailySkyGuide && _sidecarOptions.Enabled)
        {
            var latitude = _observationOptions.Latitude;
            var longitude = _observationOptions.Longitude;
            var sidecarResponse = await _skyfieldSidecarClient.GetDailySkyAsync(new SkyfieldDailySkyRequest
            {
                Date = date.ToString("yyyy-MM-dd"),
                LocationName = locationName,
                Latitude = latitude,
                Longitude = longitude,
                Timezone = timeZone
            }, cancellationToken);

            if (TryApplySidecarResponse(context, sidecarResponse))
            {
                return context;
            }

            _logger.LogWarning("Skyfield sidecar returned no usable events for {Date} at {LocationName}. Falling back to demo astronomy context.", date, locationName);
        }
        else if (contentType == ContentType.DailySkyGuide)
        {
            _logger.LogInformation("Skyfield sidecar is disabled. Using fallback astronomy context for {Date} at {LocationName}.", date, locationName);
        }

        AddFallbackEvents(context);
        return context;
    }

    private async Task AddNasaContextAsync(AstronomyContext context, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var apod = await _apodClient.GetAsync(date, cancellationToken);
            if (apod is not null)
            {
                context.NewsItems.Add(new NewsItemModel { Headline = apod.Title ?? "NASA APOD", Summary = apod.Explanation ?? "No summary available.", SourceName = "NASA APOD", PublishedDate = date, SourceUrl = apod.Hdurl ?? apod.Url });
                context.VisualIdeas.Add(new VisualIdeaModel { Title = apod.Title ?? "APOD visual", Description = "NASA APOD visual anchor for the video.", SourcePathOrUrl = apod.Hdurl ?? apod.Url });
            }

            _ = await _neoWsClient.GetFeedAsync(date, date.AddDays(2), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NASA context enrichment failed for {Date}. Continuing without NASA data.", date);
        }
    }

    private static bool TryApplySidecarResponse(AstronomyContext context, SkyfieldDailySkyResponse? sidecarResponse)
    {
        if (sidecarResponse is null || sidecarResponse.Events.Count == 0)
        {
            return false;
        }

        context.Events.AddRange(sidecarResponse.Events.Select(MapEvent));

        if (sidecarResponse.VisualIdeas.Count > 0)
        {
            context.VisualIdeas.AddRange(sidecarResponse.VisualIdeas.Select(MapVisualIdea));
        }

        return context.Events.Count > 0;
    }

    private static AstronomyEventModel MapEvent(SkyfieldDailySkyEvent sidecarEvent)
        => new()
        {
            Category = sidecarEvent.Category,
            ObjectName = sidecarEvent.ObjectName,
            VisibilityWindow = sidecarEvent.VisibilityWindow,
            Direction = sidecarEvent.Direction,
            ObservationTool = sidecarEvent.ObservationTool,
            Details = sidecarEvent.Details,
            Score = ResolveEventScore(sidecarEvent.Category)
        };

    private static VisualIdeaModel MapVisualIdea(SkyfieldVisualIdea visualIdea)
        => new()
        {
            Title = visualIdea.Title,
            Description = visualIdea.Description
        };

    private static double ResolveEventScore(string category)
        => category.Trim().ToLowerInvariant() switch
        {
            "planet" => 0.95,
            "moon" => 0.92,
            "deep sky" => 0.90,
            _ => 0.88
        };

    private static void AddFallbackEvents(AstronomyContext context)
    {
        context.Events.AddRange(new[]
        {
            new AstronomyEventModel { Category = "Moon", ObjectName = "Waxing Gibbous Moon", VisibilityWindow = "After sunset until after midnight", Direction = "East to south", ObservationTool = "Naked eye / binoculars", Details = "Strong crater contrast near the terminator.", Score = 0.88 },
            new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", VisibilityWindow = "Evening hours", Direction = "South-west", ObservationTool = "Binoculars / telescope", Details = "Good target for Galilean moon observation.", Score = 0.94 },
            new AstronomyEventModel { Category = "Deep Sky", ObjectName = "Orion Nebula", VisibilityWindow = "Early evening", Direction = "South-west", ObservationTool = "Binoculars / small telescope", Details = "Bright beginner nebula when skies are dark enough.", Score = 0.90 }
        });
    }

}
