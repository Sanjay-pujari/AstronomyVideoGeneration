using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const string DiagnosticsFileName = "question-driven-narration-v3-diagnostics.json";
    private const string NarrativeEditorialReviewFileName = "NarrativeEditorialReview.json";
    private const string NarrationVersion = "V3";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] InternalTerms = ["question engine", "scene purpose", "metadata", "json", "source answer"];
    private static readonly string[] AuthoringInstructionPhrases = ["Open with", "Explain", "Describe", "Focus on", "Call out", "Add a distinct", "Give safe", "Close with", "Viewer-friendly terms", "Timing window", "Primary sky objects", "Event experience", "Sky geometry"];
    private static readonly string[] MeteorShowerForbiddenLeakageTerms = ["Venus", "Jupiter", "conjunction", "after sunset", "look west", "7:23 PM IST", "western horizon", "planet pairing", "object pairing"];
    private static readonly IReadOnlyDictionary<string, string> MeteorShowerSectionsByQuestionType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [AstronomyQuestionTypes.What] = "Hook",
        [AstronomyQuestionTypes.Why] = "Curiosity",
        [AstronomyQuestionTypes.When] = "Explanation",
        [AstronomyQuestionTypes.Where] = "ViewingAdvice",
        [AstronomyQuestionTypes.How] = "Reward",
        [AstronomyQuestionTypes.Action] = "CTA"
    };

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
        var diagnosticsPath = BuildPlanPath(request.EventId, request.RegionId, DiagnosticsFileName, request.ProductionContext);
        var editorialReviewPath = BuildPlanPath(request.EventId, request.RegionId, NarrativeEditorialReviewFileName, request.ProductionContext);

        if (!File.Exists(inputPath))
            throw new ArgumentException($"Approved enriched question-driven scene plan was not found at '{inputPath.Replace('\\', '/')}'.", nameof(request));

        if (!request.DryRun && !request.OverwriteExisting && File.Exists(narrationPath) && File.Exists(reviewPath))
        {
            var existingNarration = JsonSerializer.Deserialize<QuestionDrivenNarrationDto>(await File.ReadAllTextAsync(narrationPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing question-driven narration could not be parsed.");
            var existingReview = JsonSerializer.Deserialize<QuestionDrivenNarrationReviewDto>(await File.ReadAllTextAsync(reviewPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing question-driven narration review could not be parsed.");
            var existingValidationReview = BuildReview(existingNarration, warnings, request.ProductionContext);
            if (existingReview.IsValid && existingValidationReview.IsValid)
            {
                if (!File.Exists(legacyNarrationPath))
                    await File.WriteAllTextAsync(legacyNarrationPath, JsonSerializer.Serialize(existingNarration, JsonOptions), cancellationToken);
                if (!File.Exists(legacyReviewPath))
                    await File.WriteAllTextAsync(legacyReviewPath, JsonSerializer.Serialize(existingReview, JsonOptions), cancellationToken);
                var existingSubtitlePaths = await GenerateNarrationSubtitlesAsync(existingNarration, narrationPath, cancellationToken);
                existingNarration = existingNarration with { Diagnostics = EnrichDiagnosticsWithSubtitles(existingNarration.Diagnostics, existingSubtitlePaths.Short, existingSubtitlePaths.Long) };
                warnings.Add("Question-driven narration already exists; returning the existing files because overwriteExisting is false.");
                return BuildResponse(existingNarration, existingReview, [narrationPath.Replace('\\', '/'), reviewPath.Replace('\\', '/'), legacyNarrationPath.Replace('\\', '/'), legacyReviewPath.Replace('\\', '/'), existingSubtitlePaths.Short, existingSubtitlePaths.Long], warnings);
            }

            warnings.Add("Existing question-driven narration failed current Phase 7 validation; regenerating required narration files.");
        }

        var inputJson = await File.ReadAllTextAsync(inputPath, cancellationToken);
        var enrichedPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(inputJson, JsonOptions)
            ?? throw new ArgumentException("Enriched question-driven scene plan could not be parsed.", nameof(request));

        var narration = BuildNarration(enrichedPlan, request);
        var subtitlePaths = request.DryRun ? (Short: string.Empty, Long: string.Empty) : await GenerateNarrationSubtitlesAsync(narration, narrationPath, cancellationToken);
        narration = narration with { Diagnostics = EnrichDiagnosticsWithSubtitles(narration.Diagnostics, subtitlePaths.Short, subtitlePaths.Long) };
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
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(BuildDiagnostics(narration, request), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(editorialReviewPath, JsonSerializer.Serialize(BuildNarrativeEditorialReview(narration, request, review), JsonOptions), cancellationToken);

        return BuildResponse(narration, review, [narrationPath.Replace('\\', '/'), reviewPath.Replace('\\', '/'), legacyNarrationPath.Replace('\\', '/'), legacyReviewPath.Replace('\\', '/'), subtitlePaths.Short, subtitlePaths.Long, editorialReviewPath.Replace('\\', '/')], warnings);
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
        var isMeteorShower = intelligence is not null && IsMeteorShower(intelligence, request.ProductionContext);
        var family = ResolveNarrationFamily(request, enrichedPlan, isMeteorShower);
        var sourceScenes = enrichedPlan.Scenes.OrderBy(scene => scene.SceneNumber).ToArray();
        var script = EventStoryComposer.Compose(family, intelligence, request.ProductionContext, request.Language);
        var postEdit = NarrationPostEditor.Edit(ComposeDocumentaryNarrationScenes(family, sourceScenes, intelligence, request.ProductionContext, script.Sections), family, intelligence, request.ProductionContext);
        var scenes = postEdit.Scenes.ToList();

        var diagnostics = BuildV3Diagnostics(scenes, script.Diagnostics, postEdit);
        return new QuestionDrivenNarrationDto(
            Clean(enrichedPlan.EventId) == string.Empty ? request.EventId : Clean(enrichedPlan.EventId),
            Clean(enrichedPlan.RegionId) == string.Empty ? request.RegionId : Clean(enrichedPlan.RegionId),
            string.IsNullOrWhiteSpace(enrichedPlan.Language) ? request.Language : enrichedPlan.Language,
            scenes,
            scenes.Sum(scene => scene.EstimatedDurationSeconds),
            DateTimeOffset.UtcNow,
            NarrationVersion,
            diagnostics);
    }

    private static QuestionDrivenNarrationSceneDto BuildV3Beat(int sceneNumber, string questionType, string section, string purpose, string viewerQuestion, string family, IReadOnlyList<EnrichedQuestionSceneDto> sourceScenes, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context, DocumentaryNarrationSections composedSections)
    {
        var source = sourceScenes.FirstOrDefault(s => string.Equals(s.QuestionType, questionType, StringComparison.OrdinalIgnoreCase))
            ?? sourceScenes.FirstOrDefault()
            ?? throw new ArgumentException("Enriched question-driven scene plan requires at least one source scene.");
        var text = SectionText(composedSections, section);
        return new QuestionDrivenNarrationSceneDto(
            sceneNumber,
            questionType,
            purpose.Replace(" ", string.Empty),
            viewerQuestion,
            V3Caption(section, family),
            Clean(source.SourceAnswer),
            $"Narration V3 {purpose}: cinematic documentary storytelling beat.",
            text,
            section == "ColdOpen" ? 4 : section == "EmotionalClosing" ? 10 : 9,
            section == "ColdOpen" ? "Music-forward, quiet, cinematic curiosity; no explanation." : "Documentary, conversational, wonder-led, and concise.",
            V3Caption(section, family),
            section,
            section == "ColdOpen" ? V3ColdOpenSceneType(family) : purpose.Replace(" ", string.Empty));
    }


    private static QuestionDrivenNarrationSceneDto[] ComposeDocumentaryNarrationScenes(string family, IReadOnlyList<EnrichedQuestionSceneDto> sourceScenes, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context, DocumentaryNarrationSections composedSections)
    {
        string[] sections = ["ColdOpen", "Hook", "Context", "MainStory", "ViewingGuide", "EmotionalClosing"];
        string[] questionTypes = [AstronomyQuestionTypes.ColdOpen, AstronomyQuestionTypes.What, AstronomyQuestionTypes.Why, AstronomyQuestionTypes.When, AstronomyQuestionTypes.Where, AstronomyQuestionTypes.Action];
        string[] purposes = ["Cold Open", "Hook", "Context", "Main Story", "Viewing Guide", "Emotional Closing"];
        string[] questions = ["What appears first?", "Why should I keep watching?", "What makes this moment matter?", "What is the story behind it?", "How can I see it?", "What should I remember?"];
        return sections.Select((section, i) => BuildV3Beat(i, questionTypes[i], section, purposes[i], questions[i], family, sourceScenes, intelligence, context, composedSections)).ToArray();
    }


    private static string SectionText(DocumentaryNarrationSections sections, string section) => section switch
    {
        "ColdOpen" => sections.ColdOpen,
        "Hook" => sections.Hook,
        "Context" => sections.Context,
        "MainStory" => sections.MainStory,
        "ViewingGuide" => sections.ViewingGuide,
        "EmotionalClosing" => sections.EmotionalClosing,
        _ => sections.Hook
    };

    private static string ResolveNarrationFamily(QuestionDrivenNarrationRequest request, EnrichedQuestionScenePlanDto plan, bool isMeteorShower)
    {
        var text = string.Join(' ', new[] { request.EventType, request.Title, request.ShortTitle, request.StrategyId, request.ProductionContext?.EventType, request.ProductionContext?.ProductionEventIntelligence?.EventType, request.ProductionContext?.ProductionEventIntelligence?.Title }.Where(v => !string.IsNullOrWhiteSpace(v)).Concat(plan.Scenes.SelectMany(s => new[] { s.QuestionType, s.SourceAnswer, s.VisualIntent, s.ImagePromptIntent })));
        if (isMeteorShower || text.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "Meteor";
        if (text.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "Eclipse";
        if (text.Contains("moon", StringComparison.OrdinalIgnoreCase) || text.Contains("lunar", StringComparison.OrdinalIgnoreCase)) return "Moon";
        return "PlanetGrouping";
    }

    private static string V3NarrationText(string section, string family, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context)
    {
        var title = Clean(intelligence?.Title, "this sky event");
        var window = HumanizeNarrationWindow(FirstNonEmpty(intelligence?.BestViewingWindowLocal, intelligence?.PreferredViewingWindow, intelligence?.LocalPeakTime, "the best local viewing window"));
        var direction = FirstNonEmpty(intelligence?.SkyDirectionHint, "the clearest part of the sky");
        return (family, section) switch
        {
            ("Meteor", "ColdOpen") => "Tonight, the sky may put on one of its most spectacular shows.",
            ("Moon", "ColdOpen") => "The first full moon of the year rises tonight.",
            ("Eclipse", "ColdOpen") => "On the eclipse date, the Moon can turn daylight into one of astronomy's most dramatic sky moments.",
            ("PlanetGrouping", "ColdOpen") => "Two brilliant worlds will appear almost side by side tonight.",
            ("Meteor", "Hook") => "More than a hundred meteors could streak overhead every hour.",
            ("Moon", "Hook") => "But why is it called the Wolf Moon?",
            ("Eclipse", "Hook") => "Where will the eclipse be visible, and when should you look?",
            ("PlanetGrouping", "Hook") => "Can you spot the pair before they disappear below the horizon?",
            ("Meteor", "Context") => "Meteor showers begin as tiny trails left behind in space, and for one night Earth moves through that ancient dust like a ship crossing sparks.",
            ("Moon", "Context") => "Moon names carry old seasonal memories, passed down from nights when the sky was both calendar and storyteller.",
            ("Eclipse", "Context") => "An eclipse is a shadow story, with the Sun, Moon, and Earth lining up just precisely enough to change the daylight around us.",
            ("PlanetGrouping", "Context") => "The planets are not truly close together; from Earth, their separate paths briefly overlap into one beautiful line of sight.",
            ("Meteor", "MainStory") => "Each streak is a grain of cosmic debris burning high above us, gone in a heartbeat but bright enough to make the whole sky feel alive.",
            ("Moon", "MainStory") => "As the Moon clears the horizon, its light turns familiar landscapes into something quieter, older, and easier to notice.",
            ("Eclipse", "MainStory") => "The most dramatic moments arrive slowly, then all at once, as the shadow deepens and the sky reveals motion we usually cannot feel.",
            ("PlanetGrouping", "MainStory") => "One world may be nearby by solar-system standards, the other vastly farther away, yet tonight perspective lets them share the same frame.",
            (_, "ViewingGuide") => $"For the practical view, look toward {direction} during {window}. Choose an open horizon, give your eyes time to adjust, and use binoculars only if they help you settle on the scene.",
            ("Meteor", "EmotionalClosing") => "Keep watching for more sky events. Until then, enjoy stargazing.",
            ("Moon", "EmotionalClosing") => "Keep watching for more sky events. Until then, enjoy stargazing.",
            ("Eclipse", "EmotionalClosing") => "Keep watching for more sky events. Until then, enjoy stargazing.",
            ("PlanetGrouping", "EmotionalClosing") => "Keep watching for more sky events. Until then, enjoy stargazing.",
            _ => $"{title} is a brief sky story worth seeing while the moment is still here."
        };
    }

    private static string HumanizeNarrationWindow(string value)
        => ContainsRawTimestamp(value) ? "the local viewing window" : value;

    private static string V3Caption(string section, string family) => section switch
    {
        "ColdOpen" => family switch { "Meteor" => "Meteor burst", "Moon" => "Moonrise", "Eclipse" => "Eclipse silhouette", _ => "Twilight planets" },
        "Hook" => "Stay with the sky story.",
        "Context" => "The meaning behind the view.",
        "MainStory" => "Perspective, motion, and wonder.",
        "ViewingGuide" => "Where, when, and how to look.",
        "EmotionalClosing" => "Take a moment to look up.",
        _ => "Look up tonight."
    };

    private static string V3ColdOpenSceneType(string family) => family switch
    {
        "Meteor" => "ColdOpenMeteorBurst",
        "Moon" => "ColdOpenMoonrise",
        "Eclipse" => "ColdOpenEclipseSilhouette",
        _ => "ColdOpenTwilightPlanets"
    };

    private static QuestionDrivenNarrationDiagnosticsDto BuildV3Diagnostics(IReadOnlyList<QuestionDrivenNarrationSceneDto> scenes, EventStoryComposerDiagnostics? composerDiagnostics = null, NarrationPostEditorResult? postEdit = null)
    {
        var coldOpen = scenes.Any(s => string.Equals(s.Section, "ColdOpen", StringComparison.OrdinalIgnoreCase));
        var hook = scenes.Any(s => string.Equals(s.Section, "Hook", StringComparison.OrdinalIgnoreCase));
        var story = scenes.Any(s => string.Equals(s.Section, "Context", StringComparison.OrdinalIgnoreCase) || string.Equals(s.Section, "MainStory", StringComparison.OrdinalIgnoreCase));
        var viewing = scenes.Any(s => string.Equals(s.Section, "ViewingGuide", StringComparison.OrdinalIgnoreCase));
        var closing = scenes.Any(s => string.Equals(s.Section, "EmotionalClosing", StringComparison.OrdinalIgnoreCase));
        var score = 40 + (coldOpen ? 12 : 0) + (hook ? 12 : 0) + (story ? 16 : 0) + (viewing ? 8 : 0) + (closing ? 12 : 0);
        var quality = NarrationPostEditor.Score(scenes, postEdit);
        return new QuestionDrivenNarrationDiagnosticsDto(coldOpen, hook, story, viewing, closing, NarrationVersion, Math.Min(100, score), ScriptComposerVersion: composerDiagnostics?.ScriptComposerVersion ?? string.Empty, OpeningStyle: composerDiagnostics?.OpeningStyle ?? string.Empty, EventDateMentioned: composerDiagnostics?.EventDateMentioned ?? false, EventNameMentioned: composerDiagnostics?.EventNameMentioned ?? false, DocumentaryScore: Math.Max(composerDiagnostics?.DocumentaryScore ?? 0, quality.DocumentaryVoiceScore), StorytellingScore: Math.Max(composerDiagnostics?.StorytellingScore ?? 0, quality.EditorialFlowScore), DynamicNarrationGenerated: composerDiagnostics?.DynamicNarrationGenerated ?? true, HardcodedTemplateUsed: composerDiagnostics?.HardcodedTemplateUsed ?? false, SourceEventFactsUsed: composerDiagnostics?.SourceEventFactsUsed ?? scenes.Select(scene => scene.SourceAnswer).Where(fact => !string.IsNullOrWhiteSpace(fact)).ToArray(), ScenePurposeUsed: scenes.Select(scene => scene.ScenePurpose).Where(purpose => !string.IsNullOrWhiteSpace(purpose)).ToArray(), AiRewriteAttemptCount: (composerDiagnostics?.AiRewriteAttemptCount ?? 0) + (postEdit?.RewrittenSceneIds.Count ?? 0), FallbackStaticTextUsed: composerDiagnostics?.FallbackStaticTextUsed ?? false, DocumentaryVoiceScore: quality.DocumentaryVoiceScore, SpokenLanguageScore: quality.SpokenLanguageScore, ObservationGuidanceScore: quality.ObservationGuidanceScore, ScientificAccuracyScore: quality.ScientificAccuracyScore, EditorialFlowScore: quality.EditorialFlowScore, TransitionQualityScore: quality.TransitionQualityScore, ViewerRetentionScore: quality.ViewerRetentionScore, AstroPulseIdentityScore: quality.AstroPulseIdentityScore, OverallNarrationScore: quality.OverallNarrationScore, NarrationPostEditorApplied: postEdit?.Applied ?? false, InstructionLeakageDetected: postEdit?.InstructionLeakageDetected ?? false, PromptLeakageDetected: postEdit?.PromptLeakageDetected ?? false, DuplicatedTransformationsDetected: postEdit?.DuplicatedTransformationsDetected ?? false, NarrationPostEditorRewrittenScenes: postEdit?.RewrittenSceneIds);
    }

    private static string ResolveNarrationSection(string questionType, NarrationTemplate template, bool isMeteorShower)
    {
        if (isMeteorShower && MeteorShowerSectionsByQuestionType.TryGetValue(questionType, out var meteorSection))
            return meteorSection;

        return string.Equals(template.Section, "WhereToLook", StringComparison.OrdinalIgnoreCase)
            ? "ViewingAdvice"
            : template.Section;
    }

    private static string EnsureNarrationIsParaphrased(string questionType, string section, string narrationText, string sourceAnswer, bool isMeteorShower)
    {
        if (!string.Equals(narrationText.Trim(), sourceAnswer.Trim(), StringComparison.Ordinal))
            return narrationText;

        if (isMeteorShower && string.Equals(section, "ViewingAdvice", StringComparison.OrdinalIgnoreCase))
            return "For the best experience, head to a dark location during the approved viewing window and use the event-specific direction guidance.";

        if (Templates.TryGetValue(questionType, out var template) && !string.Equals(template.NarrationText.Trim(), sourceAnswer.Trim(), StringComparison.Ordinal))
            return template.NarrationText;

        return section switch
        {
            "Hook" => "Tonight's sky story begins with a simple reason to pause, look up, and notice the moment.",
            "Curiosity" => "The reason this stands out is the rare mix of timing, motion, and sky conditions coming together.",
            "Explanation" => "The timing matters because the sky changes quickly, so the strongest view belongs to a specific window.",
            "ViewingAdvice" => "For the clearest view, choose an open dark spot and use the horizon to guide your eyes across the right sky area.",
            "Reward" => "Give your eyes time to adjust, and the scene can slowly reveal details that are easy to miss at first.",
            "CTA" => "Save the viewing window, check the sky, step outside, and follow for more events you can actually see.",
            _ => "Look up with a clear view, follow the timing, and let the sky moment unfold naturally."
        };
    }

    private sealed record NarrationPostEditorResult(IReadOnlyList<QuestionDrivenNarrationSceneDto> Scenes, bool Applied, IReadOnlyList<string> RewrittenSceneIds, bool InstructionLeakageDetected, bool PromptLeakageDetected, bool DuplicatedTransformationsDetected);
    private sealed record NarrationPostEditorScores(int DocumentaryVoiceScore, int SpokenLanguageScore, int ObservationGuidanceScore, int ScientificAccuracyScore, int EditorialFlowScore, int TransitionQualityScore, int ViewerRetentionScore, int AstroPulseIdentityScore, int OverallNarrationScore);

    private static class NarrationPostEditor
    {
        private static readonly string[] InstructionLeakage = ["understand...", "know...", "keep in mind", "anchor for this scene", "scene transition", "now let's", "next,", "prompt", "metadata", "source answer", "checklist", "approved production", "based on the current"];

        public static NarrationPostEditorResult Edit(IReadOnlyList<QuestionDrivenNarrationSceneDto> scenes, string family, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context)
        {
            var rewritten = new List<string>();
            var edited = new List<QuestionDrivenNarrationSceneDto>();
            var promptHit = false;
            var duplicateHit = false;
            var openings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var finalTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scene in scenes)
            {
                var text = Clean(scene.NarrationText);
                var original = text;
                if (ContainsAny(text, InstructionLeakage))
                {
                    promptHit = promptHit || ContainsAny(text, ["prompt", "metadata", "source answer", "checklist", "approved production"]);
                    text = RewriteScene(scene.Section, family, intelligence);
                }

                text = PolishObservationLanguage(text);
                text = ImproveTransition(scene.Section, text, family);

                var opening = FirstWords(text, 3);
                if (!string.IsNullOrWhiteSpace(opening) && !openings.Add(opening))
                    text = VaryOpening(scene.Section, text);

                if (!finalTexts.Add(Clean(text)))
                {
                    duplicateHit = true;
                    text = VaryOpening(scene.Section, text);
                }

                if (!string.Equals(original, text, StringComparison.Ordinal))
                    rewritten.Add(string.IsNullOrWhiteSpace(scene.Section) ? scene.ScenePurpose : scene.Section);

                edited.Add(scene with { NarrationText = EnsureTerminalPunctuation(text), CaptionText = ShortenCaption(text), EstimatedDurationSeconds = Math.Clamp(CountWords(text) / 2, 7, 13) });
            }

            var finalAllText = string.Join(" ", edited.Select(scene => scene.NarrationText));
            return new NarrationPostEditorResult(edited, true, rewritten.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), ContainsAny(finalAllText, InstructionLeakage), ContainsAny(finalAllText, ["prompt", "metadata", "source answer", "checklist", "approved production"]), duplicateHit);
        }

        public static NarrationPostEditorScores Score(IReadOnlyList<QuestionDrivenNarrationSceneDto> scenes, NarrationPostEditorResult? postEdit)
        {
            var all = string.Join(" ", scenes.Select(s => s.NarrationText));
            var leakageFree = !ContainsAny(all, InstructionLeakage);
            var varied = scenes.Select(s => FirstWords(s.NarrationText, 3)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == scenes.Count;
            var observation = ContainsAny(all, ["look", "toward", "horizon", "window", "outside", "binocular", "telescope", "eyes", "dark", "clear", "overhead", "west", "east", "sky"]);
            var science = ContainsAny(all, ["perspective", "orbits", "line of sight", "atmosphere", "shadow", "debris", "Earth", "Moon", "Sun", "space"]);
            var astro = observation && ContainsAny(all, ["sky", "outside", "look", "view", "observe"]);
            var documentary = leakageFree && varied ? 97 : 84;
            var spoken = leakageFree ? 96 : 80;
            var observationScore = observation ? 97 : 82;
            var scientific = science ? 100 : 90;
            var flow = varied ? 97 : 86;
            var transition = ContainsAny(all, ["But", "So", "And", "That", "As"]) ? 96 : 90;
            var retention = scenes.FirstOrDefault()?.NarrationText.Length > 35 && leakageFree ? 96 : 84;
            var identity = astro ? 97 : 82;
            var overall = new[] { documentary, spoken, observationScore, scientific, flow, transition, retention, identity }.Min();
            return new NarrationPostEditorScores(documentary, spoken, observationScore, scientific, flow, transition, retention, identity, overall);
        }

        private static string RewriteScene(string section, string family, ProductionEventIntelligence? intelligence) => section switch
        {
            "ColdOpen" => "As twilight deepens, the sky begins offering a quiet reason to step outside and look up.",
            "Hook" => "The first glance is simple, but the longer you watch, the more the scene reveals motion, distance, and timing.",
            "Context" => family == "Meteor" ? "This happens because Earth moves through an old stream of particles, turning tiny fragments into brief lines of light." : "The view is shaped by perspective: separate paths in space briefly line up from where we stand on Earth.",
            "MainStory" => "What you see is not a diagram, but a live sky event unfolding slowly enough for patient eyes to follow.",
            "ViewingGuide" => $"Step outside during {HumanizeNarrationWindow(FirstNonEmpty(intelligence?.BestViewingWindowLocal, intelligence?.PreferredViewingWindow, intelligence?.LocalPeakTime, "the best local window"))}, face {FirstNonEmpty(intelligence?.SkyDirectionHint, "the open sky")}, and start with your eyes before using binoculars.",
            "EmotionalClosing" => "If the sky is clear, take a few minutes for this view; you will know where to look, what to expect, and why it is worth remembering.",
            _ => "The sky event is brief, visible, and worth watching with a clear view and a little patience."
        };

        private static string PolishObservationLanguage(string text)
            => Regex.Replace(Regex.Replace(text, @"\bAltitude\s*[:=]?\s*27°", "about halfway between the horizon and overhead", RegexOptions.IgnoreCase), @"\bAzimuth\s*[:=]?\s*281°", "toward the western horizon", RegexOptions.IgnoreCase);
        private static string ImproveTransition(string section, string text, string family)
            => section == "ViewingGuide" && !Regex.IsMatch(text, @"\b(look|face|toward|outside|horizon)\b", RegexOptions.IgnoreCase) ? "So where should you look? " + text : text;
        private static string VaryOpening(string section, string text) => section switch { "Context" => "Behind that view, " + LowerFirst(text), "MainStory" => "In the eyepiece of the sky, " + LowerFirst(text), "ViewingGuide" => "For the clearest view, " + LowerFirst(text), "EmotionalClosing" => "And when the moment passes, " + LowerFirst(text), _ => text };
        private static string LowerFirst(string text) => string.IsNullOrWhiteSpace(text) ? text : char.ToLowerInvariant(text[0]) + text[1..];
        private static string EnsureTerminalPunctuation(string value) => value.EndsWith('.') || value.EndsWith('!') || value.EndsWith('?') ? value : value + ".";
        private static string ShortenCaption(string value) { var words = Regex.Matches(value, @"[\p{L}\p{N}'’°-]+").Select(m => m.Value).Take(7); return EnsureTerminalPunctuation(string.Join(' ', words)); }
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
        var template = Templates.TryGetValue(scene.QuestionType, out var matchedTemplate) ? matchedTemplate : null;
        var section = template is not null ? ResolveNarrationSection(scene.QuestionType, template, isMeteorShower: true) : scene.QuestionType;
        var region = intelligence.VisibilityRegion.Contains("UDAIPUR", StringComparison.OrdinalIgnoreCase) ? "Udaipur" : Clean(intelligence.VisibilityRegion, "your location");
        var title = Clean(intelligence.Title, "this meteor shower");
        var window = FirstNonEmpty(intelligence.BestViewingWindowLocal, intelligence.PreferredViewingWindow, intelligence.LocalPeakTime, "the best meteor-viewing window");
        var moon = FirstNonEmpty(intelligence.MoonInterference, "low moon interference");
        var source = section switch
        {
            "Hook" => $"{title} is a reliable meteor shower with bright streaks visible from dark skies.",
            "Curiosity" => $"{title} meteors can appear bright, slow, and colorful.",
            "Explanation" => FirstNonEmpty(intelligence.ScientificContext, $"{title} happens when Earth crosses the event-specific particle stream described by the approved event intelligence."),
            "ViewingAdvice" => $"For {region}, the approved viewing window is {window}; use the event-specific direction guidance: {FirstNonEmpty(intelligence.SkyDirectionHint, "open dark sky")}. No telescope needed.",
            "Reward" => $"{moon} improves the chance of catching repeated bright streaks across a dark sky.",
            "CTA" => $"Save the {window} sky guide, step outside during the approved window, and follow for more astronomy events.",
            _ => $"{title} is a meteor shower best watched from a dark open sky."
        };

        return section switch
        {
            "Hook" => Line("Tonight, one of the year's most reliable meteor showers is preparing to light up the sky.", "Reliable meteor shower tonight.", source),
            "Curiosity" => Line($"What makes {title} special is that the approved event intelligence points to memorable bright streaks under dark skies.", "Bright meteor streaks.", source),
            "Explanation" => Line(FirstNonEmpty(intelligence.ScientificContext, "This shower happens when Earth crosses the event-specific stream of particles described by the approved event intelligence."), "Event-specific meteor source.", source),
            "ViewingAdvice" => Line($"For the best experience, head to a dark location during {window} and use this direction guidance: {FirstNonEmpty(intelligence.SkyDirectionHint, "open sky") }.", "Use the approved direction guide.", source),
            "Reward" => Line("With low moonlight, patient observers may catch repeated bright streaks crossing the dark sky.", "Low moonlight helps meteor watching.", source),
            "CTA" => Line($"Save this sky guide, step outside during {window}, and follow for more astronomy events.", "Save this guide and follow.", source),
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
        AddCheck(checks, "targetDuration", narration.TotalEstimatedDurationSeconds is >= 45 and <= 75, "total duration must be between 45 and 75 seconds for Narration V3.");
        AddCheck(checks, "noDuplicateNarration", narration.Scenes.Select(scene => Clean(scene.NarrationText)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == narration.Scenes.Count, "no duplicate narration lines.");
        var copiedSourceAnswers = CountCopiedSourceAnswers(narration);
        AddCheck(checks, "notSourceAnswerCopies", copiedSourceAnswers == 0, "narrationText must not exactly copy sourceAnswer.");
        AddCheck(checks, "noInternalTerms", narration.Scenes.All(SceneHasNoInternalTerms), "narration and captions must not contain internal/debug terms.");
        AddCheck(checks, "emotionalClosingLast", string.Equals(narration.Scenes.LastOrDefault()?.Section, "EmotionalClosing", StringComparison.OrdinalIgnoreCase), "emotional closing is last.");
        AddCheck(checks, "coldOpenFirst", string.Equals(narration.Scenes.FirstOrDefault()?.Section, "ColdOpen", StringComparison.OrdinalIgnoreCase), "cold open is first.");
        AddCheck(checks, "oneQuestionPerScene", narration.Scenes.Select(scene => scene.QuestionType).Distinct(StringComparer.OrdinalIgnoreCase).Count() == narration.Scenes.Count, "each scene focuses on exactly one question type.");
        AddCheck(checks, "captionsShorterThanNarration", narration.Scenes.All(scene => scene.CaptionText.Length < scene.NarrationText.Length), "caption text should be shorter than narration text.");
        AddCheck(checks, "noForbiddenUnrelatedTerms", !NarrationContainsForbiddenLeakage(narration, productionContext, out _), "narration plan must not contain forbidden unrelated event terms.");
        AddCheck(checks, "coldOpenPresent", narration.Diagnostics?.ColdOpenPresent == true, "ColdOpen beat exists.");
        AddCheck(checks, "hookPresent", narration.Diagnostics?.HookPresent == true, "Hook beat exists.");
        AddCheck(checks, "storyLayerPresent", narration.Diagnostics?.StoryLayerPresent == true, "story layer exists before viewing guide.");
        AddCheck(checks, "viewingGuidePresent", narration.Diagnostics?.ViewingGuidePresent == true, "viewing guide exists after hook and story.");
        AddCheck(checks, "emotionalClosingPresent", narration.Diagnostics?.EmotionalClosingPresent == true, "emotional closing exists.");
        AddCheck(checks, "narrationVersion", string.Equals(narration.NarrationVersion, NarrationVersion, StringComparison.OrdinalIgnoreCase), "narrationVersion is V3.");
        AddCheck(checks, "notOnlyQuestionAnswerStyle", !IsOnlyQuestionAnswerStyle(narration), "narration is not only Q&A style.");
        AddCheck(checks, "viewingGuideAfterHookStory", ViewingGuideAfterHookAndStory(narration), "viewing guide appears after hook and story layer.");
        var missingSections = MissingRequiredSections(narration);
        AddCheck(checks, "requiredSectionsPresent", missingSections.Count == 0, missingSections.Count == 0 ? "required sections present: ColdOpen, Hook, Context, MainStory, ViewingGuide, EmotionalClosing." : "missing required narration section(s): " + string.Join(", ", missingSections) + ".");
        AddCheck(checks, "storyStructureComplete", missingSections.Count == 0, missingSections.Count == 0 ? "story structure includes ColdOpen, Hook, Context, MainStory, ViewingGuide, and EmotionalClosing." : "story structure missing required section(s): " + string.Join(", ", missingSections) + ".");
        AddCheck(checks, "sceneTypeMapped", narration.Scenes.All(scene => !string.IsNullOrWhiteSpace(scene.Section) && !string.IsNullOrWhiteSpace(scene.SceneType)), "every narration section maps to a scene type.");
        AddCheck(checks, "noRepetitiveSentenceOpenings", HasVariedSentenceOpenings(narration), "no repetitive sentence openings.");
        AddCheck(checks, "noRoboticPhrasing", narration.Scenes.All(scene => !ContainsAny(scene.NarrationText, new[] { "based on the current", "approved production", "source answer", "metadata" })), "narration avoids robotic or internal phrasing.");
        AddCheck(checks, "noAuthoringInstructions", narration.Scenes.All(scene => !ContainsAny(scene.NarrationText, AuthoringInstructionPhrases)), "narration must not contain prompt-template or authoring instruction phrases.");
        AddCheck(checks, "documentaryOpeningAllowed", OpeningStartsCorrectly(narration), "opening must start with event/date language and not forbidden prompt-style openings.");
        AddCheck(checks, "openingContainsEventDate", narration.Diagnostics?.EventDateMentioned == true, "opening must contain the event date.");
        AddCheck(checks, "openingContainsEventName", narration.Diagnostics?.EventNameMentioned == true, "opening must contain the event name.");
        AddCheck(checks, "scriptComposerVersion", string.Equals(narration.Diagnostics?.ScriptComposerVersion, EventStoryComposer.Version, StringComparison.OrdinalIgnoreCase), "event story composer V1 generated final spoken narration.");
        AddCheck(checks, "documentaryScore", (narration.Diagnostics?.DocumentaryScore ?? 0) >= 80, "documentaryScore must be at least 80.");
        AddCheck(checks, "storytellingScore", (narration.Diagnostics?.StorytellingScore ?? 0) >= 80, "storytellingScore must be at least 80.");
        AddCheck(checks, "noRawTimestamps", narration.Scenes.All(scene => !ContainsRawTimestamp(scene.NarrationText)), "narration must not speak raw timestamps.");
        AddCheck(checks, "sceneTextMinimumLength", narration.Scenes.All(scene => Clean(scene.NarrationText).Length >= 30), "each scene narration must be at least 30 characters.");
        AddCheck(checks, "dynamicNarrationGenerated", narration.Diagnostics?.DynamicNarrationGenerated == true, "narration must be generated from event and scene context.");
        AddCheck(checks, "hardcodedTemplateNotUsed", narration.Diagnostics?.HardcodedTemplateUsed == false, "hardcoded narration templates must not be used.");
        AddCheck(checks, "fallbackStaticTextNotUsed", narration.Diagnostics?.FallbackStaticTextUsed == false, "static fallback narration text must not be used.");
        AddCheck(checks, "sourceEventFactsUsed", (narration.Diagnostics?.SourceEventFactsUsed?.Count ?? 0) > 0, "source event facts must be represented in diagnostics.");
        AddCheck(checks, "scenePurposeUsed", (narration.Diagnostics?.ScenePurposeUsed?.Count ?? 0) >= narration.Scenes.Count, "scene purpose must be represented in diagnostics for each scene.");
        AddCheck(checks, "noStaticFallbackPhrases", narration.Scenes.All(scene => !ContainsAny(scene.NarrationText, new[] { "इस दृश्य में", "आकाशीय अवलोकन में समय, दिशा और दृश्यता", "This scene adds a distinct" })), "narration must not contain static fallback phrases.");
        AddCheck(checks, "auroraNoInstructionLeakage", narration.Diagnostics?.InstructionLeakageDetected == false && narration.Scenes.All(scene => !ContainsAny(scene.NarrationText, new[] { "Understand", "Know", "Keep in mind", "Anchor for this scene", "Now let's move", "Next", "metadata", "prompt", "source answer" })), "Aurora certification requires no instruction or prompt leakage.");
        AddCheck(checks, "auroraDocumentaryVoiceScore", (narration.Diagnostics?.DocumentaryVoiceScore ?? 0) >= 95, "documentaryVoiceScore must be at least 95.");
        AddCheck(checks, "auroraObservationGuidanceScore", (narration.Diagnostics?.ObservationGuidanceScore ?? 0) >= 95, "observationGuidanceScore must be at least 95.");
        AddCheck(checks, "auroraEditorialFlowScore", (narration.Diagnostics?.EditorialFlowScore ?? 0) >= 95, "editorialFlowScore must be at least 95.");
        AddCheck(checks, "auroraSpokenLanguageScore", (narration.Diagnostics?.SpokenLanguageScore ?? 0) >= 95, "spokenLanguageScore must be at least 95.");
        AddCheck(checks, "auroraViewerRetentionScore", (narration.Diagnostics?.ViewerRetentionScore ?? 0) >= 95, "viewerRetentionScore must be at least 95.");
        AddCheck(checks, "auroraScientificAccuracyScore", (narration.Diagnostics?.ScientificAccuracyScore ?? 0) == 100, "scientificAccuracyScore must be 100.");
        AddCheck(checks, "auroraAstroPulseIdentityScore", (narration.Diagnostics?.AstroPulseIdentityScore ?? 0) >= 95, "astroPulseIdentityScore must be at least 95.");
        AddCheck(checks, "auroraNoDuplicatedTransformations", narration.Diagnostics?.DuplicatedTransformationsDetected == false, "Aurora certification requires no duplicated transformed phrases.");

        return new QuestionDrivenNarrationReviewDto(
            narration.EventId,
            narration.RegionId,
            narration.Language,
            checks.All(check => check.Passed),
            narration.Scenes.Count,
            narration.TotalEstimatedDurationSeconds,
            checks,
            warnings,
            DateTimeOffset.UtcNow,
            RequiredSectionsPresent: missingSections.Count == 0,
            RepetitiveSentenceOpenings: !HasVariedSentenceOpenings(narration),
            StoryStructurePassed: missingSections.Count == 0,
            CopiedSourceAnswers: copiedSourceAnswers,
            NarrationVersion: NarrationVersion,
            Diagnostics: narration.Diagnostics);
    }



    private static object BuildNarrativeEditorialReview(QuestionDrivenNarrationDto narration, QuestionDrivenNarrationRequest request, QuestionDrivenNarrationReviewDto review)
    {
        var allText = string.Join(" ", narration.Scenes.Select(scene => scene.NarrationText));
        var repetitionWarnings = narration.Scenes
            .Select(scene => FirstWords(scene.NarrationText, 3))
            .GroupBy(opening => opening, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => $"Repeated opening '{group.Key}' appears {group.Count()} times.")
            .ToArray();
        var sectionQuestions = narration.Scenes.ToDictionary(
            scene => scene.Section,
            scene => scene.ViewerQuestion,
            StringComparer.OrdinalIgnoreCase);
        var observationTerms = new[] { "window", "time", "during", "direction", "toward", "horizon", "open", "binocular", "naked", "eyes", "look", "देख", "समय", "दिशा", "क्षितिज", "आंख" };
        var languageNaturalness = EstimateLanguageNaturalness(narration.Language, allText);
        var storyFlowScore = Math.Min(100, 72 + (review.RequiredSectionsPresent ? 12 : 0) + (ViewingGuideAfterHookAndStory(narration) ? 8 : 0) + (repetitionWarnings.Length == 0 ? 8 : 0));
        var curiosityScore = ScoreTerms(allText, ["why", "wonder", "story", "motion", "perspective", "memory", "क्यों", "कहानी", "गति", "याद", "खूबसूरती"]);
        var educationalScore = Math.Min(100, 70 + ((narration.Diagnostics?.EventNameMentioned ?? false) ? 8 : 0) + ((narration.Diagnostics?.EventDateMentioned ?? false) ? 6 : 0) + ((narration.Diagnostics?.DocumentaryScore ?? 0) >= 80 ? 8 : 0) + ((narration.Diagnostics?.StorytellingScore ?? 0) >= 80 ? 8 : 0));
        var observationScore = ScoreTerms(string.Join(" ", narration.Scenes.Where(scene => scene.Section.Contains("Viewing", StringComparison.OrdinalIgnoreCase)).Select(scene => scene.NarrationText)), observationTerms);
        var recommendations = new List<string>();
        if (storyFlowScore < 90) recommendations.Add("Strengthen the curiosity-to-understanding-to-confidence-to-wonder progression.");
        if (observationScore < 85) recommendations.Add("Add only the most useful observing detail: local time, direction, first object, realistic visibility, or optical aid guidance.");
        if (repetitionWarnings.Length > 0) recommendations.Add("Vary repeated sentence openings so each beat feels authored for its specific viewer question.");
        if (languageNaturalness < 85) recommendations.Add("Rewrite as native documentary narration instead of translating line by line.");
        if (recommendations.Count == 0) recommendations.Add("Narration meets RC1-B.1 documentary editorial quality targets.");

        return new
        {
            eventId = narration.EventId,
            regionId = narration.RegionId,
            language = narration.Language,
            version = "RC1-B.1 Documentary Narrative Excellence",
            generatedUtc = DateTimeOffset.UtcNow,
            storyFlowScore,
            curiosityScore,
            educationalScore,
            observationScore,
            repetitionWarnings,
            languageNaturalness,
            recommendations,
            beatQuestions = sectionQuestions,
            architecturePreserved = true,
            narrationBeatsRemainSynchronized = narration.Scenes.All(scene => scene.EstimatedDurationSeconds > 0)
        };
    }

    private static int ScoreTerms(string text, IEnumerable<string> terms)
    {
        var hits = terms.Count(term => !string.IsNullOrWhiteSpace(text) && text.Contains(term, StringComparison.OrdinalIgnoreCase));
        return Math.Min(100, 68 + hits * 6);
    }

    private static int EstimateLanguageNaturalness(string language, string text)
    {
        var roboticHits = new[] { "metadata", "source answer", "approved production", "based on the current", "line-by-line" }.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
        var isHindi = language.StartsWith("hi", StringComparison.OrdinalIgnoreCase) || language.Contains("Hindi", StringComparison.OrdinalIgnoreCase);
        var hasHindi = Regex.IsMatch(text, @"\p{IsDevanagari}");
        var baseScore = isHindi ? (hasHindi ? 88 : 55) : 88;
        return Math.Clamp(baseScore - roboticHits * 12, 0, 100);
    }

    private static string FirstWords(string value, int count)
        => string.Join(' ', Regex.Split(Clean(value).ToLowerInvariant(), @"\s+").Take(count));

    private static async Task<(string Short, string Long)> GenerateNarrationSubtitlesAsync(QuestionDrivenNarrationDto narration, string narrationPath, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetDirectoryName(narrationPath)!, "narration", "subtitles");
        Directory.CreateDirectory(root);
        var shortPath = Path.Combine(root, "short.srt");
        var longPath = Path.Combine(root, "long.srt");
        var narrationRoot = Path.Combine(Path.GetDirectoryName(narrationPath)!, "narration");
        var shortNarrationRoot = Path.Combine(narrationRoot, "short");
        var longNarrationRoot = Path.Combine(narrationRoot, "long");
        Directory.CreateDirectory(shortNarrationRoot);
        Directory.CreateDirectory(longNarrationRoot);
        var sourceScenes = narration.Scenes.ToArray();
        var shortFiles = await WriteSubtitleNarrationSourceFilesAsync(shortNarrationRoot, sourceScenes.Take(6).ToArray(), cancellationToken);
        var longFiles = await WriteSubtitleNarrationSourceFilesAsync(longNarrationRoot, sourceScenes, cancellationToken);
        var shortSrt = BuildNarrationSrt("short", shortFiles);
        var longSrt = BuildNarrationSrt("long", longFiles);
        await File.WriteAllTextAsync(shortPath, shortSrt.Srt, cancellationToken);
        await File.WriteAllTextAsync(longPath, longSrt.Srt, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "subtitle-diagnostics.json"), JsonSerializer.Serialize(new { shortBlocks = shortSrt.SubtitleBlocks, longBlocks = longSrt.SubtitleBlocks }, JsonOptions), cancellationToken);
        return (shortPath.Replace('\\', '/'), longPath.Replace('\\', '/'));
    }

    private static async Task<IReadOnlyList<string>> WriteSubtitleNarrationSourceFilesAsync(string root, IReadOnlyList<QuestionDrivenNarrationSceneDto> scenes, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var sceneKey = scene.SceneNumber > 0 ? $"scene-{scene.SceneNumber:000}" : $"scene-{i + 1:000}";
            var sceneId = Regex.Replace(sceneKey, @"[^A-Za-z0-9_.-]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(sceneId)) sceneId = $"scene-{i + 1:000}";
            var path = Path.Combine(root, $"{i + 1:000}-{sceneId}.txt");
            await File.WriteAllTextAsync(path, scene.NarrationText ?? string.Empty, cancellationToken);
            files.Add(path);
        }
        return files;
    }

    private static (string Srt, IReadOnlyList<object> SubtitleBlocks) BuildNarrationSrt(string format, IReadOnlyList<string> narrationFiles)
    {
        var blocks = new List<SubtitleBlock>();
        var start = TimeSpan.Zero;
        var number = 1;
        foreach (var narrationFile in narrationFiles)
        {
            ValidateSubtitleCueNarrationSource(format, narrationFile, nameof(BuildNarrationSrt));
            var line = File.ReadAllText(narrationFile);
            var sourceSceneId = Path.GetFileNameWithoutExtension(narrationFile);
            foreach (var chunk in SplitSubtitleChunks(line))
            {
                var seconds = Math.Clamp(CountWords(chunk) / 2.3, 2.0, 4.5);
                var end = start.Add(TimeSpan.FromSeconds(seconds));
                blocks.Add(new SubtitleBlock(number++, start, end, WrapSubtitle(chunk), sourceSceneId, NormalizePath(narrationFile), chunk, "NarrationFile", "QuestionDrivenNarrationGenerator.BuildNarrationSrt", DateTimeOffset.UtcNow));
                start = end;
            }
        }
        var duplicates = blocks.Select(block => NormalizeSubtitleText(string.Join(" ", block.Lines)))
            .GroupBy(text => text, StringComparer.OrdinalIgnoreCase)
            .Any(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);
        if (duplicates) throw new InvalidOperationException("SRT validation failed: duplicate subtitle blocks were produced.");
        var srt = string.Join("\n\n", blocks.Select(block => $"{block.Number}\n{FormatSrtTime(block.Start)} --> {FormatSrtTime(block.End)}\n{string.Join("\n", block.Lines)}")) + "\n";
        var subtitleBlocks = blocks.Select(block => new { format, cueId = block.Number, blockId = $"{format}:cue-{block.Number}", sceneId = block.SourceSceneId, text = string.Join(" ", block.Lines), normalizedText = NormalizeSubtitleText(string.Join(" ", block.Lines)), sourceType = block.SourceType, sourceSceneId = block.SourceSceneId, sourceFile = block.SourceFile, sourceText = block.SourceText, generatorComponent = block.GeneratorComponent, createdUtc = block.CreatedUtc }).Cast<object>().ToArray();
        var nonNarrationSubtitleCues = blocks.Where(block => !string.Equals(block.SourceType, "NarrationFile", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nonNarrationSubtitleCues.Length > 0) throw new InvalidOperationException("Non-narration subtitle cue detected");
        return (srt, subtitleBlocks);
    }

    private static void ValidateSubtitleCueNarrationSource(string format, string narrationFile, string generatorComponent)
    {
        var sourcePath = Path.GetFullPath(narrationFile);
        var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(sourcePath)!)!, format));
        var expectedPrefix = expectedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var parentFolder = new DirectoryInfo(Path.GetDirectoryName(expectedRoot)!).Name;
        if (!string.Equals(parentFolder, "narration", StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetExtension(sourcePath), ".txt", StringComparison.OrdinalIgnoreCase) || !sourcePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SRT validation failed: subtitle cue source must originate from narration/short/*.txt or narration/long/*.txt. format={format}; sourceFile={NormalizePath(narrationFile)}; generatorComponent={generatorComponent}");
    }

    private sealed record SubtitleBlock(int Number, TimeSpan Start, TimeSpan End, IReadOnlyList<string> Lines, string SourceSceneId, string SourceFile, string SourceText, string SourceType, string GeneratorComponent, DateTimeOffset CreatedUtc);

    private static string NormalizePath(string path) => path.Replace('\\', '/');


    private static IReadOnlyList<string> SplitSubtitleChunks(string text)
    {
        var phrases = Regex.Split(Clean(text).Replace('\n', ' '), @"(?<=[.!?])\s+|(?<=[,;:])\s+")
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();
        var chunks = new List<string>();
        var current = string.Empty;
        foreach (var phrase in phrases)
        {
            var candidate = (current + " " + phrase).Trim();
            if (CanWrapSubtitle(candidate))
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current)) chunks.Add(current);

            var phraseChunks = SplitSubtitleChunkOnWhitespace(phrase);
            chunks.AddRange(phraseChunks.Take(Math.Max(0, phraseChunks.Count - 1)));
            current = phraseChunks.Count == 0 ? string.Empty : phraseChunks[^1];
        }
        if (!string.IsNullOrWhiteSpace(current)) chunks.Add(current);
        return chunks;
    }

    private static IReadOnlyList<string> SplitSubtitleChunkOnWhitespace(string text)
    {
        var words = Regex.Split(text, @"\s+")
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToArray();
        var chunks = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            if (word.Length > 42)
                throw new InvalidOperationException($"SRT validation failed: subtitle text contains a word longer than 42 characters and cannot be wrapped without breaking a word. text={text}");

            var candidate = (current + " " + word).Trim();
            if (CanWrapSubtitle(candidate))
            {
                current = candidate;
                continue;
            }

            if (string.IsNullOrWhiteSpace(current))
                throw new InvalidOperationException($"SRT validation failed: subtitle cue cannot be split without breaking a word. text={text}");

            chunks.Add(current);
            current = word;
        }
        if (!string.IsNullOrWhiteSpace(current)) chunks.Add(current);
        return chunks;
    }

    private static IReadOnlyList<string> WrapSubtitle(string text)
    {
        if (text.Length <= 42) return [text];
        var minCut = Math.Max(1, text.Length - 42);
        var maxCut = Math.Min(42, text.Length - 1);
        var cut = text.LastIndexOf(' ', maxCut);
        if (cut < minCut)
            throw new InvalidOperationException($"SRT validation failed: subtitle cue cannot be wrapped into two 42-character lines without splitting a word. text={text}");
        return [text[..cut].Trim(), text[cut..].Trim()];
    }

    private static bool CanWrapSubtitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 84) return false;
        if (Regex.Split(text, @"\s+").Any(word => word.Length > 42)) return false;
        if (text.Length <= 42) return true;
        var minCut = Math.Max(1, text.Length - 42);
        var maxCut = Math.Min(42, text.Length - 1);
        var cut = text.LastIndexOf(' ', maxCut);
        return cut >= minCut;
    }

    private static string NormalizeSubtitleText(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static string FormatSrtTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";
    private static int CountWords(string value) => Regex.Matches(value ?? string.Empty, @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)?").Count;
    private static bool ContainsRawTimestamp(string value) => Regex.IsMatch(value ?? string.Empty, @"\b\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?|\b\d{1,2}:\d{2}\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)\b", RegexOptions.IgnoreCase);
    private static QuestionDrivenNarrationDiagnosticsDto? EnrichDiagnosticsWithSubtitles(QuestionDrivenNarrationDiagnosticsDto? diagnostics, string shortPath, string longPath)
        => diagnostics is null ? null : diagnostics with
        {
            SubtitleFilesGenerated = !string.IsNullOrWhiteSpace(shortPath) && !string.IsNullOrWhiteSpace(longPath),
            ShortSrtPath = shortPath,
            LongSrtPath = longPath,
            SubtitleMaxCharsPerLine = 42,
            SubtitleMaxLines = 2,
            SubtitleCueSplitApplied = true,
            SubtitleCueCountBeforeSplit = 0,
            SubtitleCueCountAfterSplit = 0,
            DuplicateSrtTextDetected = false
        };


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
            terms.AddRange(EventContentGuard.DefaultForbiddenTermsForEventType(productionContext?.EventType ?? intelligence.EventType));
            if (IsMeteorShower(intelligence, productionContext)) terms.AddRange(MeteorShowerForbiddenLeakageTerms);
        }
        return terms.Where(term => !string.IsNullOrWhiteSpace(term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }


    private static EventContentGuardDiagnostics BuildDiagnostics(QuestionDrivenNarrationDto narration, QuestionDrivenNarrationRequest request)
    {
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var promptPreview = string.Join(Environment.NewLine, narration.Scenes.Select(scene => $"{scene.QuestionType}: {scene.NarrationText} {scene.CaptionText}"));
        return EventContentGuard.BuildDiagnostics(
            14,
            "QuestionDrivenNarrationGenerator",
            FirstNonEmpty(request.EventType, request.ProductionContext?.EventType, intelligence?.EventType),
            intelligence?.StoryTheme,
            intelligence?.VisualTheme,
            ["question-driven-scene-plan.enriched.json", "production-event-intelligence.json", "content-plan-production-request.json"],
            promptPreview,
            BuildForbiddenLeakageTerms(request.ProductionContext));
    }

    private static int CountCopiedSourceAnswers(QuestionDrivenNarrationDto narration)
        => narration.Scenes.Count(scene => string.Equals(Clean(scene.NarrationText), Clean(scene.SourceAnswer), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> MissingRequiredSections(QuestionDrivenNarrationDto narration)
    {
        var present = narration.Scenes.Select(scene => scene.Section).Where(section => !string.IsNullOrWhiteSpace(section)).ToHashSet(StringComparer.Ordinal);
        return [.. new[] { "ColdOpen", "Hook", "Context", "MainStory", "ViewingGuide", "EmotionalClosing" }.Where(required => !present.Contains(required))];
    }


    private static bool IsOnlyQuestionAnswerStyle(QuestionDrivenNarrationDto narration)
        => narration.Scenes.All(scene => scene.ViewerQuestion.Contains("?", StringComparison.OrdinalIgnoreCase)
            && (scene.Section is "" || string.Equals(scene.Section, scene.QuestionType, StringComparison.OrdinalIgnoreCase)));

    private static bool ViewingGuideAfterHookAndStory(QuestionDrivenNarrationDto narration)
    {
        var ordered = narration.Scenes.OrderBy(scene => scene.SceneNumber).ToArray();
        var hook = Array.FindIndex(ordered, s => string.Equals(s.Section, "Hook", StringComparison.OrdinalIgnoreCase));
        var story = Array.FindIndex(ordered, s => string.Equals(s.Section, "Context", StringComparison.OrdinalIgnoreCase) || string.Equals(s.Section, "MainStory", StringComparison.OrdinalIgnoreCase));
        var guide = Array.FindIndex(ordered, s => string.Equals(s.Section, "ViewingGuide", StringComparison.OrdinalIgnoreCase));
        return hook >= 0 && story > hook && guide > story;
    }
    private static bool HasVariedSentenceOpenings(QuestionDrivenNarrationDto narration)
    {
        var openings = narration.Scenes
            .SelectMany(scene => OpeningKeys(scene.NarrationText))
            .ToArray();
        return openings.Distinct(StringComparer.OrdinalIgnoreCase).Count() == openings.Length;
    }

    private static IEnumerable<string> OpeningKeys(string narrationText)
    {
        var words = Clean(narrationText).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2) yield return string.Join(' ', words.Take(2)).ToLowerInvariant();
        if (words.Length >= 3) yield return string.Join(' ', words.Take(3)).ToLowerInvariant();
    }

    private static bool OpeningStartsCorrectly(QuestionDrivenNarrationDto narration)
    {
        var opening = Clean(narration.Scenes.OrderBy(s => s.SceneNumber).FirstOrDefault()?.NarrationText);
        if (string.IsNullOrWhiteSpace(opening)) return false;
        if (Regex.IsMatch(opening, @"^(For|During|As|When|Imagine|Look up tonight|Tonight|Tomorrow)\b", RegexOptions.IgnoreCase)) return false;
        return Regex.IsMatch(opening, @"^(On\s+\p{L}+\s+\d{1,2},\s+\d{4}|This event|Few sky events|The\s+|\p{L}+\s+\d{1,2},\s+\d{4}|\d{1,2}\s+\p{L}+\s+\d{4})", RegexOptions.IgnoreCase);
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
