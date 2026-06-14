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
    private const string InputFileName = "question-driven-scene-plan.enriched.json";
    private const string NarrationFileName = "question-driven-narration-v2.json";
    private const string ReviewFileName = "question-driven-narration-review-v2.json";
    private const string LegacyNarrationFileName = "question-driven-narration.json";
    private const string LegacyReviewFileName = "question-driven-narration-review.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] InternalTerms = ["question engine", "scene purpose", "metadata", "json", "source answer"];
    private static readonly string[] MeteorShowerForbiddenLeakageTerms = ["Venus", "Jupiter", "conjunction", "after sunset", "look west", "7:23 PM IST", "western horizon", "planet pairing", "object pairing"];

    private static readonly IReadOnlyDictionary<string, NarrationTemplate> Templates = new Dictionary<string, NarrationTemplate>(StringComparer.OrdinalIgnoreCase)
    {
        [AstronomyQuestionTypes.What] = new(
            "Hook",
            "Tonight, the sky is setting up one of those moments that makes you stop, look up, and stay a little longer.",
            8,
            "Immediate, cinematic, and human; open with wonder rather than a list of facts.",
            "Tonight's sky moment begins.",
            "Hook",
            "Hero"),
        [AstronomyQuestionTypes.Where] = new(
            "ViewingAdvice",
            "Once you are outside, use the horizon as your map and let the brightest landmarks guide your eyes into the right patch of sky.",
            9,
            "Practical and reassuring, like guiding a friend outdoors.",
            "Use the horizon as your map.",
            "WhereToLook",
            "ConjunctionOverlay"),
        [AstronomyQuestionTypes.When] = new(
            "Explanation",
            "The timing matters because the view changes quickly; a small window can make the difference between a faint sight and a memorable one.",
            9,
            "Calm and precise while keeping the story moving.",
            "Timing shapes the view.",
            "Explanation",
            "TimingScene"),
        [AstronomyQuestionTypes.How] = new(
            "Reward",
            "Give your eyes a minute to adjust; the reward is not just spotting the event, but watching the scene slowly reveal itself.",
            9,
            "Encouraging, sensory, and step-by-step.",
            "Let the sky reveal itself.",
            "Reward",
            "RewardScene"),
        [AstronomyQuestionTypes.Why] = new(
            "Curiosity",
            "Here is why it is worth your attention: this is not just another dot in the sky, it is a short-lived alignment of timing, motion, and perspective.",
            10,
            "Curious, appreciative, and lightly cinematic.",
            "A brief alignment of timing and motion.",
            "Curiosity",
            "SignificanceScene"),
        [AstronomyQuestionTypes.Action] = new(
            "CTA",
            "If the sky is clear, save the viewing window, step outside, and follow for more sky events you can actually see.",
            8,
            "Warm closing call-to-action, simple and sincere.",
            "Save the window and follow for more.",
            "CTA",
            "ClosingScene")
    };

    public async Task<QuestionDrivenNarrationResponse> GenerateQuestionDrivenNarrationAsync(QuestionDrivenNarrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new List<string>();
        var inputPath = BuildPlanPath(request.EventId, request.RegionId, InputFileName, request.ProductionContext);
        var narrationPath = BuildPlanPath(request.EventId, request.RegionId, NarrationFileName, request.ProductionContext);
        var reviewPath = BuildPlanPath(request.EventId, request.RegionId, ReviewFileName, request.ProductionContext);
        var legacyNarrationPath = BuildPlanPath(request.EventId, request.RegionId, LegacyNarrationFileName, request.ProductionContext);
        var legacyReviewPath = BuildPlanPath(request.EventId, request.RegionId, LegacyReviewFileName, request.ProductionContext);

        if (!File.Exists(inputPath))
            throw new ArgumentException($"Approved enriched question-driven scene plan was not found at '{inputPath.Replace('\\', '/')}'.", nameof(request));

        if (!request.DryRun && !request.OverwriteExisting && File.Exists(narrationPath) && File.Exists(reviewPath))
        {
            var existingNarration = JsonSerializer.Deserialize<QuestionDrivenNarrationDto>(await File.ReadAllTextAsync(narrationPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing question-driven narration could not be parsed.");
            var existingReview = JsonSerializer.Deserialize<QuestionDrivenNarrationReviewDto>(await File.ReadAllTextAsync(reviewPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing question-driven narration review could not be parsed.");
            if (!File.Exists(legacyNarrationPath))
                await File.WriteAllTextAsync(legacyNarrationPath, JsonSerializer.Serialize(existingNarration, JsonOptions), cancellationToken);
            if (!File.Exists(legacyReviewPath))
                await File.WriteAllTextAsync(legacyReviewPath, JsonSerializer.Serialize(existingReview, JsonOptions), cancellationToken);
            warnings.Add("Question-driven narration already exists; returning the existing files because overwriteExisting is false.");
            return BuildResponse(existingNarration, existingReview, [narrationPath.Replace('\\', '/'), reviewPath.Replace('\\', '/'), legacyNarrationPath.Replace('\\', '/'), legacyReviewPath.Replace('\\', '/')], warnings);
        }

        var inputJson = await File.ReadAllTextAsync(inputPath, cancellationToken);
        var enrichedPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(inputJson, JsonOptions)
            ?? throw new ArgumentException("Enriched question-driven scene plan could not be parsed.", nameof(request));

        var narration = BuildNarration(enrichedPlan, request);
        ValidateNarrationHasNoForbiddenLeakage(narration, request.ProductionContext);
        var review = BuildReview(narration, warnings, request.ProductionContext);
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
        await File.WriteAllTextAsync(legacyNarrationPath, JsonSerializer.Serialize(narration, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(legacyReviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);

        return BuildResponse(narration, review, [narrationPath.Replace('\\', '/'), reviewPath.Replace('\\', '/'), legacyNarrationPath.Replace('\\', '/'), legacyReviewPath.Replace('\\', '/')], warnings);
    }

    private static QuestionDrivenNarrationResponse BuildResponse(
        QuestionDrivenNarrationDto narration,
        QuestionDrivenNarrationReviewDto review,
        IReadOnlyList<string> generatedFiles,
        IReadOnlyList<string> warnings)
        => new(narration.EventId, narration.Scenes.Count, narration.TotalEstimatedDurationSeconds, review.IsValid, narration, review, generatedFiles, warnings);

    private static QuestionDrivenNarrationDto BuildNarration(EnrichedQuestionScenePlanDto enrichedPlan, QuestionDrivenNarrationRequest request)
    {
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var isProductionStrategyDriven = request.ProductionContext is not null && intelligence is not null;
        var scenes = enrichedPlan.Scenes.Select(scene =>
        {
            if (!Templates.TryGetValue(scene.QuestionType, out var template))
                throw new ArgumentException($"No narration template exists for questionType '{scene.QuestionType}'.");

            var strategyNarration = isProductionStrategyDriven
                ? BuildStrategyDrivenNarration(scene, intelligence!, request.ProductionContext!)
                : null;

            return new QuestionDrivenNarrationSceneDto(
                scene.SceneNumber,
                Clean(scene.QuestionType),
                Clean(scene.ScenePurpose),
                Clean(scene.ViewerQuestion),
                Clean(strategyNarration?.ViewerTakeaway ?? scene.ViewerTakeaway),
                Clean(strategyNarration?.SourceAnswer ?? scene.SourceAnswer),
                Clean(strategyNarration?.NarrationIntent ?? scene.NarrationIntent),
                strategyNarration?.NarrationText ?? template.NarrationText,
                strategyNarration?.EstimatedDurationSeconds ?? template.EstimatedDurationSeconds,
                strategyNarration?.VoiceDirection ?? template.VoiceDirection,
                strategyNarration?.CaptionText ?? template.CaptionText,
                template.Section,
                template.SceneType);
        }).ToArray();

        return new QuestionDrivenNarrationDto(
            Clean(enrichedPlan.EventId) == string.Empty ? request.EventId : Clean(enrichedPlan.EventId),
            Clean(enrichedPlan.RegionId) == string.Empty ? request.RegionId : Clean(enrichedPlan.RegionId),
            string.IsNullOrWhiteSpace(enrichedPlan.Language) ? request.Language : enrichedPlan.Language,
            scenes,
            scenes.Sum(scene => scene.EstimatedDurationSeconds),
            DateTimeOffset.UtcNow);
    }

    private static StrategyNarrationLine? BuildStrategyDrivenNarration(EnrichedQuestionSceneDto scene, ProductionEventIntelligence intelligence, ProductionPipelineExecutionContext context)
    {
        if (IsMeteorShower(intelligence, context))
            return BuildMeteorShowerNarration(scene, intelligence);

        var title = Clean(intelligence.Title, "this astronomy event");
        var window = FirstNonEmpty(intelligence.BestViewingWindowLocal, intelligence.PreferredViewingWindow, intelligence.LocalPeakTime, "the best local viewing window");
        var direction = FirstNonEmpty(intelligence.SkyDirectionHint, "the relevant part of the sky");
        var objects = FormatList((intelligence.ResolvedObjectNames ?? intelligence.PrimaryObjects).Where(o => !string.IsNullOrWhiteSpace(o)).ToArray(), title);
        var source = Clean(scene.SourceAnswer);
        var fact = source.Length == 0 || ContainsAny(source, intelligence.ForbiddenTerms.Concat(intelligence.ForbiddenObjectNames ?? []))
            ? $"{title} is the approved production event for {context.RegionId ?? intelligence.VisibilityRegion ?? "the selected region"}."
            : source;

        return scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => Line($"Tonight opens with {title}, a sky moment centered on {objects} that is worth catching before it passes.", $"{title}: what to watch.", fact),
            "where" => Line($"For the best view, turn toward {direction} and let the brightest part of the scene guide your eyes.", $"Look toward {direction}.", $"Sky direction: {direction}."),
            "when" => Line($"Your best chance comes during {window}, when the timing gives the event its strongest view.", $"Best window: {window}.", $"Best viewing window: {window}."),
            "how" => Line($"Make it simple: {FormatList(intelligence.ViewerInstructions, "check the sky conditions, choose a clear view, and observe safely")}.", "Use the observing guidance.", string.Join(' ', intelligence.ViewerInstructions)),
            "why" => Line($"This matters because {FirstNonEmpty(intelligence.ScientificContext, title + " has strong local skywatching value")}.", "Why this event matters.", FirstNonEmpty(intelligence.ScientificContext, title)),
            "action" => Line($"If conditions cooperate, save the {window} window, check weather, step outside, and follow for more sky events.", "Save the window and check weather.", $"CTA for {title}: {window}."),
            _ => Line(fact, ShortenCaption(fact), fact)
        };
    }

    private static StrategyNarrationLine BuildMeteorShowerNarration(EnrichedQuestionSceneDto scene, ProductionEventIntelligence intelligence)
    {
        var title = Clean(intelligence.Title, "Meteor Shower");
        var window = FirstNonEmpty(intelligence.BestViewingWindowLocal, intelligence.PreferredViewingWindow, "midnight to pre-dawn");
        var direction = FirstNonEmpty(intelligence.SkyDirectionHint, "east to overhead after 10 PM");
        var moon = FirstNonEmpty(intelligence.MoonInterference, "low moon interference");
        var source = scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => $"{title} peaks with meteor streaks from the shower radiant across the whole sky.",
            "where" => $"Look {direction}; meteors can appear anywhere across a dark open sky.",
            "when" => $"Best viewing is {window}, when the sky is darkest.",
            "how" => "No telescope is needed; choose a dark sky, avoid bright lights, and let your eyes adapt.",
            "why" => $"{title} is a strong annual meteor shower with {moon.ToLowerInvariant()} improving viewing quality.",
            "action" => $"Set a reminder for {window}, check weather, and choose a dark sky location.",
            _ => $"{title}: meteor streaks, radiant, dark sky, and naked-eye viewing."
        };

        return scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => Line($"{title} is the event to watch: meteor streaks from the radiant crossing the whole dark sky.", $"{title}: meteor streaks.", source),
            "where" => Line($"Look {direction}; use a dark open sky because meteors can streak anywhere overhead.", $"Look {direction}.", source),
            "when" => Line($"Best viewing is {window}, with the darkest hours giving the shower its strongest chance.", $"Best viewing: {window}.", source),
            "how" => Line("No telescope is needed. Pick a dark sky, avoid city lights, lie back, and let your eyes adapt for 20 minutes.", "No telescope; find dark sky.", source),
            "why" => Line($"{title} is one of the strong annual meteor showers, and {moon.ToLowerInvariant()} helps keep the sky darker for meteor streaks.", $"Strong shower; {moon.ToLowerInvariant()}.", source),
            "action" => Line($"Set a reminder for {window}, check weather, and choose a dark sky spot with a wide view overhead.", "Set reminder and check weather.", source),
            _ => Line(source, ShortenCaption(source), source)
        };
    }

    private static StrategyNarrationLine Line(string narration, string caption, string sourceAnswer)
        => new(Clean(sourceAnswer), "Strategy-driven narration from ProductionEventIntelligence and MediaEventStrategy.", Clean(narration), 9, "Clear, practical, wonder-led, and locally useful.", Clean(caption), Clean(caption));

    private static QuestionDrivenNarrationReviewDto BuildReview(QuestionDrivenNarrationDto narration, IReadOnlyList<string> warnings, ProductionPipelineExecutionContext? productionContext)
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
        AddCheck(checks, "noForbiddenUnrelatedTerms", !NarrationContainsForbiddenLeakage(narration, productionContext, out _), "narration plan must not contain forbidden unrelated event terms.");
        AddCheck(checks, "hookExists", narration.Scenes.Any(scene => string.Equals(scene.Section, "Hook", StringComparison.OrdinalIgnoreCase)), "hook section exists.");
        AddCheck(checks, "ctaExists", narration.Scenes.Any(scene => string.Equals(scene.Section, "CTA", StringComparison.OrdinalIgnoreCase)), "CTA section exists.");
        AddCheck(checks, "storyStructureComplete", new[] { "Hook", "Curiosity", "Explanation", "ViewingAdvice", "Reward", "CTA" }.All(required => narration.Scenes.Any(scene => string.Equals(scene.Section, required, StringComparison.OrdinalIgnoreCase))), "story structure includes Hook, Curiosity, Explanation, ViewingAdvice, Reward, and CTA.");
        AddCheck(checks, "sceneTypeMapped", narration.Scenes.All(scene => !string.IsNullOrWhiteSpace(scene.Section) && !string.IsNullOrWhiteSpace(scene.SceneType)), "every narration section maps to a scene type.");
        AddCheck(checks, "noRepetitiveSentenceOpenings", HasVariedSentenceOpenings(narration), "no repetitive sentence openings.");
        AddCheck(checks, "noRoboticPhrasing", narration.Scenes.All(scene => !ContainsAny(scene.NarrationText, new[] { "based on the current", "approved production", "source answer", "metadata" })), "narration avoids robotic or internal phrasing.");

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

    private static void ValidateNarrationHasNoForbiddenLeakage(QuestionDrivenNarrationDto narration, ProductionPipelineExecutionContext? productionContext)
    {
        if (NarrationContainsForbiddenLeakage(narration, productionContext, out var hits))
            throw new InvalidOperationException("Question-driven narration validation failed: forbidden unrelated terms detected: " + string.Join(", ", hits.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static bool NarrationContainsForbiddenLeakage(QuestionDrivenNarrationDto narration, ProductionPipelineExecutionContext? productionContext, out IReadOnlyList<string> hits)
    {
        var forbidden = BuildForbiddenLeakageTerms(productionContext);
        var combined = string.Join(' ', narration.Scenes.Select(scene => $"{scene.SourceAnswer} {scene.ViewerTakeaway} {scene.NarrationIntent} {scene.NarrationText} {scene.CaptionText}"));
        hits = forbidden.Where(term => ContainsToken(combined, term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return hits.Count > 0;
    }

    private static IReadOnlyList<string> BuildForbiddenLeakageTerms(ProductionPipelineExecutionContext? productionContext)
    {
        var intelligence = productionContext?.ProductionEventIntelligence;
        var terms = new List<string>();
        if (intelligence is not null)
        {
            terms.AddRange(intelligence.ForbiddenTerms);
            terms.AddRange(intelligence.ForbiddenObjectNames ?? []);
            if (IsMeteorShower(intelligence, productionContext)) terms.AddRange(MeteorShowerForbiddenLeakageTerms);
        }
        return terms.Where(term => !string.IsNullOrWhiteSpace(term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasVariedSentenceOpenings(QuestionDrivenNarrationDto narration)
    {
        var openings = narration.Scenes
            .Select(scene => Clean(scene.NarrationText).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2))
            .Select(words => string.Join(' ', words).ToLowerInvariant())
            .Where(opening => opening.Length > 0)
            .ToArray();
        return openings.Distinct(StringComparer.OrdinalIgnoreCase).Count() == openings.Length;
    }

    private static bool SceneHasNoInternalTerms(QuestionDrivenNarrationSceneDto scene)
    {
        var combined = $"{scene.NarrationText} {scene.CaptionText} {scene.VoiceDirection}";
        return InternalTerms.All(term => !combined.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddCheck(List<QuestionDrivenNarrationReviewCheckDto> checks, string name, bool passed, string message)
        => checks.Add(new QuestionDrivenNarrationReviewCheckDto(name, passed, message));

    private static bool IsMeteorShower(ProductionEventIntelligence intelligence, ProductionPipelineExecutionContext? context)
        => (context?.EventType ?? intelligence.EventType ?? string.Empty).Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || (context?.MediaEventStrategy?.EventType ?? intelligence.StrategyId ?? string.Empty).Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || intelligence.Title.Contains("meteor", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, IEnumerable<string> terms)
        => terms.Any(term => ContainsToken(value, term));

    private static bool ContainsToken(string value, string? term)
        => !string.IsNullOrWhiteSpace(term) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string FormatList(IEnumerable<string> values, string fallback)
    {
        var clean = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return clean.Length == 0 ? fallback : string.Join(", ", clean);
    }

    private static string ShortenCaption(string value)
    {
        value = Clean(value);
        if (value.Length <= 52) return value;
        return value[..49].TrimEnd() + "...";
    }

    private sealed record StrategyNarrationLine(string SourceAnswer, string NarrationIntent, string NarrationText, int EstimatedDurationSeconds, string VoiceDirection, string CaptionText, string ViewerTakeaway);

    private void ValidateRequest(QuestionDrivenNarrationRequest request)
    {
        var diagnostics = BuildNarrationRequestDiagnostics(request);
        if (!diagnostics.EventIdPresent || !diagnostics.RegionIdPresent || !diagnostics.LanguagePresent || (request.ProductionContext is not null && !diagnostics.PlanIdPresent))
            throw new ArgumentException("Question-driven narration generation requires a valid event id, region id, and language for dynamic Astronomy V1 production. " + FormatDiagnostics(diagnostics), nameof(request));

        if (IsDbApprovedAstronomyV1ProductionPlan(request) || request.ProductionContext is null)
            return;

        throw new ArgumentException("Question-driven narration generation requires a valid DB-approved Astronomy V1 production plan. " + FormatDiagnostics(diagnostics), nameof(request));
    }

    private bool IsDbApprovedAstronomyV1ProductionPlan(QuestionDrivenNarrationRequest request)
    {
        var context = request.ProductionContext;
        var diagnostics = BuildNarrationRequestDiagnostics(request);
        var hasShortOrLongVideoOutput = context?.RequestedOutputs?.Any(output =>
            string.Equals(output, "ShortVideo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(output, "LongVideo", StringComparison.OrdinalIgnoreCase)) == true;

        var useProductionPipeline = context?.UseProductionPipeline == true;
        var isDbApprovedPlanExecution = context?.IsDbApprovedPlanExecution == true;
        var contentGenerationPlanExists = diagnostics.PlanIdPresent;
        var astronomyEventIntelligenceExists = diagnostics.EventIdPresent;
        var autoGenerateAllowed = context?.AutoGenerateAllowed == true;
        var verificationStatusAllowed = !string.Equals(context?.VerificationStatus, "NeedsManualReview", StringComparison.OrdinalIgnoreCase);
        var contentStrategyAllowed = !string.Equals(context?.ContentStrategy, "SkipAutoGeneration", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context?.ContentStrategy, "EducationalOnly", StringComparison.OrdinalIgnoreCase);
        var categoryAllowed = string.Equals(context?.Category, "RareEventAlert", StringComparison.OrdinalIgnoreCase);

        logger.LogInformation(
            "Question-driven narration DB-approved Astronomy V1 eligibility for EventId={EventId}: ContentGenerationPlanId={ContentGenerationPlanId}, AstronomyEventIntelligenceId={AstronomyEventIntelligenceId}, SourceExternalEventId={SourceExternalEventId}, RegionId={RegionId}, Language={Language}, AutoGenerateAllowed={AutoGenerateAllowed}, VerificationStatus={VerificationStatus}, ContentStrategy={ContentStrategy}, Category={Category}, PlannedFormat={PlannedFormat}, RequestedOutputs={RequestedOutputs}, useProductionPipeline={UseProductionPipeline}, IsDbApprovedPlanExecution={IsDbApprovedPlanExecution}, ContentGenerationPlanExistsCondition={ContentGenerationPlanExistsCondition}, AstronomyEventIntelligenceExistsCondition={AstronomyEventIntelligenceExistsCondition}, AutoGenerateAllowedCondition={AutoGenerateAllowedCondition}, VerificationStatusAllowedCondition={VerificationStatusAllowedCondition}, ContentStrategyAllowedCondition={ContentStrategyAllowedCondition}, CategoryAllowedCondition={CategoryAllowedCondition}, RequestedVideoOutputCondition={RequestedVideoOutputCondition}",
            request.EventId,
            context?.ContentGenerationPlanId,
            context?.AstronomyEventIntelligenceId,
            context?.SourceExternalEventId,
            context?.RegionId,
            context?.Language,
            context?.AutoGenerateAllowed,
            context?.VerificationStatus,
            context?.ContentStrategy,
            context?.Category,
            context?.PlannedFormat,
            context?.RequestedOutputs is null ? null : string.Join(",", context.RequestedOutputs),
            context?.UseProductionPipeline,
            context?.IsDbApprovedPlanExecution,
            contentGenerationPlanExists,
            astronomyEventIntelligenceExists,
            autoGenerateAllowed,
            verificationStatusAllowed,
            contentStrategyAllowed,
            categoryAllowed,
            hasShortOrLongVideoOutput);

        return useProductionPipeline
            && isDbApprovedPlanExecution
            && contentGenerationPlanExists
            && astronomyEventIntelligenceExists;
    }

    private static NarrationRequestDiagnostics BuildNarrationRequestDiagnostics(QuestionDrivenNarrationRequest request)
        => new(
            PlanIdPresent: (request.PlanId ?? request.ProductionContext?.ContentGenerationPlanId ?? request.ProductionContext?.ProductionExecutionContext?.ContentGenerationPlanId) is { } planId && planId != Guid.Empty,
            EventIdPresent: Guid.TryParse(request.EventId, out var eventId) && eventId != Guid.Empty,
            RegionIdPresent: !string.IsNullOrWhiteSpace(request.RegionId),
            LanguagePresent: !string.IsNullOrWhiteSpace(request.Language),
            EventType: FirstNonEmpty(request.EventType, request.ProductionContext?.EventType, request.ProductionContext?.ProductionEventIntelligence?.EventType),
            StrategyId: FirstNonEmpty(request.StrategyId, request.ProductionContext?.ProductionEventIntelligence?.StrategyId, request.ProductionContext?.MediaEventStrategy?.EventType),
            SourceOfEventId: FirstNonEmpty(request.SourceOfEventId, request.ProductionContext?.AstronomyEventIntelligenceId is not null ? "ProductionPipelineExecutionContext.AstronomyEventIntelligenceId" : null));

    private static string FormatDiagnostics(NarrationRequestDiagnostics diagnostics)
        => $"Diagnostics: planIdPresent={diagnostics.PlanIdPresent}, eventIdPresent={diagnostics.EventIdPresent}, regionIdPresent={diagnostics.RegionIdPresent}, languagePresent={diagnostics.LanguagePresent}, eventType={diagnostics.EventType ?? "<null>"}, strategyId={diagnostics.StrategyId ?? "<null>"}, sourceOfEventId={diagnostics.SourceOfEventId ?? "<null>"}.";

    private sealed record NarrationRequestDiagnostics(
        bool PlanIdPresent,
        bool EventIdPresent,
        bool RegionIdPresent,
        bool LanguagePresent,
        string? EventType,
        string? StrategyId,
        string? SourceOfEventId);

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

    private static string Clean(string? value, string fallback)
    {
        var cleaned = string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private sealed record NarrationTemplate(
        string NarrationIntent,
        string NarrationText,
        int EstimatedDurationSeconds,
        string VoiceDirection,
        string CaptionText,
        string Section,
        string SceneType);
}
