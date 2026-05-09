using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public sealed class SeoMetadataGeneratorService : ISeoMetadataGeneratorService
{
    private readonly GrowthOptions _growthOptions;

    public SeoMetadataGeneratorService()
        : this(Microsoft.Extensions.Options.Options.Create(new GrowthOptions()))
    {
    }

    public SeoMetadataGeneratorService(IOptions<GrowthOptions> growthOptions)
        => _growthOptions = growthOptions.Value ?? new GrowthOptions();

    public Task<SeoMetadataResult> GenerateAsync(SeoMetadataRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selectedObjects = request.SelectedVisibleObjects
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mainObjects = selectedObjects.Take(3).ToList();
        var isHindi = LocalizationResolver.IsHindi(request.Language);
        var title = request.ContentType == ContentType.SpecialEventGuide
            ? BuildSpecialEventTitle(request, mainObjects, isHindi)
            : BuildTitle(request.LocationName, request.TargetDate, mainObjects, request.IsShortForm, isHindi);
        var hashtags = request.ContentType == ContentType.SpecialEventGuide
            ? BuildSpecialEventHashtags(request)
            : request.IsShortForm
                ? new[] { "#Astronomy", "#NightSky", "#Stargazing", "#Shorts" }
                : new[] { "#Astronomy", "#NightSky", "#Stargazing" };

        var growthMetadata = GrowthMetadataComposer.BuildMetadata(_growthOptions, new GrowthMetadataInput
        {
            Platform = "YouTube",
            Language = request.Language,
            Region = request.RegionId ?? request.LocationName,
            IsShortForm = request.IsShortForm,
            ContentType = request.ContentType
        });
        var description = GrowthMetadataComposer.AppendBlockOnce(
            BuildDescription(request, selectedObjects, hashtags, isHindi),
            GrowthMetadataComposer.BuildGrowthBlock(growthMetadata, request.Language));

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
            ? isHindi
                ? $"क्या आप {request.EventTitle ?? "यह खगोलीय घटना"} देख रहे हैं? नीचे अपना स्थान और देखने की स्थिति साझा करें!"
                : $"Are you watching {request.EventTitle ?? "this astronomy event"}? Share your location and viewing conditions below!"
            : isHindi
                ? $"आज रात {request.LocationName} से आप क्या देख पा रहे हैं? अपना अवलोकन समय और दिशा नीचे लिखें!"
                : $"What can you see tonight from {request.LocationName}? Drop your observation time and direction below!";

        return Task.FromResult(new SeoMetadataResult
        {
            Title = title,
            Description = description,
            TagsCsv = string.Join(",", tagSet.Where(x => !string.IsNullOrWhiteSpace(x))),
            HashtagsCsv = string.Join(",", hashtags),
            PinnedComment = pinnedComment,
            GrowthMetadata = growthMetadata
        });
    }

    public static async Task WriteToFileAsync(SeoMetadataResult result, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "seo-metadata.json");
        var payload = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, payload, cancellationToken);

        if (result.GrowthMetadata is not null)
        {
            var growthPath = Path.Combine(outputDirectory, "growth-metadata.json");
            var growthPayload = JsonSerializer.Serialize(result.GrowthMetadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(growthPath, growthPayload, cancellationToken);
        }
    }

    private static string BuildTitle(string location, DateOnly date, IReadOnlyList<string> objects, bool isShort, bool isHindi)
    {
        var objectPart = objects.Count > 0 ? string.Join(", ", objects.Take(2)) + (objects.Count > 2 ? " & " + objects[2] : string.Empty) : isHindi ? "रात का आसमान" : "Night Sky";
        var datePart = isHindi ? date.ToString("yyyy-MM-dd") : date.ToString("MMM d");
        var baseTitle = isHindi ? $"आज रात {location} का आसमान: {objectPart} ({datePart})" : $"Tonight's Sky in {location}: {objectPart} ({datePart})";
        if (!isShort)
        {
            return baseTitle;
        }

        var compact = isHindi ? $"{location} आसमान: {objectPart} #Shorts" : $"{location} Sky: {objectPart} #Shorts";
        return compact.Length <= 100 ? compact : isHindi ? $"{location} रात का आसमान #Shorts" : $"{location} Night Sky #Shorts";
    }

    private static string BuildSpecialEventTitle(SeoMetadataRequest request, IReadOnlyList<string> objects, bool isHindi)
    {
        var eventTitle = string.IsNullOrWhiteSpace(request.EventTitle) ? objects.FirstOrDefault() ?? (isHindi ? "खगोलीय घटना" : "Astronomy Event") : request.EventTitle.Trim();
        var urgency = request.TargetDate <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) ? isHindi ? "आज रात" : "Tonight" : request.TargetDate.ToString(isHindi ? "yyyy-MM-dd" : "MMM d");
        var title = isHindi
            ? $"{eventTitle} {urgency}: कैसे देखें"
            : eventTitle.Contains("Tonight", StringComparison.OrdinalIgnoreCase) ? eventTitle : $"{eventTitle} {urgency}: How to Watch";
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


    private static string BuildDescription(SeoMetadataRequest request, IReadOnlyCollection<string> selectedObjects, IReadOnlyCollection<string> hashtags, bool isHindi)
    {
        var lines = new List<string>
        {
            isHindi ? $"स्थान: {request.LocationName}" : $"Location: {request.LocationName}",
            isHindi ? $"तारीख: {request.TargetDate:yyyy-MM-dd}" : $"Date: {request.TargetDate:yyyy-MM-dd}",
            ""
        };

        foreach (var scene in request.SceneObservationContext.Where(s => selectedObjects.Contains(s.ObjectName, StringComparer.OrdinalIgnoreCase)))
        {
            lines.Add(isHindi
                ? $"- {scene.ObjectName}: {scene.LocalObservationTime:hh:mm tt} ({scene.Timezone}), दिशा {scene.DirectionLabel ?? "अज्ञात"}, ऊंचाई {(scene.AltitudeDegrees?.ToString("F1") ?? "n/a")}°"
                : $"- {scene.ObjectName}: {scene.LocalObservationTime:hh:mm tt} ({scene.Timezone}), direction {scene.DirectionLabel ?? "unknown"}, altitude {(scene.AltitudeDegrees?.ToString("F1") ?? "n/a")}°");
        }

        lines.Add("");
        lines.Add(request.ContentType == ContentType.SpecialEventGuide
            ? isHindi ? "अनुशंसित उपकरण: घटना की गाइड के अनुसार चलें; सूर्य ग्रहण के लिए प्रमाणित सुरक्षा उपकरण इस्तेमाल करें।" : "Recommended tool: match the event guidance; use certified eclipse protection for any solar eclipse viewing."
            : isHindi ? "अनुशंसित उपकरण: लक्ष्य की चमक के अनुसार दूरबीन या नंगी आंख।" : "Recommended tool: binoculars or naked eye depending on target brightness.");
        lines.Add(isHindi ? "नोट: दृश्यता मौसम और आपके स्थानीय क्षितिज पर निर्भर करती है।" : "Disclaimer: Visibility depends on weather and your local horizon.");
        if (request.ThumbnailVariants.Count > 0)
        {
            lines.Add(isHindi ? $"थंबनेल विकल्प उपलब्ध: {request.ThumbnailVariants.Count}" : $"Thumbnail variants available: {request.ThumbnailVariants.Count}");
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
