using Astronomy.MediaFactory.Contracts;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public sealed class SeoMetadataGeneratorService : ISeoMetadataGeneratorService
{
    public Task<SeoMetadataResult> GenerateAsync(SeoMetadataRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selectedObjects = request.SelectedVisibleObjects
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mainObjects = selectedObjects.Take(3).ToList();
        var title = request.ContentType == ContentType.SpecialEventGuide
            ? BuildSpecialEventTitle(request, mainObjects)
            : BuildTitle(request.LocationName, request.TargetDate, mainObjects, request.IsShortForm);
        var hashtags = request.ContentType == ContentType.SpecialEventGuide
            ? BuildSpecialEventHashtags(request)
            : request.IsShortForm
                ? new[] { "#Astronomy", "#NightSky", "#Stargazing", "#Shorts" }
                : new[] { "#Astronomy", "#NightSky", "#Stargazing" };

        var description = BuildDescription(request, selectedObjects, hashtags);

        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "astronomy",
            "night sky",
            "stargazing",
            request.ContentType == ContentType.SpecialEventGuide ? "astronomy event" : string.Empty,
            request.EventTitle ?? string.Empty,
            request.EventType ?? string.Empty,
            request.LocationName.Trim(),
            $"{request.LocationName.Trim()} night sky"
        };

        foreach (var obj in selectedObjects)
        {
            tagSet.Add(obj.ToLowerInvariant());
        }

        var pinnedComment = request.ContentType == ContentType.SpecialEventGuide
            ? $"Are you watching {request.EventTitle ?? "this astronomy event"}? Share your location and viewing conditions below!"
            : $"What can you see tonight from {request.LocationName}? Drop your observation time and direction below!";

        return Task.FromResult(new SeoMetadataResult
        {
            Title = title,
            Description = description,
            TagsCsv = string.Join(",", tagSet.Where(x => !string.IsNullOrWhiteSpace(x))),
            HashtagsCsv = string.Join(",", hashtags),
            PinnedComment = pinnedComment
        });
    }

    public static async Task WriteToFileAsync(SeoMetadataResult result, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "seo-metadata.json");
        var payload = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, payload, cancellationToken);
    }

    private static string BuildTitle(string location, DateOnly date, IReadOnlyList<string> objects, bool isShort)
    {
        var objectPart = objects.Count > 0 ? string.Join(", ", objects.Take(2)) + (objects.Count > 2 ? " & " + objects[2] : string.Empty) : "Night Sky";
        var datePart = date.ToString("MMM d");
        var baseTitle = $"Tonight's Sky in {location}: {objectPart} ({datePart})";
        if (!isShort)
        {
            return baseTitle;
        }

        var compact = $"{location} Sky: {objectPart} #Shorts";
        return compact.Length <= 100 ? compact : $"{location} Night Sky #Shorts";
    }

    private static string BuildSpecialEventTitle(SeoMetadataRequest request, IReadOnlyList<string> objects)
    {
        var eventTitle = string.IsNullOrWhiteSpace(request.EventTitle) ? objects.FirstOrDefault() ?? "Astronomy Event" : request.EventTitle.Trim();
        var urgency = request.TargetDate <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) ? "Tonight" : request.TargetDate.ToString("MMM d");
        var title = eventTitle.Contains("Tonight", StringComparison.OrdinalIgnoreCase)
            ? eventTitle
            : $"{eventTitle} {urgency}: How to Watch";
        return title.Length <= 100 ? title : title[..100].TrimEnd();
    }

    private static string[] BuildSpecialEventHashtags(SeoMetadataRequest request)
    {
        var tags = new List<string> { "#Astronomy", "#Skywatching", "#Stargazing", "#AstronomyEvent" };
        var eventText = $"{request.EventType} {request.EventTitle}";
        if (eventText.Contains("moon", StringComparison.OrdinalIgnoreCase)) tags.Add("#FullMoon");
        if (eventText.Contains("meteor", StringComparison.OrdinalIgnoreCase)) tags.Add("#MeteorShower");
        if (eventText.Contains("conjunction", StringComparison.OrdinalIgnoreCase)) tags.Add("#PlanetaryConjunction");
        if (eventText.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) tags.Add("#Eclipse");
        if (request.IsShortForm) tags.Add("#Shorts");
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }


    private static string BuildDescription(SeoMetadataRequest request, IReadOnlyCollection<string> selectedObjects, IReadOnlyCollection<string> hashtags)
    {
        var lines = new List<string>
        {
            $"Location: {request.LocationName}",
            $"Date: {request.TargetDate:yyyy-MM-dd}",
            ""
        };

        foreach (var scene in request.SceneObservationContext.Where(s => selectedObjects.Contains(s.ObjectName, StringComparer.OrdinalIgnoreCase)))
        {
            lines.Add($"- {scene.ObjectName}: {scene.LocalObservationTime:hh:mm tt} ({scene.Timezone}), direction {scene.DirectionLabel ?? "unknown"}, altitude {(scene.AltitudeDegrees?.ToString("F1") ?? "n/a")}°");
        }

        lines.Add("");
        lines.Add(request.ContentType == ContentType.SpecialEventGuide
            ? "Recommended tool: match the event guidance; use certified eclipse protection for any solar eclipse viewing."
            : "Recommended tool: binoculars or naked eye depending on target brightness.");
        lines.Add("Disclaimer: Visibility depends on weather and your local horizon.");
        if (request.ThumbnailVariants.Count > 0)
        {
            lines.Add($"Thumbnail variants available: {request.ThumbnailVariants.Count}");
        }

        lines.Add(string.Join(" ", hashtags));

        var description = string.Join(Environment.NewLine, lines.Where(x => !request.IsShortForm || !string.IsNullOrWhiteSpace(x)));
        if (request.IsShortForm && description.Length > 500)
        {
            description = description[..500];
        }

        return description;
    }
}
