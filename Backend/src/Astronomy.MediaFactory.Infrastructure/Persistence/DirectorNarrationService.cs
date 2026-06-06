using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DirectorNarrationService(
    INarrationPlanningService narrationPlanningService,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<DirectorNarrationService> logger) : IDirectorNarrationService
{
    private const string DirectorStyle = "ProfessionalAstronomyDocumentary";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<DirectorNarrationResult> GenerateDirectorNarrationAsync(DirectorNarrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var directorNarrations = new List<DirectorNarrationDocument>();
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim();

        var draftResult = await narrationPlanningService.GenerateNarrationScriptsAsync(new NarrationPlanningRequest(
            request.RegionId,
            request.ContentCategories,
            request.PlannedFormats,
            request.PlanIds,
            language,
            request.MaxPlans,
            DryRun: true,
            OverwriteExisting: false), cancellationToken);

        warnings.AddRange(draftResult.Warnings.Select(w => $"Phase 9A draft warning: {w}"));
        var root = ResolveWorkingDirectoryRoot();

        foreach (var draft in draftResult.NarrationScripts)
        {
            try
            {
                var outputPath = BuildOutputPath(root, draft.RegionId, draft.ContentGenerationPlanId);
                if (!request.DryRun && File.Exists(outputPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing director narration for plan {draft.ContentGenerationPlanId}. Set overwriteExisting=true to replace it.");
                    continue;
                }

                var directorCut = BuildDirectorCut(draft);
                directorNarrations.Add(directorCut);

                if (!request.DryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? root);
                    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(directorCut, JsonOptions), cancellationToken);
                    generatedFiles.Add(outputPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to generate director narration for plan {draft.ContentGenerationPlanId}: {ex.Message}");
                logger.LogWarning(ex, "Phase 9A.1 director narration generation failed for plan {PlanId}", draft.ContentGenerationPlanId);
            }
        }

        logger.LogInformation("Phase 9A.1 director narration processed {PlanCount} draft(s). Generated={GeneratedCount} DryRun={DryRun}", draftResult.PlanCount, directorNarrations.Count, request.DryRun);
        return new DirectorNarrationResult(draftResult.PlanCount, directorNarrations.Count, directorNarrations, generatedFiles, warnings);
    }

    private static DirectorNarrationDocument BuildDirectorCut(NarrationScriptDocument draft)
    {
        var segments = draft.Segments.Select((segment, index) => BuildDirectorSegment(draft, segment, index)).ToList();
        var estimatedDuration = Math.Max(draft.EstimatedDurationSeconds, segments.Sum(s => EstimateDurationSeconds(s.DirectorNarration)) + segments.Count);

        return new DirectorNarrationDocument(
            draft.Title,
            DirectorStyle,
            estimatedDuration,
            segments,
            draft.ContentGenerationPlanId,
            draft.ContentCategory,
            draft.RegionId,
            draft.LocationName);
    }

    private static DirectorNarrationSegment BuildDirectorSegment(NarrationScriptDocument draft, NarrationScriptSegment segment, int index)
    {
        var isOpening = index == 0;
        var directorNarration = draft.ContentCategory switch
        {
            "RareEventAlert" => BuildRareEventDirectorNarration(draft, segment, isOpening),
            "PlanetConjunction" => BuildPlanetConjunctionDirectorNarration(draft, segment, isOpening),
            "PlanetGrouping" => BuildPlanetGroupingDirectorNarration(draft, segment, isOpening),
            "WeeklySkyForecast" => BuildWeeklyForecastDirectorNarration(draft, segment, isOpening, index),
            _ => BuildGeneralDirectorNarration(draft, segment, isOpening)
        };

        return new DirectorNarrationSegment(
            segment.SceneNumber,
            segment.SceneName,
            segment.Script,
            directorNarration,
            RetentionPurpose(draft.ContentCategory, segment, isOpening),
            Emotion(draft.ContentCategory, isOpening, index),
            PauseHints(directorNarration, isOpening),
            EmphasisWords(draft.ContentCategory, directorNarration, isOpening),
            AssetSynchronizationHints(draft.ContentCategory, segment, isOpening));
    }

    private static string BuildRareEventDirectorNarration(NarrationScriptDocument draft, NarrationScriptSegment segment, bool isOpening)
    {
        if (isOpening)
            return $"If the sky over {draft.LocationName} clears, this is the kind of moment that can pass quietly above us. [pause] {draft.Title} is worth watching for — not because it is guaranteed, but because the window may be brief, and the view may be memorable.";

        return segment.SceneNumber switch
        {
            2 => $"Look first for the brightest anchor in the scene, then let your eyes adjust to the darker sky around it. {segment.Script} The important thing is patience: conditions can change minute by minute.",
            3 => $"Before you search, make the view safe and simple. {segment.Script} A darker horizon gives subtle events their best chance to reveal themselves.",
            _ => $"If the clouds move away, give this a careful look. [pause] The rarest sky alerts are not always loud; sometimes they are quiet moments that reward being ready."
        };
    }

    private static string BuildPlanetConjunctionDirectorNarration(NarrationScriptDocument draft, NarrationScriptSegment segment, bool isOpening)
    {
        if (isOpening)
            return $"Two worlds separated by vast distances will appear to draw close in the sky over {draft.LocationName}. [pause] {draft.Title} is not a collision or a meeting in space — it is perspective, turning the horizon into a precise celestial alignment.";

        return segment.SceneNumber switch
        {
            2 => "This is what a conjunction really means. The planets are still following their own separate paths around the Sun, but from Earth, our line of sight places them near one another on the sky.",
            3 => $"For the best chance to see it, use the viewing window carefully. {segment.Script} Begin wide, then narrow your attention to the pairing.",
            4 => "The significance is in the geometry. As Earth moves, the planets shift against the background stars, and for a short time their apparent paths overlap from our point of view.",
            _ => $"Hold the view for a few seconds longer than you normally would. [pause] A conjunction is simple astronomy, but visually it can feel like the sky has paused to show its scale."
        };
    }

    private static string BuildPlanetGroupingDirectorNarration(NarrationScriptDocument draft, NarrationScriptSegment segment, bool isOpening)
    {
        if (isOpening)
            return $"Tonight, do not search for one object first. Start with the whole sky. [pause] {draft.Title} is a grouping — a visual path your eyes can follow from one bright point to the next.";

        return segment.SceneNumber switch
        {
            2 => $"Use the brightest object as your anchor, then move slowly across the scene. {segment.Script} The story is not just what is visible, but how each point leads you to the next.",
            3 => $"Let the horizon and the Moon, if visible, set your orientation. {segment.Script} This is how a grouping becomes a map rather than a list.",
            _ => $"Step back from the details and take in the arrangement. [pause] The pattern is temporary, shaped by motion, distance, and our viewpoint from Earth."
        };
    }

    private static string BuildWeeklyForecastDirectorNarration(NarrationScriptDocument draft, NarrationScriptSegment segment, bool isOpening, int index)
    {
        if (isOpening)
            return $"This week does not unfold as a list of events. It opens like a slow journey across the night sky above {draft.LocationName}. [pause] Each evening changes the view, and each change tells you where to look next.";

        return index switch
        {
            1 => $"The week begins with rhythm: the Moon shifts the mood, the planets hold their places, and the brightest targets give your eyes a path to follow. {segment.Script}",
            2 => $"By the middle of the story, the question becomes practical: which night gives the sky its best chance? {segment.Script} Choose one clear window, then let the forecast guide your attention.",
            3 => $"What changes is not only the sky, but our angle on it. {segment.Script} Night by night, Earth turns, the Moon advances, and the scene slowly rearranges itself.",
            4 => $"If one evening is lost to haze or cloud, the journey is not over. {segment.Script} A weekly forecast should leave room for the real sky.",
            _ => "By the end of the week, the point is not to collect every event. It is to notice motion: time crossing the sky, one evening at a time."
        };
    }

    private static string BuildGeneralDirectorNarration(NarrationScriptDocument draft, NarrationScriptSegment segment, bool isOpening)
        => isOpening
            ? $"Some sky moments reveal themselves only when you slow down. [pause] {draft.Title} asks for that kind of attention — a careful look at what changes above {draft.LocationName}."
            : $"{segment.Script} [pause] Watch for the reason this scene matters, then let the next view answer what changes after it.";

    private static string RetentionPurpose(string category, NarrationScriptSegment segment, bool isOpening)
        => isOpening
            ? category switch
            {
                "RareEventAlert" => "Create immediate trustworthy urgency without overstating certainty.",
                "WeeklySkyForecast" => "Promise a guided week-long journey instead of a checklist.",
                "PlanetConjunction" => "Open a curiosity gap around apparent closeness versus real distance.",
                "PlanetGrouping" => "Invite the viewer to visually explore the whole arrangement.",
                _ => "Create curiosity and give the viewer a reason to continue."
            }
            : $"Answer why this scene matters and bridge toward the next sky-viewing decision after '{segment.SceneName}'.";

    private static string Emotion(string category, bool isOpening, int index)
    {
        if (category == "RareEventAlert") return isOpening ? "measured anticipation" : "trustworthy discovery";
        if (category == "WeeklySkyForecast") return index switch { 0 => "curiosity", 1 => "wonder", 2 => "anticipation", 3 => "discovery", _ => "reflective wonder" };
        if (category == "PlanetConjunction") return isOpening ? "curiosity" : index < 3 ? "discovery" : "wonder";
        if (category == "PlanetGrouping") return isOpening ? "curiosity" : "guided wonder";
        return isOpening ? "curiosity" : "discovery";
    }

    private static IReadOnlyList<string> PauseHints(string narration, bool isOpening)
    {
        var hints = new List<string>();
        if (isOpening) hints.Add("Hold 0.8s after the opening image before the first key reveal.");
        if (narration.Contains("[pause]", StringComparison.Ordinal)) hints.Add("Use a 0.6s reflective pause at [pause].");
        hints.Add("Leave a short breath before the final sentence so the visual can land.");
        return hints;
    }

    private static IReadOnlyList<string> EmphasisWords(string category, string narration, bool isOpening)
    {
        var words = new List<string>();
        if (isOpening) words.Add(category == "RareEventAlert" ? "brief" : "sky");
        foreach (var candidate in new[] { "perspective", "alignment", "journey", "motion", "view", "window", "quiet", "distance", "horizon" })
        {
            if (narration.Contains(candidate, StringComparison.OrdinalIgnoreCase)) words.Add(candidate);
        }
        return words.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
    }

    private static IReadOnlyList<string> AssetSynchronizationHints(string category, NarrationScriptSegment segment, bool isOpening)
    {
        var hints = new List<string>
        {
            $"Start with asset cue: {segment.AssetCue}.",
            $"Transition style: {segment.TransitionHint}."
        };

        if (isOpening) hints.Add("Reveal the key object or alignment only after the first curiosity sentence.");
        if (category == "PlanetConjunction") hints.Add("Cut to a simple line-of-sight graphic when narration mentions perspective or alignment.");
        if (category == "PlanetGrouping") hints.Add("Animate a gentle visual path from the anchor object to the remaining objects.");
        if (category == "WeeklySkyForecast") hints.Add("Use a night-by-night montage rather than isolated event cards.");
        if (category == "RareEventAlert") hints.Add("Keep urgency visual language restrained: clean timer/window graphic, no alarm-style effects.");
        return hints;
    }

    private static int EstimateDurationSeconds(string narration)
    {
        var words = narration.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        return Math.Max(6, (int)Math.Ceiling(words / 2.45m));
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string BuildOutputPath(string root, string regionId, string planId)
        => Path.Combine(root, "assets", SanitizePathSegment(regionId) ?? "unknown-region", "plans", planId, "narration", "narration-director-cut.json");

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static void Validate(DirectorNarrationRequest request)
    {
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");
    }
}
