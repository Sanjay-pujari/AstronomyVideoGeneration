using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyQuestionEngine(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    IMediaEventStrategyResolver strategyResolver,
    ILogger<AstronomyQuestionEngine> logger) : IQuestionEngine
{
    private const string Version = "v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] RequiredQuestionTypes =
    [
        AstronomyQuestionTypes.What,
        AstronomyQuestionTypes.Where,
        AstronomyQuestionTypes.When,
        AstronomyQuestionTypes.How,
        AstronomyQuestionTypes.Why,
        AstronomyQuestionTypes.Action
    ];

    private static readonly Regex GuidPattern = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{32}", RegexOptions.Compiled);
    private static readonly Regex FilePattern = new(@"\b[\w\-.]+\.(json|png|jpg|jpeg|mp3|wav|mp4|mov|webm|txt)\b|(?:[A-Za-z]:[\\/]|[\\/])(?:[^\s\\/]+[\\/])+[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LocalClockTimePattern = new(@"\b(?:[01]?\d|2[0-3]):[0-5]\d\s?(?:AM|PM|am|pm)?\b|\b(?:1[0-2]|0?[1-9])\s?(?:AM|PM|am|pm)\b", RegexOptions.Compiled);
    private static readonly string[] GenericWhyPhrases =
    [
        "easy to spot",
        "viewer-friendly",
        "easy to explain"
    ];
    private static readonly string[] WhySignificanceTerms =
    [
        "°",
        "angular separation",
        "closest approach",
        "visually striking",
        "bright planets appearing close together",
        "easy to compare",
        "rarity",
        "rare",
        "uncommon",
        "close pairing",
        "planetary pairing",
        "brightness",
        "bright",
        "event stands out",
        "alignment",
        "conjunction",
        "meteor",
        "meteor shower",
        "annual meteor shower",
        "moon interference",
        "dark sky",
        "full moon",
        "lunar",
        "eclipse",
        "rare",
        "culture",
        "scientific",
        "Milky Way"
    ];
    private static readonly (string Term, Regex Pattern)[] InternalTermPatterns =
    [
        ("GUID", ExactTermPattern("GUID")),
        ("Json", ExactTermPattern("Json")),
        ("JSON", ExactTermPattern("JSON")),
        ("metadata", ExactTermPattern("metadata")),
        ("MetadataJson", ExactTermPattern("MetadataJson")),
        ("file", ExactTermPattern("file")),
        ("path", ExactTermPattern("path")),
        ("sourcePath", ExactTermPattern("sourcePath")),
        ("assetType", ExactTermPattern("assetType")),
        ("TextOverlayCard", ExactTermPattern("TextOverlayCard")),
        ("SkyMapCard", ExactTermPattern("SkyMapCard")),
        ("PlannedVisual", ExactTermPattern("PlannedVisual")),
        ("prompt", ExactTermPattern("prompt")),
        ("database", ExactTermPattern("database")),
        ("UTC", ExactTermPattern("UTC")),
        ("Overview:", new Regex(@"(?<![A-Za-z0-9])Overview\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Closing mark:", new Regex(@"(?<![A-Za-z0-9])Closing\s+mark\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("local time", new Regex(@"(?<![A-Za-z0-9])local\s+time(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("sky window", new Regex(@"(?<![A-Za-z0-9])sky\s+window(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("magnitude", ExactTermPattern("magnitude")),
        ("internal id", new Regex(@"(?<![A-Za-z0-9])internal\s+id(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("internal words", new Regex(@"(?<![A-Za-z0-9])internal\s+words(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    public async Task<QuestionAnswerGenerationResponse> GenerateQuestionAnswersAsync(QuestionAnswerGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new List<string>();
        var events = await ResolveEventsAsync(request, warnings, cancellationToken);
        if (request.MaxEvents > 0)
            events = events.Take(request.MaxEvents).ToList();

        var existingByEventId = await LoadExistingQuestionSetsAsync(events, request, cancellationToken);
        var questionSets = new List<QuestionAnswerSetDto>();
        var generatedFiles = new List<string>();
        var savedCount = 0;

        foreach (var evt in events)
        {
            if (!request.OverwriteExisting && existingByEventId.TryGetValue(evt.Id, out var existing))
            {
                warnings.Add($"Question answers already exist for '{SafeTitle(evt)}'; returning the existing set because overwriteExisting is false.");
                var existingDto = ToDto(existing);
                questionSets.Add(existingDto);
                generatedFiles.Add(await WriteQuestionSetFileAsync(existingDto, request.ProductionContext, cancellationToken));
                continue;
            }

            var setDto = BuildQuestionSet(evt, request.RegionId, request.Language, warnings, strategyResolver);
            var validationIssues = ValidateQuestionSet(setDto);
            questionSets.Add(setDto);

            if (validationIssues.Count > 0)
            {
                warnings.AddRange(validationIssues);
                warnings.Add($"Question answers for '{SafeTitle(evt)}' failed validation and were not persisted or written to disk.");
                continue;
            }

            generatedFiles.Add(await WriteQuestionSetFileAsync(setDto, request.ProductionContext, cancellationToken));

            if (!request.DryRun)
            {
                if (request.OverwriteExisting && existingByEventId.TryGetValue(evt.Id, out var existingToSupersede))
                    existingToSupersede.Status = AstronomyQuestionSetStatus.Superseded;

                var entity = ToEntity(setDto);
                db.AstronomyQuestionAnswerSets.Add(entity);
                savedCount++;
            }
        }

        if (!request.DryRun && savedCount > 0)
            await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Question engine completed. Events={EventCount}; Sets={QuestionSetCount}; Saved={SavedCount}; DryRun={DryRun}", events.Count, questionSets.Count, savedCount, request.DryRun);

        return new QuestionAnswerGenerationResponse(events.Count, questionSets.Count, generatedFiles, questionSets, warnings);
    }


    public async Task<QuestionAnswerValidationResponse> ValidateQuestionAnswerSetAsync(QuestionAnswerValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new List<string>();
        var evt = await ResolveSingleEventAsync(request, cancellationToken);
        var setDto = BuildQuestionSet(evt, request.RegionId, request.Language, warnings, strategyResolver);
        var checks = ValidateQuestionSetForApproval(setDto);
        var approvedCount = checks.Count(c => c.Approved);
        var isApproved = checks.Count == RequiredQuestionTypes.Length && checks.All(c => c.Approved);
        var score = checks.Count == 0 ? 0 : (int)Math.Round(approvedCount * 100m / RequiredQuestionTypes.Length, MidpointRounding.AwayFromZero);

        return new QuestionAnswerValidationResponse(evt.Id.ToString("D"), isApproved, score, checks, warnings);
    }

    private async Task<List<AstronomyEventIntelligence>> ResolveEventsAsync(QuestionAnswerGenerationRequest request, List<string> warnings, CancellationToken cancellationToken)
    {
        if (request.PlanIds is { Count: > 0 } && request.PlanIds.Any(p => !string.IsNullOrWhiteSpace(p)))
        {
            var planGuids = ParseGuids(request.PlanIds, "planIds", warnings);
            if (planGuids.Count == 0) return [];

            var eventIds = await db.ContentGenerationPlans
                .AsNoTracking()
                .Where(p => planGuids.Contains(p.Id) && p.AstronomyEventIntelligenceId.HasValue)
                .Select(p => p.AstronomyEventIntelligenceId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (eventIds.Count == 0)
                warnings.Add("No astronomy events were linked to the supplied plans.");

            return await BaseEventQuery(request.RegionId)
                .Where(e => eventIds.Contains(e.Id))
                .OrderByDescending(e => e.ContentOpportunityScore)
                .ThenBy(e => e.PeakUtc ?? e.StartUtc)
                .ToListAsync(cancellationToken);
        }

        if (request.EventIds is { Count: > 0 } && request.EventIds.Any(e => !string.IsNullOrWhiteSpace(e)))
        {
            var eventGuids = new List<Guid>();
            var eventCodes = new List<string>();
            foreach (var id in request.EventIds.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                if (Guid.TryParse(id, out var guid)) eventGuids.Add(guid);
                else eventCodes.Add(id.Trim());
            }

            return await BaseEventQuery(request.RegionId)
                .Where(e => eventGuids.Contains(e.Id) || eventCodes.Contains(e.EventCode))
                .OrderByDescending(e => e.ContentOpportunityScore)
                .ThenBy(e => e.PeakUtc ?? e.StartUtc)
                .ToListAsync(cancellationToken);
        }

        return await BaseEventQuery(request.RegionId)
            .Where(e => e.Status == "Candidate" || e.Status == "Planned")
            .OrderByDescending(e => e.CreatedUtc)
            .ThenByDescending(e => e.ContentOpportunityScore)
            .ThenBy(e => e.PeakUtc ?? e.StartUtc)
            .Take(request.MaxEvents)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<AstronomyEventIntelligence> BaseEventQuery(string regionId) => db.AstronomyEventIntelligences
        .Include(e => e.Objects)
        .Where(e => e.RegionId == regionId || e.RegionId == null || e.RegionId == "");


    private async Task<AstronomyEventIntelligence> ResolveSingleEventAsync(QuestionAnswerValidationRequest request, CancellationToken cancellationToken)
    {
        var query = BaseEventQuery(request.RegionId);
        query = Guid.TryParse(request.EventId, out var eventGuid)
            ? query.Where(e => e.Id == eventGuid)
            : query.Where(e => e.EventCode == request.EventId.Trim());

        var evt = await query
            .OrderByDescending(e => e.ContentOpportunityScore)
            .ThenBy(e => e.PeakUtc ?? e.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return evt ?? throw new ArgumentException("No astronomy event was found for the supplied eventId and regionId.", nameof(request));
    }

    private async Task<Dictionary<Guid, AstronomyQuestionAnswerSet>> LoadExistingQuestionSetsAsync(IReadOnlyList<AstronomyEventIntelligence> events, QuestionAnswerGenerationRequest request, CancellationToken cancellationToken)
    {
        if (events.Count == 0) return [];
        var eventIds = events.Select(e => e.Id).ToArray();
        var existing = await db.AstronomyQuestionAnswerSets
            .Include(s => s.AstronomyEventIntelligence)
            .Include(s => s.Answers)
            .AsTracking()
            .Where(s => eventIds.Contains(s.AstronomyEventIntelligenceId)
                && s.RegionId == request.RegionId
                && s.Language == request.Language
                && s.Status == AstronomyQuestionSetStatus.Generated)
            .OrderByDescending(s => s.GeneratedUtc)
            .ToListAsync(cancellationToken);

        return existing
            .GroupBy(s => s.AstronomyEventIntelligenceId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private static QuestionAnswerSetDto BuildQuestionSet(AstronomyEventIntelligence evt, string regionId, string language, List<string> warnings, IMediaEventStrategyResolver strategyResolver)
    {
        var timezone = ResolveTimezone(evt, regionId, warnings);
        var localPeak = ToLocal(evt.PeakUtc ?? evt.StartUtc, timezone);
        var timeZoneAbbreviation = FormatTimeZoneAbbreviation(timezone, localPeak);
        var location = !string.IsNullOrWhiteSpace(evt.LocationName) ? evt.LocationName! : HumanizeLocation(regionId);
        var intelligence = BuildProductionEventIntelligence(evt, regionId, timezone, localPeak, timeZoneAbbreviation);
        var strategy = strategyResolver.Resolve(intelligence.EventType, intelligence.Title);
        var context = new QuestionAnswerSetBuildContext(
            evt.Id,
            evt.EventCode,
            regionId,
            language,
            Version,
            location,
            localPeak,
            timeZoneAbbreviation,
            DateTimeOffset.UtcNow);
        return strategy.BuildQuestionAnswerSet(intelligence, context);
    }

    private static ProductionEventIntelligence BuildProductionEventIntelligence(AstronomyEventIntelligence evt, string regionId, string timezone, DateTimeOffset localPeak, string timeZoneAbbreviation)
    {
        var objectNames = evt.Objects
            .Where(o => !string.IsNullOrWhiteSpace(o.ObjectName))
            .OrderBy(o => o.Magnitude ?? decimal.MaxValue)
            .ThenBy(o => o.ObjectName)
            .Select(o => o.ObjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var primaryObjects = evt.Objects
            .Where(o => !string.IsNullOrWhiteSpace(o.ObjectName) && ((o.ObjectRole?.Contains("Primary", StringComparison.OrdinalIgnoreCase) == true) || o.ObjectType.Contains("Meteor", StringComparison.OrdinalIgnoreCase)))
            .Select(o => o.ObjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty(objectNames.FirstOrDefault() ?? string.Empty)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToArray();
        if (primaryObjects.Length == 0) primaryObjects = objectNames.Take(1).ToArray();
        var secondaryObjects = objectNames.Except(primaryObjects, StringComparer.OrdinalIgnoreCase).ToArray();
        var bestWindow = FirstMetadataValue(evt, "bestViewingWindowLocal", "bestViewingWindow", "viewingWindowLocal");
        var localPeakText = FirstMetadataValue(evt, "localPeakTime", "moonriseLocal", "eclipseTimeLocal") ?? $"{localPeak:h:mm tt} {timeZoneAbbreviation}";
        var direction = FirstMetadataValue(evt, "skyDirectionHint", "directionHint", "direction", "viewingDirection", "azimuthDirection")
            ?? DirectionFromAzimuth(FirstDecimal(evt, "azimuth", "azimuthDegrees", "bestViewingAzimuthDegrees"));
        return new ProductionEventIntelligence(
            Domain: "Astronomy",
            EventType: evt.EventType,
            Title: SafeTitle(evt),
            ShortTitle: SafeTitle(evt),
            EventDate: evt.PeakUtc ?? evt.StartUtc,
            PeakUtc: evt.PeakUtc,
            LocalPeakTime: localPeakText,
            BestViewingWindowLocal: bestWindow,
            SkyDirectionHint: direction,
            VisibilityRegion: FirstMetadataValue(evt, "visibilityRegion", "localVisibility") ?? evt.LocationName ?? regionId,
            PrimaryObjects: primaryObjects,
            SecondaryObjects: secondaryObjects,
            ViewingQuality: evt.VisibilityScore > 0 ? $"Visibility score {evt.VisibilityScore:0.##}/10" : null,
            MoonInterference: FirstMetadataValue(evt, "moonInterference"),
            MoonIlluminationPercent: FirstDecimal(evt, "moonIlluminationPercent"),
            ScientificContext: SafeTitle(evt),
            ViewerInstructions: [],
            VisualMotifs: [],
            SceneStrategy: [],
            QualityWarnings: [],
            ForbiddenTerms: [],
            AngularSeparationDegrees: FirstDecimal(evt, "angularSeparation", "angularSeparationDegrees", "separationDegrees"),
            AltitudeDegrees: FirstDecimal(evt, "altitude", "altitudeDegrees", "maxAltitudeDegrees"),
            ReferenceObject: FirstMetadataValue(evt, "radiantVisibilityNote", "constellation", "referenceConstellation", "referenceObject"));
    }

    private static QuestionAnswerDto Answer(string type, string question, string title, string answer, int order) => new(null, type, question, title, Clean(answer), order);

    private IReadOnlyList<string> ValidateQuestionSet(QuestionAnswerSetDto set)
    {
        var issues = new List<string>();

        foreach (var type in RequiredQuestionTypes)
        {
            var answer = set.Answers.FirstOrDefault(a => string.Equals(a.QuestionType, type, StringComparison.OrdinalIgnoreCase));
            if (answer is null || string.IsNullOrWhiteSpace(answer.AnswerText))
                issues.Add($"Question answer '{type}' must be non-empty.");
        }

        foreach (var answer in set.Answers)
        {
            if (TryMatchForbiddenTerm(answer.AnswerText, out var forbiddenTerm, out _))
            {
                logger.LogWarning(
                    "Question answer validation failed. QuestionType={QuestionType}; ForbiddenTerm={ForbiddenTerm}; AnswerText={AnswerText}",
                    answer.QuestionType,
                    forbiddenTerm,
                    answer.AnswerText);
                issues.Add($"Question answer '{answer.QuestionType}' contains internal wording: matched forbidden term '{forbiddenTerm}' in answer text '{answer.AnswerText}'.");
            }
        }

        var whyAnswer = set.Answers.FirstOrDefault(a => string.Equals(a.QuestionType, AstronomyQuestionTypes.Why, StringComparison.OrdinalIgnoreCase));
        if (whyAnswer is not null)
        {
            var includesSignificance = WhySignificanceTerms.Any(t => whyAnswer.AnswerText.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (GenericWhyPhrases.Any(p => whyAnswer.AnswerText.Contains(p, StringComparison.OrdinalIgnoreCase)) && !includesSignificance)
                issues.Add($"Question answer '{AstronomyQuestionTypes.Why}' must explain event significance, not just generic viewing ease.");

            if (!includesSignificance)
                issues.Add($"Question answer '{AstronomyQuestionTypes.Why}' should include angular separation, rarity, close pairing, brightness, or event significance.");
        }


        ValidateEventSpecificRules(set, issues);

        return issues;
    }

    private static void ValidateEventSpecificRules(QuestionAnswerSetDto set, List<string> issues)
    {
        var combined = string.Join(" ", set.Answers.Select(a => a.AnswerText));
        var isMeteor = IsEventType(set, "MeteorShower") || TokenContains(combined, "meteor");
        var actualAllowsVenus = TokenContains(combined, "Venus") && set.EventTitle.Contains("Venus", StringComparison.OrdinalIgnoreCase);
        var actualAllowsJupiter = TokenContains(combined, "Jupiter") && set.EventTitle.Contains("Jupiter", StringComparison.OrdinalIgnoreCase);

        if (isMeteor)
        {
            if (TokenContains(combined, "conjunction")) issues.Add("MeteorShower question answers must not mention conjunction unless the source event is a conjunction.");
            if (TokenContains(combined, "Venus") && !actualAllowsVenus) issues.Add("MeteorShower question answers must not mention Venus unless it is a source object.");
            if (TokenContains(combined, "Jupiter") && !actualAllowsJupiter) issues.Add("MeteorShower question answers must not mention Jupiter unless it is a source object.");
            if (!TokenContains(combined, "meteor") || !ContainsAny(combined, "dark sky", "darkest") || !ContainsAny(combined, "no telescope")) issues.Add("MeteorShower answers must include meteor, dark-sky, and no-telescope guidance.");
            var when = AnswerText(set, AstronomyQuestionTypes.When);
            if (Regex.IsMatch(when, @"\b(?:1[01]|[6-9]):[0-5]\d\s?(?:AM|am)\b|\b(?:12|1|2|3|4|5):[0-5]\d\s?(?:PM|pm)\b"))
                issues.Add("MeteorShower best viewing time must use a dark night window, not a daytime local peak time.");
        }

        if (!isMeteor && (TokenContains(combined, "meteor") || TokenContains(combined, "radiant")))
            issues.Add("Non-meteor events must not mention meteor or radiant wording.");

        if (IsEventType(set, "NamedFullMoon") && (!TokenContains(combined, "Moon") || !ContainsAny(combined, "full moon")))
            issues.Add("NamedFullMoon answers must include Moon and full moon context.");

        if (IsEventType(set, "NewMoon"))
        {
            if (!ContainsAny(combined, "dark sky", "darker night", "moonlight is absent")) issues.Add("NewMoon answers must include dark-sky context.");
            if (ContainsAny(combined, "visible moon", "bright moon", "fully illuminated")) issues.Add("NewMoon answers must not describe visible full-moon imagery.");
        }

        if (IsEventType(set, "LunarEclipse") && (!TokenContains(combined, "eclipse") || !TokenContains(combined, "Moon") || !TokenContains(combined, "phase")))
            issues.Add("LunarEclipse answers must include eclipse, Moon, and phase timing.");

        if (IsEventType(set, "SolarEclipse"))
        {
            if (!ContainsAny(combined, "certified eclipse glasses", "certified eye protection", "solar filters")) issues.Add("SolarEclipse answers must include eye safety.");
            if (ContainsAny(combined, "look directly at the Sun")) issues.Add("SolarEclipse answers must not instruct viewers to look directly at the Sun.");
        }
    }

    private static bool IsEventType(QuestionAnswerSetDto set, string eventType)
        => set.EventType.Contains(eventType, StringComparison.OrdinalIgnoreCase);

    private static string AnswerText(QuestionAnswerSetDto set, string questionType)
        => set.Answers.FirstOrDefault(a => string.Equals(a.QuestionType, questionType, StringComparison.OrdinalIgnoreCase))?.AnswerText ?? string.Empty;

    private IReadOnlyList<QuestionAnswerValidationCheckDto> ValidateQuestionSetForApproval(QuestionAnswerSetDto set)
    {
        var contract = strategyResolver.Resolve(set.EventType, set.EventTitle).QuestionQualityContract;
        var checks = new List<QuestionAnswerValidationCheckDto>();
        var answersByType = set.Answers
            .GroupBy(a => a.QuestionType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.DisplayOrder).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var type in RequiredQuestionTypes)
        {
            var issues = new List<string>();
            var recommendations = new List<string>();

            if (!answersByType.TryGetValue(type, out var answer) || string.IsNullOrWhiteSpace(answer.AnswerText))
            {
                issues.Add($"{type.ToUpperInvariant()} answer is missing.");
                recommendations.Add($"Add a viewer-facing {type.ToUpperInvariant()} answer before approving this question set.");
                checks.Add(new QuestionAnswerValidationCheckDto(type, false, issues, recommendations));
                continue;
            }

            var text = Clean(answer.AnswerText);
            ValidateViewerFacingLanguage(type, text, issues, recommendations);
            ValidateSceneRole(type, text, set, contract, issues, recommendations);
            ValidateVisualReadiness(type, text, issues, recommendations);
            ValidateAccessibility(type, text, issues, recommendations);

            checks.Add(new QuestionAnswerValidationCheckDto(type, issues.Count == 0, issues, recommendations));
        }

        var eventIssues = new List<string>();
        ValidateEventSpecificRules(set, eventIssues);
        if (eventIssues.Count > 0)
        {
            var actionIndex = checks.FindIndex(c => string.Equals(c.QuestionType, AstronomyQuestionTypes.Action, StringComparison.OrdinalIgnoreCase));
            if (actionIndex >= 0)
            {
                var current = checks[actionIndex];
                checks[actionIndex] = current with
                {
                    Approved = false,
                    Issues = current.Issues.Concat(eventIssues).ToArray(),
                    Recommendations = current.Recommendations.Concat(["Apply the event-specific strategy requirements before approval."]).ToArray()
                };
            }
        }

        return checks;
    }

    private static void ValidateViewerFacingLanguage(string questionType, string text, List<string> issues, List<string> recommendations)
    {
        if (TryMatchForbiddenTerm(text, out var forbiddenTerm, out var matchedText))
        {
            issues.Add($"{questionType.ToUpperInvariant()} contains non-viewer-facing wording: matched forbidden term '{forbiddenTerm}' in '{matchedText}'.");
            recommendations.Add("Rewrite the answer as plain viewer-facing language without implementation labels, identifiers, file references, UTC timestamps, or prompt metadata.");
        }
    }

    private static void ValidateSceneRole(string questionType, string text, QuestionAnswerSetDto set, QuestionQualityContract contract, List<string> issues, List<string> recommendations)
    {
        var requiredIntents = RequiredIntentsFor(questionType, contract);
        foreach (var intent in requiredIntents)
        {
            if (intent.AcceptedPhrases.Count == 0 || intent.AcceptedPhrases.Any(phrase => ContainsIntentPhrase(text, phrase)))
                continue;

            issues.Add($"{questionType.ToUpperInvariant()} missing required intent '{intent.Intent}'. Accepted cues: {string.Join(", ", intent.AcceptedPhrases)}.");
            recommendations.Add($"Add viewer-facing wording that satisfies the '{intent.Intent}' intent without using internal labels.");
        }

        switch (questionType)
        {
            case AstronomyQuestionTypes.What:
                if (StartsWithAny(text, "if ", "look ", "find "))
                {
                    issues.Add("WHAT must work as the opening overview.");
                    recommendations.Add("Start with what the event is and what the viewer will see, not an instruction.");
                }
                if (!MentionsEventIdentityOrObject(set, text))
                {
                    issues.Add("WHAT must mention the event title, event type, or source object in public language.");
                    recommendations.Add("Name the event, the public event type, or the main visible object in the opening answer.");
                }
                break;
            case AstronomyQuestionTypes.When:
                if (!LocalClockTimePattern.IsMatch(text) || text.Contains("UTC", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add("WHEN must include a local clock time and must not use UTC.");
                    recommendations.Add("Use a viewer-facing local time, such as '7:23 PM IST', instead of UTC.");
                }
                break;
        }
    }

    private static IReadOnlyList<QuestionQualityIntentGroup> RequiredIntentsFor(string questionType, QuestionQualityContract contract)
        => questionType switch
        {
            AstronomyQuestionTypes.What => contract.WhatRequiredIntents,
            AstronomyQuestionTypes.Where => contract.WhereRequiredIntents,
            AstronomyQuestionTypes.When => contract.WhenRequiredIntents,
            AstronomyQuestionTypes.How => contract.HowRequiredIntents,
            AstronomyQuestionTypes.Why => contract.WhyRequiredIntents,
            AstronomyQuestionTypes.Action => contract.ActionRequiredIntents,
            _ => []
        };

    private static bool ContainsIntentPhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        return phrase.Length <= 2 || phrase.Any(char.IsPunctuation)
            ? text.Contains(phrase, StringComparison.OrdinalIgnoreCase)
            : TokenContains(text, phrase);
    }

    private static bool MentionsEventIdentityOrObject(QuestionAnswerSetDto set, string text)
    {
        foreach (var token in PublicIdentityTokens(set.EventTitle).Concat(PublicIdentityTokens(set.EventType)))
            if (ContainsIntentPhrase(text, token)) return true;
        return false;
    }

    private static IEnumerable<string> PublicIdentityTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        foreach (Match match in Regex.Matches(value.Replace('_', ' ').Replace('-', ' '), @"[\p{L}\p{N}]{3,}"))
        {
            var token = match.Value;
            if (IsGenericIdentityToken(token)) continue;
            yield return token;
        }
    }

    private static bool IsGenericIdentityToken(string token)
        => token.Equals("peak", StringComparison.OrdinalIgnoreCase)
            || token.Equals("title", StringComparison.OrdinalIgnoreCase)
            || token.Equals("event", StringComparison.OrdinalIgnoreCase);

    private static void ValidateVisualReadiness(string questionType, string text, List<string> issues, List<string> recommendations)
    {
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < 4 || wordCount > 28 || text.Contains('{') || text.Contains('}') || text.Contains('[') || text.Contains(']'))
        {
            issues.Add($"{questionType.ToUpperInvariant()} must be convertible into a narration line, overlay text, and image prompt instruction.");
            recommendations.Add("Keep the answer as one concise natural-language sentence with no JSON-like structure.");
        }
    }

    private static void ValidateAccessibility(string questionType, string text, List<string> issues, List<string> recommendations)
    {
        if (!text.Any(char.IsLetter) || string.Equals(text, "it", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "this", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{questionType.ToUpperInvariant()} must be understandable without audio.");
            recommendations.Add("Make the answer self-contained enough to read as overlay text.");
        }
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(term => TokenContains(text, term));

    private static bool StartsWithAny(string text, params string[] terms)
        => terms.Any(term => text.StartsWith(term, StringComparison.OrdinalIgnoreCase));

    private static bool TokenContains(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term)) return false;
        var escaped = Regex.Escape(term.Trim()).Replace("\\ ", @"\s+");
        return Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}_]){escaped}(?![\p{{L}}\p{{N}}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool TryMatchForbiddenTerm(string answerText, out string forbiddenTerm, out string matchedText)
    {
        var guidMatch = GuidPattern.Match(answerText);
        if (guidMatch.Success)
        {
            forbiddenTerm = "GUID";
            matchedText = guidMatch.Value;
            return true;
        }

        var fileMatch = FilePattern.Match(answerText);
        if (fileMatch.Success)
        {
            forbiddenTerm = "file";
            matchedText = fileMatch.Value;
            return true;
        }

        foreach (var (term, pattern) in InternalTermPatterns)
        {
            var match = pattern.Match(answerText);
            if (!match.Success) continue;

            forbiddenTerm = term;
            matchedText = match.Value;
            return true;
        }

        forbiddenTerm = string.Empty;
        matchedText = string.Empty;
        return false;
    }

    private static Regex ExactTermPattern(string term)
        => new($"(?<![\\p{{L}}\\p{{N}}_]){Regex.Escape(term)}(?![\\p{{L}}\\p{{N}}_])", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private async Task<string> WriteQuestionSetFileAsync(QuestionAnswerSetDto set, ProductionPipelineExecutionContext? productionContext, CancellationToken cancellationToken)
    {
        var eventFolder = set.AstronomyEventIntelligenceId.ToString("D");
        var questionRoot = ResolveQuestionRoot(productionContext, set.RegionId, eventFolder);
        var outputPath = Path.Combine(questionRoot, "question-answer-set.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(set, JsonOptions), cancellationToken);
        return outputPath.Replace('\\', '/');
    }

    private string ResolveQuestionRoot(ProductionPipelineExecutionContext? productionContext, string regionId, string eventFolder)
        => !string.IsNullOrWhiteSpace(productionContext?.QuestionRoot)
            ? productionContext!.QuestionRoot!
            : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", eventFolder, "question-engine");

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static AstronomyQuestionAnswerSet ToEntity(QuestionAnswerSetDto dto) => new()
    {
        AstronomyEventIntelligenceId = dto.AstronomyEventIntelligenceId,
        RegionId = dto.RegionId,
        Language = dto.Language,
        Version = dto.Version,
        Status = dto.Status,
        GeneratedUtc = dto.GeneratedUtc,
        Answers = dto.Answers.Select(a => new AstronomyQuestionAnswer
        {
            QuestionType = a.QuestionType,
            QuestionText = a.QuestionText,
            Title = a.Title,
            AnswerText = a.AnswerText,
            DisplayOrder = a.DisplayOrder
        }).ToList()
    };

    private static QuestionAnswerSetDto ToDto(AstronomyQuestionAnswerSet entity) => new(
        entity.Id,
        entity.AstronomyEventIntelligenceId,
        entity.AstronomyEventIntelligence?.EventCode ?? string.Empty,
        entity.AstronomyEventIntelligence?.Title ?? string.Empty,
        entity.AstronomyEventIntelligence?.EventType ?? string.Empty,
        entity.RegionId,
        entity.Language,
        entity.Version,
        entity.Status,
        entity.GeneratedUtc,
        entity.Answers.OrderBy(a => a.DisplayOrder).Select(a => new QuestionAnswerDto(a.Id, a.QuestionType, a.QuestionText, a.Title, a.AnswerText, a.DisplayOrder)).ToArray());

    private static string ResolveTimezone(AstronomyEventIntelligence evt, string regionId, List<string> warnings)
    {
        var tz = evt.TimeZone;
        if (string.IsNullOrWhiteSpace(tz) && regionId.StartsWith("IN-", StringComparison.OrdinalIgnoreCase)) tz = "Asia/Kolkata";
        if (string.IsNullOrWhiteSpace(tz)) tz = TimeZoneInfo.Local.Id;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(tz); return tz; }
        catch (TimeZoneNotFoundException) { warnings.Add($"Timezone '{tz}' was not available; local server time was used for '{SafeTitle(evt)}'."); return TimeZoneInfo.Local.Id; }
        catch (InvalidTimeZoneException) { warnings.Add($"Timezone '{tz}' was invalid; local server time was used for '{SafeTitle(evt)}'."); return TimeZoneInfo.Local.Id; }
    }

    private static DateTimeOffset ToLocal(DateTimeOffset utc, string timeZoneId)
        => TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

    private static string FormatWhatAnswer(string eventType, string primaryObjects, string location)
        => IsClosePairing(eventType)
            ? $"{primaryObjects} will appear close together in {location}’s evening sky."
            : $"{primaryObjects} will be the highlight in {location}’s sky.";

    private static string FormatHowAnswer(IReadOnlyList<string> objectNames, string primaryObjects, string referencePoint)
    {
        if (objectNames.Count >= 2)
            return $"Find bright {objectNames[0]} first, then look slightly nearby for {objectNames[1]}.";

        return $"Find {primaryObjects} first, then use {referencePoint} as your guide.";
    }

    private static string FormatWhyAnswer(AstronomyEventIntelligence evt, IReadOnlyList<string> objectNames, string primaryObjects, decimal? separation, string eventType)
    {
        if (IsPlanetConjunction(evt.EventType))
        {
            if (separation.HasValue)
                return $"This conjunction is visually striking because near closest approach {primaryObjects} appear only {separation.Value:0.##}° apart, making the alignment easy to compare.";

            if (objectNames.Count >= 2)
                return $"This conjunction is visually striking because {primaryObjects} are bright planets appearing close together, making the alignment easy to compare.";

            return $"This conjunction is visually striking because {primaryObjects} form a close planetary alignment that is easy to compare in Earth’s sky.";
        }

        if (separation.HasValue)
            return $"{primaryObjects} appear only {separation.Value:0.##}° apart, creating a striking close pairing.";

        if (HasBrightObjects(evt))
            return $"{primaryObjects} stand out because their brightness makes the event visually prominent in the sky.";

        if (evt.RarityScore >= 7m)
            return $"This {eventType} is special because it is an uncommon sky event worth watching near its peak.";

        if (IsClosePairing(eventType))
            return $"This {eventType} is special because the objects form a close pairing from our point of view on Earth.";

        return $"This {eventType} is special because it highlights a notable change or alignment in the night sky.";
    }

    private static string FormatActionAnswer(DateTimeOffset localPeak)
        => IsEvening(localPeak)
            ? "If skies are clear, step outside after sunset and enjoy the view."
            : "If skies are clear, step outside at the best time and enjoy the view.";

    private static string FormatAltitude(decimal? altitude)
    {
        if (!altitude.HasValue) return "comfortably";

        var rounded = Math.Round(altitude.Value);
        return rounded switch
        {
            >= 25 and <= 35 => "about one-third",
            >= 15 and < 25 => "not far",
            > 35 and <= 55 => "about halfway",
            > 55 => "high",
            _ => "low"
        };
    }

    private static string FormatSkyDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)) return "clearest open sky";

        var normalized = direction.Trim().ToLowerInvariant()
            .Replace(" direction", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" sky", string.Empty, StringComparison.OrdinalIgnoreCase);

        return normalized switch
        {
            "north" => "northern sky",
            "northeast" or "north-east" => "northeastern sky",
            "east" => "eastern sky",
            "southeast" or "south-east" => "southeastern sky",
            "south" => "southern sky",
            "southwest" or "south-west" => "southwestern sky",
            "west" => "western sky",
            "northwest" or "north-west" => "northwestern sky",
            _ => normalized
        };
    }

    private static string DescribeViewingTime(DateTimeOffset localPeak)
    {
        if (IsEvening(localPeak)) return "shortly after sunset";
        if (localPeak.Hour is >= 0 and < 4) return "after midnight";
        if (localPeak.Hour is >= 4 and < 6) return "before sunrise";
        return "near the peak of the event";
    }

    private static bool IsEvening(DateTimeOffset localPeak) => localPeak.Hour is >= 17 and <= 21;

    private static string FormatTimeZoneAbbreviation(string timeZoneId, DateTimeOffset localTime)
        => timeZoneId switch
        {
            "Asia/Kolkata" => "IST",
            "Etc/UTC" or "UTC" => "GMT",
            _ => TimeZoneInfo.FindSystemTimeZoneById(timeZoneId).IsDaylightSavingTime(localTime)
                ? TimeZoneInfo.FindSystemTimeZoneById(timeZoneId).DaylightName
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId).StandardName
        };

    private static bool IsClosePairing(string eventType)
        => eventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("close", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("pair", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlanetConjunction(string eventType)
        => eventType.Contains("PlanetConjunction", StringComparison.OrdinalIgnoreCase)
            || (eventType.Contains("planet", StringComparison.OrdinalIgnoreCase)
                && eventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase));

    private static bool HasBrightObjects(AstronomyEventIntelligence evt)
        => evt.Objects.Any(o => o.Magnitude.HasValue && o.Magnitude <= 1.5m) || evt.VisibilityScore >= 7m;

    private static string DirectionFromAzimuth(decimal? azimuth) => !azimuth.HasValue ? "clearest open horizon" : azimuth.Value switch
    {
        >= 337.5m or < 22.5m => "north",
        < 67.5m => "northeast",
        < 112.5m => "east",
        < 157.5m => "southeast",
        < 202.5m => "south",
        < 247.5m => "southwest",
        < 292.5m => "west",
        _ => "northwest"
    };

    private static string? FirstMetadataValue(AstronomyEventIntelligence evt, params string[] names)
    {
        foreach (var value in MetadataValues(evt.MetadataJson, names).Concat(evt.Objects.SelectMany(o => MetadataValues(o.MetadataJson, names))))
            if (!string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    private static decimal? FirstDecimal(AstronomyEventIntelligence evt, params string[] names)
    {
        foreach (var value in MetadataValues(evt.MetadataJson, names).Concat(evt.Objects.SelectMany(o => MetadataValues(o.MetadataJson, names))))
            if (decimal.TryParse(value, out var result)) return result;
        return null;
    }

    private static IEnumerable<string> MetadataValues(string? json, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return names.SelectMany(name => FindProperties(doc.RootElement, name)).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<string> FindProperties(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    yield return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
                foreach (var nested in FindProperties(property.Value, name)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var nested in FindProperties(item, name)) yield return nested;
        }
    }

    private static List<Guid> ParseGuids(IReadOnlyList<string> values, string fieldName, List<string> warnings)
    {
        var guids = new List<Guid>();
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (Guid.TryParse(value, out var guid)) guids.Add(guid);
            else warnings.Add($"Ignored invalid {fieldName} value.");
        }
        return guids;
    }

    private static string JoinNatural(IReadOnlyList<string> values) => values.Count switch
    {
        0 => "the main sky target",
        1 => values[0],
        2 => $"{values[0]} and {values[1]}",
        _ => $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}"
    };

    private static string Humanize(string value) => string.IsNullOrWhiteSpace(value) ? "astronomy event" : value.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();

    private static string HumanizeLocation(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "your area";

        var lastSegment = value.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(lastSegment)) return value;

        return string.Join(' ', lastSegment.Split('_', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant() switch
            {
                var location when location.Length > 0 => char.ToUpperInvariant(location[0]) + location[1..],
                _ => value
            };
    }

    private static string SafeTitle(AstronomyEventIntelligence evt) => string.IsNullOrWhiteSpace(evt.Title) ? Humanize(evt.EventType) : evt.Title;
    private static string Clean(string text) => Regex.Replace(text, "\\s+", " ").Trim();
    private static string SanitizePathSegment(string value) => string.Join('-', value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim().Replace(' ', '-');

    private static void ValidateRequest(QuestionAnswerGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
        if (request.MaxEvents <= 0) throw new ArgumentException("maxEvents must be greater than zero.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language)) throw new ArgumentException("language is required.", nameof(request));
    }

    private static void ValidateRequest(QuestionAnswerValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.EventId)) throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language)) throw new ArgumentException("language is required.", nameof(request));
    }
}
