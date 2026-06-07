using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionDrivenNarrationGenerator(
    IOptions<RenderingOptions> renderingOptions,
    ILogger<QuestionDrivenNarrationGenerator> logger) : IQuestionDrivenNarrationGenerator
{
    private const string GoldenEventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string GoldenRegionId = "IN-RJ-UDAIPUR";
    private const string GoldenLanguage = "en";
    private const string InputFileName = "question-driven-scene-plan.enriched.json";
    private const string NarrationFileName = "question-driven-narration.json";
    private const string ReviewFileName = "question-driven-narration-review.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] InternalTerms = ["question engine", "scene purpose", "metadata", "json", "source answer"];

    private static readonly IReadOnlyDictionary<string, NarrationTemplate> Templates = new Dictionary<string, NarrationTemplate>(StringComparer.OrdinalIgnoreCase)
    {
        [AstronomyQuestionTypes.What] = new(
            "OpeningHook",
            "In Udaipur tonight, Venus and Jupiter gather close in the evening sky, giving the west a bright, easy-to-spot planetary moment.",
            10,
            "Warm, inviting, with a gentle sense of wonder.",
            "Venus and Jupiter shine close tonight."),
        [AstronomyQuestionTypes.Where] = new(
            "LocationOrientation",
            "Face the western horizon. Look roughly one-third of the way up, where the twilight fades and the two bright points begin to stand out.",
            10,
            "Clear and practical, like guiding a friend outdoors.",
            "Look west, about one-third above the horizon."),
        [AstronomyQuestionTypes.When] = new(
            "LocalTiming",
            "The sweet spot is around 7:23 PM IST, shortly after sunset, while the sky is dim enough for both planets to pop.",
            9,
            "Calm and precise, emphasizing local time.",
            "Best around 7:23 PM IST after sunset."),
        [AstronomyQuestionTypes.How] = new(
            "ObservationSteps",
            "Start with Venus, the brighter beacon. Once you have it, scan nearby for Jupiter, softer but still bright in the same patch of sky.",
            10,
            "Encouraging and step-by-step.",
            "Find Venus first, then Jupiter nearby."),
        [AstronomyQuestionTypes.Why] = new(
            "ViewingSignificance",
            "What makes this special is the pairing: two of the sky’s brightest planets appearing close enough to feel like a shared evening signal.",
            10,
            "Reflective, appreciative, and lightly cinematic.",
            "Two bright planets make a rare-looking pair."),
        [AstronomyQuestionTypes.Action] = new(
            "ClosingCallToAction",
            "So if the clouds stay away, step outside this evening, look west, and give yourself a minute with Venus and Jupiter.",
            9,
            "Warm closing call-to-action, simple and sincere.",
            "Clear skies? Step outside and look west.")
    };

    public async Task<QuestionDrivenNarrationResponse> GenerateQuestionDrivenNarrationAsync(QuestionDrivenNarrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new List<string>();
        var inputPath = BuildPlanPath(request.EventId, request.RegionId, InputFileName);
        var narrationPath = BuildPlanPath(request.EventId, request.RegionId, NarrationFileName);
        var reviewPath = BuildPlanPath(request.EventId, request.RegionId, ReviewFileName);

        if (!File.Exists(inputPath))
            throw new ArgumentException($"Approved enriched question-driven scene plan was not found at '{inputPath.Replace('\\', '/')}'.", nameof(request));

        if (!request.DryRun && !request.OverwriteExisting && File.Exists(narrationPath) && File.Exists(reviewPath))
        {
            var existingNarration = JsonSerializer.Deserialize<QuestionDrivenNarrationDto>(await File.ReadAllTextAsync(narrationPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing question-driven narration could not be parsed.");
            var existingReview = JsonSerializer.Deserialize<QuestionDrivenNarrationReviewDto>(await File.ReadAllTextAsync(reviewPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing question-driven narration review could not be parsed.");
            warnings.Add("Question-driven narration already exists; returning the existing files because overwriteExisting is false.");
            return BuildResponse(existingNarration, existingReview, [narrationPath.Replace('\\', '/'), reviewPath.Replace('\\', '/')], warnings);
        }

        var inputJson = await File.ReadAllTextAsync(inputPath, cancellationToken);
        var enrichedPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(inputJson, JsonOptions)
            ?? throw new ArgumentException("Enriched question-driven scene plan could not be parsed.", nameof(request));

        var narration = BuildNarration(enrichedPlan, request);
        var review = BuildReview(narration, warnings);
        if (!review.IsValid)
        {
            warnings.AddRange(review.Checks.Where(check => !check.Passed).Select(check => check.Message));
            logger.LogWarning("Question-driven narration validation failed for EventId={EventId}. Issues={Issues}", request.EventId, string.Join(" | ", warnings));
            return BuildResponse(narration, review with { Warnings = warnings.ToArray() }, [], warnings);
        }

        if (request.DryRun)
            return BuildResponse(narration, review, [], warnings);

        Directory.CreateDirectory(Path.GetDirectoryName(narrationPath)!);
        await File.WriteAllTextAsync(narrationPath, JsonSerializer.Serialize(narration, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);

        return BuildResponse(narration, review, [narrationPath.Replace('\\', '/'), reviewPath.Replace('\\', '/')], warnings);
    }

    private static QuestionDrivenNarrationResponse BuildResponse(
        QuestionDrivenNarrationDto narration,
        QuestionDrivenNarrationReviewDto review,
        IReadOnlyList<string> generatedFiles,
        IReadOnlyList<string> warnings)
        => new(narration.EventId, narration.Scenes.Count, narration.TotalEstimatedDurationSeconds, review.IsValid, narration, review, generatedFiles, warnings);

    private static QuestionDrivenNarrationDto BuildNarration(EnrichedQuestionScenePlanDto enrichedPlan, QuestionDrivenNarrationRequest request)
    {
        var scenes = enrichedPlan.Scenes.Select(scene =>
        {
            if (!Templates.TryGetValue(scene.QuestionType, out var template))
                throw new ArgumentException($"No narration template exists for questionType '{scene.QuestionType}'.");

            return new QuestionDrivenNarrationSceneDto(
                scene.SceneNumber,
                Clean(scene.QuestionType),
                Clean(scene.ScenePurpose),
                Clean(scene.ViewerQuestion),
                Clean(scene.ViewerTakeaway),
                Clean(scene.SourceAnswer),
                Clean(scene.NarrationIntent),
                template.NarrationText,
                template.EstimatedDurationSeconds,
                template.VoiceDirection,
                template.CaptionText);
        }).ToArray();

        return new QuestionDrivenNarrationDto(
            Clean(enrichedPlan.EventId) == string.Empty ? request.EventId : Clean(enrichedPlan.EventId),
            Clean(enrichedPlan.RegionId) == string.Empty ? request.RegionId : Clean(enrichedPlan.RegionId),
            string.IsNullOrWhiteSpace(enrichedPlan.Language) ? request.Language : enrichedPlan.Language,
            scenes,
            scenes.Sum(scene => scene.EstimatedDurationSeconds),
            DateTimeOffset.UtcNow);
    }

    private static QuestionDrivenNarrationReviewDto BuildReview(QuestionDrivenNarrationDto narration, IReadOnlyList<string> warnings)
    {
        var checks = new List<QuestionDrivenNarrationReviewCheckDto>();
        AddCheck(checks, "narrationTextNonEmpty", narration.Scenes.All(scene => !string.IsNullOrWhiteSpace(scene.NarrationText)), "narrationText non-empty for every scene.");
        AddCheck(checks, "captionTextNonEmpty", narration.Scenes.All(scene => !string.IsNullOrWhiteSpace(scene.CaptionText)), "captionText non-empty for every scene.");
        AddCheck(checks, "positiveDurations", narration.Scenes.All(scene => scene.EstimatedDurationSeconds > 0), "estimatedDurationSeconds > 0 for every scene.");
        AddCheck(checks, "targetDuration", narration.TotalEstimatedDurationSeconds is >= 45 and <= 70, "total duration must be between 45 and 70 seconds.");
        AddCheck(checks, "noDuplicateNarration", narration.Scenes.Select(scene => Clean(scene.NarrationText)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == narration.Scenes.Count, "no duplicate narration lines.");
        AddCheck(checks, "notSourceAnswerCopies", narration.Scenes.All(scene => !string.Equals(Clean(scene.NarrationText), Clean(scene.SourceAnswer), StringComparison.OrdinalIgnoreCase)), "narrationText must not exactly copy sourceAnswer.");
        AddCheck(checks, "noInternalTerms", narration.Scenes.All(SceneHasNoInternalTerms), "narration and captions must not contain internal/debug terms.");
        AddCheck(checks, "actionLast", string.Equals(narration.Scenes.LastOrDefault()?.QuestionType, AstronomyQuestionTypes.Action, StringComparison.OrdinalIgnoreCase), "action scene is last.");
        AddCheck(checks, "whatFirst", string.Equals(narration.Scenes.FirstOrDefault()?.QuestionType, AstronomyQuestionTypes.What, StringComparison.OrdinalIgnoreCase), "what scene is first.");
        AddCheck(checks, "oneQuestionPerScene", narration.Scenes.Select(scene => scene.QuestionType).Distinct(StringComparer.OrdinalIgnoreCase).Count() == narration.Scenes.Count, "each scene focuses on exactly one question type.");
        AddCheck(checks, "captionsShorterThanNarration", narration.Scenes.All(scene => scene.CaptionText.Length < scene.NarrationText.Length), "caption text should be shorter than narration text.");

        return new QuestionDrivenNarrationReviewDto(
            narration.EventId,
            narration.RegionId,
            narration.Language,
            checks.All(check => check.Passed),
            narration.Scenes.Count,
            narration.TotalEstimatedDurationSeconds,
            checks,
            warnings,
            DateTimeOffset.UtcNow);
    }

    private static bool SceneHasNoInternalTerms(QuestionDrivenNarrationSceneDto scene)
    {
        var combined = $"{scene.NarrationText} {scene.CaptionText} {scene.VoiceDirection}";
        return InternalTerms.All(term => !combined.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddCheck(List<QuestionDrivenNarrationReviewCheckDto> checks, string name, bool passed, string message)
        => checks.Add(new QuestionDrivenNarrationReviewCheckDto(name, passed, message));

    private static void ValidateRequest(QuestionDrivenNarrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
            throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegionId))
            throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("language is required.", nameof(request));
        if (!string.Equals(request.EventId, GoldenEventId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.RegionId, GoldenRegionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Language, GoldenLanguage, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Question-driven narration generation is enabled only for the approved golden pilot event e7013ee4-55c6-4f01-b1d0-7c500f26f98b / IN-RJ-UDAIPUR / en.", nameof(request));
    }

    private string BuildPlanPath(string eventId, string regionId, string fileName)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine", fileName);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private sealed record NarrationTemplate(
        string NarrationIntent,
        string NarrationText,
        int EstimatedDurationSeconds,
        string VoiceDirection,
        string CaptionText);
}
