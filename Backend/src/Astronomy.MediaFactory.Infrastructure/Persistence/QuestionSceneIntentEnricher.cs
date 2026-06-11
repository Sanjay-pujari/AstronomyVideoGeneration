using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionSceneIntentEnricher(
    IOptions<RenderingOptions> renderingOptions,
    ILogger<QuestionSceneIntentEnricher> logger) : IQuestionSceneIntentEnricher
{
    private const string InputFileName = "question-driven-scene-plan.json";
    private const string OutputFileName = "question-driven-scene-plan.enriched.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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

    private static readonly IReadOnlyDictionary<string, IntentTemplate> Templates = new Dictionary<string, IntentTemplate>(StringComparer.OrdinalIgnoreCase)
    {
        [AstronomyQuestionTypes.What] = new(
            "Understand what sky event is happening.",
            "Create curiosity and introduce the event in a warm, simple way.",
            "Show a hero astronomy scene with Venus and Jupiter clearly emphasized.",
            "Generate a cinematic western evening sky background suitable for a hero opening.",
            "Use a short title and one clear viewing cue.",
            "Even without audio, the viewer should know the event is Venus and Jupiter tonight."),
        [AstronomyQuestionTypes.Where] = new(
            "Know where in the sky to look.",
            "Orient the viewer toward the correct part of the sky.",
            "Show western horizon, direction marker, and planet positions.",
            "Generate a clean sky-location infographic background with horizon space.",
            "Use labels for West, Venus, Jupiter, and horizon.",
            "Muted viewers should understand the viewing direction."),
        [AstronomyQuestionTypes.When] = new(
            "Know the best local viewing time.",
            "Explain the viewing window using local time.",
            "Show a sunset-to-viewing-time timeline.",
            "Generate a calm twilight-to-night timing visual with space for a time marker.",
            "Show best time in IST and shortly-after-sunset cue.",
            "Muted viewers should understand when to go outside."),
        [AstronomyQuestionTypes.How] = new(
            "Know how to find the planets.",
            "Give simple step-by-step observing guidance.",
            "Show arrows or steps: find Venus first, then Jupiter nearby.",
            "Generate a practical observation-guide background with open space for arrows.",
            "Use 2–3 short steps, not long sentences.",
            "Muted viewers should understand the finding steps."),
        [AstronomyQuestionTypes.Why] = new(
            "Understand why the event is worth seeing.",
            "Explain the significance of the close planetary pairing.",
            "Show closeness, brightness, or pairing significance visually.",
            "Generate a comparison-style astronomy visual that highlights the close pairing.",
            "Use one short significance line.",
            "Muted viewers should understand why this event matters."),
        [AstronomyQuestionTypes.Action] = new(
            "Know what to do next.",
            "Close with a simple, memorable call to action.",
            "Show a beautiful closing sky with a minimal clear-sky reminder.",
            "Generate an emotional closing astronomy background with Venus and Jupiter mood.",
            "Use a short call-to-action only.",
            "Muted viewers should know to step outside after sunset.")
    };

    public async Task<QuestionSceneIntentEnrichmentResponse> EnrichQuestionScenePlanAsync(QuestionSceneIntentEnrichmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var warnings = new List<string>();
        var inputPath = BuildPlanPath(request.EventId, request.RegionId, InputFileName);
        var outputPath = BuildPlanPath(request.EventId, request.RegionId, OutputFileName);

        if (!File.Exists(inputPath))
            throw new ArgumentException($"Question-driven scene plan was not found at '{inputPath.Replace('\\', '/')}'.", nameof(request));

        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existingJson = await File.ReadAllTextAsync(outputPath, cancellationToken);
            var existingPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(existingJson, JsonOptions)
                ?? throw new InvalidOperationException("Existing enriched question-driven scene plan could not be parsed.");
            var existingIssues = ValidateEnrichedPlan(existingPlan);
            warnings.Add("Enriched question-driven scene plan already exists; returning the existing file because overwriteExisting is false.");
            warnings.AddRange(existingIssues);
            return BuildResponse(existingPlan, existingIssues.Count == 0, [outputPath.Replace('\\', '/')], warnings);
        }

        var inputJson = await File.ReadAllTextAsync(inputPath, cancellationToken);
        var sourcePlan = JsonSerializer.Deserialize<QuestionDrivenScenePlanDto>(inputJson, JsonOptions)
            ?? throw new ArgumentException("Question-driven scene plan could not be parsed.", nameof(request));

        var enrichedPlan = BuildEnrichedPlan(sourcePlan, request);
        var validationIssues = ValidateEnrichedPlan(enrichedPlan);
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

    private static EnrichedQuestionScenePlanDto BuildEnrichedPlan(QuestionDrivenScenePlanDto sourcePlan, QuestionSceneIntentEnrichmentRequest request)
    {
        var isMeteorShower = sourcePlan.Scenes.Any(scene => ContainsMeteorContext(scene.SourceAnswer) || ContainsMeteorContext(scene.ViewerTakeaway) || ContainsMeteorContext(scene.VisualIntent));
        var scenes = sourcePlan.Scenes.Select(scene =>
        {
            if (!Templates.TryGetValue(scene.QuestionType, out var defaultTemplate))
                throw new ArgumentException($"No scene intent enrichment template exists for questionType '{scene.QuestionType}'.");
            var template = isMeteorShower ? BuildMeteorTemplate(scene) : defaultTemplate;

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

        return new EnrichedQuestionScenePlanDto(
            Clean(sourcePlan.EventId) == string.Empty ? Clean(request.EventId) : Clean(sourcePlan.EventId),
            Clean(sourcePlan.RegionId) == string.Empty ? Clean(request.RegionId) : Clean(sourcePlan.RegionId),
            string.IsNullOrWhiteSpace(sourcePlan.Language) ? request.Language : sourcePlan.Language,
            NormalizeSupportedValue(request.ViewerPersona, SupportedViewerPersonas),
            NormalizeSupportedValue(request.KnowledgeLevel, SupportedKnowledgeLevels),
            scenes,
            true,
            DateTimeOffset.UtcNow);
    }

    private static IntentTemplate BuildMeteorTemplate(QuestionDrivenSceneDto scene)
    {
        var answer = Clean(scene.SourceAnswer);
        return scene.QuestionType switch
        {
            AstronomyQuestionTypes.What => new("Understand the Geminids peak-night alert.", "Create urgency for the Geminids meteor shower peak night.", "Show Geminids meteor streaks radiating from Gemini over a dark Udaipur night sky.", "Generate a cinematic dark night sky over Udaipur with meteor streaks and a subtle Gemini radiant hint.", "Use event title and peak-night cue, not generic Rare Event text.", "Muted viewers should know this is the Geminids meteor shower peak."),
            AstronomyQuestionTypes.Where => new("Know where to look for meteors.", "Orient viewers to east-to-overhead dark open sky viewing.", "Show east-to-overhead sky direction with dark open-sky context and Gemini radiant hint.", "Generate a dark open sky over Udaipur with east-to-overhead direction cue, meteor streaks, and subtle constellation guide.", "Use East to overhead and dark sky cues.", "Muted viewers should understand where to look."),
            AstronomyQuestionTypes.When => new("Know the midnight-to-pre-dawn viewing window.", "Explain the best local night window, not daytime peak time.", "Show midnight to pre-dawn timeline with dark sky and meteor activity.", "Generate a night timing visual with meteor streaks and the 00:00–05:00 IST viewing window.", "Show 2026-12-14 00:00–05:00 IST.", "Muted viewers should know when to watch."),
            AstronomyQuestionTypes.How => new("Know how to observe without equipment.", "Give simple meteor-shower watching steps.", "Show a viewer under dark sky, no telescope, avoiding city lights, eyes adapting.", "Generate an observer-friendly meteor shower scene with dark sky, no telescope, and low light pollution.", "Use no telescope, dark location, eyes 20 minutes.", "Muted viewers should know how to watch."),
            AstronomyQuestionTypes.Why => new("Understand why Geminids are worth seeing.", "Explain strong annual shower and low moon interference.", "Show abundant Geminids meteor streaks with a low-moon-interference quality cue.", "Generate a premium editorial meteor shower sky with many streaks, Gemini radiant hint, and low moon interference mood.", "Use strongest annual shower and low moon interference.", "Muted viewers should know why it matters."),
            AstronomyQuestionTypes.Action => new("Know the next action.", "Close with save-date reminder and weather/dark-location checklist.", "Show a save-date reminder mood for Dec 13/14 under a meteor-filled Udaipur sky.", "Generate an inspirational Geminids meteor shower CTA image with dark night sky, meteor streaks, and Udaipur viewing context.", "Use reminder, weather check, dark location.", "Muted viewers should save the Dec 13/14 night."),
            _ => new("Understand the meteor shower.", "Support the meteor shower viewing plan.", $"Show meteor shower visuals based on: {answer}", "Generate a dark night meteor shower scene with streaks and constellation context.", "Use concise meteor-specific overlay text.", "Muted viewers should understand the meteor shower scene.")
        };
    }

    private static bool ContainsMeteorContext(string? value)
        => !string.IsNullOrWhiteSpace(value) && (value.Contains("meteor", StringComparison.OrdinalIgnoreCase) || value.Contains("Geminids", StringComparison.OrdinalIgnoreCase) || value.Contains("radiant", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ValidateEnrichedPlan(EnrichedQuestionScenePlanDto plan)
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

        return issues;
    }

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

    private sealed record IntentTemplate(
        string ViewerTakeaway,
        string NarrationIntent,
        string VisualIntent,
        string ImagePromptIntent,
        string OverlayIntent,
        string AccessibilityIntent);
}
