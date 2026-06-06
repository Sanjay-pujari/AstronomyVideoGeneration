using System.Globalization;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class NarrationPlanningService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<NarrationPlanningService> logger) : INarrationPlanningService
{
    private const string PlannedStatus = "Planned";
    private const string NarrationStyle = "ProfessionalCinematic";
    private const string GenerationSource = "Phase9A";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] QualityChecklist =
    [
        "Clear opening hook",
        "No robotic narration",
        "No fake certainty",
        "Scientifically safe wording",
        "Short sentences for voiceover",
        "Scene-by-scene pacing",
        "Professional documentary tone"
    ];

    public async Task<NarrationPlanningResult> GenerateNarrationScriptsAsync(NarrationPlanningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var scripts = new List<NarrationScriptDocument>();
        var skipped = 0;
        var failed = 0;
        var planIds = request.PlanIds is { Count: > 0 } ? request.PlanIds.Where(x => x != Guid.Empty).ToHashSet() : null;
        var categories = ToSet(request.ContentCategories);
        var formats = ToSet(request.PlannedFormats);
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim();

        var query = db.ContentGenerationPlans
            .AsNoTracking()
            .Include(p => p.AstronomyEventIntelligence)
            .Include(p => p.AstronomyContentOpportunity)
                .ThenInclude(o => o!.AstronomyEventIntelligence)
            .Where(p => p.PlanStatus == PlannedStatus)
            .Where(p => p.AstronomyContentOpportunityId != null || p.AstronomyEventIntelligenceId != null);

        if (!string.IsNullOrWhiteSpace(request.RegionId))
            query = query.Where(p => p.RegionId == request.RegionId);
        if (planIds is { Count: > 0 })
            query = query.Where(p => planIds.Contains(p.Id));
        if (categories is { Count: > 0 })
            query = query.Where(p => categories.Contains(p.ContentCategoryCode));
        if (formats is { Count: > 0 })
            query = query.Where(p => p.PlannedFormat != null && formats.Contains(p.PlannedFormat));

        query = query.OrderByDescending(p => p.PriorityScore ?? 0m).ThenBy(p => p.ScheduledUtc ?? DateTimeOffset.MaxValue);
        if (request.MaxPlans.HasValue)
            query = query.Take(request.MaxPlans.Value);

        var plans = await query.ToListAsync(cancellationToken);
        var root = ResolveWorkingDirectoryRoot();

        foreach (var plan in plans)
        {
            try
            {
                var outputPath = BuildOutputPath(root, plan.RegionId, plan.Id);
                if (!request.DryRun && File.Exists(outputPath) && !request.OverwriteExisting)
                {
                    skipped++;
                    continue;
                }

                var script = BuildNarrationScript(plan, language);
                scripts.Add(script);

                if (!request.DryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? root);
                    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(script, JsonOptions), cancellationToken);
                    generatedFiles.Add(outputPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                warnings.Add($"Failed to generate narration script for plan {plan.Id:D}: {ex.Message}");
                logger.LogWarning(ex, "Phase 9A narration script generation failed for plan {PlanId}", plan.Id);
            }
        }

        logger.LogInformation("Phase 9A narration planning processed {PlanCount} plan(s). Generated={GeneratedCount} Skipped={SkippedCount} Failed={FailedCount} DryRun={DryRun}", plans.Count, scripts.Count, skipped, failed, request.DryRun);
        return new NarrationPlanningResult(plans.Count, scripts.Count, skipped, failed, scripts, generatedFiles, warnings);
    }

    private NarrationScriptDocument BuildNarrationScript(ContentGenerationPlan plan, string language)
    {
        var intelligence = plan.AstronomyEventIntelligence ?? plan.AstronomyContentOpportunity?.AstronomyEventIntelligence;
        var category = string.IsNullOrWhiteSpace(plan.ContentCategoryCode) ? plan.AstronomyContentOpportunity?.ContentCategory ?? "AstronomyUpdate" : plan.ContentCategoryCode;
        var format = string.IsNullOrWhiteSpace(plan.PlannedFormat) ? "Long" : plan.PlannedFormat!;
        var location = FirstNonEmpty(intelligence?.LocationName, plan.RegionId, "your sky");
        var title = FirstNonEmpty(plan.Title, plan.AstronomyContentOpportunity?.Title, intelligence?.Title, BuildFallbackTitle(category, location));
        var objectNames = ParseStringArray(plan.PlannedObjectNamesJson);
        if (objectNames.Count == 0)
            objectNames = ParseStringArray(plan.AstronomyContentOpportunity?.SelectedObjectNamesJson);
        if (objectNames.Count == 0 && !string.IsNullOrWhiteSpace(plan.PrimaryCelestialObjectCode))
            objectNames = [plan.PrimaryCelestialObjectCode!];

        var context = new NarrationContext(plan, intelligence, category, format, location, title, objectNames);
        var segments = BuildSegments(context);
        return new NarrationScriptDocument(
            plan.Id.ToString("D"),
            plan.AstronomyEventIntelligenceId?.ToString("D") ?? string.Empty,
            plan.AstronomyContentOpportunityId?.ToString("D") ?? string.Empty,
            category,
            plan.PlannedFormat,
            language,
            plan.RegionId,
            location,
            title,
            NarrationStyle,
            segments.Sum(s => s.EstimatedDurationSeconds),
            segments,
            QualityChecklist,
            GenerationSource,
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<NarrationScriptSegment> BuildSegments(NarrationContext context)
        => context.Category switch
        {
            "RareEventAlert" => BuildRareEventAlert(context),
            "PlanetConjunction" => BuildPlanetConjunction(context),
            "PlanetGrouping" => BuildPlanetGrouping(context),
            "WeeklySkyForecast" => BuildWeeklySkyForecast(context),
            _ => BuildGeneralDocumentary(context)
        };

    private static IReadOnlyList<NarrationScriptSegment> BuildRareEventAlert(NarrationContext c)
    {
        var time = DescribeTime(c);
        var objects = DescribeObjects(c.ObjectNames, "the sky");
        return
        [
            Segment(1, "Immediate hook", "urgent, calm, trustworthy", $"Step outside if your sky is clear. {c.Title} is the kind of sky moment that is easy to miss, and worth checking carefully.", 9, "Sky alert", "Cinematic night-sky opener", "fast fade"),
            Segment(2, "What to watch", "clear and grounded", $"Look for {objects}. {time} Treat the timing as a viewing window, not a guarantee. Clouds, haze, and local horizon conditions can change what you see.", 13, "Look up during the viewing window", "Simple annotated sky direction card", "clean cut"),
            Segment(3, "Viewing guidance", "practical and concise", $"Find a dark, open spot. Give your eyes a few minutes to adjust. If the object is faint, use binoculars, but never point optics near the Sun.", 12, "Dark sky. Open horizon. Safe viewing.", "Safety and viewing tips overlay", "soft fade"),
            Segment(4, "Close", "measured urgency", "If the sky clears, take a look. A quiet minute under the night sky may be all this event needs.", 8, "Watch safely", "Closing wide sky", "fade out")
        ];
    }

    private static IReadOnlyList<NarrationScriptSegment> BuildPlanetConjunction(NarrationContext c)
    {
        var time = DescribeTime(c);
        var objects = DescribeObjects(c.ObjectNames, "the planets");
        return
        [
            Segment(1, "Opening sky moment", "warm cinematic", $"Tonight, the eye is drawn to a close meeting in the sky. {objects} appear near each other, turning a familiar horizon into a small celestial scene.", 14, "A close meeting in the sky", "Wide twilight or night horizon", "slow dissolve"),
            Segment(2, "What conjunction means", "documentary explainer", "Astronomers call this a conjunction. It does not mean the worlds are physically close. It means they line up along our line of sight from Earth.", 16, "Conjunction = close in our sky", "Simple Earth-line-of-sight graphic", "clean cut"),
            Segment(3, "Where and when", "practical guide", $"For {c.LocationName}, use {time} Start with the open horizon and scan slowly. A phone sky map can help, but let your eyes confirm the view.", 16, "Check the open horizon", "Sky map card with direction and time", "gentle pan"),
            Segment(4, "Why it looks close", "curious and clear", "The planets move on separate paths around the Sun. From our moving viewpoint, those paths sometimes overlap on the dome of the sky. That is why the pairing can look so close.", 18, "Apparent closeness, real distance", "Orbital perspective graphic", "soft dissolve"),
            Segment(5, "Viewing tips", "calm practical", "Use binoculars if you have them. Keep the view steady. If the sky is hazy, wait a few minutes and look again as the contrast changes.", 14, "Binoculars help, patience helps more", "Viewing tips overlay", "fade"),
            Segment(6, "Closing recap", "cinematic close", "It is a simple alignment, but it feels personal from the ground. Two distant worlds sharing one patch of sky.", 10, "Two worlds. One view.", "Closing hero composition", "fade out")
        ];
    }

    private static IReadOnlyList<NarrationScriptSegment> BuildPlanetGrouping(NarrationContext c)
    {
        var objects = c.ObjectNames.Count > 0 ? c.ObjectNames : ["the brightest planets"];
        var time = DescribeTime(c);
        return
        [
            Segment(1, "Opening orientation", "inviting cinematic", "The best planet groupings do not need a telescope. They need a few quiet minutes and a clear sense of where to begin.", 12, "Planet grouping guide", "Wide sky orientation", "slow fade"),
            Segment(2, "Guide through objects", "patient sky guide", $"Begin with {objects[0]}. Then let your gaze move across the group{(objects.Count > 1 ? $" toward {string.Join(", ", objects.Skip(1))}" : string.Empty)}. Do not rush the pattern. Let the spacing reveal itself.", 18, "Follow the spacing", "Annotated object sequence", "gentle pan"),
            Segment(3, "When to look", "practical", $"For {c.LocationName}, use {time} Choose an open horizon and avoid bright streetlights if you can.", 13, "Open horizon. Low glare.", "Sky map card", "clean cut"),
            Segment(4, "Why it matters", "wonder with restraint", "A grouping is a perspective effect. The objects may be far apart in space, yet they share the same visual stage from Earth.", 13, "A perspective effect", "Subtle orbital depth graphic", "soft dissolve"),
            Segment(5, "Close", "clear and uncluttered", "Keep the narration simple in your head. One object, then the next. The beauty is in the arrangement.", 10, "One object. Then the next.", "Closing grouped sky", "fade out")
        ];
    }

    private static IReadOnlyList<NarrationScriptSegment> BuildWeeklySkyForecast(NarrationContext c)
    {
        var time = DescribeTime(c);
        var objects = DescribeObjects(c.ObjectNames, "the Moon and planets");
        return
        [
            Segment(1, "Weekly opening", "flowing documentary", $"This week, the sky does not arrive as a checklist. It unfolds slowly, night by night, above {c.LocationName}.", 14, "This week in the sky", "Wide cinematic night-sky establishing shot", "slow dissolve"),
            Segment(2, "Story setup", "curious and calm", $"The first thing to notice is the rhythm. The Moon changes the mood of the evening. The planets hold their places. And {objects} give the week its shape.", 18, "The week has a rhythm", "Moon phase and planet visibility montage", "gentle pan"),
            Segment(3, "Best moments", "guided and practical", $"Use {time} Do not chase every object at once. Pick the clearest night, step away from direct lights, and let the brightest targets anchor your view.", 18, "Choose the clearest night", "Best-night viewing card", "clean cut"),
            Segment(4, "Scientific context", "clear documentary", "What changes from night to night is our viewing angle. Earth turns beneath the sky, the Moon advances along its orbit, and the planets shift just enough to make each evening feel different.", 21, "Earth turns. The Moon moves. The view changes.", "Simple orbital motion graphic", "soft dissolve"),
            Segment(5, "Viewer guidance", "reassuring", "If clouds interrupt one night, try the next. A weekly forecast is a guide, not a promise. The real sky always has the final word.", 15, "A guide, not a promise", "Clouds clearing over skyline", "fade"),
            Segment(6, "Closing", "cinematic and personal", "So give the week one quiet look upward. You are not just watching objects. You are watching time move across the sky.", 13, "Watch time move across the sky", "Closing wide star field", "fade out")
        ];
    }

    private static IReadOnlyList<NarrationScriptSegment> BuildGeneralDocumentary(NarrationContext c)
    {
        var time = DescribeTime(c);
        return
        [
            Segment(1, "Opening hook", "cinematic", $"Some sky moments are subtle. {c.Title} is one of those moments that rewards a slower look.", 12, c.Title, "Opening sky hero", "slow fade"),
            Segment(2, "What matters", "clear documentary", $"For {c.LocationName}, use {time} Watch with patience, and treat the forecast as guidance rather than certainty.", 13, "Viewing window", "Sky map card", "clean cut"),
            Segment(3, "Practical close", "warm practical", "Find a darker spot if possible. Let your eyes adjust. Then let the sky do the rest.", 9, "Let your eyes adjust", "Closing viewing tips", "fade out")
        ];
    }

    private static NarrationScriptSegment Segment(int sceneNumber, string sceneName, string voiceTone, string script, int duration, string onScreenTextHint, string assetCue, string transitionHint)
        => new(sceneNumber, sceneName, voiceTone, script, duration, onScreenTextHint, assetCue, transitionHint);

    private static string DescribeTime(NarrationContext c)
    {
        var peak = c.Intelligence?.PeakUtc ?? c.Plan.ScheduledUtc ?? c.Intelligence?.StartUtc;
        if (peak.HasValue)
            return $"the planned viewing window around {peak.Value.UtcDateTime:yyyy-MM-dd HH:mm} UTC.";
        return "the planned local viewing window for this event.";
    }

    private static string DescribeObjects(IReadOnlyList<string> objectNames, string fallback)
        => objectNames.Count switch
        {
            0 => fallback,
            1 => objectNames[0],
            2 => string.Join(" and ", objectNames),
            _ => string.Join(", ", objectNames.Take(objectNames.Count - 1)) + ", and " + objectNames[^1]
        };

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string BuildOutputPath(string root, string regionId, Guid planId)
        => Path.Combine(root, "assets", SanitizePathSegment(regionId) ?? "unknown-region", "plans", planId.ToString("D"), "narration", $"narration-script-{planId:D}.json");

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
            ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string BuildFallbackTitle(string category, string location)
        => string.Create(CultureInfo.InvariantCulture, $"{category} for {location}");

    private static void Validate(NarrationPlanningRequest request)
    {
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");
    }

    private sealed record NarrationContext(
        ContentGenerationPlan Plan,
        AstronomyEventIntelligence? Intelligence,
        string Category,
        string Format,
        string LocationName,
        string Title,
        IReadOnlyList<string> ObjectNames);
}
