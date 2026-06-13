using System.Text.RegularExpressions;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionSceneIntentEnricher(
    IOptions<RenderingOptions> renderingOptions,
    ILogger<QuestionSceneIntentEnricher> logger,
    IMediaEventStrategyResolver? strategyResolver = null) : IQuestionSceneIntentEnricher
{
    private const string InputFileName = "question-driven-scene-plan.json";
    private const string OutputFileName = "question-driven-scene-plan.enriched.json";
    private const string SolarEclipseEyeSafetyWarning = "Never view the Sun directly without certified solar viewing glasses.";
    private const string EnrichmentSourceStrategy = "Strategy";
    private const string EnrichmentSourceGenericFallback = "GenericFallback";
    private const string EnrichmentSourceLegacyFallback = "LegacyFallback";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] KnownObjectNames =
    [
        "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Moon", "Sun"
    ];

    private static readonly string[] DefaultLeakageTerms =
    [
        "Venus", "Jupiter", "western horizon", "after sunset", "planet positions", "find Venus first", "planet pairing"
    ];

    private static readonly HashSet<string> SupportedViewerPersonas = new(StringComparer.OrdinalIgnoreCase)
    {
        "CasualSkyWatcher",
        "AstroPhotographyBeginner",
        "AstronomyEnthusiast",
        "AdvancedObserver"
    };

    private static readonly HashSet<string> SupportedKnowledgeLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Beginner",
        "Intermediate",
        "Advanced"
    };

    public async Task<QuestionSceneIntentEnrichmentResponse> EnrichQuestionScenePlanAsync(QuestionSceneIntentEnrichmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new List<string>();
        var inputPath = BuildPlanPath(request.EventId, request.RegionId, InputFileName, request.ProductionContext);
        var outputPath = BuildPlanPath(request.EventId, request.RegionId, OutputFileName, request.ProductionContext);

        if (!File.Exists(inputPath))
            throw new ArgumentException($"Question-driven scene plan was not found at '{inputPath.Replace('\\', '/')}'.", nameof(request));

        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existingJson = await File.ReadAllTextAsync(outputPath, cancellationToken);
            var existingPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(existingJson, JsonOptions)
                ?? throw new InvalidOperationException("Existing enriched question-driven scene plan could not be parsed.");
            var existingIssues = ValidateEnrichedPlan(existingPlan, request.ProductionContext?.ProductionEventIntelligence);
            warnings.Add("Enriched question-driven scene plan already exists; returning the existing file because overwriteExisting is false.");
            warnings.AddRange(existingIssues);
            return BuildResponse(existingPlan, existingIssues.Count == 0, [outputPath.Replace('\\', '/')], warnings);
        }

        var inputJson = await File.ReadAllTextAsync(inputPath, cancellationToken);
        var sourcePlan = JsonSerializer.Deserialize<QuestionDrivenScenePlanDto>(inputJson, JsonOptions)
            ?? throw new ArgumentException("Question-driven scene plan could not be parsed.", nameof(request));

        var enrichedPlan = BuildEnrichedPlan(sourcePlan, request);
        var validationIssues = ValidateEnrichedPlan(enrichedPlan, request.ProductionContext?.ProductionEventIntelligence);
        warnings.AddRange(validationIssues);
        if (validationIssues.Count > 0)
        {
            logger.LogWarning("Question scene intent enrichment validation failed for EventId={EventId}. Issues={Issues}", enrichedPlan.EventId, string.Join(" | ", validationIssues));
            var invalidPlan = enrichedPlan with { IsValid = false };
            return BuildResponse(invalidPlan, false, [], warnings);
        }

        var validPlan = enrichedPlan with { IsValid = true };
        if (request.DryRun)
            return BuildResponse(validPlan, true, [], warnings);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(validPlan, JsonOptions), cancellationToken);
        return BuildResponse(validPlan, true, [outputPath.Replace('\\', '/')], warnings);
    }

    private static QuestionSceneIntentEnrichmentResponse BuildResponse(EnrichedQuestionScenePlanDto plan, bool isValid, IReadOnlyList<string> generatedFiles, IReadOnlyList<string> warnings)
        => new(plan.EventId, plan.Scenes.Count, isValid, plan, generatedFiles, warnings);

    private EnrichedQuestionScenePlanDto BuildEnrichedPlan(QuestionDrivenScenePlanDto sourcePlan, QuestionSceneIntentEnrichmentRequest request)
    {
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var resolvedStrategy = request.ProductionContext?.MediaEventStrategy
            ?? (intelligence is not null ? strategyResolver?.Resolve(intelligence.EventType, intelligence.Title) : null);
        var strategyDefinition = intelligence is not null && resolvedStrategy is not null
            ? resolvedStrategy.BuildDefinition(intelligence)
            : null;
        var enrichmentSource = intelligence is not null ? EnrichmentSourceStrategy : EnrichmentSourceGenericFallback;
        var requiredVisualObjects = ResolveRequiredVisualObjects(intelligence, strategyDefinition).ToArray();
        var forbiddenObjectNames = ResolveForbiddenObjectNames(intelligence, strategyDefinition).ToArray();

        var scenes = sourcePlan.Scenes.Select(scene =>
        {
            var template = intelligence is not null
                ? BuildStrategyTemplate(scene, intelligence, resolvedStrategy?.EventType ?? intelligence.StrategyId ?? intelligence.EventType, requiredVisualObjects)
                : BuildGenericFallbackTemplate(scene);

            return new EnrichedQuestionSceneDto(
                scene.SceneNumber,
                scene.QuestionType,
                Clean(scene.ScenePurpose),
                Clean(scene.ViewerQuestion),
                Clean(scene.SourceAnswer),
                NormalizeSupportedValue(request.ViewerPersona, SupportedViewerPersonas),
                NormalizeSupportedValue(request.KnowledgeLevel, SupportedKnowledgeLevels),
                template.ViewerTakeaway,
                template.NarrationIntent,
                template.VisualIntent,
                template.ImagePromptIntent,
                template.OverlayIntent,
                template.AccessibilityIntent,
                scene.IsRequired);
        }).ToArray();

        if (IsPlanetGroupingLock(intelligence))
            scenes = InjectPlanetGroupingVisualIntents(scenes, intelligence!);

        var preliminary = new EnrichedQuestionScenePlanDto(
            Clean(sourcePlan.EventId) == string.Empty ? Clean(request.EventId) : Clean(sourcePlan.EventId),
            Clean(sourcePlan.RegionId) == string.Empty ? Clean(request.RegionId) : Clean(sourcePlan.RegionId),
            string.IsNullOrWhiteSpace(sourcePlan.Language) ? request.Language : sourcePlan.Language,
            NormalizeSupportedValue(request.ViewerPersona, SupportedViewerPersonas),
            NormalizeSupportedValue(request.KnowledgeLevel, SupportedKnowledgeLevels),
            scenes,
            true,
            DateTimeOffset.UtcNow);

        return preliminary with
        {
            Diagnostics = BuildDiagnostics(preliminary, intelligence, resolvedStrategy?.EventType ?? intelligence?.StrategyId ?? intelligence?.EventType ?? "Generic", requiredVisualObjects, forbiddenObjectNames, enrichmentSource)
        };
    }

    private static IntentTemplate BuildStrategyTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence, string strategyId, IReadOnlyList<string> requiredVisualObjects)
        => strategyId switch
        {
            "MeteorShower" => BuildMeteorTemplate(scene, intelligence),
            "PlanetPairing" => BuildPlanetPairingTemplate(scene, intelligence),
            "PlanetGrouping" or "PLANET_GROUPING" => BuildPlanetGroupingTemplate(scene, intelligence),
            "Conjunction" => BuildPlanetPairingTemplate(scene, intelligence),
            "NamedFullMoon" => BuildNamedFullMoonTemplate(scene, intelligence),
            "NewMoon" => BuildNewMoonTemplate(scene, intelligence),
            "LunarEclipse" => BuildLunarEclipseTemplate(scene, intelligence),
            "SolarEclipse" => BuildSolarEclipseTemplate(scene, intelligence),
            _ => BuildEventSafeTemplate(scene, intelligence, requiredVisualObjects)
        };

    private static EnrichedQuestionSceneDto[] InjectPlanetGroupingVisualIntents(IReadOnlyList<EnrichedQuestionSceneDto> scenes, ProductionEventIntelligence intelligence)
    {
        var objects = Objects(intelligence, "the grouped planets");
        var objectPhrase = JoinNatural(objects);
        var direction = Direction(intelligence, "western horizon");
        var horizonStart = ResolveGroupingHorizonStart(direction);
        var scanOrder = BuildPlanetGroupingScanOrder(objects);
        var groupIntent = $"Planet grouping: show {objectPhrase} together in one viewing region with exact labels.";
        var scanIntent = $"Guided scan path: begin at {horizonStart} and follow the visual scan path along the grouping arc in order: {scanOrder}.";

        return scenes.Select(scene =>
        {
            if (string.Equals(scene.QuestionType, AstronomyQuestionTypes.What, StringComparison.OrdinalIgnoreCase))
                return scene with
                {
                    VisualIntent = AppendIntent(scene.VisualIntent, groupIntent),
                    ImagePromptIntent = AppendIntent(scene.ImagePromptIntent, groupIntent),
                    OverlayIntent = AppendIntent(scene.OverlayIntent, "Label planet grouping and every listed planet in one viewing region.")
                };

            if (string.Equals(scene.QuestionType, AstronomyQuestionTypes.Where, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene.QuestionType, AstronomyQuestionTypes.How, StringComparison.OrdinalIgnoreCase))
                return scene with
                {
                    VisualIntent = AppendIntent(scene.VisualIntent, scanIntent),
                    ImagePromptIntent = AppendIntent(scene.ImagePromptIntent, scanIntent),
                    OverlayIntent = AppendIntent(scene.OverlayIntent, "Show guided scan path, horizon starting point, planet identification order, and grouping arc.")
                };

            return scene;
        }).ToArray();
    }

    private static string ResolveGroupingHorizonStart(string direction)
    {
        var cleaned = Clean(direction);
        return string.IsNullOrWhiteSpace(cleaned)
            ? "the western horizon"
            : cleaned.Contains("horizon", StringComparison.OrdinalIgnoreCase)
                ? cleaned
                : $"the {cleaned} horizon";
    }

    private static string BuildPlanetGroupingScanOrder(IReadOnlyList<string> objects)
    {
        if (objects.Count == 0)
            return "from the horizon starting point upward through the planet grouping";
        if (objects.Count == 1)
            return $"from the horizon starting point toward {objects[0]}";

        return $"from {objects[^1]} toward {string.Join(", ", objects.Reverse().Skip(1))}";
    }

    private static string AppendIntent(string existing, string addition)
    {
        var cleanedExisting = Clean(existing);
        var cleanedAddition = Clean(addition);
        if (string.IsNullOrWhiteSpace(cleanedExisting)) return cleanedAddition;
        if (string.IsNullOrWhiteSpace(cleanedAddition) || ContainsTerm(cleanedExisting, cleanedAddition)) return cleanedExisting;
        return $"{cleanedExisting} {cleanedAddition}";
    }

    private static bool IsPlanetGroupingLock(ProductionEventIntelligence? intelligence)
        => intelligence is not null && string.Equals(intelligence.EventType, "PLANET_GROUPING", StringComparison.OrdinalIgnoreCase);

    private static IntentTemplate BuildMeteorTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence)
    {
        var answer = Clean(scene.SourceAnswer);
        var window = ViewingWindow(intelligence);
        var direction = Direction(intelligence, "dark open sky");
        return scene.QuestionType switch
        {
            AstronomyQuestionTypes.What => new("Understand the meteor shower peak-night alert.", "Create urgency for this meteor shower peak night.", $"Show meteor streaks and a subtle radiant over a dark local night sky: {answer}", "Generate a cinematic dark sky with meteor streaks, a subtle radiant guide, and open landscape context.", "Use event title and peak-night cue.", "Muted viewers should know this is a meteor shower peak."),
            AstronomyQuestionTypes.Where => new("Know where to look for meteors.", "Orient viewers to the radiant and open dark sky.", $"Show the radiant direction near {direction}, plus meteors crossing a broad dark sky.", "Generate a dark open-sky location guide with radiant hint, direction label, and meteor streaks.", "Use radiant, dark sky, and direction cues.", "Muted viewers should understand where to look."),
            AstronomyQuestionTypes.When => new("Know the dark-sky viewing window.", "Explain the best local night window.", $"Show a night viewing window timeline for {window} with meteor activity.", "Generate a night timing visual with meteor streaks and a clear local viewing-window marker.", "Show the approved viewing window.", "Muted viewers should know when to watch."),
            AstronomyQuestionTypes.How => new("Know how to observe without equipment.", "Give simple meteor-shower watching steps.", "Show a viewer under dark sky, no telescope, avoiding city lights, eyes adapting.", "Generate an observer-friendly meteor shower scene with dark sky, no telescope, and low light pollution.", "Use no telescope, dark location, eyes 20 minutes.", "Muted viewers should know how to watch."),
            AstronomyQuestionTypes.Why => new("Understand why this meteor shower is worth seeing.", "Explain shower strength and moon interference from the generated facts.", $"Show meteor streak activity with a moon-interference quality cue: {answer}", "Generate a premium editorial meteor shower sky with streaks, radiant hint, and viewing-quality mood.", "Use significance and moon-interference facts.", "Muted viewers should know why it matters."),
            _ => new("Know the next action.", "Close with reminder and weather/dark-location checklist.", $"Show a save-date reminder under a meteor-filled local sky for {window}.", "Generate an inspirational meteor shower CTA image with dark night sky, meteor streaks, and local viewing context.", "Use reminder, weather check, dark location.", "Muted viewers should save the viewing night.")
        };
    }

    private static IntentTemplate BuildPlanetGroupingTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence)
    {
        var objects = Objects(intelligence, "the grouped planets");
        var objectPhrase = JoinNatural(objects);
        var anchor = intelligence.PrimaryObjects.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o)) ?? objects.First();
        var direction = Direction(intelligence, "the correct sky direction");
        var window = ViewingWindow(intelligence);
        return scene.QuestionType switch
        {
            AstronomyQuestionTypes.What => new("Understand the planet grouping.", "Introduce the full multi-planet arrangement.", $"Use PlanetGroupingSceneStrategy to show {objectPhrase} together with exact labels.", "Generate a realistic multi-planet grouping scene with all listed planets, no generic sky-only fallback.", "Use planet grouping and exact object names.", "Muted viewers should know multiple planets are grouped."),
            AstronomyQuestionTypes.Where => new("Know where to scan for the grouping.", "Orient viewers to the grouping direction and scan path.", $"Show {objectPhrase} toward {direction}, connected by a subtle guided scan path.", "Generate a horizon direction guide for the complete planet grouping with exact labels.", "Use direction and guided scan path.", "Muted viewers should know where the group sits."),
            AstronomyQuestionTypes.When => new("Know the viewing window.", "Explain when the whole grouping is visible.", $"Show a timing card for {window} beside the grouped planets.", "Generate a planet-grouping timing visual with the full group above the horizon.", "Use the approved viewing window.", "Muted viewers should know when to watch."),
            AstronomyQuestionTypes.How => new("Know how to find each planet.", "Give anchor-first scan instructions.", $"Start at {anchor}, then scan through {objectPhrase} using exact labels only.", "Generate an anchor-and-scan planet grouping guide with real-looking planet textures.", "Use anchor planet and scan path.", "Muted viewers should know the order to scan."),
            AstronomyQuestionTypes.Why => new("Understand why the grouping is notable.", "Explain the value of multiple planets in one observing window.", $"Show {objectPhrase} sharing one viewing window, emphasizing the grouping rather than a single planet.", "Generate an explanatory planet grouping visual with realistic textures and arrangement context.", "Use multi-planet grouping significance.", "Muted viewers should know why this is special."),
            _ => new("Know the next action.", "Close with reminder and horizon/weather check.", $"Show a clear-horizon CTA for the full planet grouping during {window}.", "Generate a PlanetGroupingThumbnailStrategy-friendly CTA image with all listed planets visible.", "Use save window, check horizon, watch grouping.", "Muted viewers should prepare for the full grouping.")
        };
    }

    private static IntentTemplate BuildPlanetPairingTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence)
    {
        var objects = Objects(intelligence, "the paired objects");
        var primary = intelligence.PrimaryObjects.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o)) ?? objects.First();
        var secondary = intelligence.SecondaryObjects.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o)) ?? objects.Skip(1).FirstOrDefault() ?? "the nearby companion";
        var objectPhrase = JoinNatural(objects);
        var direction = Direction(intelligence, "the correct sky direction");
        var window = ViewingWindow(intelligence);
        var separation = intelligence.AngularSeparationDegrees.HasValue ? $" about {intelligence.AngularSeparationDegrees.Value:0.##}° apart" : " close together";
        return scene.QuestionType switch
        {
            AstronomyQuestionTypes.What => new("Understand which objects form the close pairing.", "Introduce the actual paired objects from event intelligence.", $"Show {objectPhrase} as the only emphasized close pairing{separation}.", $"Generate a clean astronomy hero scene featuring {objectPhrase} close together with accurate labels.", $"Use labels for {primary} and {secondary}.", $"Muted viewers should know the event is {objectPhrase}."),
            AstronomyQuestionTypes.Where => new("Know where in the sky to look.", "Orient the viewer using the event-specific direction.", $"Show {objectPhrase} in {direction} with a horizon/altitude guide.", $"Generate a sky-location infographic for {objectPhrase} with direction marker and altitude cue.", $"Use direction and labels for {primary} and {secondary}.", "Muted viewers should understand the viewing direction."),
            AstronomyQuestionTypes.When => new("Know the best local viewing time.", "Explain the event-specific viewing window.", $"Show a local viewing-time marker for {window} and the {objectPhrase} pairing.", $"Generate a timing visual with {objectPhrase} and the approved local viewing window.", "Show best time from the source answer.", "Muted viewers should understand when to go outside."),
            AstronomyQuestionTypes.How => new("Know how to find the pairing.", "Give object-specific observing guidance.", $"Show steps to find {primary} first, then locate {secondary} nearby.", $"Generate a practical finding-guide background with arrows between {primary} and {secondary}.", $"Use 2–3 short steps naming {primary} and {secondary}.", "Muted viewers should understand the finding steps."),
            AstronomyQuestionTypes.Why => new("Understand why the pairing is worth seeing.", "Explain closeness and angular context.", $"Show the small angular separation of {objectPhrase}{separation}.", $"Generate a comparison-style astronomy visual highlighting the close spacing of {objectPhrase}.", "Use one short significance line.", "Muted viewers should understand why this pairing matters."),
            _ => new("Know what to do next.", "Close with a simple, memorable call to action.", $"Show a closing sky with only {objectPhrase} emphasized and a clear-sky reminder.", $"Generate an emotional closing astronomy background featuring {objectPhrase} only.", "Use a short call-to-action only.", "Muted viewers should know to step outside at the event time.")
        };
    }

    private static IntentTemplate BuildNamedFullMoonTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence)
    {
        var title = string.IsNullOrWhiteSpace(intelligence.ShortTitle) ? intelligence.Title : intelligence.ShortTitle;
        var window = ViewingWindow(intelligence);
        var direction = Direction(intelligence, "the eastern sky");
        if (!direction.Contains("east", StringComparison.OrdinalIgnoreCase)) direction = "the moonrise side of the sky";
        return scene.QuestionType switch
        {
            AstronomyQuestionTypes.What => new("Understand the named full moon event.", "Introduce the seasonal full moon name and bright lunar view.", $"Show the Moon as a large full moon glow for {title}, without unrelated planet visuals.", "Generate a cinematic full Moon hero over a warm horizon with clean lunar labels.", "Use the full moon name and moonrise cue.", "Muted viewers should know this is a named full moon."),
            AstronomyQuestionTypes.Where => new("Know where to watch moonrise.", "Orient the viewer toward moonrise and the eastern sky.", $"Show the Moon rising in the eastern sky near {direction} with an open horizon.", "Generate a moonrise direction infographic with eastern sky cue and full Moon glow.", "Use Moon, moonrise, and eastern sky labels.", "Muted viewers should understand where to look for moonrise."),
            AstronomyQuestionTypes.When => new("Know the local moonrise or peak viewing time.", "Explain the event-specific full moon timing.", $"Show a local time marker for {window} beside the full Moon.", "Generate a lunar timing visual with full Moon glow and local peak/moonrise time.", "Show localPeakTime or best viewing time.", "Muted viewers should know when to watch."),
            AstronomyQuestionTypes.How => new("Know how to find the Moon.", "Give simple moonrise observing guidance.", "Show an open eastern sky horizon first, then the bright Moon rising higher.", "Generate a practical moonrise guide with full Moon, horizon, and gentle elevation cue.", "Use open horizon and Moon-rises-higher steps.", "Muted viewers should know how to watch moonrise."),
            AstronomyQuestionTypes.Why => new("Understand the seasonal full moon meaning.", "Explain seasonal name and bright lunar appeal.", $"Show {title} as a seasonal named full Moon with warm lunar glow.", "Generate a cultural full Moon visual with seasonal name treatment and lunar glow.", "Use one short seasonal-name line.", "Muted viewers should understand why the named full moon matters."),
            _ => new("Know what to do next.", "Close with a moonrise reminder and weather check.", "Show a calm full Moon closing scene with a clear eastern-view reminder.", "Generate an emotional full Moon CTA image with moonrise glow and clear-sky reminder.", "Use save moonrise time and check clouds.", "Muted viewers should know to prepare a clear Moon-facing view.")
        };
    }

    private static IntentTemplate BuildNewMoonTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence)
    {
        var window = ViewingWindow(intelligence);
        return scene.QuestionType switch
        {
            AstronomyQuestionTypes.What => new("Understand the dark-sky opportunity.", "Introduce New Moon as moonlight-free stargazing.", "Show a dark star field with no visible full Moon.", "Generate a dark-sky stargazing hero with Milky Way hint, stars, and no full Moon disk.", "Use New Moon and dark sky cue.", "Muted viewers should know this is a dark-sky night."),
            AstronomyQuestionTypes.Where => new("Know where to stargaze.", "Guide viewers to a dark open sky away from lights.", "Show a dark open landscape away from city lights with broad star field.", "Generate a dark-site location guide with low light pollution and open sky.", "Use dark sky, away from city lights.", "Muted viewers should know where to go."),
            AstronomyQuestionTypes.When => new("Know the darkest viewing window.", "Explain the local dark-sky window.", $"Show a nighttime stargazing window for {window} with no moonlight.", "Generate a dark-sky timing visual with star field and local viewing-window marker.", "Show best stargazing time.", "Muted viewers should know when to watch."),
            AstronomyQuestionTypes.How => new("Know how to observe faint stars.", "Give simple stargazing guidance.", "Show eyes adapting, dim red light, star map, and scanning the darkest sky.", "Generate a practical stargazing guide with dark adaptation and constellation cues.", "Use eyes adjust, scan, star map.", "Muted viewers should know how to stargaze."),
            AstronomyQuestionTypes.Why => new("Understand why New Moon matters.", "Explain moonlight absence and faint-sky targets.", "Show faint stars, clusters, and Milky Way hint under a dark moonless sky.", "Generate a premium dark-sky visual emphasizing faint stars without a full Moon.", "Use dark sky improves faint stars.", "Muted viewers should understand why New Moon helps."),
            _ => new("Know what to do next.", "Close with a dark-site planning reminder.", "Show a quiet stargazing CTA scene under a moonless star field.", "Generate an inspirational dark-sky CTA image with stars and weather reminder.", "Use save window, check weather, low-light spot.", "Muted viewers should plan a stargazing session.")
        };
    }

    private static IntentTemplate BuildLunarEclipseTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence)
    {
        var window = ViewingWindow(intelligence);
        return scene.QuestionType switch
        {
            AstronomyQuestionTypes.What => new("Understand the lunar eclipse event.", "Introduce Earth’s shadow crossing the Moon.", "Show the Moon entering eclipse shadow with red/copper lunar color.", "Generate a dramatic lunar eclipse hero with Moon, Earth shadow arc, and copper-red tint.", "Use lunar eclipse and Moon turns red cue.", "Muted viewers should know this is a lunar eclipse."),
            AstronomyQuestionTypes.Where => new("Know where to look for the eclipsed Moon.", "Orient viewers toward the visible Moon.", $"Show the Moon above {Direction(intelligence, "the horizon")} with eclipse shadow.", "Generate a Moon-facing direction guide with horizon and eclipse-shadow cue.", "Use Moon and clear Moon-facing view.", "Muted viewers should know where to look."),
            AstronomyQuestionTypes.When => new("Know eclipse timing.", "Explain the event-specific eclipse phases.", $"Show eclipse phase timing for {window}.", "Generate a lunar eclipse timeline with phase markers and copper Moon.", "Show eclipse timing.", "Muted viewers should know when phases happen."),
            AstronomyQuestionTypes.How => new("Know how to watch the phases.", "Give simple lunar eclipse observing guidance.", "Show finding the Moon and watching each shadow phase; binoculars optional.", "Generate a practical lunar-eclipse viewing guide with Moon phase sequence.", "Use find Moon, watch phases.", "Muted viewers should know how to watch."),
            AstronomyQuestionTypes.Why => new("Understand why the Moon turns red.", "Explain Earth shadow and copper color.", "Show Earth’s shadow tinting the Moon red/copper.", "Generate an explanatory lunar eclipse visual with Earth shadow arc and copper Moon.", "Use one short Earth-shadow line.", "Muted viewers should understand why it matters."),
            _ => new("Know what to do next.", "Close with phase-time reminder and weather check.", "Show a lunar eclipse CTA with copper Moon and clear-view reminder.", "Generate an emotional lunar eclipse CTA image with Moon in shadow.", "Use save phase times and check weather.", "Muted viewers should prepare a Moon-facing view.")
        };
    }

    private static IntentTemplate BuildSolarEclipseTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence)
    {
        var window = ViewingWindow(intelligence);
        return scene.QuestionType switch
        {
            AstronomyQuestionTypes.What => new("Understand the solar eclipse event.", "Introduce Moon covering the Sun with safety-first framing.", "Show Sun and Moon eclipse silhouette with eye-safety cue.", "Generate a safe solar eclipse hero with Sun-Moon silhouette and certified eye-protection label.", "Use solar eclipse and eye safety cue.", "Muted viewers should know this is a solar eclipse."),
            AstronomyQuestionTypes.Where => new("Know where visibility applies.", "Explain local visibility while keeping safety visible.", "Show local visibility map/sky cue plus filtered Sun icon.", "Generate a solar eclipse visibility guide with safety label and filtered Sun treatment.", "Use visible from and eye protection.", "Muted viewers should know visibility and safety."),
            AstronomyQuestionTypes.When => new("Know eclipse timing.", "Explain the event-specific timing with protection reminder.", $"Show eclipse timing for {window} with certified eye-protection reminder.", "Generate a solar eclipse timeline with contact-time markers and safety badge.", "Show eclipse timing and safety.", "Muted viewers should know when to watch safely."),
            AstronomyQuestionTypes.How => new("Safe Viewing: use certified protection before any Sun viewing.", $"Give certified eye-protection instructions and state: {SolarEclipseEyeSafetyWarning}", $"Show certified solar eclipse glasses, an approved solar filter, and the warning: {SolarEclipseEyeSafetyWarning}", "Generate a practical solar-eclipse Safe Viewing guide with certified glasses/filter icons and no direct naked-eye viewing.", SolarEclipseEyeSafetyWarning, "Muted viewers should know safe viewing rules before looking toward the Sun."),
            AstronomyQuestionTypes.Why => new("Understand why the eclipse is special.", "Explain Sun-Moon alignment from our viewpoint.", "Show the alignment geometry of Moon crossing the Sun safely stylized.", "Generate an explanatory solar eclipse visual with Sun-Moon alignment and safety-first labels.", "Use one short alignment line.", "Muted viewers should understand why it matters."),
            _ => new("Know what to do next.", "Close with weather, timing, and eye-safety preparation.", $"Show a solar eclipse CTA with certified solar eclipse glasses and saved timing. {SolarEclipseEyeSafetyWarning}", "Generate an urgent safety-first solar eclipse CTA image with eye-protection reminder.", "Use check weather, save time, prepare certified solar eclipse glasses.", "Muted viewers should prepare certified eye protection.")
        };
    }

    private static IntentTemplate BuildEventSafeTemplate(QuestionDrivenSceneDto scene, ProductionEventIntelligence intelligence, IReadOnlyList<string> requiredVisualObjects)
    {
        var objects = requiredVisualObjects.Count > 0 ? JoinNatural(requiredVisualObjects) : JoinNatural(Objects(intelligence, "the event target"));
        return new IntentTemplate(
            "Understand the event using the approved source answer.",
            "Support the question answer without adding unrelated sky objects.",
            $"Show only event-safe visuals for {objects}: {Clean(scene.SourceAnswer)}",
            $"Generate a clean astronomy visual for {objects}, using only the source answer and event intelligence.",
            "Use concise source-backed labels only.",
            "Muted viewers should understand this scene without unrelated object leakage.");
    }

    private static IntentTemplate BuildGenericFallbackTemplate(QuestionDrivenSceneDto scene)
        => new(
            "Understand this scene from the approved question answer.",
            "Turn the approved answer into a clear visual beat without adding unrelated sky objects.",
            $"Show only the objects, direction, timing, and viewing cues already present in the base scene plan: {Clean(scene.VisualIntent)} {Clean(scene.SourceAnswer)}",
            $"Generate an astronomy visual based only on this base scene plan and answer: {Clean(scene.SourceAnswer)}",
            "Use concise labels from the approved answer only.",
            "Muted viewers should understand the approved answer without unrelated fallback content.");

    private static QuestionSceneEnrichmentDiagnostics BuildDiagnostics(EnrichedQuestionScenePlanDto plan, ProductionEventIntelligence? intelligence, string strategyId, IReadOnlyList<string> requiredVisualObjects, IReadOnlyList<string> forbiddenObjectNames, string enrichmentSource)
    {
        var objectDiagnostics = BuildObjectValidationDiagnostics(plan, intelligence, requiredVisualObjects, forbiddenObjectNames).ToArray();
        var leakageTerms = objectDiagnostics
            .Where(d => d.ValidationResult.Equals("Fail", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.ObjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new QuestionSceneEnrichmentDiagnostics(
            strategyId,
            requiredVisualObjects,
            forbiddenObjectNames,
            BuildScannedFieldNames(plan),
            leakageTerms,
            enrichmentSource,
            ResolveAllowedContextTerms(intelligence).ToArray(),
            intelligence?.PrimaryObjects ?? Array.Empty<string>(),
            intelligence?.SecondaryObjects ?? Array.Empty<string>(),
            objectDiagnostics);
    }

    private static IReadOnlyList<string> ValidateEnrichedPlan(EnrichedQuestionScenePlanDto plan, ProductionEventIntelligence? intelligence = null)
    {
        var issues = new List<string>();
        if (plan.Scenes.Count == 0)
        {
            issues.Add("Enriched scene plan must include at least one scene.");
            return issues;
        }

        ValidateAudienceContext("Root", "viewerPersona", plan.ViewerPersona, SupportedViewerPersonas, issues);
        ValidateAudienceContext("Root", "knowledgeLevel", plan.KnowledgeLevel, SupportedKnowledgeLevels, issues);

        foreach (var scene in plan.Scenes)
        {
            ValidateAudienceContext($"Scene {scene.SceneNumber}", "viewerPersona", scene.ViewerPersona, SupportedViewerPersonas, issues);
            ValidateAudienceContext($"Scene {scene.SceneNumber}", "knowledgeLevel", scene.KnowledgeLevel, SupportedKnowledgeLevels, issues);
            ValidateSceneIntentNotSourceAnswer(scene.SceneNumber, "viewerTakeaway", scene.ViewerTakeaway, scene.SourceAnswer, issues);
            ValidateSceneIntentNotSourceAnswer(scene.SceneNumber, "narrationIntent", scene.NarrationIntent, scene.SourceAnswer, issues);
            ValidateSceneIntentNotSourceAnswer(scene.SceneNumber, "visualIntent", scene.VisualIntent, scene.SourceAnswer, issues);
            if (string.IsNullOrWhiteSpace(scene.ImagePromptIntent)) issues.Add($"Scene {scene.SceneNumber} must have imagePromptIntent.");
            if (string.IsNullOrWhiteSpace(scene.OverlayIntent)) issues.Add($"Scene {scene.SceneNumber} must have overlayIntent.");
            if (string.IsNullOrWhiteSpace(scene.AccessibilityIntent)) issues.Add($"Scene {scene.SceneNumber} must have accessibilityIntent.");
        }

        if (!string.Equals(plan.Scenes.First().QuestionType, AstronomyQuestionTypes.What, StringComparison.OrdinalIgnoreCase))
            issues.Add("What must be first.");
        if (!string.Equals(plan.Scenes.Last().QuestionType, AstronomyQuestionTypes.Action, StringComparison.OrdinalIgnoreCase))
            issues.Add("Action must be last.");

        var duplicatePurposes = plan.Scenes
            .GroupBy(s => s.ScenePurpose, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => g.Key);
        foreach (var duplicate in duplicatePurposes)
            issues.Add($"Scene purpose '{duplicate}' must not be duplicated.");

        ValidateStrategyLeakage(plan, intelligence, issues);
        return issues;
    }

    private static void ValidateStrategyLeakage(EnrichedQuestionScenePlanDto plan, ProductionEventIntelligence? intelligence, List<string> issues)
    {
        if (plan.Diagnostics?.EnrichmentSource == EnrichmentSourceLegacyFallback)
            issues.Add("LegacyFallback enrichment must never be used in production.");

        var scanned = BuildScannedFields(plan).ToArray();
        var required = plan.Diagnostics?.RequiredVisualObjects ?? ResolveRequiredVisualObjects(intelligence, null).ToArray();
        foreach (var requiredObject in required.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (!scanned.Any(field => ContainsTerm(field, requiredObject)))
                issues.Add($"Enriched scene plan must contain required visual object '{requiredObject}'.");
        }

        var diagnostics = plan.Diagnostics?.ObjectValidationDiagnostics ?? BuildObjectValidationDiagnostics(
            plan,
            intelligence,
            required,
            plan.Diagnostics?.ForbiddenObjectNames ?? ResolveForbiddenObjectNames(intelligence, null).ToArray()).ToArray();
        foreach (var failure in diagnostics.Where(d => d.ValidationResult.Equals("Fail", StringComparison.OrdinalIgnoreCase)))
            issues.Add($"Enriched scene plan contains forbidden or absent object '{failure.ObjectName}' as {failure.OccurrenceRole} in {failure.OccurrenceSource}.");

        if (intelligence is not null && string.Equals(intelligence.EventType, "PlanetPairing", StringComparison.OrdinalIgnoreCase))
        {
            var objects = Objects(intelligence, "").Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
            foreach (var obj in objects)
            {
                if (!scanned.Any(field => ContainsTerm(field, obj)))
                    issues.Add($"PlanetPairing enrichment must use actual object name '{obj}'.");
            }
        }
    }

    private static IEnumerable<string> ResolveRequiredVisualObjects(ProductionEventIntelligence? intelligence, MediaEventStrategyDefinition? strategyDefinition)
    {
        if (intelligence?.RequiredVisualObjects is { Count: > 0 } requiredFromIntelligence)
            return requiredFromIntelligence;
        if (strategyDefinition?.RequiredVisualObjects is { Count: > 0 } requiredFromStrategy)
            return requiredFromStrategy;
        if (intelligence is null)
            return [];

        return intelligence.EventType switch
        {
            "MeteorShower" => ["meteor streaks", "radiant", "dark sky", "viewing window"],
            "PlanetPairing" or "Conjunction" => Objects(intelligence, "paired objects").Concat(["close pairing"]),
            "PlanetGrouping" or "PLANET_GROUPING" => Objects(intelligence, "grouped planets").Concat(["planet grouping", "guided scan path"]),
            "NamedFullMoon" => ["Moon", "moonrise", "eastern sky", "full moon glow"],
            "NewMoon" => ["dark sky", "stargazing", "no visible full Moon"],
            "LunarEclipse" => ["Moon", "eclipse", "copper Moon", "eclipse timing"],
            "SolarEclipse" => ["Sun", "eclipse", "eye safety", "eclipse timing"],
            _ => Objects(intelligence, "sky target")
        };
    }

    private static IEnumerable<string> ResolveForbiddenObjectNames(ProductionEventIntelligence? intelligence, MediaEventStrategyDefinition? strategyDefinition)
    {
        var forbidden = new List<string>();
        if (intelligence?.ForbiddenObjectNames is { Count: > 0 }) forbidden.AddRange(intelligence.ForbiddenObjectNames);
        if (intelligence?.ForbiddenTerms is { Count: > 0 }) forbidden.AddRange(intelligence.ForbiddenTerms);
        if (strategyDefinition?.ForbiddenUnrelatedObjects is { Count: > 0 }) forbidden.AddRange(strategyDefinition.ForbiddenUnrelatedObjects);
        if (intelligence is not null && string.Equals(intelligence.EventType, "NamedFullMoon", StringComparison.OrdinalIgnoreCase))
            forbidden.AddRange(["Venus", "Jupiter", "planet pairing", "planet positions", "find Venus first"]);
        return forbidden.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ResolveAbsentObjectNames(ProductionEventIntelligence? intelligence)
    {
        if (intelligence is null) return [];
        var allowed = Objects(intelligence, "")
            .Concat(AllowedObjectsForStrategy(intelligence.EventType))
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return KnownObjectNames.Where(name => !allowed.Contains(name));
    }

    private static IEnumerable<ObjectValidationDiagnostic> BuildObjectValidationDiagnostics(
        EnrichedQuestionScenePlanDto plan,
        ProductionEventIntelligence? intelligence,
        IEnumerable<string> requiredVisualObjects,
        IEnumerable<string> forbiddenObjectNames)
    {
        var allowedVisualObjects = (intelligence is null ? Array.Empty<string>() : Objects(intelligence, ""))
            .Concat(AllowedObjectsForStrategy(intelligence?.EventType ?? string.Empty))
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredSet = requiredVisualObjects.Where(o => !string.IsNullOrWhiteSpace(o)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var forbiddenSet = forbiddenObjectNames.Concat(ResolveAbsentObjectNames(intelligence)).Concat(DefaultLeakageTermsForStrategy(intelligence?.EventType ?? string.Empty))
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedContextTerms = ResolveAllowedContextTerms(intelligence).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateObjects = KnownObjectNames
            .Concat(requiredSet)
            .Concat(forbiddenSet)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var occurrence in BuildScannedFieldOccurrences(plan))
        {
            foreach (var objectName in candidateObjects.Where(term => ContainsTerm(occurrence.Value, term)))
            {
                var role = ResolveOccurrenceRole(occurrence.Source, occurrence.Value, objectName, allowedContextTerms);
                var isAllowedVisualObject = allowedVisualObjects.Contains(objectName);
                var isAllowedContext = role == ObjectOccurrenceRole.ContextTerm && allowedContextTerms.Contains(objectName);
                var isForbiddenVisualObject = role != ObjectOccurrenceRole.ContextTerm
                    && (forbiddenSet.Contains(objectName) || KnownObjectNames.Contains(objectName, StringComparer.OrdinalIgnoreCase))
                    && !isAllowedVisualObject;
                var result = isForbiddenVisualObject ? "Fail" : "Pass";
                var allowedBecause = result == "Fail"
                    ? "object is not in currentEventLock primary/secondary visual objects"
                    : isAllowedVisualObject
                        ? "object is in currentEventLock primary/secondary visual objects"
                        : isAllowedContext
                            ? "context term is present in event intelligence"
                            : "object occurrence is not a forbidden visual role";

                yield return new ObjectValidationDiagnostic(objectName, occurrence.Source, role.ToString(), allowedBecause, result);
            }
        }
    }

    private static IEnumerable<string> ResolveAllowedContextTerms(ProductionEventIntelligence? intelligence)
    {
        if (intelligence is null) return [];

        var terms = new List<string>();
        AddContextObjectTerms(terms, intelligence.MoonInterference);
        if (intelligence.MoonIlluminationPercent.HasValue)
            terms.AddRange(["Moon", "moonlight", "moon illumination", "moon interference"]);
        AddContextObjectTerms(terms, intelligence.ScientificContext);
        foreach (var value in intelligence.QualityWarnings.Concat(intelligence.ViewerInstructions).Concat(intelligence.VisualMotifs))
            AddContextObjectTerms(terms, value);

        if (string.Equals(intelligence.EventType, "MeteorShower", StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(intelligence.MoonInterference) || intelligence.MoonIlluminationPercent.HasValue))
            terms.AddRange(["Moon", "moonlight", "moon illumination", "moon interference"]);

        return terms.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddContextObjectTerms(List<string> terms, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var knownObject in KnownObjectNames.Where(name => ContainsTerm(value, name)))
            terms.Add(knownObject);
        if (ContainsTerm(value, "moonlight")) terms.AddRange(["Moon", "moonlight"]);
        if (ContainsTerm(value, "moon illumination")) terms.AddRange(["Moon", "moon illumination"]);
        if (ContainsTerm(value, "moon interference")) terms.AddRange(["Moon", "moon interference"]);
    }

    private static ObjectOccurrenceRole ResolveOccurrenceRole(string source, string value, string objectName, HashSet<string> allowedContextTerms)
    {
        if (source.EndsWith(".imagePromptIntent", StringComparison.OrdinalIgnoreCase))
            return IsContextPhrase(value, objectName, allowedContextTerms) ? ObjectOccurrenceRole.ContextTerm : ObjectOccurrenceRole.DrawableObject;
        if (source.EndsWith(".visualIntent", StringComparison.OrdinalIgnoreCase))
            return IsContextPhrase(value, objectName, allowedContextTerms) ? ObjectOccurrenceRole.ContextTerm : ObjectOccurrenceRole.DrawableObject;
        if (source.EndsWith(".overlayIntent", StringComparison.OrdinalIgnoreCase))
            return IsContextPhrase(value, objectName, allowedContextTerms) ? ObjectOccurrenceRole.ContextTerm : ObjectOccurrenceRole.Label;
        return ObjectOccurrenceRole.ContextTerm;
    }

    private static bool IsContextPhrase(string value, string objectName, HashSet<string> allowedContextTerms)
    {
        if (!allowedContextTerms.Contains(objectName)) return false;
        return ContainsAnyTerm(value, "moonlight", "moon illumination", "moon interference", "Moon interference", "moon-interference", "viewing condition", "quality cue");
    }

    private static bool ContainsAnyTerm(string field, params string[] terms)
        => terms.Any(term => ContainsTerm(field, term));

    private static IEnumerable<string> AllowedObjectsForStrategy(string eventType) => eventType switch
    {
        "NamedFullMoon" or "NewMoon" or "LunarEclipse" => ["Moon"],
        "SolarEclipse" => ["Sun", "Moon"],
        _ => []
    };

    private static IEnumerable<string> DefaultLeakageTermsForStrategy(string strategyId)
        => string.Equals(strategyId, "NamedFullMoon", StringComparison.OrdinalIgnoreCase)
            ? DefaultLeakageTerms
            : [];

    private static IReadOnlyList<string> BuildScannedFieldNames(EnrichedQuestionScenePlanDto plan)
        => plan.Scenes.SelectMany(scene => new[]
        {
            $"scene[{scene.SceneNumber}].viewerTakeaway",
            $"scene[{scene.SceneNumber}].narrationIntent",
            $"scene[{scene.SceneNumber}].visualIntent",
            $"scene[{scene.SceneNumber}].imagePromptIntent",
            $"scene[{scene.SceneNumber}].overlayIntent",
            $"scene[{scene.SceneNumber}].accessibilityIntent"
        }).ToArray();

    private static IEnumerable<string> BuildScannedFields(EnrichedQuestionScenePlanDto plan)
        => BuildScannedFieldOccurrences(plan).Select(field => field.Value);

    private static IEnumerable<ScannedFieldOccurrence> BuildScannedFieldOccurrences(EnrichedQuestionScenePlanDto plan)
    {
        foreach (var scene in plan.Scenes)
        {
            yield return new($"scene[{scene.SceneNumber}].viewerTakeaway", scene.ViewerTakeaway);
            yield return new($"scene[{scene.SceneNumber}].narrationIntent", scene.NarrationIntent);
            yield return new($"scene[{scene.SceneNumber}].visualIntent", scene.VisualIntent);
            yield return new($"scene[{scene.SceneNumber}].imagePromptIntent", scene.ImagePromptIntent);
            yield return new($"scene[{scene.SceneNumber}].overlayIntent", scene.OverlayIntent);
            yield return new($"scene[{scene.SceneNumber}].accessibilityIntent", scene.AccessibilityIntent);
        }
    }

    private static IEnumerable<string> FindLeakageTerms(IEnumerable<string> fields, IEnumerable<string> terms)
    {
        var fieldArray = fields.Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
        return terms.Where(term => !string.IsNullOrWhiteSpace(term) && fieldArray.Any(field => ContainsTerm(field, term))).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsTerm(string field, string term)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(term)) return false;
        var escaped = Regex.Escape(term.Trim()).Replace("\\ ", "\\s+");
        return Regex.IsMatch(field, $"(?<![A-Za-z0-9]){escaped}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string[] Objects(ProductionEventIntelligence intelligence, params string[] fallback)
        => intelligence.PrimaryObjects.Concat(intelligence.SecondaryObjects).Where(o => !string.IsNullOrWhiteSpace(o)).DefaultIfEmpty(fallback.FirstOrDefault() ?? "sky target").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string JoinNatural(IReadOnlyList<string> values) => values.Count switch
    {
        0 => "the main sky target",
        1 => values[0],
        2 => $"{values[0]} and {values[1]}",
        _ => $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}"
    };

    private static string ViewingWindow(ProductionEventIntelligence intelligence)
        => !string.IsNullOrWhiteSpace(intelligence.BestViewingWindowLocal)
            ? intelligence.BestViewingWindowLocal!
            : !string.IsNullOrWhiteSpace(intelligence.LocalPeakTime)
                ? intelligence.LocalPeakTime!
                : "the event viewing window";

    private static string Direction(ProductionEventIntelligence intelligence, string fallback)
        => string.IsNullOrWhiteSpace(intelligence.SkyDirectionHint) ? fallback : intelligence.SkyDirectionHint!;

    private static void ValidateAudienceContext(string owner, string fieldName, string value, HashSet<string> supportedValues, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{owner} {fieldName} is required.");
            return;
        }

        if (!supportedValues.Contains(value))
            issues.Add($"{owner} {fieldName} '{value}' is not supported.");
    }

    private static void ValidateSceneIntentNotSourceAnswer(int sceneNumber, string fieldName, string value, string sourceAnswer, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"Scene {sceneNumber} must have {fieldName}.");
            return;
        }

        if (string.Equals(Clean(value), Clean(sourceAnswer), StringComparison.OrdinalIgnoreCase))
            issues.Add($"Scene {sceneNumber} {fieldName} must not equal sourceAnswer.");
    }

    private static void ValidateRequest(QuestionSceneIntentEnrichmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
            throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegionId))
            throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("language is required.", nameof(request));
    }

    private static string NormalizeSupportedValue(string value, HashSet<string> supportedValues)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return cleaned;

        return supportedValues.FirstOrDefault(supported => string.Equals(supported, cleaned, StringComparison.OrdinalIgnoreCase)) ?? cleaned;
    }

    private string BuildPlanPath(string eventId, string regionId, string fileName, ProductionPipelineExecutionContext? productionContext = null)
        => !string.IsNullOrWhiteSpace(productionContext?.QuestionRoot)
            ? Path.Combine(productionContext!.QuestionRoot!, fileName)
            : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine", fileName);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private sealed record IntentTemplate(
        string ViewerTakeaway,
        string NarrationIntent,
        string VisualIntent,
        string ImagePromptIntent,
        string OverlayIntent,
        string AccessibilityIntent);

    private sealed record ScannedFieldOccurrence(string Source, string Value);

    private enum ObjectOccurrenceRole
    {
        ContextTerm,
        RequiredVisualObject,
        DrawableObject,
        Label,
        ForbiddenObject
    }
}
