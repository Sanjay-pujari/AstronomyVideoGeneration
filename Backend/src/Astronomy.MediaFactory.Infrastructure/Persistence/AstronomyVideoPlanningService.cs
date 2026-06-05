using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyVideoPlanningService(
    MediaFactoryDbContext db,
    ILogger<AstronomyVideoPlanningService> logger) : IAstronomyVideoPlanningService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string PlanningPhase = "Phase7D-category-video-planning-engine";

    private static readonly IReadOnlyDictionary<string, string[]> SceneTemplates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["RareEventAlert"] = ["Hook", "What is happening", "When and where to look", "Why it matters", "CTA / reminder"],
        ["PlanetConjunction"] = ["Hook with object pair", "Sky map / where to look", "Closest approach / peak timing", "Why conjunction happens", "Viewing tips", "Myth/story/cultural context optional", "Recap"],
        ["PlanetGrouping"] = ["Hook", "Objects involved", "Sky direction and timing", "Constellation guide", "Viewing tips", "Weekly relevance"],
        ["WeeklySkyForecast"] = ["Weekly hook", "Top sky highlight", "Moon phase sequence", "Planet visibility", "Grouping/conjunction highlight", "Best viewing nights", "Outro"],
        ["MoonSpecials"] = ["Moon phase hook", "Illumination and timing", "How to observe/photograph", "Cultural/story angle", "Outro"],
        ["AstroPhotographyGuide"] = ["Photography hook", "Target and timing", "Gear and settings", "Composition tips", "CTA / shot checklist"],
        ["AstroExplainer"] = ["Explainer hook", "Phenomenon overview", "How the geometry works", "What viewers can observe", "Scientific context", "Common misconceptions", "Recap"]
    };

    private static readonly IReadOnlyDictionary<string, string> FormatMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RareEventAlert"] = "Short",
        ["PlanetConjunction"] = "ShortAndLong",
        ["WeeklySkyForecast"] = "Long",
        ["PlanetGrouping"] = "ShortAndLong",
        ["MoonSpecials"] = "Short",
        ["AstroPhotographyGuide"] = "Short",
        ["AstroExplainer"] = "Long"
    };

    public async Task<AstronomyVideoPlanningResult> GenerateVideoPlansAsync(AstronomyVideoPlanningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var warnings = new List<string>();
        var requestedCategories = request.ContentCategories is { Count: > 0 }
            ? request.ContentCategories.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        var categories = await db.ContentCategories.AsNoTracking().ToListAsync(cancellationToken);
        var enabledCategoryCodes = categories.Where(c => c.Enabled).Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requestedCategories is not null)
        {
            foreach (var category in requestedCategories.Where(c => !enabledCategoryCodes.Contains(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                warnings.Add($"Content category '{category}' is not present or enabled in content_categories; saved plans can still reference existing opportunity categories but downstream execution may reject them.");
        }

        var defaultNarrationStyle = await db.NarrationStyles.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.Priority).Select(x => x.Code).FirstOrDefaultAsync(cancellationToken);
        var defaultThumbnailStyle = await db.ThumbnailStyles.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.Priority).Select(x => x.Code).FirstOrDefaultAsync(cancellationToken);
        if (defaultNarrationStyle is null) warnings.Add("No enabled narration style was found in narration_styles; narration strategy uses a planning-only fallback style.");
        if (defaultThumbnailStyle is null) warnings.Add("No enabled thumbnail style was found in thumbnail_styles; thumbnail strategy uses a planning-only fallback style.");

        var query = db.AstronomyContentOpportunities
            .AsNoTracking()
            .Include(o => o.AstronomyEventIntelligence)
                .ThenInclude(e => e!.Objects)
            .Where(o => o.Status == "Proposed" && o.PriorityScore >= request.MinPriorityScore);

        if (!string.IsNullOrWhiteSpace(request.RegionId))
            query = query.Where(o => o.AstronomyEventIntelligence != null && o.AstronomyEventIntelligence.RegionId == request.RegionId);

        if (request.StartUtc.HasValue)
            query = query.Where(o => o.AstronomyEventIntelligence != null && o.AstronomyEventIntelligence.StartUtc >= request.StartUtc.Value);

        if (request.EndUtc.HasValue)
            query = query.Where(o => o.AstronomyEventIntelligence != null && o.AstronomyEventIntelligence.StartUtc <= request.EndUtc.Value);

        if (requestedCategories is not null && requestedCategories.Count > 0)
            query = query.Where(o => requestedCategories.Contains(o.ContentCategory));

        var opportunities = await query
            .OrderByDescending(o => o.PriorityScore)
            .ThenBy(o => o.AstronomyEventIntelligence!.StartUtc)
            .ToListAsync(cancellationToken);

        var generated = new List<AstronomyVideoPlanDto>();
        var maxPlans = request.MaxPlans ?? int.MaxValue;
        foreach (var opportunity in opportunities)
        {
            if (opportunity.AstronomyEventIntelligence is null)
            {
                warnings.Add($"Opportunity '{opportunity.Id}' skipped because it has no astronomy event intelligence record.");
                continue;
            }

            foreach (var format in ResolveFormats(opportunity.ContentCategory))
            {
                if (generated.Count >= maxPlans) break;
                generated.Add(BuildPlan(opportunity, opportunity.AstronomyEventIntelligence, format, defaultNarrationStyle, defaultThumbnailStyle));
            }

            if (generated.Count >= maxPlans) break;
        }

        var savedCount = 0;
        var skippedDuplicates = 0;
        if (!request.DryRun)
        {
            var persisted = new List<AstronomyVideoPlanDto>(generated.Count);
            foreach (var plan in generated)
            {
                var marker = DuplicateMarker(plan.OpportunityId, plan.PlannedFormat);
                var duplicate = await db.ContentGenerationPlans.AsNoTracking().AnyAsync(p =>
                    p.ContentCategoryCode == plan.ContentCategory &&
                    p.RegionId == plan.RegionId &&
                    p.PlanningReason != null &&
                    p.PlanningReason.Contains(marker), cancellationToken);

                if (duplicate)
                {
                    skippedDuplicates++;
                    persisted.Add(plan with { DuplicateSkipped = true });
                    continue;
                }

                var entity = ToContentGenerationPlan(plan);
                db.ContentGenerationPlans.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                savedCount++;
                persisted.Add(plan with { ContentGenerationPlanId = entity.Id });
            }

            generated = persisted;
        }

        logger.LogInformation("Generated {PlanCount} astronomy video plans. Saved={SavedCount}, Duplicates={SkippedDuplicates}, DryRun={DryRun}", generated.Count, savedCount, skippedDuplicates, request.DryRun);
        return new AstronomyVideoPlanningResult(generated.Count, savedCount, skippedDuplicates, request.DryRun, generated, warnings);
    }

    private static AstronomyVideoPlanDto BuildPlan(AstronomyContentOpportunity opportunity, AstronomyEventIntelligence evt, string plannedFormat, string? narrationStyleCode, string? thumbnailStyleCode)
    {
        var scenes = ResolveScenes(opportunity.ContentCategory, plannedFormat);
        var objectNames = evt.Objects.Select(o => o.ObjectName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var scheduledUtc = evt.PeakUtc ?? evt.StartUtc;
        var locationName = evt.LocationName;
        var regionId = evt.RegionId ?? string.Empty;

        var sceneStrategies = scenes.Select((name, index) => new
        {
            sceneNumber = index + 1,
            name,
            purpose = ScenePurpose(name),
            renderingStatus = "PlanningOnly"
        }).ToArray();

        var visualStrategyJson = JsonSerializer.Serialize(new
        {
            phase = PlanningPhase,
            opportunityId = opportunity.Id,
            astronomyEventIntelligenceId = evt.Id,
            plannedFormat,
            sceneCount = scenes.Count,
            sceneStrategy = sceneStrategies,
            sourceOpportunityVisualStrategy = TryParseJson(opportunity.VisualStrategyJson),
            requiresRendering = false,
            renderingEnginesToAvoid = new[] { "FFmpeg", "Stellarium", "ImageGeneration" },
            skyContext = new { evt.EventCode, evt.EventType, evt.Title, objectNames, evt.StartUtc, evt.PeakUtc, evt.EndUtc, evt.RegionId, evt.LocationName }
        }, JsonOptions);

        var narrationStrategyJson = JsonSerializer.Serialize(new
        {
            phase = PlanningPhase,
            language = "en",
            narrationStyleCode = narrationStyleCode ?? "planning-fallback",
            tone = Tone(opportunity.ContentCategory),
            sourceOpportunityNarrationStrategy = TryParseJson(opportunity.NarrationStrategyJson),
            sceneNarration = sceneStrategies.Select(s => new { s.sceneNumber, s.name, target = NarrationTarget(plannedFormat) }),
            generationStatus = "NotGenerated"
        }, JsonOptions);

        var thumbnailStrategyJson = JsonSerializer.Serialize(new
        {
            phase = PlanningPhase,
            thumbnailStyleCode = thumbnailStyleCode ?? "planning-fallback",
            thumbnailText = ThumbnailText(opportunity.Title),
            category = opportunity.ContentCategory,
            format = plannedFormat,
            generationStatus = "NotGenerated"
        }, JsonOptions);

        return new AstronomyVideoPlanDto(
            null,
            opportunity.Id,
            evt.Id,
            evt.EventCode,
            evt.EventType,
            opportunity.ContentCategory,
            opportunity.Title,
            "en",
            regionId,
            locationName,
            plannedFormat,
            scenes.Count,
            visualStrategyJson,
            narrationStrategyJson,
            thumbnailStrategyJson,
            "Planned",
            opportunity.PriorityScore,
            scheduledUtc,
            false);
    }

    private static ContentGenerationPlan ToContentGenerationPlan(AstronomyVideoPlanDto plan) => new()
    {
        ContentCategoryCode = plan.ContentCategory,
        Title = plan.SuggestedTitle,
        Language = plan.Language,
        RegionId = plan.RegionId,
        ScheduledUtc = plan.ScheduledUtc,
        Status = "Planned",
        PrimaryAstronomyEventTypeCode = plan.EventType,
        GeneratedByAi = false,
        Priority = PriorityFromScore(plan.PriorityScore),
        PlanningReason = BuildPlanningReason(plan)
    };

    private static string BuildPlanningReason(AstronomyVideoPlanDto plan) => JsonSerializer.Serialize(new
    {
        phase = PlanningPhase,
        opportunityId = plan.OpportunityId,
        astronomyEventIntelligenceId = plan.AstronomyEventIntelligenceId,
        eventCode = plan.EventCode,
        eventType = plan.EventType,
        contentCategory = plan.ContentCategory,
        suggestedTitle = plan.SuggestedTitle,
        language = plan.Language,
        regionId = plan.RegionId,
        locationName = plan.LocationName,
        plannedFormat = plan.PlannedFormat,
        sceneCount = plan.SceneCount,
        status = plan.Status,
        priorityScore = plan.PriorityScore,
        scheduledUtc = plan.ScheduledUtc,
        duplicateMarker = DuplicateMarker(plan.OpportunityId, plan.PlannedFormat),
        visualStrategy = TryParseJson(plan.VisualStrategyJson),
        narrationStrategy = TryParseJson(plan.NarrationStrategyJson),
        thumbnailStrategy = TryParseJson(plan.ThumbnailStrategyJson)
    }, JsonOptions);

    private static string DuplicateMarker(Guid opportunityId, string plannedFormat) => $"Phase7D:{opportunityId:N}:{plannedFormat}";

    private static string[] ResolveFormats(string contentCategory)
    {
        var plannedFormat = FormatMap.TryGetValue(contentCategory, out var format) ? format : "Long";
        return plannedFormat.Equals("ShortAndLong", StringComparison.OrdinalIgnoreCase) ? ["Short", "Long"] : [plannedFormat];
    }

    private static IReadOnlyList<string> ResolveScenes(string contentCategory, string plannedFormat)
    {
        var template = SceneTemplates.TryGetValue(contentCategory, out var scenes) ? scenes : SceneTemplates["AstroExplainer"];
        if (plannedFormat.Equals("Short", StringComparison.OrdinalIgnoreCase) && template.Length > 5)
            return template.Take(5).ToArray();
        return template;
    }

    private static JsonElement? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int PriorityFromScore(decimal priorityScore) => Math.Clamp(100 - (int)Math.Round(priorityScore * 10m, MidpointRounding.AwayFromZero), 1, 100);

    private static string ScenePurpose(string sceneName) => sceneName switch
    {
        "Hook" or "Weekly hook" or "Moon phase hook" or "Photography hook" or "Explainer hook" or "Hook with object pair" => "Open with a high-retention reason to watch.",
        "CTA / reminder" or "Outro" or "Recap" or "CTA / shot checklist" => "Close with an action, reminder, or concise recap.",
        _ => "Explain the event clearly while preserving a planning-only production state."
    };

    private static string Tone(string category) => category switch
    {
        "RareEventAlert" => "urgent, concise, practical",
        "WeeklySkyForecast" => "calm, curated, anticipatory",
        "MoonSpecials" => "warm, observational, story-rich",
        "AstroPhotographyGuide" => "practical, checklist-driven",
        "AstroExplainer" => "clear, educational, curious",
        _ => "excited, beginner-friendly, observational"
    };

    private static string NarrationTarget(string plannedFormat) => plannedFormat.Equals("Short", StringComparison.OrdinalIgnoreCase)
        ? "single concise voiceover beat"
        : "expanded educational narration beat";

    private static string ThumbnailText(string title) => title.Length <= 42 ? title : title[..39] + "...";

    private static void Validate(AstronomyVideoPlanningRequest request)
    {
        if (request.StartUtc.HasValue && request.EndUtc.HasValue && request.StartUtc > request.EndUtc)
            throw new ArgumentException("StartUtc must be before or equal to EndUtc.");
        if (request.MinPriorityScore < 0m || request.MinPriorityScore > 10m)
            throw new ArgumentException("MinPriorityScore must be between 0 and 10.");
        if (request.MaxPlans is <= 0)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");
    }
}
