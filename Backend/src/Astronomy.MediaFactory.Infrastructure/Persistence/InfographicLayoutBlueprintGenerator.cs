using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class InfographicLayoutBlueprintGenerator(
    IOptions<RenderingOptions> renderingOptions,
    ILogger<InfographicLayoutBlueprintGenerator> logger) : IInfographicLayoutBlueprintGenerator
{
    private const string GoldenEventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string GoldenRegionId = "IN-RJ-UDAIPUR";
    private const string GoldenLanguage = "en";
    private const string BlueprintFileName = "scene-001-layout-blueprint.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] InputFileNames =
    [
        "question-answer-set.json",
        "question-driven-scene-plan.enriched.json",
        "question-driven-narration.json"
    ];

    public async Task<InfographicLayoutBlueprintResponse> GenerateInfographicLayoutBlueprintAsync(InfographicLayoutBlueprintRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateGoldenRequest(request);

        var warnings = new List<string>();
        var questionEngineRoot = BuildQuestionEngineRoot(request.EventId, request.RegionId);
        foreach (var fileName in InputFileNames)
        {
            var path = Path.Combine(questionEngineRoot, fileName);
            if (!File.Exists(path))
                throw new ArgumentException($"Required question-engine input file was not found at '{path.Replace('\\', '/')}'.", nameof(request));
        }

        warnings.Add("Blueprint phase only: no images, audio, TTS, video rendering, publishing, DailySkyGuide, or /api/pipeline/run work was invoked.");
        warnings.Add("Constellation and reference-star zones are placeholders for future support and are not approval blockers.");

        var outputDirectory = Path.Combine(questionEngineRoot, "layout-blueprints");
        var outputPath = Path.Combine(outputDirectory, BlueprintFileName);
        if (!request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<InfographicLayoutBlueprintResponse>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing infographic layout blueprint could not be parsed.");
            return existing with { Warnings = existing.Warnings.Concat(["Infographic layout blueprint already exists; returning the existing file because overwriteExisting is false."]).ToArray() };
        }

        var blueprints = BuildBlueprints();
        var validationWarnings = ValidateBlueprints(blueprints);
        warnings.AddRange(validationWarnings);
        var response = new InfographicLayoutBlueprintResponse(
            request.EventId,
            blueprints.Count,
            validationWarnings.Count == 0,
            blueprints,
            warnings);

        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(response, JsonOptions), cancellationToken);
        logger.LogInformation("Generated infographic layout blueprint file for golden pilot EventId={EventId} at {Path}", request.EventId, outputPath);

        return response;
    }

    private static IReadOnlyList<InfographicLayoutBlueprint> BuildBlueprints()
    {
        return
        [
            new(1, "WHAT", "AstronomyMagazineCover", 90, 10,
                Zones(
                    title: Zone("top 10%", "Small title only; no title box, banner, or slide header.", "x=6%, y=3%, width=88%, height=7%"),
                    hero: Zone("center 75%", "Hero Venus + Jupiter over realistic sky; celestial objects dominate the composition.", "x=5%, y=10%, width=90%, height=75%"),
                    subtitle: Zone("bottom 15%", "Short subtitle anchored low over the sky, never inside a rectangle card.", "x=10%, y=86%, width=80%, height=9%"),
                    annotation: Zone("micro labels", "Tiny Venus/Jupiter labels only if needed; keep background visually open.", "near planets inside hero zone"),
                    celestial: Zone("hero object cluster", "Venus and Jupiter are the primary readable shapes.", "center-weighted in hero zone")),
                ["background", "celestialObjects", "annotation"],
                ["large_rectangle_box", "title_slide", "powerpoint_card", "fake_circle_planets", "large_title_box", "card_layout", "full_width_banner"]),

            new(2, "WHERE", "ObservationChart", 80, 20,
                Zones(
                    title: Zone("top-left marker", "West marker, small and directional rather than title-card typography.", "x=5%, y=5%, width=20%, height=8%"),
                    hero: Zone("center sky chart", "Venus and Jupiter placed relative to western horizon.", "x=22%, y=14%, width=52%, height=58%"),
                    annotation: Zone("right-side guide labels", "Altitude guide labels and short observation notes.", "x=78%, y=16%, width=16%, height=52%"),
                    constellation: EmptyZone(),
                    referenceStar: EmptyZone(),
                    celestial: Zone("center", "Venus and Jupiter plotted as real local assets, not fake circles.", "x=30%, y=22%, width=38%, height=28%"),
                    horizon: Zone("bottom", "Clear horizon line for where-to-look grounding.", "x=0%, y=76%, width=100%, height=7%"),
                    altitude: Zone("right side", "Altitude guide one-third above horizon.", "x=82%, y=22%, width=10%, height=50%"),
                    skyGuidance: Zone("western orientation", "West marker plus horizon/altitude geometry.", "top-left plus bottom horizon plus right guide")),
                ["background", "celestialObjects", "skyGuidance", "annotation"],
                ["large_rectangle_box", "title_slide", "powerpoint_card", "fake_circle_planets"]),

            new(3, "WHEN", "TimelineInfographic", 80, 20,
                Zones(
                    title: Zone("top", "Best Time Tonight, small label above the timeline.", "x=12%, y=6%, width=76%, height=10%"),
                    hero: Zone("center timeline", "Sunset-to-best-time timeline is the hero educational visual.", "x=8%, y=28%, width=84%, height=30%"),
                    subtitle: Zone("bottom", "Viewing Window note below timeline.", "x=12%, y=72%, width=76%, height=12%"),
                    annotation: Zone("timeline callouts", "7:23 PM IST and short timing markers.", "attached to timeline ticks"),
                    celestial: Zone("small context icons", "Optional tiny Venus/Jupiter context icons only; timeline remains the hero visual.", "x=72%, y=18%, width=18%, height=12%"),
                    timeline: Zone("center", "Primary horizontal timeline with best-time emphasis.", "x=8%, y=32%, width=84%, height=20%"),
                    viewingWindow: Zone("bottom", "Viewing window message separated from title area.", "x=12%, y=70%, width=76%, height=12%")),
                ["background", "educationalLayer", "annotation"],
                ["large_rectangle_box", "title_slide", "powerpoint_card", "fake_circle_planets"]),

            new(4, "HOW", "ObservationGuide", 75, 25,
                Zones(
                    title: Zone("bottom instruction", "Face West guidance at the bottom, not a top title slide.", "x=28%, y=84%, width=44%, height=9%"),
                    hero: Zone("center", "Venus → Jupiter scan path across the sky.", "x=28%, y=18%, width=44%, height=45%"),
                    annotation: Zone("step labels", "Minimal Step 1 and Step 2 labels outside the main object path.", "left and right thirds"),
                    constellation: EmptyZone(),
                    referenceStar: EmptyZone(),
                    celestial: Zone("center", "Venus and Jupiter with arrow between them.", "x=32%, y=24%, width=36%, height=25%"),
                    horizon: Zone("bottom grounding", "Subtle western horizon under the face-west guidance.", "x=0%, y=76%, width=100%, height=7%"),
                    step: Zone("left/center/right", "Step 1 left, Venus → Jupiter center, Step 2 right.", "x=6%, y=20%, width=88%, height=48%"),
                    skyGuidance: Zone("bottom", "Face West orientation instruction.", "x=28%, y=84%, width=44%, height=9%")),
                ["background", "celestialObjects", "skyGuidance", "educationalLayer", "annotation"],
                ["large_rectangle_box", "title_slide", "powerpoint_card", "fake_circle_planets"]),

            new(5, "WHY", "SignificanceGraphic", 80, 20,
                Zones(
                    title: Zone("top", "Why It Matters label, compact and editorial.", "x=12%, y=6%, width=76%, height=10%"),
                    hero: Zone("center", "Venus ↔ Jupiter relationship graphic with no card container.", "x=18%, y=20%, width=64%, height=46%"),
                    subtitle: Zone("bottom", "One short significance line.", "x=10%, y=78%, width=80%, height=10%"),
                    annotation: Zone("relationship callout", "Small closeness/brightness annotation.", "near Venus ↔ Jupiter connector"),
                    celestial: Zone("center", "Venus and Jupiter separated by significance connector.", "x=25%, y=28%, width=50%, height=24%"),
                    significance: Zone("center/bottom", "Visual pairing connector plus significance line.", "x=18%, y=20%, width=64%, height=68%")),
                ["background", "celestialObjects", "educationalLayer", "annotation"],
                ["large_rectangle_box", "title_slide", "powerpoint_card", "fake_circle_planets"]),

            new(6, "ACTION", "AstronomyPoster", 90, 10,
                Zones(
                    hero: Zone("center", "Beautiful sky, Venus, and Jupiter carry the poster composition.", "x=0%, y=0%, width=100%, height=84%"),
                    subtitle: Zone("bottom", "Minimal CTA only; no full-width banner.", "x=18%, y=86%, width=64%, height=8%"),
                    annotation: Zone("minimal labels", "Optional tiny labels if readability requires them.", "inside hero zone near planets"),
                    celestial: Zone("center sky", "Venus and Jupiter remain visible in the beautiful sky.", "x=30%, y=24%, width=40%, height=28%"),
                    cta: Zone("bottom", "Minimal call to action over sky.", "x=18%, y=86%, width=64%, height=8%")),
                ["background", "celestialObjects", "annotation"],
                ["large_rectangle_box", "title_slide", "powerpoint_card", "fake_circle_planets", "full_width_banner"])
        ];
    }

    private static InfographicLayoutZones Zones(
        IReadOnlyDictionary<string, string>? title = null,
        IReadOnlyDictionary<string, string>? hero = null,
        IReadOnlyDictionary<string, string>? subtitle = null,
        IReadOnlyDictionary<string, string>? annotation = null,
        IReadOnlyDictionary<string, string>? constellation = null,
        IReadOnlyDictionary<string, string>? referenceStar = null,
        IReadOnlyDictionary<string, string>? celestial = null,
        IReadOnlyDictionary<string, string>? horizon = null,
        IReadOnlyDictionary<string, string>? altitude = null,
        IReadOnlyDictionary<string, string>? timeline = null,
        IReadOnlyDictionary<string, string>? viewingWindow = null,
        IReadOnlyDictionary<string, string>? step = null,
        IReadOnlyDictionary<string, string>? skyGuidance = null,
        IReadOnlyDictionary<string, string>? significance = null,
        IReadOnlyDictionary<string, string>? cta = null)
        => new(
            title ?? EmptyZone(),
            hero ?? EmptyZone(),
            subtitle ?? EmptyZone(),
            annotation ?? EmptyZone(),
            constellation ?? EmptyZone(),
            referenceStar ?? EmptyZone(),
            celestial ?? EmptyZone(),
            horizon ?? EmptyZone(),
            altitude ?? EmptyZone(),
            timeline ?? EmptyZone(),
            viewingWindow ?? EmptyZone(),
            step ?? EmptyZone(),
            skyGuidance ?? EmptyZone(),
            significance ?? EmptyZone(),
            cta ?? EmptyZone());

    private static IReadOnlyDictionary<string, string> Zone(string placement, string purpose, string bounds)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["placement"] = placement,
            ["purpose"] = purpose,
            ["bounds"] = bounds
        };

    private static IReadOnlyDictionary<string, string> EmptyZone()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ValidateBlueprints(IReadOnlyList<InfographicLayoutBlueprint> blueprints)
    {
        var warnings = new List<string>();
        foreach (var blueprint in blueprints)
        {
            if (blueprint.TextCoveragePercent > 25) warnings.Add($"Scene {blueprint.SceneNumber} text coverage exceeds 25%.");
            if (blueprint.VisualCoveragePercent < 75) warnings.Add($"Scene {blueprint.SceneNumber} visual coverage is below 75%.");
            if (ContainsForbiddenLayout(blueprint)) warnings.Add($"Scene {blueprint.SceneNumber} uses a forbidden card/title-slide layout pattern.");
            if (blueprint.LayoutZones.HeroZone.Count == 0) warnings.Add($"Scene {blueprint.SceneNumber} is missing a hero zone.");
            if (blueprint.LayoutZones.CelestialObjectZone.Count == 0) warnings.Add($"Scene {blueprint.SceneNumber} is missing a celestial object zone.");
        }

        if (blueprints.Select(x => x.LayoutTemplate).Distinct(StringComparer.OrdinalIgnoreCase).Count() != blueprints.Count)
            warnings.Add("Same template reused incorrectly; each golden-pilot scene must use a distinct template.");

        AddSceneSpecificCheck(blueprints, "WHERE", scene => scene.LayoutZones.HorizonZone.Count > 0, "WHERE is missing the required horizon zone.", warnings);
        AddSceneSpecificCheck(blueprints, "WHEN", scene => scene.LayoutZones.TimelineZone.Count > 0, "WHEN is missing the required timeline zone.", warnings);
        AddSceneSpecificCheck(blueprints, "HOW", scene => scene.LayoutZones.StepZone.Count > 0, "HOW is missing the required step zone.", warnings);
        AddSceneSpecificCheck(blueprints, "WHY", scene => scene.LayoutZones.SignificanceZone.Count > 0, "WHY is missing the required significance zone.", warnings);
        AddSceneSpecificCheck(blueprints, "ACTION", scene => scene.LayoutZones.CtaZone.Count > 0, "ACTION is missing the required CTA zone.", warnings);
        return warnings;
    }

    private static bool ContainsForbiddenLayout(InfographicLayoutBlueprint blueprint)
        => blueprint.ForbiddenPatterns.Any(pattern => pattern.Contains("large_rectangle_card", StringComparison.OrdinalIgnoreCase))
           || blueprint.LayoutTemplate.Contains("TitleSlide", StringComparison.OrdinalIgnoreCase)
           || blueprint.LayoutTemplate.Contains("PowerPoint", StringComparison.OrdinalIgnoreCase);

    private static void AddSceneSpecificCheck(IReadOnlyList<InfographicLayoutBlueprint> blueprints, string sceneKey, Func<InfographicLayoutBlueprint, bool> predicate, string message, List<string> warnings)
    {
        var scene = blueprints.FirstOrDefault(x => string.Equals(x.SceneKey, sceneKey, StringComparison.OrdinalIgnoreCase));
        if (scene is null || !predicate(scene)) warnings.Add(message);
    }

    private static void ValidateGoldenRequest(InfographicLayoutBlueprintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId)) throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language)) throw new ArgumentException("language is required.", nameof(request));
        if (!string.Equals(request.EventId, GoldenEventId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.RegionId, GoldenRegionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Language, GoldenLanguage, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Infographic layout blueprint generation is enabled only for the approved golden pilot event e7013ee4-55c6-4f01-b1d0-7c500f26f98b / IN-RJ-UDAIPUR / en.", nameof(request));
        }
    }

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine");

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
