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
    private static readonly Regex FilePattern = new(@"\b[\w\-.]+\.(json|png|jpg|jpeg|mp3|wav|mp4|mov|webm|txt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly (string Term, Regex Pattern)[] InternalTermPatterns =
    [
        ("GUID", ExactTermPattern("GUID")),
        ("Json", ExactTermPattern("Json")),
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
        ("internal id", new Regex(@"(?<![A-Za-z0-9])internal\s+id(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled))
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
                generatedFiles.Add(await WriteQuestionSetFileAsync(existingDto, cancellationToken));
                continue;
            }

            var setDto = BuildQuestionSet(evt, request.RegionId, request.Language, warnings);
            var validationIssues = ValidateQuestionSet(setDto);
            questionSets.Add(setDto);

            if (validationIssues.Count > 0)
            {
                warnings.AddRange(validationIssues);
                warnings.Add($"Question answers for '{SafeTitle(evt)}' failed validation and were not persisted or written to disk.");
                continue;
            }

            generatedFiles.Add(await WriteQuestionSetFileAsync(setDto, cancellationToken));

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

    private static QuestionAnswerSetDto BuildQuestionSet(AstronomyEventIntelligence evt, string regionId, string language, List<string> warnings)
    {
        var timezone = ResolveTimezone(evt, regionId, warnings);
        var localPeak = ToLocal(evt.PeakUtc ?? evt.StartUtc, timezone);
        var localStart = ToLocal(evt.StartUtc, timezone);
        var localEnd = evt.EndUtc.HasValue ? ToLocal(evt.EndUtc.Value, timezone) : localPeak.AddHours(2);
        var objectNames = evt.Objects.Select(o => o.ObjectName).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var primaryObjects = objectNames.Length == 0 ? "the main sky target" : JoinNatural(objectNames.Take(3).ToArray());
        var direction = FirstMetadataValue(evt, "direction", "viewingDirection", "azimuthDirection") ?? DirectionFromAzimuth(FirstDecimal(evt, "azimuth", "azimuthDegrees", "bestViewingAzimuthDegrees"));
        var altitude = FirstDecimal(evt, "altitude", "altitudeDegrees", "maxAltitudeDegrees");
        var magnitude = evt.Objects.Select(o => o.Magnitude).FirstOrDefault(m => m.HasValue);
        var constellation = FirstMetadataValue(evt, "constellation", "referenceConstellation", "referenceObject") ?? "near a familiar bright reference point";
        var separation = FirstDecimal(evt, "angularSeparation", "angularSeparationDegrees", "separationDegrees");
        var location = !string.IsNullOrWhiteSpace(evt.LocationName) ? evt.LocationName! : regionId;
        var eventType = Humanize(evt.EventType);
        var bestWindow = FormatWindow(localPeak, localStart, localEnd);

        return new QuestionAnswerSetDto(
            null,
            evt.Id,
            evt.EventCode,
            SafeTitle(evt),
            evt.EventType,
            regionId,
            language,
            Version,
            AstronomyQuestionSetStatus.Generated,
            DateTimeOffset.UtcNow,
            [
                Answer(AstronomyQuestionTypes.What, "What is happening?", "Opening overview", $"Overview: {eventType} featuring {primaryObjects}, a clean sky story for tonight.", 1),
                Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", $"Look {direction} from {location}; aim {FormatAltitude(altitude)} above the horizon.", 2),
                Answer(AstronomyQuestionTypes.When, "When is the best time?", "Best viewing time", $"Best view is around {localPeak:h:mm tt} local time, within the {bestWindow} sky window.", 3),
                Answer(AstronomyQuestionTypes.How, "How can I find it?", "How to observe", $"Start with {primaryObjects}; {FormatBrightness(magnitude)} and use {constellation} as your guide.", 4),
                Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", $"This {eventType.ToLowerInvariant()} stands out because {FormatSeparation(separation)} and makes the sky easy to explain visually.", 5),
                Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Closing mark", $"Closing mark: If clouds stay away, step outside, face {direction}, and watch for {primaryObjects}.", 6)
            ]);
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

        return issues;
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
        => new($"(?<![A-Za-z0-9]){Regex.Escape(term)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private async Task<string> WriteQuestionSetFileAsync(QuestionAnswerSetDto set, CancellationToken cancellationToken)
    {
        var eventFolder = set.AstronomyEventIntelligenceId.ToString("D");
        var outputPath = Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(set.RegionId), "events", eventFolder, "question-engine", "question-answer-set.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(set, JsonOptions), cancellationToken);
        return outputPath.Replace('\\', '/');
    }

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

    private static string FormatWindow(DateTimeOffset peak, DateTimeOffset start, DateTimeOffset end)
    {
        var windowStart = start.Date == peak.Date ? start : peak.AddHours(-1);
        var windowEnd = end > windowStart ? end : peak.AddHours(1);
        return $"{windowStart:h:mm tt} to {windowEnd:h:mm tt}";
    }

    private static string FormatAltitude(decimal? altitude) => altitude.HasValue ? $"about {Math.Round(altitude.Value)}°" : "comfortably";
    private static string FormatBrightness(decimal? magnitude) => magnitude.HasValue ? $"it is around magnitude {magnitude.Value:0.#}" : "choose the clearest part of the sky";
    private static string FormatSeparation(decimal? separation) => separation.HasValue ? $"the objects sit about {separation.Value:0.#}° apart" : "the timing and arrangement are viewer-friendly";
    private static string DirectionFromAzimuth(decimal? azimuth) => !azimuth.HasValue ? "toward the clearest open horizon" : azimuth.Value switch
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
    private static string SafeTitle(AstronomyEventIntelligence evt) => string.IsNullOrWhiteSpace(evt.Title) ? Humanize(evt.EventType) : evt.Title;
    private static string Clean(string text) => Regex.Replace(text.Replace(" UTC", " local time", StringComparison.OrdinalIgnoreCase), "\\s+", " ").Trim();
    private static string SanitizePathSegment(string value) => string.Join('-', value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim().Replace(' ', '-');

    private static void ValidateRequest(QuestionAnswerGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
        if (request.MaxEvents <= 0) throw new ArgumentException("maxEvents must be greater than zero.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language)) throw new ArgumentException("language is required.", nameof(request));
    }
}
