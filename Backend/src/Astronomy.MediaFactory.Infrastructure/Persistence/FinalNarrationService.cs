using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class FinalNarrationService(
    IDirectorNarrationService directorNarrationService,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<FinalNarrationService> logger) : IFinalNarrationService
{
    private const string ExecutiveProducerStyle = "PremiumAstronomyDocumentary";
    private const string GenerationSource = "Phase9A.2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ForbiddenNarrationPhrases = ["Conjunction guide:", "Rare sky alert:"];

    public async Task<FinalNarrationResult> GenerateFinalNarrationAsync(FinalNarrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var finalNarrations = new List<FinalNarrationDocument>();
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim();

        var directorResult = await directorNarrationService.GenerateDirectorNarrationAsync(new DirectorNarrationRequest(
            request.RegionId,
            request.PlanIds,
            request.ContentCategories,
            request.PlannedFormats,
            language,
            request.MaxPlans,
            DryRun: true,
            OverwriteExisting: false), cancellationToken);

        warnings.AddRange(directorResult.Warnings.Select(w => $"Phase 9A.1 director warning: {w}"));
        var root = ResolveWorkingDirectoryRoot();

        foreach (var directorCut in directorResult.DirectorNarrations)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directorCut.ContentGenerationPlanId))
                {
                    warnings.Add($"Skipped final narration for '{directorCut.Title}' because the director cut did not include a plan id.");
                    continue;
                }

                var outputPath = BuildOutputPath(root, directorCut.RegionId, directorCut.ContentGenerationPlanId);
                if (!request.DryRun && File.Exists(outputPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing final narration for plan {directorCut.ContentGenerationPlanId}. Set overwriteExisting=true to replace it.");
                    continue;
                }

                var finalNarration = BuildFinalNarration(directorCut);
                finalNarrations.Add(finalNarration);
                if (!finalNarration.QualityChecklist.ReadyForTts)
                    warnings.Add($"Narration not ready for TTS. Plan {directorCut.ContentGenerationPlanId} scored {finalNarration.QualityScore}.");

                if (!request.DryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? root);
                    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(finalNarration, JsonOptions), cancellationToken);
                    generatedFiles.Add(outputPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to generate final narration for plan {directorCut.ContentGenerationPlanId}: {ex.Message}");
                logger.LogWarning(ex, "Phase 9A.2 final narration generation failed for plan {PlanId}", directorCut.ContentGenerationPlanId);
            }
        }

        var ready = finalNarrations.Count(n => n.QualityChecklist.ReadyForTts);
        logger.LogInformation("Phase 9A.2 final narration processed {PlanCount} director cut(s). Generated={GeneratedCount} ReadyForTts={ReadyForTtsCount} DryRun={DryRun}", directorResult.PlanCount, finalNarrations.Count, ready, request.DryRun);
        return new FinalNarrationResult(directorResult.PlanCount, finalNarrations.Count, ready, finalNarrations.Count - ready, finalNarrations, generatedFiles, warnings);
    }

    private static FinalNarrationDocument BuildFinalNarration(DirectorNarrationDocument directorCut)
    {
        var segments = new List<FinalNarrationSegment>();
        var usedNarrations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedPurposes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < directorCut.Segments.Count; i++)
        {
            var source = directorCut.Segments[i];
            var isOpening = i == 0;
            var isClosing = i == directorCut.Segments.Count - 1;
            var narration = BuildFinalNarrationText(directorCut, source, i, isOpening, isClosing);
            narration = RemoveForbiddenTitleLanguage(narration, directorCut.Title);
            narration = EnsureUniqueNarration(narration, source, usedNarrations, directorCut.ContentCategory);
            usedNarrations.Add(NormalizeForComparison(narration));

            var purpose = BuildScenePurpose(directorCut.ContentCategory, source, i, isOpening, isClosing);
            purpose = EnsureUniquePurpose(purpose, source, usedPurposes);
            usedPurposes.Add(purpose);

            segments.Add(new FinalNarrationSegment(
                source.SceneNumber,
                source.SceneName,
                narration,
                purpose,
                RetentionRole(directorCut.ContentCategory, i, directorCut.Segments.Count),
                VoiceDirection(directorCut.ContentCategory, isOpening, isClosing),
                FinalPauseHints(narration, isOpening, isClosing),
                FinalEmphasisWords(directorCut.ContentCategory, narration, isOpening),
                VisualCue(directorCut.ContentCategory, source, isOpening, isClosing),
                TransitionCue(isClosing ? "Fade out after a final breath." : "Let the next visual arrive on the final word.")));
        }

        var estimatedDuration = Math.Max(segments.Sum(s => EstimateDurationSeconds(s.FinalNarration)) + segments.Count, directorCut.EstimatedDurationSeconds);
        var checklist = BuildQualityChecklist(directorCut, segments);
        var qualityScore = Score(checklist);
        if (qualityScore < 90 && checklist.ReadyForTts)
            checklist = checklist with { ReadyForTts = false };
        if (qualityScore >= 90 && !checklist.ReadyForTts)
            qualityScore = Math.Min(89, qualityScore);

        return new FinalNarrationDocument(
            directorCut.Title,
            directorCut.RegionId,
            directorCut.ContentGenerationPlanId,
            directorCut.ContentCategory,
            ExecutiveProducerStyle,
            estimatedDuration,
            segments,
            qualityScore,
            checklist,
            GenerationSource,
            DateTimeOffset.UtcNow);
    }

    private static string BuildFinalNarrationText(DirectorNarrationDocument directorCut, DirectorNarrationSegment segment, int index, bool isOpening, bool isClosing)
    {
        return directorCut.ContentCategory switch
        {
            "RareEventAlert" => BuildRareEventFinal(directorCut, segment, isOpening, isClosing),
            "PlanetConjunction" => BuildPlanetConjunctionFinal(directorCut, segment, isOpening, isClosing),
            "PlanetGrouping" => BuildPlanetGroupingFinal(directorCut, segment, isOpening, isClosing),
            "WeeklySkyForecast" => BuildWeeklyForecastFinal(directorCut, segment, index, isOpening, isClosing),
            _ => BuildGeneralFinal(directorCut, segment, isOpening, isClosing)
        };
    }

    private static string BuildRareEventFinal(DirectorNarrationDocument directorCut, DirectorNarrationSegment segment, bool isOpening, bool isClosing)
    {
        if (isOpening)
            return $"If the sky over {directorCut.LocationName} opens tonight, do not waste the first clear minutes. This window may be brief. Watch calmly, and let the evidence in the sky lead the moment.";
        if (isClosing)
            return "If clouds win, the sky owes us nothing. But if it clears, you will know where to look, why it matters, and how to give the moment its best chance.";

        return segment.SceneNumber switch
        {
            2 => "Start with the brightest landmark in the view. Let your eyes settle before searching for the subtler detail nearby. Urgency helps you arrive on time; patience helps you actually see it.",
            3 => "Use the viewing window as guidance, not a guarantee. Weather, haze, and horizon clutter can change the result quickly, so check the sky before you commit.",
            4 => "What makes this worth watching is scarcity, not certainty. The event may be faint, partial, or brief, and that honest uncertainty is part of the experience.",
            _ => RewriteDirectorLine(segment.DirectorNarration)
        };
    }

    private static string BuildPlanetConjunctionFinal(DirectorNarrationDocument directorCut, DirectorNarrationSegment segment, bool isOpening, bool isClosing)
    {
        if (isOpening)
            return "Two bright worlds can seem to gather in one small patch of sky. They are not close together in space. Earth is giving us a clean line of sight.";
        if (isClosing)
            return "That is the quiet magic of a conjunction. Real worlds remain far apart, yet from here, for a little while, they share one view.";

        return segment.SceneNumber switch
        {
            2 => "Look for the apparent pairing first. Treat it as a visual meeting, not a physical one. The closeness belongs to our viewpoint.",
            3 => $"For {directorCut.LocationName}, choose an open horizon and give yourself a few minutes before the best time. A steady view matters more than rushing the exact second.",
            4 => "The planets follow separate paths around the Sun. From Earth, those paths can overlap against the background sky. That perspective turns distance into alignment.",
            5 => "Binoculars can sharpen the scene, but your unaided eyes are enough. Notice the spacing, the brightness, and how the pair sits inside the wider sky.",
            _ => RewriteDirectorLine(segment.DirectorNarration)
        };
    }

    private static string BuildPlanetGroupingFinal(DirectorNarrationDocument directorCut, DirectorNarrationSegment segment, bool isOpening, bool isClosing)
    {
        if (isOpening)
            return "Tonight's pattern is not about one object stealing the show. It is about how separate lights create a shape that your eyes can follow.";
        if (isClosing)
            return "Keep the memory simple. One light leads to the next, and the whole sky becomes easier to read.";

        return segment.SceneNumber switch
        {
            2 => "Begin with the brightest member of the group. Then move slowly across the pattern. The spacing is the story.",
            3 => $"For {directorCut.LocationName}, protect your night vision and keep the horizon open. Small choices on the ground can make the arrangement much clearer.",
            4 => "The objects are separated by enormous distances. From Earth, they temporarily share the same visual stage, and that is what makes the grouping feel designed.",
            _ => RewriteDirectorLine(segment.DirectorNarration)
        };
    }

    private static string BuildWeeklyForecastFinal(DirectorNarrationDocument directorCut, DirectorNarrationSegment segment, int index, bool isOpening, bool isClosing)
    {
        if (isOpening)
            return $"This week above {directorCut.LocationName}, the night sky has a beginning, a middle, and a quiet final note. Do not treat it like a list. Follow its rhythm.";
        if (isClosing)
            return "By the end of the week, the lesson is simple. The sky changes slowly enough to follow, and beautifully enough to remember.";

        return index switch
        {
            1 => "First, notice the mood of the week. The Moon changes the light. The planets hold the frame. Together, they give each night a different character.",
            2 => "Choose your best night rather than chasing every target. A clear sky, a darker corner, and a few unhurried minutes will do more than a crowded checklist.",
            3 => "Night by night, Earth turns beneath the view. The Moon advances along its path. The planets shift just enough for the scene to feel alive.",
            4 => "If one evening is lost to clouds, the story is not over. A weekly forecast gives you options, and the real sky decides which one works.",
            _ => RewriteDirectorLine(segment.DirectorNarration)
        };
    }

    private static string BuildGeneralFinal(DirectorNarrationDocument directorCut, DirectorNarrationSegment segment, bool isOpening, bool isClosing)
    {
        if (isOpening)
            return $"Some sky moments ask for a slower look. Tonight over {directorCut.LocationName}, give this one enough quiet to reveal itself.";
        if (isClosing)
            return "Step outside, let your eyes adjust, and leave room for the sky to surprise you.";
        return RewriteDirectorLine(segment.DirectorNarration);
    }

    private static string RewriteDirectorLine(string narration)
    {
        var cleaned = narration.Replace("[pause]", string.Empty, StringComparison.Ordinal).Trim();
        foreach (var phrase in ForbiddenNarrationPhrases)
            cleaned = cleaned.Replace(phrase, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return ShortenIfNeeded(cleaned);
    }

    private static string RemoveForbiddenTitleLanguage(string narration, string title)
    {
        var cleaned = narration;
        foreach (var phrase in ForbiddenNarrationPhrases)
            cleaned = cleaned.Replace(phrase, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (!string.IsNullOrWhiteSpace(title))
            cleaned = cleaned.Replace(title, "this sky moment", StringComparison.OrdinalIgnoreCase).Trim();
        return cleaned;
    }

    private static string EnsureUniqueNarration(string narration, DirectorNarrationSegment segment, ISet<string> usedNarrations, string category)
    {
        if (!usedNarrations.Contains(NormalizeForComparison(narration)))
            return narration;

        return category switch
        {
            "WeeklySkyForecast" => $"This scene turns the week forward. {segment.SceneName} becomes the next beat, with a fresh reason to keep watching.",
            "PlanetConjunction" => $"This scene separates the view from the physics. {segment.SceneName} shows why apparent closeness can happen across real distance.",
            "RareEventAlert" => $"This scene keeps the alert useful. {segment.SceneName} explains how to act quickly without overstating what the sky will show.",
            _ => $"This scene adds a new viewing detail. {segment.SceneName} gives the audience one practical reason to stay oriented."
        };
    }

    private static string BuildScenePurpose(string category, DirectorNarrationSegment segment, int index, bool isOpening, bool isClosing)
    {
        if (isOpening) return category switch
        {
            "RareEventAlert" => "Create calm urgency and explain why the viewing window deserves attention.",
            "PlanetConjunction" => "Hook the viewer with apparent closeness while immediately protecting scientific accuracy.",
            "WeeklySkyForecast" => "Frame the week as a narrative episode rather than a list of events.",
            _ => "Invite the viewer into the sky story with a clear non-clickbait hook."
        };
        if (isClosing) return "Leave the viewer with a memorable emotional takeaway and a clear next action.";

        return category switch
        {
            "RareEventAlert" => index == 1 ? "Guide the first look without panic." : index == 2 ? "Set practical expectations around timing and weather." : "Explain rarity without exaggeration.",
            "PlanetConjunction" => index == 1 ? "Clarify that the pairing is apparent from Earth." : index == 2 ? "Give the viewer a grounded observing plan." : index == 3 ? "Explain line-of-sight geometry and real distance." : "Turn the science into a viewer-friendly experience.",
            "WeeklySkyForecast" => index == 1 ? "Introduce the weekly rhythm and changing mood." : index == 2 ? "Convert the forecast into a practical observing choice." : index == 3 ? "Explain the motion that makes each night different." : "Keep the story resilient when weather interrupts.",
            _ => $"Advance the scene with a distinct purpose: {segment.SceneName}."
        };
    }

    private static string EnsureUniquePurpose(string purpose, DirectorNarrationSegment segment, ISet<string> usedPurposes)
        => usedPurposes.Contains(purpose) ? $"Give scene {segment.SceneNumber} its own job: {segment.SceneName}." : purpose;

    private static string RetentionRole(string category, int index, int count)
    {
        if (index == 0) return "Hook";
        if (index == count - 1) return "EmotionalClose";
        if (category == "PlanetConjunction" && index is 1 or 3) return "Clarify";
        if (category == "WeeklySkyForecast" && index == 3) return "Explain";
        if (index == count - 2) return "PracticalTakeaway";
        return index % 2 == 0 ? "Guide" : "Explain";
    }

    private static string VoiceDirection(string category, bool isOpening, bool isClosing)
    {
        if (isOpening) return category == "RareEventAlert" ? "Calm urgency; controlled pace; no alarm tone." : "Warm cinematic hook; confident but restrained.";
        if (isClosing) return "Slow down slightly; let the final image breathe.";
        return category switch
        {
            "RareEventAlert" => "Clear and trustworthy; keep urgency practical.",
            "PlanetConjunction" => "Precise and curious; separate viewpoint from physical distance.",
            "WeeklySkyForecast" => "Story-led guide voice; connect this beat to the week.",
            _ => "Natural documentary voice; one idea per sentence."
        };
    }

    private static IReadOnlyList<string> FinalPauseHints(string narration, bool isOpening, bool isClosing)
    {
        var hints = new List<string>();
        if (isOpening) hints.Add("Hold 0.7s before the first sentence so the opening visual lands.");
        if (narration.Contains(';')) hints.Add("Use a short breath at the semicolon.");
        if (isClosing) hints.Add("Leave 1.0s after the last sentence before music resolves.");
        hints.Add("Keep sentence breaks audible for TTS pacing.");
        return hints;
    }

    private static IReadOnlyList<string> FinalEmphasisWords(string category, string narration, bool isOpening)
    {
        var candidates = category switch
        {
            "RareEventAlert" => new[] { "brief", "calmly", "guidance", "scarcity", "chance" },
            "PlanetConjunction" => new[] { "appear", "Earth", "line of sight", "distance", "view" },
            "WeeklySkyForecast" => new[] { "week", "rhythm", "night", "changes", "remember" },
            _ => new[] { "sky", "quiet", "look", "view", "surprise" }
        };

        var words = candidates.Where(c => narration.Contains(c, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
        if (isOpening && words.Count == 0) words.Add("sky");
        return words;
    }

    private static string VisualCue(string category, DirectorNarrationSegment segment, bool isOpening, bool isClosing)
    {
        if (isOpening) return "Open with a premium wide sky image; avoid title-card narration timing.";
        if (isClosing) return "Hold a clean final sky composition with minimal text.";
        return category switch
        {
            "PlanetConjunction" => "Show apparent pairing first, then a simple line-of-sight or orbital-depth graphic.",
            "RareEventAlert" => "Use restrained timing and visibility graphics; avoid alarm visuals.",
            "WeeklySkyForecast" => "Use connected night-by-night montage imagery, not isolated list cards.",
            _ => segment.AssetSynchronizationHints.FirstOrDefault() ?? "Match visuals to the spoken viewing guidance."
        };
    }

    private static string TransitionCue(string cue) => cue;

    private static FinalNarrationQualityChecklist BuildQualityChecklist(DirectorNarrationDocument directorCut, IReadOnlyList<FinalNarrationSegment> segments)
    {
        var narrations = segments.Select(s => s.FinalNarration).ToList();
        var allNarration = string.Join("\n", narrations);
        var titleNotRead = string.IsNullOrWhiteSpace(directorCut.Title) || !allNarration.Contains(directorCut.Title, StringComparison.OrdinalIgnoreCase);
        titleNotRead &= ForbiddenNarrationPhrases.All(p => !allNarration.Contains(p, StringComparison.OrdinalIgnoreCase));
        var noDuplicateNarration = narrations.Select(NormalizeForComparison).Distinct(StringComparer.OrdinalIgnoreCase).Count() == narrations.Count;
        var uniqueScenePurpose = segments.Select(s => s.ScenePurpose).Distinct(StringComparer.OrdinalIgnoreCase).Count() == segments.Count;
        var strongHook = segments.Count > 0 && IsStrongHook(segments[0].FinalNarration, directorCut.ContentCategory);
        var professionalTone = !ContainsClickbait(allNarration);
        var scientificallySafe = IsScientificallySafe(directorCut.ContentCategory, allNarration);
        var voiceFriendly = narrations.All(IsVoiceFriendly);
        var ready = titleNotRead && noDuplicateNarration && uniqueScenePurpose && strongHook && professionalTone && scientificallySafe && voiceFriendly;
        return new FinalNarrationQualityChecklist(titleNotRead, noDuplicateNarration, uniqueScenePurpose, strongHook, professionalTone, scientificallySafe, voiceFriendly, ready);
    }

    private static int Score(FinalNarrationQualityChecklist checklist)
    {
        var score = 100;
        if (!checklist.TitleNotRead) score -= 20;
        if (!checklist.NoDuplicateNarration) score -= 15;
        if (!checklist.UniqueScenePurpose) score -= 10;
        if (!checklist.StrongHook) score -= 15;
        if (!checklist.ProfessionalTone) score -= 15;
        if (!checklist.ScientificallySafe) score -= 15;
        if (!checklist.VoiceFriendly) score -= 10;
        return Math.Clamp(score, 0, 100);
    }

    private static bool IsStrongHook(string narration, string category)
    {
        if (string.IsNullOrWhiteSpace(narration)) return false;
        if (ContainsClickbait(narration)) return false;
        return category switch
        {
            "RareEventAlert" => narration.Contains("window", StringComparison.OrdinalIgnoreCase) && narration.Contains("calm", StringComparison.OrdinalIgnoreCase),
            "PlanetConjunction" => narration.Contains("close", StringComparison.OrdinalIgnoreCase) || narration.Contains("line of sight", StringComparison.OrdinalIgnoreCase),
            "WeeklySkyForecast" => narration.Contains("beginning", StringComparison.OrdinalIgnoreCase) && narration.Contains("rhythm", StringComparison.OrdinalIgnoreCase),
            _ => narration.Length > 40
        };
    }

    private static bool IsScientificallySafe(string category, string narration)
    {
        if (ContainsClickbait(narration)) return false;
        return category switch
        {
            "PlanetConjunction" => narration.Contains("not close together in space", StringComparison.OrdinalIgnoreCase)
                && narration.Contains("line of sight", StringComparison.OrdinalIgnoreCase)
                && narration.Contains("view", StringComparison.OrdinalIgnoreCase)
                && narration.Contains("physical", StringComparison.OrdinalIgnoreCase),
            "RareEventAlert" => narration.Contains("guidance", StringComparison.OrdinalIgnoreCase)
                || narration.Contains("not a guarantee", StringComparison.OrdinalIgnoreCase)
                || narration.Contains("chance", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool IsVoiceFriendly(string narration)
    {
        if (narration.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 70)
            return false;
        return narration.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(sentence => sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length <= 24);
    }

    private static bool ContainsClickbait(string text)
        => new[] { "once in a lifetime", "you won't believe", "shocking", "must see before it disappears forever" }.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static string ShortenIfNeeded(string narration)
    {
        var words = narration.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length <= 55) return narration;
        return string.Join(' ', words.Take(55)).TrimEnd(',', ';', ':') + ".";
    }

    private static string NormalizeForComparison(string text)
        => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim().TrimEnd('.');

    private static int EstimateDurationSeconds(string narration)
    {
        var words = narration.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        return Math.Max(6, (int)Math.Ceiling(words / 2.45m));
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string BuildOutputPath(string root, string regionId, string planId)
        => Path.Combine(root, "assets", SanitizePathSegment(regionId) ?? "unknown-region", "plans", planId, "narration", "narration-final.json");

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static void Validate(FinalNarrationRequest request)
    {
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");
    }
}
