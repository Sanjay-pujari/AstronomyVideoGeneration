using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyEventDiscoveryPreviewService(
    IOptions<RenderingOptions> renderingOptions,
    IOptions<SchedulerOptions> schedulerOptions,
    TimeProvider timeProvider,
    ILogger<AstronomyEventDiscoveryPreviewService> logger) : IAstronomyEventDiscoveryPreviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly DateTimeOffset ReferenceNewMoonUtc = new(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
    private const double SynodicMonthDays = 29.530588853;
    private static readonly IReadOnlyList<string> DefaultContentTypes = ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"];

    public async Task<AstronomyEventDiscoveryPreviewResponse> DiscoverAstronomyEventsAsync(AstronomyEventDiscoveryPreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var region = ResolveRegion(request.RegionId);
        var zone = ResolveZone(region.Timezone);
        var warnings = new List<string>();
        var events = new List<AstronomyEventPreviewItem>();

        events.AddRange(BuildMoonEvents(request.Year, request.RegionId, zone));
        events.AddRange(BuildMeteorShowers(request.Year, request.RegionId, zone));
        events.AddRange(BuildKnownEclipses(request.Year, request.RegionId, zone));
        events.AddRange(BuildManualPlanetEvents(request.Year, request.RegionId, zone));

        warnings.Add("Preview V1 uses internal mean lunar-cycle calculations, known annual meteor-shower calendar rules, and manual seed events where Skyfield event-wide searches are not yet wired into this endpoint.");
        warnings.Add("Verify exact local circumstances with authoritative ephemeris data before publishing precise eclipse, conjunction, occultation, comet, or alignment claims.");

        var ordered = events
            .GroupBy(e => e.EventId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.ContentWorthinessScore).First())
            .OrderBy(e => e.PeakUtc)
            .ThenByDescending(e => e.ContentWorthinessScore)
            .ToArray();

        var document = new AstronomyEventPreviewDocument(
            request.Year,
            request.RegionId,
            NormalizeLanguage(request.Language),
            ordered.Length,
            ordered,
            ordered.GroupBy(e => e.EventType, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            ordered.Where(e => e.ContentWorthinessScore >= 70).OrderByDescending(e => e.ContentWorthinessScore).ThenBy(e => e.PeakUtc).Take(12).Select(e => e.EventId).ToArray(),
            warnings,
            timeProvider.GetUtcNow());

        var outputDirectory = BuildOutputDirectory(request.RegionId, request.Year);
        var outputPath = Path.Combine(outputDirectory, $"astronomy-event-preview-{request.Year}.json");
        var generatedFiles = new List<string>();
        var generated = false;

        if (File.Exists(outputPath) && !request.OverwriteExisting)
        {
            logger.LogInformation("Astronomy event discovery preview already exists at {OutputPath}; overwriteExisting=false.", outputPath);
        }
        else if (!request.DryRun)
        {
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
            generatedFiles.Add(outputPath);
            generated = true;
            logger.LogInformation("Astronomy event discovery preview generated at {OutputPath} with {EventCount} events.", outputPath, document.EventCount);
        }

        return new AstronomyEventDiscoveryPreviewResponse(request.Year, request.RegionId, generated, outputPath, document.EventCount, document.TopEvents.Count, generatedFiles);
    }

    private static IReadOnlyList<AstronomyEventPreviewItem> BuildMoonEvents(int year, string regionId, TimeZoneInfo zone)
    {
        var events = new List<AstronomyEventPreviewItem>();
        var fullMoonsByMonth = new Dictionary<int, List<DateTimeOffset>>();
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var firstCycle = Math.Floor((start - ReferenceNewMoonUtc.UtcDateTime).TotalDays / SynodicMonthDays) - 1;

        for (var cycle = firstCycle; cycle < firstCycle + 16; cycle++)
        {
            var newMoon = ReferenceNewMoonUtc.AddDays(cycle * SynodicMonthDays);
            if (newMoon.UtcDateTime >= start && newMoon.UtcDateTime <= end)
            {
                events.Add(CreateEvent($"new-moon-{year}-{newMoon:yyyyMMdd}", "NewMoon", "New Moon", "New Moon", newMoon, 12, regionId, zone,
                    ["Moon"], [], "Not directly visible; best for dark-sky planning.", 58, 25, 35, 62, "Computed",
                    "Approximate new moon timing from mean synodic month calculation.", ["Mean lunar-cycle timing; verify exact phase time before precision publishing."]));
            }

            var fullMoon = ReferenceNewMoonUtc.AddDays((cycle + 0.5) * SynodicMonthDays);
            if (fullMoon.UtcDateTime < start || fullMoon.UtcDateTime > end) continue;

            if (!fullMoonsByMonth.TryGetValue(fullMoon.Month, out var monthFullMoons))
            {
                monthFullMoons = [];
                fullMoonsByMonth[fullMoon.Month] = monthFullMoons;
            }
            monthFullMoons.Add(fullMoon);

            events.Add(CreateEvent($"full-moon-{year}-{fullMoon:yyyyMMdd}", "FullMoon", "Full Moon", "Full Moon", fullMoon, 18, regionId, zone,
                ["Moon"], [], "Eastern sky near moonrise; overhead around local midnight when visible.", 72, 92, 45, 84, "Computed",
                "Approximate full moon timing calculated from the mean lunar synodic cycle.", ["Full moon peak time is approximate in Preview V1."]));
        }

        foreach (var pair in fullMoonsByMonth.OrderBy(p => p.Key))
        {
            var fullMoon = pair.Value.OrderBy(d => d).First();
            var name = NamedFullMoonName(pair.Key);
            events.Add(CreateEvent($"named-full-moon-{year}-{fullMoon:yyyyMMdd}", "NamedFullMoon", $"{name} Moon Full Moon", $"{name} Moon", fullMoon, 18, regionId, zone,
                ["Moon"], [], "Eastern sky near moonrise; overhead around local midnight when visible.", 76, 92, 48, 88, "Computed",
                "Named full moon label mapped from common public monthly naming conventions; phase timing uses mean synodic month calculation.", ["Full moon peak time is approximate in Preview V1."]));
        }

        foreach (var pair in fullMoonsByMonth.Where(p => p.Value.Count > 1))
        {
            var blue = pair.Value.OrderBy(d => d).Last();
            events.Add(CreateEvent($"blue-moon-{year}-{blue:yyyyMMdd}", "BlueMoon", "Blue Moon", "Blue Moon", blue, 18, regionId, zone,
                ["Moon"], [], "Eastern sky near moonrise; overhead around local midnight when visible.", 88, 92, 85, 92, "Computed",
                "Calendar blue moon rule: second full moon in a calendar month using approximate full moon timings.", ["Blue moon classification depends on time zone and exact full moon instant; verify before publishing."]));
        }

        return events;
    }

    private static IReadOnlyList<AstronomyEventPreviewItem> BuildMeteorShowers(int year, string regionId, TimeZoneInfo zone)
    {
        MeteorDef[] showers =
        [
            new("Quadrantids", 1, 4, -8, 8, 82, 64, 78, "Northern sky after midnight", "Northern Hemisphere"),
            new("Lyrids", 4, 22, -6, 5, 72, 60, 70, "Northeast to overhead after midnight", "Northern Hemisphere and tropics"),
            new("Eta Aquariids", 5, 6, -12, 15, 72, 58, 72, "Eastern sky before dawn", "Tropics and Southern Hemisphere; also visible from India before dawn"),
            new("Perseids", 8, 12, -14, 12, 92, 80, 94, "Northeast after midnight", "Northern Hemisphere"),
            new("Orionids", 10, 21, -16, 14, 76, 66, 76, "East after midnight near Orion", "Global, best after midnight"),
            new("Leonids", 11, 17, -11, 10, 78, 58, 78, "East after midnight", "Global, best after midnight"),
            new("Geminids", 12, 14, -10, 7, 95, 84, 96, "East to overhead after 10 PM", "Global, strong from Northern Hemisphere"),
            new("Ursids", 12, 22, -5, 4, 62, 48, 54, "North after midnight", "Northern Hemisphere")
        ];

        return showers.Select(s =>
        {
            var peak = new DateTimeOffset(year, s.Month, s.Day, 6, 0, 0, TimeSpan.Zero);
            return CreateEvent($"meteor-shower-{s.Name.ToLowerInvariant().Replace(' ', '-')}-{year}", "MeteorShower", $"{s.Name} Meteor Shower Peak", s.Name, peak, Math.Max(Math.Abs(s.StartOffset), Math.Abs(s.EndOffset)) * 24, regionId, zone,
                [s.Name], ["Meteors"], s.Direction, s.Content, s.Visibility, s.Rarity, s.PublicInterest, "KnownCalendarRule",
                $"Annual meteor shower definition with peak date from known calendar rule; visibility region: {s.VisibilityRegion}.", ["Peak time is an approximate annual rule; hourly rates vary by moonlight, radiant altitude, and weather."]);
        }).ToArray();
    }

    private static IReadOnlyList<AstronomyEventPreviewItem> BuildKnownEclipses(int year, string regionId, TimeZoneInfo zone)
    {
        if (year != 2026) return [];

        return
        [
            CreateEvent("lunar-eclipse-20260303-total", "LunarEclipse", "Total Lunar Eclipse", "Total Lunar Eclipse", new DateTimeOffset(2026, 3, 3, 11, 0, 0, TimeSpan.Zero), 4, regionId, zone,
                ["Moon"], ["Earth shadow"], "Moon direction at eclipse time; local visibility depends on Moon being above horizon.", 91, 62, 92, 94, "ManualSeed",
                "Manual seed for the 2026 total lunar eclipse broad event window.", ["Broad UTC peak only; local contact times and Udaipur visibility require authoritative eclipse circumstances."]),
            CreateEvent("solar-eclipse-20260812-total", "SolarEclipse", "Total Solar Eclipse", "Total Solar Eclipse", new DateTimeOffset(2026, 8, 12, 18, 0, 0, TimeSpan.Zero), 4, regionId, zone,
                ["Sun", "Moon"], [], "Sun direction during local daytime only; path-specific visibility required.", 96, 35, 98, 99, "ManualSeed",
                "Manual seed for the 2026 total solar eclipse, with totality far outside India.", ["Not a local totality event for Udaipur; use only as global astronomy news unless local partial circumstances are verified."])
        ];
    }

    private static IReadOnlyList<AstronomyEventPreviewItem> BuildManualPlanetEvents(int year, string regionId, TimeZoneInfo zone)
    {
        if (year != 2026) return [];

        return
        [
            CreateEvent("planet-pairing-venus-jupiter-20260812", "PlanetPairing", "Venus and Jupiter Close Pairing", "Venus-Jupiter", new DateTimeOffset(2026, 8, 12, 2, 0, 0, TimeSpan.Zero), 8, regionId, zone,
                ["Venus", "Jupiter"], [], "Eastern predawn sky; verify local altitude and separation.", 86, 74, 80, 90, "ManualSeed",
                "Manual bright-planet pairing seed retained until endpoint-level Skyfield conjunction search is added.", ["Approximate date/time; do not state exact angular separation without Skyfield verification."]),
            CreateEvent("conjunction-moon-saturn-20261208", "Conjunction", "Moon and Saturn Conjunction", "Moon-Saturn", new DateTimeOffset(2026, 12, 8, 1, 0, 0, TimeSpan.Zero), 8, regionId, zone,
                ["Moon", "Saturn"], [], "Evening to late-night sky; verify local altitude and Moon phase.", 72, 68, 62, 76, "ManualSeed",
                "Manual Moon-planet conjunction seed retained until endpoint-level Skyfield conjunction search is added.", ["Approximate date/time; verify exact local circumstances before publishing."])
        ];
    }

    private static AstronomyEventPreviewItem CreateEvent(string eventId, string eventType, string title, string shortTitle, DateTimeOffset peakUtc, int windowHours, string regionId, TimeZoneInfo zone, IReadOnlyList<string> primaryObjects, IReadOnlyList<string> secondaryObjects, string directionHint, int contentScore, int visibilityScore, int rarityScore, int publicInterestScore, string sourceType, string sourceNotes, IReadOnlyList<string> warnings)
    {
        var start = peakUtc.AddHours(-Math.Max(1, windowHours));
        var end = peakUtc.AddHours(Math.Max(1, windowHours));
        return new AstronomyEventPreviewItem(
            eventId,
            eventType,
            title,
            shortTitle,
            start,
            peakUtc,
            end,
            TimeZoneInfo.ConvertTime(peakUtc, zone).ToString("yyyy-MM-dd HH:mm zzz"),
            regionId,
            primaryObjects,
            secondaryObjects,
            directionHint,
            Math.Clamp(contentScore, 0, 100),
            Math.Clamp(visibilityScore, 0, 100),
            Math.Clamp(rarityScore, 0, 100),
            Math.Clamp(publicInterestScore, 0, 100),
            DefaultContentTypes,
            new RecommendedPublishWindow(peakUtc.AddDays(-7), peakUtc.AddHours(-2)),
            sourceType,
            sourceNotes,
            warnings,
            EventFamilyResolver.Resolve(eventType, null, primaryObjects, secondaryObjects, title).ToString(),
            eventId);
    }

    private RegionScheduleOptions ResolveRegion(string regionId)
    {
        var normalized = NormalizeRegion(regionId);
        var configured = schedulerOptions.Value.Regions.Items.FirstOrDefault(r =>
            NormalizeRegion(r.RegionId).Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || NormalizeRegion(Slugify(r.RegionId)).Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (configured is not null)
            return configured;

        if (normalized.Equals("in-rj-udaipur", StringComparison.OrdinalIgnoreCase) || normalized.Equals("india-udaipur", StringComparison.OrdinalIgnoreCase))
        {
            return new RegionScheduleOptions
            {
                RegionId = regionId,
                DisplayName = "Udaipur, India",
                Latitude = 24.5854,
                Longitude = 73.7125,
                Timezone = "Asia/Kolkata",
                Language = "en",
                Enabled = true
            };
        }

        return new RegionScheduleOptions { RegionId = regionId, DisplayName = regionId, Timezone = "UTC", Language = "en", Enabled = true };
    }

    private string BuildOutputDirectory(string regionId, int year)
    {
        var root = string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
        return Path.Combine(root, "assets", SanitizePathSegment(regionId), "event-discovery", year.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static TimeZoneInfo ResolveZone(string timezone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static string NamedFullMoonName(int month) => month switch
    {
        1 => "Wolf",
        2 => "Snow",
        3 => "Worm",
        4 => "Pink",
        5 => "Flower",
        6 => "Strawberry",
        7 => "Buck",
        8 => "Sturgeon",
        9 => "Harvest",
        10 => "Hunter's",
        11 => "Beaver",
        12 => "Cold",
        _ => "Full Moon"
    };

    private static void Validate(AstronomyEventDiscoveryPreviewRequest request)
    {
        if (request.Year is < 1900 or > 2100) throw new ArgumentOutOfRangeException(nameof(request.Year), "year must be between 1900 and 2100.");
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
    }

    private static string NormalizeLanguage(string? language) => string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
    private static string NormalizeRegion(string value) => value.Trim().Replace('_', '-').ToLowerInvariant();
    private static string Slugify(string value) => NormalizeRegion(value).Replace(" ", "-");
    private static string SanitizePathSegment(string value) => string.Concat(value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));

    private sealed record MeteorDef(string Name, int Month, int Day, int StartOffset, int EndOffset, int Content, int Visibility, int PublicInterest, string Direction, string VisibilityRegion)
    {
        public int Rarity => Content switch
        {
            >= 90 => 86,
            >= 80 => 78,
            >= 70 => 66,
            _ => 55
        };
    }
}
