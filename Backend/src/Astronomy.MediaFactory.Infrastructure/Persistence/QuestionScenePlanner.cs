using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionScenePlanner(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    IMediaEventStrategyResolver strategyResolver,
    ILogger<QuestionScenePlanner> logger) : IQuestionScenePlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] SceneOrder =
    [
        AstronomyQuestionTypes.What,
        AstronomyQuestionTypes.Where,
        AstronomyQuestionTypes.When,
        AstronomyQuestionTypes.How,
        AstronomyQuestionTypes.Why,
        AstronomyQuestionTypes.Action
    ];

    private static readonly IReadOnlyDictionary<string, string> ScenePurposeByQuestionType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [AstronomyQuestionTypes.What] = "OpeningOverview",
        [AstronomyQuestionTypes.Where] = "LocationGuide",
        [AstronomyQuestionTypes.When] = "TimingGuide",
        [AstronomyQuestionTypes.How] = "ObservationGuide",
        [AstronomyQuestionTypes.Why] = "Significance",
        [AstronomyQuestionTypes.Action] = "ClosingAction"
    };

    private static readonly Regex GuidPattern = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{32}", RegexOptions.Compiled);
    private static readonly Regex FilePattern = new(@"\b[\w\-.]+\.(json|png|jpg|jpeg|mp3|wav|mp4|mov|webm|txt)\b|(?:[A-Za-z]:[\\/]|[\\/])(?:[^\s\\/]+[\\/])+[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LocalClockTimePattern = new(@"\b(?:[01]?\d|2[0-3]):[0-5]\d\s?(?:AM|PM|am|pm)?\b|\b(?:1[0-2]|0?[1-9])\s?(?:AM|PM|am|pm)\b", RegexOptions.Compiled);
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

    public async Task<QuestionScenePlanResponse> GenerateQuestionScenePlanAsync(QuestionScenePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new List<string>();
        var questionSet = await ResolveQuestionSetAsync(request, warnings, cancellationToken);
        var outputPath = BuildOutputPath(questionSet.AstronomyEventIntelligenceId, request.RegionId, request.ProductionContext);

        if (!request.OverwriteExisting && File.Exists(outputPath))
        {
            var existingJson = await File.ReadAllTextAsync(outputPath, cancellationToken);
            var existingPlan = JsonSerializer.Deserialize<QuestionDrivenScenePlanDto>(existingJson, JsonOptions)
                ?? throw new InvalidOperationException("Existing question-driven scene plan could not be parsed.");
            var existingIssues = ValidateScenePlan(existingPlan);
            warnings.Add("Question-driven scene plan already exists; returning the existing file because overwriteExisting is false.");
            warnings.AddRange(existingIssues);
            return new QuestionScenePlanResponse(
                existingPlan.EventId,
                existingPlan.Scenes.Count,
                existingIssues.Count == 0,
                existingPlan,
                [outputPath.Replace('\\', '/')],
                warnings);
        }

        var plan = BuildScenePlan(questionSet, request.RegionId, request.Language);
        var validationIssues = ValidateScenePlan(plan);
        warnings.AddRange(validationIssues);
        if (validationIssues.Count > 0)
        {
            logger.LogWarning("Question-driven scene plan validation failed for EventId={EventId}. Issues={Issues}", plan.EventId, string.Join(" | ", validationIssues));
            return new QuestionScenePlanResponse(plan.EventId, plan.Scenes.Count, false, plan, [], warnings);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);

        return new QuestionScenePlanResponse(
            plan.EventId,
            plan.Scenes.Count,
            true,
            plan,
            [outputPath.Replace('\\', '/')],
            warnings);
    }

    private async Task<AstronomyQuestionAnswerSet> ResolveQuestionSetAsync(QuestionScenePlanRequest request, List<string> warnings, CancellationToken cancellationToken)
    {
        var query = db.AstronomyQuestionAnswerSets
            .Include(s => s.AstronomyEventIntelligence)
            .Include(s => s.Answers)
            .AsTracking()
            .Where(s => s.RegionId == request.RegionId
                && s.Language == request.Language
                && (s.Status == AstronomyQuestionSetStatus.Approved || s.Status == AstronomyQuestionSetStatus.Generated));

        query = Guid.TryParse(request.EventId, out var eventGuid)
            ? query.Where(s => s.AstronomyEventIntelligenceId == eventGuid)
            : query.Where(s => s.AstronomyEventIntelligence != null && s.AstronomyEventIntelligence.EventCode == request.EventId.Trim());

        var set = await query
            .OrderByDescending(s => s.GeneratedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (set is null)
            throw new ArgumentException("No approved or generated question answer set was found for the supplied eventId, regionId, and language.", nameof(request));

        if (!string.Equals(set.Status, AstronomyQuestionSetStatus.Generated, StringComparison.OrdinalIgnoreCase))
            return set;

        var validation = ValidateGeneratedQuestionSet(set);
        if (validation.IsApproved)
        {
            warnings.Add("QuestionAnswerSet status is Generated but passed validation.");
            return set;
        }

        var issues = validation.Checks
            .SelectMany(c => c.Issues.Select(issue => $"{c.QuestionType}: {issue}"))
            .ToArray();
        var issueMessage = issues.Length == 0 ? "validation score was below approval threshold." : string.Join(" | ", issues);
        throw new ArgumentException($"Latest generated question answer set did not pass validation ({validation.Score}/100): {issueMessage}", nameof(request));
    }


    private QuestionAnswerValidationResponse ValidateGeneratedQuestionSet(AstronomyQuestionAnswerSet set)
    {
        var checks = ValidateQuestionSetForApproval(set);
        var approvedCount = checks.Count(c => c.Approved);
        var isApproved = checks.Count == SceneOrder.Length && checks.All(c => c.Approved);
        var score = checks.Count == 0 ? 0 : (int)Math.Round(approvedCount * 100m / SceneOrder.Length, MidpointRounding.AwayFromZero);

        return new QuestionAnswerValidationResponse(set.AstronomyEventIntelligenceId.ToString("D"), isApproved, score, checks, []);
    }

    private IReadOnlyList<QuestionAnswerValidationCheckDto> ValidateQuestionSetForApproval(AstronomyQuestionAnswerSet set)
    {
        var checks = new List<QuestionAnswerValidationCheckDto>();
        var contract = strategyResolver.Resolve(set.AstronomyEventIntelligence?.EventType ?? string.Empty, set.AstronomyEventIntelligence?.Title ?? string.Empty).QuestionQualityContract;
        var answersByType = set.Answers
            .GroupBy(a => a.QuestionType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.DisplayOrder).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var type in SceneOrder)
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
            ValidateSceneRole(type, text, contract, issues, recommendations);
            ValidateVisualReadiness(type, text, issues, recommendations);
            ValidateAccessibility(type, text, issues, recommendations);

            checks.Add(new QuestionAnswerValidationCheckDto(type, issues.Count == 0, issues, recommendations));
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

    private static void ValidateSceneRole(string questionType, string text, QuestionQualityContract contract, List<string> issues, List<string> recommendations)
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
                    recommendations.Add("Summarize the event in one clean opening sentence that names what the viewer will see.");
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

    private static QuestionDrivenScenePlanDto BuildScenePlan(AstronomyQuestionAnswerSet questionSet, string regionId, string language)
    {
        var answersByType = questionSet.Answers
            .GroupBy(a => a.QuestionType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.DisplayOrder).First(), StringComparer.OrdinalIgnoreCase);
        var isMeteorShower = questionSet.AstronomyEventIntelligence?.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) == true
            || questionSet.AstronomyEventIntelligence?.Title.Contains("meteor", StringComparison.OrdinalIgnoreCase) == true;

        var scenes = SceneOrder.Select((questionType, index) =>
        {
            if (!answersByType.TryGetValue(questionType, out var answer))
                throw new ArgumentException($"Question answer set is missing the required '{questionType}' answer.");

            var scenePurpose = ScenePurposeByQuestionType[questionType];
            var sourceAnswer = Clean(answer.AnswerText);
            return new QuestionDrivenSceneDto(
                index + 1,
                questionType,
                scenePurpose,
                Clean(answer.QuestionText),
                sourceAnswer,
                sourceAnswer,
                BuildVisualIntent(questionType, scenePurpose, sourceAnswer, isMeteorShower),
                BuildNarrationIntent(questionType, scenePurpose, sourceAnswer, isMeteorShower),
                true);
        }).ToArray();

        return new QuestionDrivenScenePlanDto(
            questionSet.AstronomyEventIntelligenceId.ToString("D"),
            regionId,
            language,
            scenes,
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<string> ValidateScenePlan(QuestionDrivenScenePlanDto plan)
    {
        var issues = new List<string>();
        if (plan.Scenes.Count == 0)
        {
            issues.Add("Scene plan must include at least one scene.");
            return issues;
        }

        foreach (var scene in plan.Scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.ViewerQuestion)) issues.Add($"Scene {scene.SceneNumber} must have viewerQuestion.");
            if (string.IsNullOrWhiteSpace(scene.ViewerTakeaway)) issues.Add($"Scene {scene.SceneNumber} must have viewerTakeaway.");
            if (string.IsNullOrWhiteSpace(scene.VisualIntent)) issues.Add($"Scene {scene.SceneNumber} must have visualIntent.");
            if (string.IsNullOrWhiteSpace(scene.NarrationIntent)) issues.Add($"Scene {scene.SceneNumber} must have narrationIntent.");
        }

        var duplicatePurposes = plan.Scenes
            .GroupBy(s => s.ScenePurpose, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => g.Key);
        foreach (var duplicate in duplicatePurposes)
            issues.Add($"Scene purpose '{duplicate}' must not be duplicated.");

        if (!string.Equals(plan.Scenes.First().QuestionType, AstronomyQuestionTypes.What, StringComparison.OrdinalIgnoreCase))
            issues.Add("What must be first.");
        if (!string.Equals(plan.Scenes.Last().QuestionType, AstronomyQuestionTypes.Action, StringComparison.OrdinalIgnoreCase))
            issues.Add("Action must be last.");

        return issues;
    }

    private static string BuildVisualIntent(string questionType, string scenePurpose, string sourceAnswer, bool isMeteorShower)
    {
        if (isMeteorShower)
        {
            var motif = "meteor streaks, dark sky, radiant hint, open local landscape, clean cinematic astronomy style, no unrelated planets";
            return questionType switch
            {
                AstronomyQuestionTypes.What => $"Hook — meteor shower peak alert with {motif}: {sourceAnswer}",
                AstronomyQuestionTypes.When => $"Best time — midnight to pre-dawn timing card over a meteor-filled dark sky: {sourceAnswer}",
                AstronomyQuestionTypes.Where => $"Where to look — east-to-overhead sky guide with radiant hint and meteor streaks: {sourceAnswer}",
                AstronomyQuestionTypes.How => $"Viewing tips — dark open location, no telescope, low moon interference, meteor streaks overhead: {sourceAnswer}",
                AstronomyQuestionTypes.Why => $"Why special — cinematic meteor shower explanation with radiant/debris stream context: {sourceAnswer}",
                AstronomyQuestionTypes.Action => $"Reminder CTA — viewer reminder over dark open landscape and meteor streaks, no unrelated planets: {sourceAnswer}",
                _ => $"Meteor shower scene with {motif}: {sourceAnswer}"
            };
        }

        return questionType switch
        {
            AstronomyQuestionTypes.What => $"Open with a clear overview visual that establishes the sky event: {sourceAnswer}",
            AstronomyQuestionTypes.Where => $"Show a location or sky-direction guide that helps viewers know where to look: {sourceAnswer}",
            AstronomyQuestionTypes.When => $"Show a timing guide focused on the best local viewing window: {sourceAnswer}",
            AstronomyQuestionTypes.How => $"Show a practical observation guide with simple finding steps: {sourceAnswer}",
            AstronomyQuestionTypes.Why => $"Show a significance visual that emphasizes why the event matters: {sourceAnswer}",
            AstronomyQuestionTypes.Action => $"Close with a simple viewer action cue: {sourceAnswer}",
            _ => $"Support the {scenePurpose} scene with visuals based on: {sourceAnswer}"
        };
    }

    private static string BuildNarrationIntent(string questionType, string scenePurpose, string sourceAnswer, bool isMeteorShower)
    {
        var prefix = isMeteorShower ? "Use story-driven meteor-shower narration with the generated question answer; include what viewers see, when and where to look, no telescope, moon conditions, and CTA. " : string.Empty;
        return questionType switch
        {
            AstronomyQuestionTypes.What => $"{prefix}Plan narration to introduce the event using the approved answer without drafting final narration: {sourceAnswer}",
            AstronomyQuestionTypes.Where => $"{prefix}Plan narration to orient viewers toward the correct sky area without drafting final narration: {sourceAnswer}",
            AstronomyQuestionTypes.When => $"{prefix}Plan narration to explain the best viewing time without drafting final narration: {sourceAnswer}",
            AstronomyQuestionTypes.How => $"{prefix}Plan narration to turn the observation answer into simple guidance without drafting final narration: {sourceAnswer}",
            AstronomyQuestionTypes.Why => $"{prefix}Plan narration to explain the significance without drafting final narration: {sourceAnswer}",
            AstronomyQuestionTypes.Action => $"{prefix}Plan narration to end with the approved viewer action without drafting final narration: {sourceAnswer}",
            _ => $"{prefix}Plan narration intent for {scenePurpose} without drafting final narration: {sourceAnswer}"
        };
    }

    private static void ValidateRequest(QuestionScenePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RegionId))
            throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.EventId))
            throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("language is required.", nameof(request));
    }

    private string BuildOutputPath(Guid eventId, string regionId, ProductionPipelineExecutionContext? productionContext)
        => Path.Combine(!string.IsNullOrWhiteSpace(productionContext?.QuestionRoot)
            ? productionContext!.QuestionRoot!
            : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", eventId.ToString("D"), "question-engine"),
            "question-driven-scene-plan.json");

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
}
