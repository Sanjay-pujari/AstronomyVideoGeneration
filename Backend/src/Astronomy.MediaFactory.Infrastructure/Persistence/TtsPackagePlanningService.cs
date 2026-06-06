using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed partial class TtsPackagePlanningService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<TtsPackagePlanningService> logger) : ITtsPackagePlanningService
{
    private const string GenerationSource = "Phase9B";
    private const string Provider = "AzureSpeech";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<TtsPackagePlanningResult> GenerateTtsPackagesAsync(TtsPackagePlanningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var packages = new List<TtsPackageDocument>();
        var root = ResolveWorkingDirectoryRoot();
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim();
        var candidates = await ResolveCandidatesAsync(root, request, warnings, cancellationToken);

        foreach (var candidate in candidates)
        {
            try
            {
                var outputPath = BuildOutputPath(root, candidate.Polished.RegionId, candidate.Polished.ContentGenerationPlanId);
                if (!request.DryRun && File.Exists(outputPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing TTS package for plan {candidate.Polished.ContentGenerationPlanId}. Set overwriteExisting=true to replace it.");
                    continue;
                }

                var package = BuildPackage(candidate.Polished, candidate.PlannedFormat, language, root);
                packages.Add(package);

                if (!request.DryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? root);
                    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(package, JsonOptions), cancellationToken);
                    generatedFiles.Add(outputPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to create TTS package for polished narration '{candidate.Path}': {ex.Message}");
                logger.LogWarning(ex, "Phase 9B TTS package generation failed for {Path}", candidate.Path);
            }
        }

        var ready = packages.Count(p => p.ReadyForAudioGeneration);
        logger.LogInformation("Phase 9B processed {PlanCount} polished narration(s). Generated={GeneratedCount} ReadyForAudio={ReadyForAudioCount} DryRun={DryRun}", candidates.Count, packages.Count, ready, request.DryRun);
        return new TtsPackagePlanningResult(candidates.Count, packages.Count, ready, packages, generatedFiles, warnings);
    }

    private async Task<IReadOnlyList<TtsCandidate>> ResolveCandidatesAsync(string root, TtsPackagePlanningRequest request, List<string> warnings, CancellationToken cancellationToken)
    {
        var requestedPlanIds = request.PlanIds is { Count: > 0 }
            ? request.PlanIds.ToHashSet()
            : null;
        var requestedCategories = ToSet(request.ContentCategories);
        var requestedFormats = ToSet(request.PlannedFormats);
        var region = SanitizePathSegment(request.RegionId);
        var files = EnumeratePolishedNarrationFiles(root, region, requestedPlanIds);
        var candidates = new List<TtsCandidate>();

        foreach (var path in files)
        {
            if (request.MaxPlans is { } maxPlans && candidates.Count >= maxPlans)
                break;

            try
            {
                var polished = await ReadPolishedNarrationAsync(path, cancellationToken);
                if (polished is null)
                {
                    warnings.Add($"Skipped invalid polished narration JSON at {path}.");
                    continue;
                }

                if (requestedPlanIds is not null && (!Guid.TryParse(polished.ContentGenerationPlanId, out var planGuid) || !requestedPlanIds.Contains(planGuid)))
                    continue;
                if (!string.IsNullOrWhiteSpace(request.RegionId) && !string.Equals(polished.RegionId, request.RegionId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (requestedCategories is not null && !requestedCategories.Contains(polished.ContentCategory))
                    continue;

                var plannedFormat = await ResolvePlannedFormatAsync(root, polished.RegionId, polished.ContentGenerationPlanId, cancellationToken);
                if (requestedFormats is not null && !requestedFormats.Contains(plannedFormat))
                    continue;

                candidates.Add(new TtsCandidate(path, polished, plannedFormat));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Skipped polished narration at {path}: {ex.Message}");
            }
        }

        return candidates;
    }

    private static IEnumerable<string> EnumeratePolishedNarrationFiles(string root, string? region, HashSet<Guid>? requestedPlanIds)
    {
        var assetsRoot = Path.Combine(root, "assets");
        if (requestedPlanIds is { Count: > 0 })
        {
            var regionRoots = !string.IsNullOrWhiteSpace(region)
                ? new[] { Path.Combine(assetsRoot, region) }
                : Directory.Exists(assetsRoot) ? Directory.EnumerateDirectories(assetsRoot).ToArray() : Array.Empty<string>();

            return regionRoots.SelectMany(regionRoot => requestedPlanIds.Select(planId => Path.Combine(regionRoot, "plans", planId.ToString("D"), "narration", "narration-polished.json")))
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var searchRoot = !string.IsNullOrWhiteSpace(region) ? Path.Combine(assetsRoot, region, "plans") : assetsRoot;
        if (!Directory.Exists(searchRoot))
            return [];

        return Directory.EnumerateFiles(searchRoot, "narration-polished.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<PolishedNarrationDocument?> ReadPolishedNarrationAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PolishedNarrationDocument>(stream, JsonOptions, cancellationToken);
    }

    private async Task<string> ResolvePlannedFormatAsync(string root, string regionId, string planId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(planId, out var guid))
            return string.Empty;

        var dbFormat = await db.ContentGenerationPlans
            .AsNoTracking()
            .Where(p => p.Id == guid)
            .Select(p => p.PlannedFormat ?? string.Empty)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dbFormat))
            return dbFormat;

        var scriptPath = Path.Combine(root, "assets", SanitizePathSegment(regionId) ?? "unknown-region", "plans", planId, "narration", $"narration-script-{planId}.json");
        if (!File.Exists(scriptPath))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scriptPath, cancellationToken));
            return document.RootElement.TryGetProperty("plannedFormat", out var plannedFormat)
                ? plannedFormat.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static TtsPackageDocument BuildPackage(PolishedNarrationDocument polished, string plannedFormat, string language, string root)
    {
        var voice = BuildVoiceProfile(polished);
        var music = BuildMusicProfile(polished.ContentCategory, polished.TtsReadiness.RecommendedMusicMood);
        var segments = polished.Segments.Select(segment => BuildSegment(polished, segment, voice, root)).ToList();
        var totalDuration = segments.Sum(s => s.EstimatedDurationSeconds);

        return new TtsPackageDocument(
            polished.ContentGenerationPlanId,
            polished.RegionId,
            language,
            polished.ContentCategory,
            plannedFormat,
            polished.Title,
            Provider,
            voice,
            music,
            segments,
            totalDuration,
            segments.Count > 0,
            GenerationSource,
            DateTimeOffset.UtcNow);
    }

    private static TtsPackageSegment BuildSegment(PolishedNarrationDocument polished, PolishedNarrationSegment segment, TtsVoiceProfile voice, string root)
    {
        var text = NormalizeWhitespace(segment.FinalNarration);
        var duration = EstimateDurationSeconds(text, voice.Rate);
        return new TtsPackageSegment(
            segment.SceneNumber,
            segment.SceneName,
            text,
            BuildSsml(text, segment.PauseHints, segment.EmphasisWords, voice),
            duration,
            segment.PauseHints,
            segment.EmphasisWords,
            segment.VoicePerformance,
            BuildSegmentAudioPath(root, polished.RegionId, polished.ContentGenerationPlanId, segment.SceneNumber));
    }

    private static TtsVoiceProfile BuildVoiceProfile(PolishedNarrationDocument polished)
        => polished.ContentCategory switch
        {
            "RareEventAlert" => new("calm urgent newscaster", "en-US-GuyNeural", "serious newscast documentary", "neutral", "-5%", "medium"),
            "PlanetConjunction" => new("calm documentary guide", "en-US-DavisNeural", "documentary calm", "neutral-warm", "-3%", "medium"),
            "PlanetGrouping" => new("calm sky guide", "en-US-DavisNeural", "calm guide", polished.TtsReadiness.RecommendedPitch, "-3%", "medium"),
            "WeeklySkyForecast" => new("storyteller", "en-US-AriaNeural", "narrative", polished.TtsReadiness.RecommendedPitch, "-4%", "medium"),
            _ => new(polished.TtsReadiness.RecommendedVoice, "en-US-GuyNeural", polished.TtsReadiness.RecommendedStyle, polished.TtsReadiness.RecommendedPitch, polished.TtsReadiness.RecommendedSpeechRate, "medium")
        };

    private static TtsMusicProfile BuildMusicProfile(string category, string fallbackMood)
        => category switch
        {
            "RareEventAlert" => new("subtle tension", "low", "cinematic tension bed"),
            "PlanetConjunction" => new("wonder", "low-medium", "ambient wonder"),
            "PlanetGrouping" => new("calm discovery", "low", "ambient discovery"),
            "WeeklySkyForecast" => new("exploration", "medium", "cinematic exploration"),
            _ => new(string.IsNullOrWhiteSpace(fallbackMood) ? "calm astronomy" : fallbackMood, "low", "ambient astronomy")
        };

    private static string BuildSsml(string text, IReadOnlyList<string> pauseHints, IReadOnlyList<string> emphasisWords, TtsVoiceProfile voice)
    {
        var sentenceParts = SentenceRegex().Split(text).Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
        var emphasized = emphasisWords.Where(w => !string.IsNullOrWhiteSpace(w)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();
        var elements = new List<object>();
        var pauseIndex = 0;
        foreach (var part in sentenceParts)
        {
            var fragment = XElement.Parse($"<fragment>{ApplyEmphasis(SecurityElementEscape(part.Trim()), emphasized)}</fragment>");
            foreach (var node in fragment.Nodes())
                elements.Add(node);
            if (pauseIndex < pauseHints.Count)
            {
                elements.Add(new XElement("break", new XAttribute("time", BreakDuration(pauseHints[pauseIndex]))));
                pauseIndex++;
            }
        }

        var prosody = new XElement("prosody",
            new XAttribute("rate", voice.Rate),
            new XAttribute("pitch", ToSsmlPitch(voice.Pitch)),
            new XAttribute("volume", voice.Volume));
        foreach (var element in elements)
            prosody.Add(element);

        var speak = new XElement("speak",
            new XAttribute("version", "1.0"),
            new XAttribute(XNamespace.Xml + "lang", "en-US"),
            new XElement("voice",
                new XAttribute("name", voice.VoiceName),
                prosody));

        return speak.ToString(SaveOptions.DisableFormatting);
    }

    private static string ApplyEmphasis(string escapedText, IReadOnlyList<string> emphasisWords)
    {
        var result = escapedText;
        foreach (var word in emphasisWords)
        {
            var escapedWord = Regex.Escape(SecurityElementEscape(word));
            result = Regex.Replace(result, $"(?<![\\p{{L}}\\p{{N}}])({escapedWord})(?![\\p{{L}}\\p{{N}}])", "<emphasis level=\"moderate\">$1</emphasis>", RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static string BreakDuration(string hint)
    {
        if (hint.Contains("long", StringComparison.OrdinalIgnoreCase) || hint.Contains("dramatic", StringComparison.OrdinalIgnoreCase)) return "700ms";
        if (hint.Contains("short", StringComparison.OrdinalIgnoreCase) || hint.Contains("brief", StringComparison.OrdinalIgnoreCase)) return "300ms";
        return "500ms";
    }

    private static string ToSsmlPitch(string pitch)
        => pitch switch
        {
            var value when value.Contains("warm", StringComparison.OrdinalIgnoreCase) => "+2%",
            var value when value.Contains("low", StringComparison.OrdinalIgnoreCase) => "-2%",
            _ => "+0%"
        };

    private static int EstimateDurationSeconds(string text, string rate)
    {
        var words = WordRegex().Matches(text).Count;
        var wpm = rate switch
        {
            "-5%" => 133,
            "-4%" => 134,
            "-3%" => 136,
            "slow" => 120,
            _ => 140
        };
        return Math.Max(1, (int)Math.Ceiling(words / (wpm / 60d)));
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string BuildOutputPath(string root, string regionId, string planId)
        => Path.Combine(root, "assets", SanitizePathSegment(regionId) ?? "unknown-region", "plans", planId, "tts", "tts-package.json");

    private static string BuildSegmentAudioPath(string root, string regionId, string planId, int sceneNumber)
        => Path.Combine(root, "assets", SanitizePathSegment(regionId) ?? "unknown-region", "plans", planId, "tts", "audio", $"scene-{sceneNumber:00}.wav");

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
            ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

    private static string NormalizeWhitespace(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string SecurityElementEscape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private sealed record TtsCandidate(string Path, PolishedNarrationDocument Polished, string PlannedFormat);

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\b[\p{L}\p{N}']+\b")]
    private static partial Regex WordRegex();
}
