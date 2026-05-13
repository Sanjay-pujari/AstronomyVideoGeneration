using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class AstronomyEventDiscoveryService : IAstronomyEventDiscoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly DateTimeOffset ReferenceNewMoonUtc = new(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
    private const double SynodicMonthDays = 29.530588853;

    private readonly AstronomyEventsOptions _options;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly IAstronomyEventScoringService _scoringService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AstronomyEventDiscoveryService> _logger;
    private readonly IAstronomyEventStore? _store;

    public AstronomyEventDiscoveryService(
        IOptions<AstronomyEventsOptions> options,
        IOptions<MaintenanceOptions> maintenanceOptions,
        IAstronomyEventScoringService scoringService,
        TimeProvider timeProvider,
        ILogger<AstronomyEventDiscoveryService> logger,
        IAstronomyEventStore? store = null)
    {
        _options = options.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _scoringService = scoringService;
        _timeProvider = timeProvider;
        _logger = logger;
        _store = store;
    }

    public async Task<IReadOnlyCollection<AstronomyEvent>> RefreshAsync(int? days, CancellationToken cancellationToken)
    {
        var lookAheadDays = NormalizeDays(days);
        var now = _timeProvider.GetUtcNow();
        var events = _options.Enabled
            ? DiscoverInternal(now, lookAheadDays)
            : [];

        var scored = await _scoringService.ScoreAsync(events, now, cancellationToken);
        var ordered = scored.OrderBy(e => e.PeakUtc ?? e.StartUtc).ThenByDescending(e => e.ContentOpportunityScore).ToArray();

        await WriteDiagnosticsAsync(ordered, now, lookAheadDays, cancellationToken);
        if (_store is not null)
        {
            await _store.UpsertEventsAsync(ordered, cancellationToken);
        }
        return ordered;
    }

    public async Task<IReadOnlyCollection<AstronomyEvent>> GetUpcomingAsync(int? days, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var end = now.AddDays(NormalizeDays(days));
        if (_store is not null)
        {
            var stored = await _store.GetUpcomingAsync(DateOnly.FromDateTime(now.UtcDateTime.Date), DateOnly.FromDateTime(end.UtcDateTime.Date), null, cancellationToken);
            if (stored.Count > 0)
                return stored.OrderBy(e => e.PeakUtc ?? e.StartUtc).ThenByDescending(e => e.ContentOpportunityScore).ToArray();
        }

        var cached = await ReadCacheAsync(cancellationToken);
        if (cached.Count == 0)
            return await RefreshAsync(days, cancellationToken);

        return cached
            .Where(e => (e.PeakUtc ?? e.StartUtc) >= now.AddHours(-12) && e.StartUtc <= end)
            .OrderBy(e => e.PeakUtc ?? e.StartUtc)
            .ThenByDescending(e => e.ContentOpportunityScore)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<AstronomyEvent>> GetTopAsync(int? days, CancellationToken cancellationToken)
    {
        var upcoming = await GetUpcomingAsync(days, cancellationToken);
        return upcoming
            .Where(e => e.ContentOpportunityScore >= _options.MinimumContentOpportunityScore)
            .OrderByDescending(e => e.ContentOpportunityScore)
            .ThenBy(e => e.PeakUtc ?? e.StartUtc)
            .ToArray();
    }


    public async Task<IReadOnlyCollection<AstronomyEvent>> RefreshEventsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var events = await RefreshAsync(Math.Max(1, toDate.DayNumber - DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime.Date).DayNumber + 1), cancellationToken);
        return events.Where(e => EffectiveTargetDate(e) >= fromDate && EffectiveTargetDate(e) <= toDate).ToArray();
    }

    public async Task<IReadOnlyCollection<AstronomyEvent>> DiscoverEventsForRegionAsync(string regionId, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var events = await GetUpcomingAsync(Math.Max(_options.LookAheadDays, targetDate.DayNumber - DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime.Date).DayNumber + 1), cancellationToken);
        return events.Where(e => EffectiveTargetDate(e) == targetDate && IsVisibleInRegion(e, regionId)).ToArray();
    }

    public async Task<IReadOnlyCollection<AstronomyEvent>> GetTopEventsAsync(string regionId, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var events = await DiscoverEventsForRegionAsync(regionId, targetDate, cancellationToken);
        return events.Where(e => e.ContentOpportunityScore >= _options.MinimumContentOpportunityScore).OrderByDescending(e => e.ContentOpportunityScore).ToArray();
    }

    public async Task<AstronomyEvent?> GetByIdAsync(string eventId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return null;

        if (_store is not null)
        {
            var stored = await _store.GetByEventIdAsync(eventId, cancellationToken);
            if (stored is not null)
                return stored;
        }

        var cached = await ReadCacheAsync(cancellationToken);
        return cached.FirstOrDefault(e => e.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase));
    }

    private static DateOnly EffectiveTargetDate(AstronomyEvent astronomyEvent)
        => astronomyEvent.TargetDate == default ? DateOnly.FromDateTime((astronomyEvent.PeakUtc ?? astronomyEvent.StartUtc).UtcDateTime) : astronomyEvent.TargetDate;

    private static bool IsVisibleInRegion(AstronomyEvent astronomyEvent, string regionId)
        => astronomyEvent.GlobalVisibility
            || string.IsNullOrWhiteSpace(regionId)
            || astronomyEvent.RegionId is null
            || string.Equals(astronomyEvent.RegionId, regionId, StringComparison.OrdinalIgnoreCase)
            || astronomyEvent.VisibilityRegions.Any(r => r.Contains(regionId, StringComparison.OrdinalIgnoreCase) || regionId.Contains(r, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyCollection<AstronomyEvent> DiscoverInternal(DateTimeOffset now, int lookAheadDays)
    {
        var events = new List<AstronomyEvent>();
        var windowStart = now.UtcDateTime.Date;
        var windowEnd = windowStart.AddDays(lookAheadDays);

        if (_options.Sources.MoonPhases)
            events.AddRange(BuildMoonPhaseEvents(windowStart, windowEnd));
        if (_options.Sources.MeteorShowers)
            events.AddRange(BuildMeteorShowerEvents(windowStart, windowEnd));
        if (_options.Sources.PlanetaryConjunctions)
            events.AddRange(BuildPlanetaryConjunctionFallbackEvents(windowStart, windowEnd));
        if (_options.Sources.Eclipses)
            events.AddRange(BuildEclipseFallbackEvents(windowStart, windowEnd));

        return events
            .GroupBy(e => e.EventId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.ConfidenceScore).First())
            .ToArray();
    }

    private static IEnumerable<AstronomyEvent> BuildMoonPhaseEvents(DateTime windowStart, DateTime windowEnd)
    {
        var firstCycle = Math.Floor((windowStart - ReferenceNewMoonUtc.UtcDateTime).TotalDays / SynodicMonthDays) - 1;
        var phaseOffsets = new[]
        {
            new { Name = "New Moon", Type = "moon_phase", Offset = 0.0, Rarity = 0.35, Visibility = 0.2, Audience = 0.45, Content = "short explainer" },
            new { Name = "First Quarter Moon", Type = "moon_phase", Offset = 0.25, Rarity = 0.3, Visibility = 0.7, Audience = 0.5, Content = "daily sky guide segment" },
            new { Name = "Full Moon", Type = "full_moon", Offset = 0.5, Rarity = 0.45, Visibility = 0.95, Audience = 0.85, Content = "short-form skywatching alert" },
            new { Name = "Last Quarter Moon", Type = "moon_phase", Offset = 0.75, Rarity = 0.3, Visibility = 0.55, Audience = 0.45, Content = "daily sky guide segment" }
        };

        for (var cycle = firstCycle; cycle < firstCycle + 5; cycle++)
        {
            foreach (var phase in phaseOffsets)
            {
                var peak = ReferenceNewMoonUtc.AddDays((cycle + phase.Offset) * SynodicMonthDays);
                if (peak.UtcDateTime < windowStart || peak.UtcDateTime > windowEnd)
                    continue;

                yield return new AstronomyEvent
                {
                    EventId = $"moon-{phase.Name.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()}-{peak:yyyyMMdd}",
                    EventType = phase.Type,
                    Title = phase.Name,
                    Description = $"Approximate {phase.Name.ToLowerInvariant()} timing calculated from the mean lunar synodic cycle.",
                    StartUtc = peak.AddHours(-12),
                    PeakUtc = peak,
                    EndUtc = peak.AddHours(12),
                    VisibilityRegions = ["Global where the Moon is above the horizon"],
                    GlobalVisibility = true,
                    RelatedObjects = ["Moon"],
                    RarityScore = phase.Rarity,
                    VisibilityScore = phase.Visibility,
                    AudienceInterestScore = phase.Audience,
                    RecommendedContentType = phase.Content,
                    TargetDate = DateOnly.FromDateTime(peak.UtcDateTime),
                    Source = "FallbackCalendar",
                    ConfidenceScore = 0.72
                };
            }
        }
    }

    private static IEnumerable<AstronomyEvent> BuildMeteorShowerEvents(DateTime windowStart, DateTime windowEnd)
    {
        var showers = new[]
        {
            new MeteorShower("Quadrantids", 1, 4, -8, 8, 0.78, 0.62, 0.72, ["Northern Hemisphere"]),
            new MeteorShower("Lyrids", 4, 22, -6, 5, 0.62, 0.58, 0.66, ["Northern Hemisphere", "Southern tropics"]),
            new MeteorShower("Eta Aquariids", 5, 6, -12, 15, 0.68, 0.55, 0.65, ["Southern Hemisphere", "Tropics"]),
            new MeteorShower("Perseids", 8, 12, -14, 12, 0.82, 0.78, 0.92, ["Northern Hemisphere"]),
            new MeteorShower("Orionids", 10, 21, -16, 14, 0.64, 0.62, 0.7, ["Global, best after midnight"]),
            new MeteorShower("Leonids", 11, 17, -11, 10, 0.74, 0.55, 0.72, ["Global, best after midnight"]),
            new MeteorShower("Geminids", 12, 14, -10, 7, 0.86, 0.82, 0.94, ["Global, best from mid-northern latitudes"]),
            new MeteorShower("Ursids", 12, 22, -5, 4, 0.55, 0.5, 0.48, ["Northern Hemisphere"])
        };

        for (var year = windowStart.Year - 1; year <= windowEnd.Year + 1; year++)
        {
            foreach (var shower in showers)
            {
                var peak = new DateTimeOffset(year, shower.PeakMonth, shower.PeakDay, 6, 0, 0, TimeSpan.Zero);
                var start = peak.AddDays(shower.StartOffsetDays);
                var end = peak.AddDays(shower.EndOffsetDays);
                if (end.UtcDateTime < windowStart || start.UtcDateTime > windowEnd)
                    continue;

                yield return new AstronomyEvent
                {
                    EventId = $"meteor-{shower.Name.ToLowerInvariant()}-{year}".Replace(" ", "-", StringComparison.Ordinal),
                    EventType = "meteor_shower",
                    Title = $"{shower.Name} meteor shower peak",
                    Description = $"Recurring {shower.Name} meteor shower activity window with peak timing estimated from known annual shower patterns.",
                    StartUtc = start,
                    PeakUtc = peak,
                    EndUtc = end,
                    VisibilityRegions = shower.VisibilityRegions,
                    GlobalVisibility = shower.VisibilityRegions.Any(r => r.Contains("Global", StringComparison.OrdinalIgnoreCase)),
                    RelatedObjects = [shower.Name, "Meteors"],
                    RarityScore = shower.RarityScore,
                    VisibilityScore = shower.VisibilityScore,
                    AudienceInterestScore = shower.AudienceInterestScore,
                    RecommendedContentType = "short-form skywatching alert",
                    TargetDate = DateOnly.FromDateTime(peak.UtcDateTime),
                    Source = "FallbackCalendar",
                    ConfidenceScore = 0.82
                };
            }
        }
    }


    private static IEnumerable<AstronomyEvent> BuildPlanetaryConjunctionFallbackEvents(DateTime windowStart, DateTime windowEnd)
    {
        var year = windowStart.Year;
        var candidates = new[]
        {
            new { Date = new DateTimeOffset(year, 8, 12, 2, 0, 0, TimeSpan.Zero), Title = "Venus and Jupiter close approach", Objects = new[] { "Venus", "Jupiter" } },
            new { Date = new DateTimeOffset(year, 12, 8, 1, 0, 0, TimeSpan.Zero), Title = "Moon and Saturn conjunction", Objects = new[] { "Moon", "Saturn" } }
        };

        foreach (var item in candidates)
        {
            if (item.Date.UtcDateTime < windowStart || item.Date.UtcDateTime > windowEnd)
                continue;

            yield return new AstronomyEvent
            {
                EventId = $"conjunction-{item.Title.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal)}-{item.Date:yyyyMMdd}",
                EventType = "planetary_conjunction",
                Title = item.Title,
                Description = "Approximate bright-object conjunction from fallback calendar; verify local horizon and exact separation before publishing precise claims.",
                StartUtc = item.Date.AddHours(-8),
                PeakUtc = item.Date,
                EndUtc = item.Date.AddHours(8),
                TargetDate = DateOnly.FromDateTime(item.Date.UtcDateTime),
                VisibilityRegions = ["Global where objects are above the horizon"],
                GlobalVisibility = true,
                RelatedObjects = item.Objects,
                RarityScore = 0.76,
                VisibilityScore = 0.68,
                AudienceInterestScore = 0.78,
                RecommendedContentType = "daily sky guide segment",
                Source = "FallbackCalendar",
                ConfidenceScore = 0.58
            };
        }
    }

    private static IEnumerable<AstronomyEvent> BuildEclipseFallbackEvents(DateTime windowStart, DateTime windowEnd)
    {
        var eclipses = new[]
        {
            new { Date = new DateTimeOffset(2026, 8, 12, 18, 0, 0, TimeSpan.Zero), Title = "Total solar eclipse", Type = "eclipse", Regions = new[] { "Arctic", "Greenland", "Iceland", "Spain" }, Objects = new[] { "Sun", "Moon" } },
            new { Date = new DateTimeOffset(2026, 3, 3, 11, 0, 0, TimeSpan.Zero), Title = "Total lunar eclipse", Type = "eclipse", Regions = new[] { "Americas", "Pacific", "East Asia" }, Objects = new[] { "Moon" } }
        };

        foreach (var item in eclipses)
        {
            if (item.Date.UtcDateTime < windowStart || item.Date.UtcDateTime > windowEnd)
                continue;

            yield return new AstronomyEvent
            {
                EventId = $"eclipse-{item.Title.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal)}-{item.Date:yyyyMMdd}",
                EventType = item.Type,
                Title = item.Title,
                Description = "Known eclipse fallback entry with broad visibility regions; use authoritative local circumstances for exact contact times.",
                StartUtc = item.Date.AddHours(-3),
                PeakUtc = item.Date,
                EndUtc = item.Date.AddHours(3),
                TargetDate = DateOnly.FromDateTime(item.Date.UtcDateTime),
                VisibilityRegions = item.Regions,
                GlobalVisibility = false,
                RelatedObjects = item.Objects,
                RarityScore = 0.98,
                VisibilityScore = 0.72,
                AudienceInterestScore = 0.98,
                RecommendedContentType = "SpecialEventGuide",
                Source = "FallbackCalendar",
                ConfidenceScore = 0.68
            };
        }
    }

    private int NormalizeDays(int? days) => Math.Clamp(days ?? _options.LookAheadDays, 1, 365);

    private string CachePath => Path.Combine(WorkingDirectory, "astronomy-events-cache.json");
    private string ReportPath => Path.Combine(WorkingDirectory, "astronomy-event-scoring-report.json");
    private string WorkingDirectory => string.IsNullOrWhiteSpace(_maintenanceOptions.WorkingDirectory) ? "./media-output" : _maintenanceOptions.WorkingDirectory;

    private async Task<IReadOnlyCollection<AstronomyEvent>> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(CachePath))
                return [];

            await using var stream = File.OpenRead(CachePath);
            var payload = await JsonSerializer.DeserializeAsync<AstronomyEventCachePayload>(stream, JsonOptions, cancellationToken);
            return payload?.Events ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to read astronomy events cache; refreshing discovery data.");
            return [];
        }
    }

    private async Task WriteDiagnosticsAsync(IReadOnlyCollection<AstronomyEvent> events, DateTimeOffset now, int lookAheadDays, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(WorkingDirectory);
        var cache = new AstronomyEventCachePayload(now, lookAheadDays, events.ToArray());
        await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(cache, JsonOptions), cancellationToken);

        var report = new
        {
            generatedUtc = now,
            lookAheadDays,
            minimumContentOpportunityScore = _options.MinimumContentOpportunityScore,
            scoringFormula = "rarityScore * 0.35 + visibilityScore * 0.30 + audienceInterestScore * 0.25 + timingUrgencyScore * 0.10",
            sourceStatus = new
            {
                moonPhases = _options.Sources.MoonPhases ? "internal fallback enabled" : "disabled",
                meteorShowers = _options.Sources.MeteorShowers ? "internal fallback enabled" : "disabled",
                planetaryConjunctions = _options.Sources.PlanetaryConjunctions ? "fallback calendar enabled" : "disabled",
                eclipses = _options.Sources.Eclipses ? "fallback known-events calendar enabled" : "disabled",
                comets = _options.Sources.Comets ? "no authoritative source configured in Phase 7A" : "disabled",
                issPasses = _options.Sources.IssPasses ? "no authoritative source configured in Phase 7A" : "disabled"
            },
            events = events.Select(e => new
            {
                e.EventId,
                e.EventType,
                e.Title,
                e.StartUtc,
                e.PeakUtc,
                e.EndUtc,
                e.RarityScore,
                e.VisibilityScore,
                e.AudienceInterestScore,
                e.TimingUrgencyScore,
                e.ContentOpportunityScore,
                e.Source,
                e.ConfidenceScore
            })
        };
        await File.WriteAllTextAsync(ReportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
    }

    private sealed record AstronomyEventCachePayload(DateTimeOffset GeneratedUtc, int LookAheadDays, AstronomyEvent[] Events);
    private sealed record MeteorShower(string Name, int PeakMonth, int PeakDay, int StartOffsetDays, int EndOffsetDays, double RarityScore, double VisibilityScore, double AudienceInterestScore, string[] VisibilityRegions);
}

public sealed class AstronomyEventScoringService : IAstronomyEventScoringService
{
    public Task<IReadOnlyCollection<AstronomyEvent>> ScoreAsync(IReadOnlyCollection<AstronomyEvent> events, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var scored = events.Select(e => Score(e, now)).ToArray();
        return Task.FromResult<IReadOnlyCollection<AstronomyEvent>>(scored);
    }

    public AstronomyEvent Score(AstronomyEvent astronomyEvent, DateTimeOffset now)
    {
        var rarity = astronomyEvent.RarityScore > 0 ? astronomyEvent.RarityScore : DefaultRarity(astronomyEvent.EventType);
        var visibility = astronomyEvent.VisibilityScore > 0 ? astronomyEvent.VisibilityScore : DefaultVisibility(astronomyEvent.EventType, astronomyEvent.GlobalVisibility);
        var audience = astronomyEvent.AudienceInterestScore > 0 ? astronomyEvent.AudienceInterestScore : DefaultAudience(astronomyEvent.EventType);
        var urgency = TimingUrgency(astronomyEvent.PeakUtc ?? astronomyEvent.StartUtc, now);
        var opportunity = Clamp01(rarity) * 0.35 + Clamp01(visibility) * 0.30 + Clamp01(audience) * 0.25 + urgency * 0.10;

        if (astronomyEvent.TargetDate == default)
            astronomyEvent.TargetDate = DateOnly.FromDateTime((astronomyEvent.PeakUtc ?? astronomyEvent.StartUtc).UtcDateTime);
        astronomyEvent.RarityScore = Math.Round(Clamp01(rarity), 3);
        astronomyEvent.VisibilityScore = Math.Round(Clamp01(visibility), 3);
        astronomyEvent.AudienceInterestScore = Math.Round(Clamp01(audience), 3);
        astronomyEvent.TimingUrgencyScore = Math.Round(urgency, 3);
        astronomyEvent.ContentOpportunityScore = Math.Round(opportunity, 3);
        return astronomyEvent;
    }

    private static double TimingUrgency(DateTimeOffset target, DateTimeOffset now)
    {
        var days = (target - now).TotalDays;
        if (days < -1) return 0.05;
        if (days <= 1) return 1.0;
        if (days <= 3) return 0.85;
        if (days <= 7) return 0.7;
        if (days <= 14) return 0.5;
        if (days <= 30) return 0.32;
        return 0.18;
    }

    private static double DefaultRarity(string eventType) => eventType.ToLowerInvariant() switch
    {
        "eclipse" => 0.95,
        "comet" => 0.9,
        "planetary_conjunction" => 0.78,
        "opposition" => 0.7,
        "meteor_shower" => 0.68,
        "full_moon" => 0.45,
        _ => 0.35
    };

    private static double DefaultVisibility(string eventType, bool globalVisibility) => eventType.ToLowerInvariant() switch
    {
        "full_moon" => 0.95,
        "moon_phase" => 0.65,
        "meteor_shower" => globalVisibility ? 0.72 : 0.6,
        "opposition" => 0.72,
        "planetary_conjunction" => 0.62,
        "eclipse" => globalVisibility ? 0.9 : 0.48,
        _ => 0.5
    };

    private static double DefaultAudience(string eventType) => eventType.ToLowerInvariant() switch
    {
        "eclipse" => 0.98,
        "comet" => 0.9,
        "meteor_shower" => 0.82,
        "full_moon" => 0.82,
        "planetary_conjunction" => 0.72,
        "opposition" => 0.7,
        _ => 0.5
    };

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}
