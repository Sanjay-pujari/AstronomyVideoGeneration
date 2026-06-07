using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionScenePlanner(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
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

    public async Task<QuestionScenePlanResponse> GenerateQuestionScenePlanAsync(QuestionScenePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new List<string>();
        var questionSet = await ResolveApprovedQuestionSetAsync(request, cancellationToken);
        var outputPath = BuildOutputPath(questionSet.AstronomyEventIntelligenceId, request.RegionId);

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

    private async Task<AstronomyQuestionAnswerSet> ResolveApprovedQuestionSetAsync(QuestionScenePlanRequest request, CancellationToken cancellationToken)
    {
        var query = db.AstronomyQuestionAnswerSets
            .Include(s => s.AstronomyEventIntelligence)
            .Include(s => s.Answers)
            .AsTracking()
            .Where(s => s.RegionId == request.RegionId
                && s.Language == request.Language
                && s.Status == AstronomyQuestionSetStatus.Approved);

        query = Guid.TryParse(request.EventId, out var eventGuid)
            ? query.Where(s => s.AstronomyEventIntelligenceId == eventGuid)
            : query.Where(s => s.AstronomyEventIntelligence != null && s.AstronomyEventIntelligence.EventCode == request.EventId.Trim());

        var set = await query
            .OrderByDescending(s => s.GeneratedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return set ?? throw new ArgumentException("No approved question answer set was found for the supplied eventId, regionId, and language.", nameof(request));
    }

    private static QuestionDrivenScenePlanDto BuildScenePlan(AstronomyQuestionAnswerSet questionSet, string regionId, string language)
    {
        var answersByType = questionSet.Answers
            .GroupBy(a => a.QuestionType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.DisplayOrder).First(), StringComparer.OrdinalIgnoreCase);

        var scenes = SceneOrder.Select((questionType, index) =>
        {
            if (!answersByType.TryGetValue(questionType, out var answer))
                throw new ArgumentException($"Approved question answer set is missing the required '{questionType}' answer.");

            var scenePurpose = ScenePurposeByQuestionType[questionType];
            var sourceAnswer = Clean(answer.AnswerText);
            return new QuestionDrivenSceneDto(
                index + 1,
                questionType,
                scenePurpose,
                Clean(answer.QuestionText),
                sourceAnswer,
                sourceAnswer,
                BuildVisualIntent(questionType, scenePurpose, sourceAnswer),
                BuildNarrationIntent(questionType, scenePurpose, sourceAnswer),
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

    private static string BuildVisualIntent(string questionType, string scenePurpose, string sourceAnswer) => questionType switch
    {
        AstronomyQuestionTypes.What => $"Open with a clear overview visual that establishes the sky event: {sourceAnswer}",
        AstronomyQuestionTypes.Where => $"Show a location or sky-direction guide that helps viewers know where to look: {sourceAnswer}",
        AstronomyQuestionTypes.When => $"Show a timing guide focused on the best local viewing window: {sourceAnswer}",
        AstronomyQuestionTypes.How => $"Show a practical observation guide with simple finding steps: {sourceAnswer}",
        AstronomyQuestionTypes.Why => $"Show a significance visual that emphasizes why the event matters: {sourceAnswer}",
        AstronomyQuestionTypes.Action => $"Close with a simple viewer action cue: {sourceAnswer}",
        _ => $"Support the {scenePurpose} scene with visuals based on: {sourceAnswer}"
    };

    private static string BuildNarrationIntent(string questionType, string scenePurpose, string sourceAnswer) => questionType switch
    {
        AstronomyQuestionTypes.What => $"Plan narration to introduce the event using the approved answer without drafting final narration: {sourceAnswer}",
        AstronomyQuestionTypes.Where => $"Plan narration to orient viewers toward the correct sky area without drafting final narration: {sourceAnswer}",
        AstronomyQuestionTypes.When => $"Plan narration to explain the best viewing time without drafting final narration: {sourceAnswer}",
        AstronomyQuestionTypes.How => $"Plan narration to turn the observation answer into simple guidance without drafting final narration: {sourceAnswer}",
        AstronomyQuestionTypes.Why => $"Plan narration to explain the significance without drafting final narration: {sourceAnswer}",
        AstronomyQuestionTypes.Action => $"Plan narration to end with the approved viewer action without drafting final narration: {sourceAnswer}",
        _ => $"Plan narration intent for {scenePurpose} without drafting final narration: {sourceAnswer}"
    };

    private static void ValidateRequest(QuestionScenePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RegionId))
            throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.EventId))
            throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("language is required.", nameof(request));
    }

    private string BuildOutputPath(Guid eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", eventId.ToString("D"), "question-engine", "question-driven-scene-plan.json");

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
