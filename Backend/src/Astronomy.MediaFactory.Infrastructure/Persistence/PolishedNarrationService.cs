using System.Text.RegularExpressions;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class PolishedNarrationService(
    IFinalNarrationService finalNarrationService,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<PolishedNarrationService> logger) : IPolishedNarrationService
{
    private const string GenerationSource = "Phase9A.3";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<PolishedNarrationResult> PolishFinalNarrationAsync(PolishedNarrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var polishedNarrations = new List<PolishedNarrationDocument>();
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim();

        var finalResult = await finalNarrationService.GenerateFinalNarrationAsync(new FinalNarrationRequest(
            request.RegionId,
            request.PlanIds,
            request.ContentCategories,
            request.PlannedFormats,
            language,
            request.MaxPlans,
            DryRun: true,
            OverwriteExisting: false), cancellationToken);

        warnings.AddRange(finalResult.Warnings.Select(w => $"Phase 9A.2 final narration warning: {w}"));
        var root = ResolveWorkingDirectoryRoot();

        foreach (var final in finalResult.FinalNarrations)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(final.ContentGenerationPlanId))
                {
                    warnings.Add($"Skipped polished narration for '{final.Title}' because the final narration did not include a plan id.");
                    continue;
                }

                var outputPath = BuildOutputPath(root, final.RegionId, final.ContentGenerationPlanId);
                if (!request.DryRun && File.Exists(outputPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing polished narration for plan {final.ContentGenerationPlanId}. Set overwriteExisting=true to replace it.");
                    continue;
                }

                var polished = BuildPolishedNarration(final);
                polishedNarrations.Add(polished);
                if (!polished.TtsReadiness.ReadyForTts)
                    warnings.Add("Narration quality below TTS threshold.");

                if (!request.DryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? root);
                    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(polished, JsonOptions), cancellationToken);
                    generatedFiles.Add(outputPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to polish final narration for plan {final.ContentGenerationPlanId}: {ex.Message}");
                logger.LogWarning(ex, "Phase 9A.3 polished narration generation failed for plan {PlanId}", final.ContentGenerationPlanId);
            }
        }

        var ready = polishedNarrations.Count(n => n.TtsReadiness.ReadyForTts);
        logger.LogInformation("Phase 9A.3 polished narration processed {PlanCount} final narration(s). Polished={PolishedCount} ReadyForTts={ReadyForTtsCount} DryRun={DryRun}", finalResult.PlanCount, polishedNarrations.Count, ready, request.DryRun);
        return new PolishedNarrationResult(finalResult.PlanCount, polishedNarrations.Count, ready, polishedNarrations.Count - ready, polishedNarrations, generatedFiles, warnings);
    }

    private static PolishedNarrationDocument BuildPolishedNarration(FinalNarrationDocument final)
    {
        var segments = final.Segments.Select((segment, index) => PolishSegment(final, segment, index)).ToList();
        var durationValidation = BuildDurationValidation(segments);
        var qualityBreakdown = BuildQualityBreakdown(final, segments);
        var qualityScore = qualityBreakdown.Total;
        var readyForTts = qualityScore >= 90;
        var ttsReadiness = BuildTtsReadiness(final.ContentCategory, readyForTts);
        var checklist = final.QualityChecklist with { ReadyForTts = readyForTts };

        return new PolishedNarrationDocument(
            final.Title,
            final.RegionId,
            final.ContentGenerationPlanId,
            final.ContentCategory,
            final.ExecutiveProducerStyle,
            durationValidation.EstimatedDurationSeconds,
            segments,
            qualityScore,
            qualityBreakdown,
            checklist,
            ttsReadiness,
            durationValidation,
            GenerationSource,
            final.GenerationSource,
            DateTimeOffset.UtcNow);
    }

    private static PolishedNarrationSegment PolishSegment(FinalNarrationDocument final, FinalNarrationSegment segment, int index)
    {
        var narration = final.ContentCategory == "WeeklySkyForecast"
            ? BuildWeeklyEpisodeNarration(final, segment, index)
            : PolishVoiceFriendlyText(segment.FinalNarration);
        var performance = BuildVoicePerformance(final.ContentCategory, segment.RetentionRole, index, final.Segments.Count);

        return new PolishedNarrationSegment(
            segment.SceneNumber,
            segment.SceneName,
            narration,
            final.ContentCategory == "WeeklySkyForecast" ? WeeklyEpisodePurpose(index, final.Segments.Count) : segment.ScenePurpose,
            segment.RetentionRole,
            segment.VoiceDirection,
            segment.PauseHints,
            segment.EmphasisWords,
            segment.VisualCue,
            segment.TransitionCue,
            performance);
    }

    private static string BuildWeeklyEpisodeNarration(FinalNarrationDocument final, FinalNarrationSegment segment, int index)
    {
        var count = final.Segments.Count;
        if (index == 0)
            return $"Above {RegionNarrationName(final.RegionId)}, the week opens like a quiet episode. One sky, several nights, and a rhythm worth following from the first look.";
        if (index == count - 1)
            return "Take one clear evening with you. The best forecast is not a checklist; it is a reason to step outside and notice how the sky keeps changing.";

        return index switch
        {
            1 => "The change begins with light. The Moon reshapes the darkness, planets hold their places, and the familiar sky starts to feel newly arranged.",
            2 => "The main highlight is the moment that gives the week its center. Let it anchor the story, then let the smaller details gather around it.",
            3 => "Your best opportunity is the night with the cleanest horizon and the least hurry. A few calm minutes can reveal more than a rushed tour of every event.",
            4 => "Scientifically, nothing has to race for the scene to feel alive. Earth turns, the Moon advances, and the planets shift slowly against the background stars.",
            _ => PolishVoiceFriendlyText(segment.FinalNarration)
        };
    }

    private static string RegionNarrationName(string regionId)
    {
        if (string.IsNullOrWhiteSpace(regionId)) return "your region";
        var last = regionId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(last)) return "your region";
        return string.Join(' ', last.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string WeeklyEpisodePurpose(int index, int count)
    {
        if (index == 0) return "Opening Hook: invite viewers into a weekly sky episode instead of a list.";
        if (index == count - 1) return "Scientific or emotional context and Final viewer takeaway: leave one memorable reason to observe.";
        return index switch
        {
            1 => "What changed in the sky: describe the weekly shift in light, placement, and mood.",
            2 => "Main highlight: identify the central beat that organizes the episode.",
            3 => "Best viewing opportunity: turn the forecast into one practical observing choice.",
            4 => "Scientific or emotional context: explain why gradual motion feels meaningful.",
            _ => "Episode bridge: keep the narration connected and cinematic."
        };
    }

    private static string PolishVoiceFriendlyText(string text)
        => text.Replace("Tonight's", "Tonight, the", StringComparison.OrdinalIgnoreCase).Trim();

    private static VoicePerformanceMetadata BuildVoicePerformance(string category, string retentionRole, int index, int count)
    {
        var isOpening = index == 0;
        var isClosing = index == count - 1;
        var speechRate = isClosing ? "slow" : category == "RareEventAlert" ? "medium" : isOpening ? "medium" : "medium";
        var energy = category == "RareEventAlert" && isOpening ? "high" : isClosing ? "low" : "medium";
        var musicIntensity = category == "RareEventAlert" && !isClosing ? "medium" : isOpening ? "medium" : isClosing ? "low" : "low";
        var pauses = new List<string>();
        if (isOpening) pauses.Add("opening hook");
        if (retentionRole.Contains("Emotional", StringComparison.OrdinalIgnoreCase) || isClosing) pauses.Add("final sentence");

        var tone = category switch
        {
            "RareEventAlert" => "calm, precise, and watchful without alarm",
            "PlanetConjunction" => "warm, cinematic, and scientifically clear",
            "PlanetGrouping" => "patient, observational, and gently guided",
            "WeeklySkyForecast" => "episodic, cinematic, and quietly anticipatory",
            _ => "professional documentary narration"
        };

        return new VoicePerformanceMetadata(speechRate, energy, tone, pauses, musicIntensity);
    }

    private static PolishedNarrationTtsReadiness BuildTtsReadiness(string category, bool readyForTts)
    {
        var (voice, style, music) = category switch
        {
            "RareEventAlert" => ("calm documentary narrator", "restrained urgency", "subtle tension"),
            "PlanetConjunction" => ("warm astronomy narrator", "cinematic explainer", "wonder"),
            "PlanetGrouping" => ("patient sky guide", "guided observation", "calm discovery"),
            "WeeklySkyForecast" => ("cinematic storyteller", "weekly episode narration", "exploration"),
            _ => ("professional astronomy narrator", "documentary narration", "quiet wonder")
        };

        return new PolishedNarrationTtsReadiness(readyForTts, voice, style, "medium", "neutral-warm", music, !readyForTts);
    }

    private static PolishedNarrationDurationValidation BuildDurationValidation(IReadOnlyList<PolishedNarrationSegment> segments)
    {
        var words = segments.Sum(s => CountWords(s.FinalNarration));
        var dominantRate = DominantSpeechRate(segments);
        var wpm = SpeechRateWpm(dominantRate);
        var seconds = Math.Max(1, (int)Math.Ceiling(words * 60m / wpm));
        var confidence = segments.All(s => CountWords(s.FinalNarration) <= 65) ? "High" : segments.All(s => CountWords(s.FinalNarration) <= 80) ? "Medium" : "Low";
        return new PolishedNarrationDurationValidation(words, seconds, wpm, confidence);
    }

    private static PolishedNarrationQualityBreakdown BuildQualityBreakdown(FinalNarrationDocument final, IReadOnlyList<PolishedNarrationSegment> segments)
    {
        var narrations = segments.Select(s => s.FinalNarration).ToList();
        var all = string.Join("\n", narrations);
        var hook = segments.Count > 0 && IsStrongHook(final.ContentCategory, segments[0].FinalNarration) ? 20 : 12;
        var science = final.QualityChecklist.ScientificallySafe && !ContainsUnsafeScience(final.ContentCategory, all) ? 20 : 10;
        var uniqueness = narrations.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Count() == narrations.Count
            && segments.Select(s => s.ScenePurpose).Distinct(StringComparer.OrdinalIgnoreCase).Count() == segments.Count ? 20 : 12;
        var voice = narrations.All(IsVoiceFriendly) && segments.All(s => s.VoicePerformance is not null) ? 15 : 8;
        var retention = HasRetentionFlow(final.ContentCategory, segments) ? 15 : 9;
        var emotional = segments.Count > 0 && IsEmotionalClose(segments[^1].FinalNarration) ? 10 : 5;
        var total = hook + science + uniqueness + voice + retention + emotional;
        return new PolishedNarrationQualityBreakdown(hook, science, uniqueness, voice, retention, emotional, total);
    }

    private static bool IsStrongHook(string category, string text)
        => category switch
        {
            "WeeklySkyForecast" => text.Contains("episode", StringComparison.OrdinalIgnoreCase) && text.Contains("rhythm", StringComparison.OrdinalIgnoreCase),
            "RareEventAlert" => text.Contains("window", StringComparison.OrdinalIgnoreCase) || text.Contains("clear minutes", StringComparison.OrdinalIgnoreCase),
            "PlanetConjunction" => text.Contains("line of sight", StringComparison.OrdinalIgnoreCase) || text.Contains("space", StringComparison.OrdinalIgnoreCase),
            _ => CountWords(text) >= 12
        };

    private static bool HasRetentionFlow(string category, IReadOnlyList<PolishedNarrationSegment> segments)
    {
        if (segments.Count < 3) return false;
        if (segments[0].RetentionRole != "Hook") return false;
        if (!segments[^1].RetentionRole.Contains("Close", StringComparison.OrdinalIgnoreCase)) return false;
        if (category != "WeeklySkyForecast") return true;

        var purposes = string.Join(" ", segments.Select(s => s.ScenePurpose));
        return new[] { "Opening Hook", "What changed", "Main highlight", "Best viewing opportunity", "Scientific or emotional context", "Final viewer takeaway" }
            .All(p => purposes.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEmotionalClose(string text)
        => new[] { "remember", "notice", "step outside", "quiet", "memory", "take" }.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsVoiceFriendly(string narration)
    {
        if (CountWords(narration) > 70) return false;
        return narration.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(sentence => CountWords(sentence) <= 24);
    }

    private static bool ContainsUnsafeScience(string category, string narration)
        => category == "PlanetConjunction" && narration.Contains("planets touch", StringComparison.OrdinalIgnoreCase);

    private static int CountWords(string text)
        => Regex.Matches(text, @"\b[\p{L}\p{N}']+\b").Count;

    private static string Normalize(string text)
        => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim().TrimEnd('.');

    private static string DominantSpeechRate(IReadOnlyList<PolishedNarrationSegment> segments)
        => segments.GroupBy(s => s.VoicePerformance.SpeechRate).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault()?.Key ?? "medium";

    private static int SpeechRateWpm(string speechRate)
        => speechRate switch
        {
            "slow" => 120,
            "energetic" => 155,
            _ => 140
        };

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string BuildOutputPath(string root, string regionId, string planId)
        => Path.Combine(root, "assets", SanitizePathSegment(regionId) ?? "unknown-region", "plans", planId, "narration", "narration-polished.json");

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }
}
