using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyContentOpportunityService(
    MediaFactoryDbContext db,
    ILogger<AstronomyContentOpportunityService> logger) : IAstronomyContentOpportunityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, string[]> CategoryMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["PLANET_CONJUNCTION"] = ["PlanetConjunction", "RareEventAlert", "WeeklySkyForecast"],
        ["PLANET_GROUPING"] = ["PlanetGrouping", "WeeklySkyForecast", "AstroExplainer"],
        ["BRIGHT_PLANET_VISIBILITY"] = ["PlanetVisibilityGuide", "WeeklySkyForecast"],
        ["MOON_SPECIAL"] = ["MoonSpecials", "AstroPhotographyGuide"]
    };

    private static readonly IReadOnlyDictionary<string, decimal> CategoryWeights = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
    {
        ["RareEventAlert"] = 2.00m,
        ["PlanetConjunction"] = 1.75m,
        ["PlanetGrouping"] = 1.00m,
        ["AstroExplainer"] = 0.75m,
        ["WeeklySkyForecast"] = 0.50m,
        ["MoonSpecials"] = 0.50m,
        ["AstroPhotographyGuide"] = 0.40m,
        ["PlanetVisibilityGuide"] = 0.00m
    };

    public async Task<AstronomyContentOpportunityResult> GenerateAsync(AstronomyContentOpportunityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var requestedTypes = request.EventTypes is { Count: > 0 }
            ? request.EventTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Select(NormalizeEventType).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        var query = db.AstronomyEventIntelligences
            .AsNoTracking()
            .Where(e => e.Status == "Candidate");

        if (!string.IsNullOrWhiteSpace(request.RegionId))
            query = query.Where(e => e.RegionId == request.RegionId);

        if (request.StartUtc.HasValue)
            query = query.Where(e => e.StartUtc >= request.StartUtc.Value);

        if (request.EndUtc.HasValue)
            query = query.Where(e => e.StartUtc <= request.EndUtc.Value);

        if (requestedTypes is not null && requestedTypes.Count > 0)
            query = query.Where(e => requestedTypes.Contains(e.EventType));

        var events = await query
            .Include(e => e.Objects)
            .OrderByDescending(e => e.ContentOpportunityScore)
            .ThenByDescending(e => e.VisibilityScore)
            .ThenBy(e => e.StartUtc)
            .ToListAsync(cancellationToken);

        var generated = events
            .SelectMany(BuildOpportunities)
            .OrderByDescending(o => o.PriorityScore)
            .ThenBy(o => o.EventTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.ContentCategory, StringComparer.OrdinalIgnoreCase)
            .Take(request.MaxOpportunities ?? int.MaxValue)
            .ToList();

        var duplicateKeys = await LoadDuplicateKeysAsync(generated, cancellationToken);
        var annotated = generated
            .Select(o => o with { DuplicateSkipped = duplicateKeys.Contains(Key(o.AstronomyEventIntelligenceId, o.ContentCategory)) })
            .ToList();

        var savedCount = 0;
        if (!request.DryRun)
        {
            for (var i = 0; i < annotated.Count; i++)
            {
                var dto = annotated[i];
                if (dto.DuplicateSkipped) continue;

                var entity = ToEntity(dto);
                db.AstronomyContentOpportunities.Add(entity);
                annotated[i] = dto with { Id = entity.Id };
                savedCount++;
            }

            if (savedCount > 0)
                await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Astronomy content opportunity generation completed. DryRun={DryRun}; Generated={GeneratedCount}; Saved={SavedCount}; SkippedDuplicates={SkippedDuplicates}", request.DryRun, annotated.Count, savedCount, annotated.Count(o => o.DuplicateSkipped));

        return new AstronomyContentOpportunityResult(
            annotated.Count,
            savedCount,
            annotated.Count(o => o.DuplicateSkipped),
            request.DryRun,
            annotated);
    }

    private async Task<HashSet<string>> LoadDuplicateKeysAsync(IReadOnlyList<AstronomyContentOpportunityDto> opportunities, CancellationToken cancellationToken)
    {
        if (opportunities.Count == 0) return [];

        var eventIds = opportunities.Select(o => o.AstronomyEventIntelligenceId).Distinct().ToArray();
        var categories = opportunities.Select(o => o.ContentCategory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var existing = await db.AstronomyContentOpportunities
            .AsNoTracking()
            .Where(o => eventIds.Contains(o.AstronomyEventIntelligenceId) && categories.Contains(o.ContentCategory))
            .Select(o => new { o.AstronomyEventIntelligenceId, o.ContentCategory })
            .ToListAsync(cancellationToken);

        return existing.Select(o => Key(o.AstronomyEventIntelligenceId, o.ContentCategory)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<AstronomyContentOpportunityDto> BuildOpportunities(AstronomyEventIntelligence evt)
    {
        if (!CategoryMap.TryGetValue(NormalizeEventType(evt.EventType), out var categories))
            yield break;

        foreach (var category in categories)
            yield return BuildOpportunity(evt, category);
    }

    private static AstronomyContentOpportunityDto BuildOpportunity(AstronomyEventIntelligence evt, string category)
    {
        var storyScore = Clamp(evt.AudienceInterestScore);
        var viralPotentialScore = Clamp(evt.ContentOpportunityScore);
        var basePriorityScore = Clamp(
            (evt.VisibilityScore * 0.30m) +
            (evt.RarityScore * 0.25m) +
            (storyScore * 0.20m) +
            (viralPotentialScore * 0.15m) +
            (evt.ConfidenceScore * 0.10m));
        var categoryWeight = CategoryWeight(category);
        var priorityScore = Clamp(basePriorityScore + categoryWeight);
        var scoringReason = ScoringReason(category);
        var educationalValueScore = EducationalValueScore(evt, category, storyScore);
        var viralScore = ViralScore(evt, category, viralPotentialScore);
        var productionReadinessScore = ProductionReadinessScore(evt, category);
        var requiresConstellationGuide = IsConjunctionOrGrouping(evt.EventType);
        var requiresStellarium = IsConjunctionOrGrouping(evt.EventType) || NormalizeEventType(evt.EventType) == "BRIGHT_PLANET_VISIBILITY";
        var requiresNasaAssets = category is "MoonSpecials" or "AstroExplainer";
        var requiresAiImages = RequiresAiImages(category);

        var selectedObjects = evt.Objects
            .OrderBy(o => o.CreatedUtc)
            .ThenBy(o => o.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AstronomyContentOpportunityDto(
            null,
            evt.Id,
            evt.EventCode,
            evt.EventType,
            evt.Title,
            category,
            BuildTitle(evt, category),
            BuildAngle(evt, category),
            AudienceSegment(category),
            priorityScore,
            basePriorityScore,
            categoryWeight,
            scoringReason,
            Clamp(evt.VisibilityScore),
            Clamp(evt.RarityScore),
            storyScore,
            viralPotentialScore,
            Clamp(evt.ConfidenceScore),
            educationalValueScore,
            viralScore,
            productionReadinessScore,
            RequiresSkyfield: true,
            RequiresConstellationGuide: requiresConstellationGuide,
            RequiresStellarium: requiresStellarium,
            RequiresNasaAssets: requiresNasaAssets,
            RequiresAiImages: requiresAiImages,
            Status: "Proposed",
            SelectedEventObjectIds: selectedObjects.Select(o => o.Id).ToArray(),
            SelectedObjectNames: selectedObjects.Select(o => o.ObjectName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            DuplicateSkipped: false);
    }

    private static AstronomyContentOpportunity ToEntity(AstronomyContentOpportunityDto dto) => new()
    {
        AstronomyEventIntelligenceId = dto.AstronomyEventIntelligenceId,
        ContentCategory = dto.ContentCategory,
        Title = dto.Title,
        Angle = dto.Angle,
        AudienceSegment = dto.AudienceSegment,
        PriorityScore = dto.PriorityScore,
        Status = dto.Status,
        SelectedEventObjectIdsJson = JsonSerializer.Serialize(dto.SelectedEventObjectIds, JsonOptions),
        SelectedObjectNamesJson = JsonSerializer.Serialize(dto.SelectedObjectNames, JsonOptions),
        VisualStrategyJson = JsonSerializer.Serialize(new
        {
            dto.RequiresSkyfield,
            dto.RequiresConstellationGuide,
            dto.RequiresStellarium,
            dto.RequiresNasaAssets,
            dto.RequiresAiImages,
            visualStyle = VisualStyle(dto.ContentCategory)
        }, JsonOptions),
        NarrationStrategyJson = JsonSerializer.Serialize(new
        {
            hook = Hook(dto.ContentCategory),
            tone = Tone(dto.ContentCategory),
            educationalValueScore = dto.EducationalValueScore,
            viralScore = dto.ViralScore,
            scoring = new
            {
                basePriorityScore = dto.BasePriorityScore,
                categoryWeight = dto.CategoryWeight,
                finalPriorityScore = dto.PriorityScore,
                scoringReason = dto.ScoringReason
            }
        }, JsonOptions),
        MetadataJson = JsonSerializer.Serialize(new
        {
            phase = "Phase7C-content-opportunity-scoring-engine",
            dto.EventCode,
            dto.EventType,
            dto.EventTitle,
            dto.VisibilityScore,
            dto.RarityScore,
            dto.StoryScore,
            dto.ViralPotentialScore,
            dto.ConfidenceScore,
            dto.EducationalValueScore,
            dto.ViralScore,
            dto.ProductionReadinessScore,
            dto.BasePriorityScore,
            dto.CategoryWeight,
            finalPriorityScore = dto.PriorityScore,
            dto.ScoringReason,
            dto.RequiresSkyfield,
            dto.RequiresConstellationGuide,
            dto.RequiresStellarium,
            dto.RequiresNasaAssets,
            dto.RequiresAiImages,
            scoringFormula = "FinalPriorityScore = min(10.00, (VisibilityScore * 0.30 + RarityScore * 0.25 + StoryScore * 0.20 + ViralPotentialScore * 0.15 + ConfidenceScore * 0.10) + CategoryWeight)"
        }, JsonOptions)
    };

    private static string BuildTitle(AstronomyEventIntelligence evt, string category) => category switch
    {
        "PlanetConjunction" => $"Conjunction guide: {evt.Title}",
        "RareEventAlert" => $"Rare sky alert: {evt.Title}",
        "WeeklySkyForecast" => $"Weekly forecast highlight: {evt.Title}",
        "PlanetGrouping" => $"Planet grouping guide: {evt.Title}",
        "AstroExplainer" => $"Explainer: why {evt.Title} matters",
        "PlanetVisibilityGuide" => $"Bright planet viewing guide: {evt.Title}",
        "MoonSpecials" => $"Moon special: {evt.Title}",
        "AstroPhotographyGuide" => $"Astrophotography plan: {evt.Title}",
        _ => evt.Title
    };

    private static string BuildAngle(AstronomyEventIntelligence evt, string category) => category switch
    {
        "PlanetConjunction" => "Show viewers when and where to look, why the close apparent pairing happens, and what simple gear improves the view.",
        "RareEventAlert" => "Package the event as a time-sensitive alert with rarity context and a clear step-outside viewing window.",
        "WeeklySkyForecast" => "Rank the event as a weekly sky highlight without starting or changing the WeeklySkyForecast rendering flow.",
        "PlanetGrouping" => "Turn the multi-planet alignment into a beginner-friendly sky map and naked-eye observing plan.",
        "AstroExplainer" => "Explain the orbital geometry and visual storytelling behind the event using educational cinematic support.",
        "PlanetVisibilityGuide" => "Create a practical naked-eye planet guide focused on direction, timing, brightness, and viewer expectations.",
        "MoonSpecials" => "Build a moon-focused short with phase, illumination, viewing timing, and cultural/seasonal context.",
        "AstroPhotographyGuide" => "Translate the event into a simple phone or camera shot list with timing and framing advice.",
        _ => evt.Summary ?? evt.Description
    };

    private static decimal EducationalValueScore(AstronomyEventIntelligence evt, string category, decimal storyScore)
    {
        var bonus = category is "AstroExplainer" or "AstroPhotographyGuide" ? 1.2m : category is "WeeklySkyForecast" ? 0.6m : 0.3m;
        if (NormalizeEventType(evt.EventType) == "MOON_SPECIAL") bonus += 0.4m;
        return Clamp((storyScore * 0.65m) + (evt.ConfidenceScore * 0.25m) + bonus);
    }

    private static decimal ViralScore(AstronomyEventIntelligence evt, string category, decimal viralPotentialScore)
    {
        var bonus = category is "RareEventAlert" or "MoonSpecials" ? 1.0m : category is "PlanetGrouping" or "PlanetConjunction" ? 0.6m : 0.2m;
        return Clamp((viralPotentialScore * 0.70m) + (evt.RarityScore * 0.20m) + bonus);
    }

    private static decimal ProductionReadinessScore(AstronomyEventIntelligence evt, string category)
    {
        var toolPenalty = category is "AstroExplainer" or "RareEventAlert" ? 0.8m : category is "WeeklySkyForecast" ? 0.4m : 0.2m;
        return Clamp((evt.ConfidenceScore * 0.45m) + (evt.VisibilityScore * 0.35m) + 2.0m - toolPenalty);
    }

    private static string AudienceSegment(string category) => category switch
    {
        "RareEventAlert" => "Casual sky watchers and short-form alert viewers",
        "WeeklySkyForecast" => "Weekly astronomy forecast viewers",
        "AstroExplainer" => "Curious learners and science explainers audience",
        "AstroPhotographyGuide" => "Phone photographers and beginner astrophotographers",
        _ => "Beginner astronomy viewers"
    };

    private static decimal CategoryWeight(string category) => CategoryWeights.TryGetValue(category, out var weight) ? weight : 0m;

    private static string ScoringReason(string category) => category switch
    {
        "RareEventAlert" => "Rare alert format has the highest urgency and strongest short-form hook.",
        "PlanetConjunction" => "Specific conjunction event has higher urgency and stronger short-form hook than generic visibility.",
        "PlanetGrouping" => "Multi-object grouping is more visually distinctive than a generic visibility window.",
        "AstroExplainer" => "Explainer angle adds story depth and educational retention potential.",
        "WeeklySkyForecast" => "Weekly highlight benefits from forecast context while staying below specific rare-event alerts.",
        "MoonSpecials" => "Moon-focused special adds seasonal and visual hook value over routine visibility.",
        "AstroPhotographyGuide" => "Photography guide adds practical capture value for viewers planning a sky shot.",
        "PlanetVisibilityGuide" => "Generic planet visibility keeps the base score without an event-specific urgency boost.",
        _ => "No category-specific priority adjustment was configured."
    };

    private static bool RequiresAiImages(string category) => category is "RareEventAlert" or "PlanetGrouping" or "AstroExplainer" or "MoonSpecials" or "AstroPhotographyGuide";
    private static bool IsConjunctionOrGrouping(string eventType) => NormalizeEventType(eventType) is "PLANET_CONJUNCTION" or "PLANET_GROUPING";
    private static string VisualStyle(string category) => RequiresAiImages(category) ? "cinematic-educational-sky-visuals" : "observational-sky-map-and-timing-card";
    private static string Hook(string category) => category == "RareEventAlert" ? "urgent-rare-sky-moment" : "clear-viewing-payoff";
    private static string Tone(string category) => category == "AstroExplainer" ? "curious-educational" : "practical-inspiring";
    private static string Key(Guid eventId, string category) => $"{eventId:N}:{category}";

    private static decimal Clamp(decimal score) => Math.Clamp(Math.Round(score, 2), 0m, 10m);

    private static string NormalizeEventType(string eventType)
    {
        var normalized = eventType.Trim().Replace('-', '_').Replace(' ', '_');
        if (!normalized.Contains('_'))
            normalized = string.Concat(normalized.Select((ch, index) => index > 0 && char.IsUpper(ch) ? $"_{ch}" : ch.ToString()));

        return normalized.ToUpperInvariant();
    }

    private static void Validate(AstronomyContentOpportunityRequest request)
    {
        if (request.EndUtc.HasValue && request.StartUtc.HasValue && request.EndUtc.Value < request.StartUtc.Value)
            throw new ArgumentException("endUtc must be greater than or equal to startUtc.", nameof(request));

        if (request.MaxOpportunities is <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.MaxOpportunities), "maxOpportunities must be greater than zero when provided.");
    }
}
