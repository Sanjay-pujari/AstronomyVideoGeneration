using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
namespace Astronomy.MediaFactory.AstroData.Services;

public sealed class AstronomyContextProvider : IAstronomyContextProvider
{
    private readonly NasaApodClient _apodClient; private readonly NasaNeoWsClient _neoWsClient;
    public AstronomyContextProvider(NasaApodClient apodClient, NasaNeoWsClient neoWsClient) { _apodClient = apodClient; _neoWsClient = neoWsClient; }
    public async Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
    {
        var context = new AstronomyContext { Date = date, LocationName = locationName, TimeZone = timeZone };
        var apod = await _apodClient.GetAsync(date, cancellationToken);
        if (apod is not null)
        {
            context.NewsItems.Add(new NewsItemModel { Headline = apod.Title ?? "NASA APOD", Summary = apod.Explanation ?? "No summary available.", SourceName = "NASA APOD", PublishedDate = date, SourceUrl = apod.Hdurl ?? apod.Url });
            context.VisualIdeas.Add(new VisualIdeaModel { Title = apod.Title ?? "APOD visual", Description = "NASA APOD visual anchor for the video.", SourcePathOrUrl = apod.Hdurl ?? apod.Url });
        }
        _ = await _neoWsClient.GetFeedAsync(date, date.AddDays(2), cancellationToken);
        context.Events.AddRange(new[] {
            new AstronomyEventModel{ Category="Moon", ObjectName="Waxing Gibbous Moon", VisibilityWindow="After sunset until after midnight", Direction="East to south", ObservationTool="Naked eye / binoculars", Details="Strong crater contrast near the terminator.", Score=0.88 },
            new AstronomyEventModel{ Category="Planet", ObjectName="Jupiter", VisibilityWindow="Evening hours", Direction="South-west", ObservationTool="Binoculars / telescope", Details="Good target for Galilean moon observation.", Score=0.94 },
            new AstronomyEventModel{ Category="Deep Sky", ObjectName="Orion Nebula", VisibilityWindow="Early evening", Direction="South-west", ObservationTool="Binoculars / small telescope", Details="Bright beginner nebula when skies are dark enough.", Score=0.90 }
        });
        return context;
    }
}
