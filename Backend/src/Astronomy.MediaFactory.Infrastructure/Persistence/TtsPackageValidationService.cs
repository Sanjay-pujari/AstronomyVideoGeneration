using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed partial class TtsPackageValidationService(
    IOptions<RenderingOptions> renderingOptions,
    ILogger<TtsPackageValidationService> logger) : ITtsPackageValidationService
{
    private const string InputFileName = "tts-package.json";
    private const string CleanFileName = "tts-package-clean.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> ValidVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        "en-US-GuyNeural",
        "en-US-DavisNeural",
        "en-US-AriaNeural",
        "en-US-JennyNeural",
        "en-US-AvaNeural",
        "en-US-AndrewNeural",
        "en-US-EmmaNeural",
        "en-US-BrianNeural"
    };
    private static readonly HashSet<string> NamedRates = new(StringComparer.OrdinalIgnoreCase) { "x-slow", "slow", "medium", "fast", "x-fast", "default" };
    private static readonly HashSet<string> NamedPitches = new(StringComparer.OrdinalIgnoreCase) { "x-low", "low", "medium", "high", "x-high", "default" };
    private static readonly HashSet<string> NamedVolumes = new(StringComparer.OrdinalIgnoreCase) { "silent", "x-soft", "soft", "medium", "loud", "x-loud", "default" };

    public async Task<TtsPackageValidationResult> ValidateTtsPackagesAsync(TtsPackageValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var cleanPackages = new List<CleanTtsPackageDocument>();
        var candidates = EnumeratePackageFiles(root, request).ToList();

        foreach (var path in candidates)
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var package = await JsonSerializer.DeserializeAsync<TtsPackageDocument>(stream, JsonOptions, cancellationToken);
                if (package is null)
                {
                    warnings.Add($"Skipped invalid TTS package JSON at {path}.");
                    continue;
                }

                if (!MatchesFilters(package, request))
                    continue;

                var clean = CleanAndValidatePackage(package);
                cleanPackages.Add(clean);

                var outputPath = Path.Combine(Path.GetDirectoryName(path) ?? root, CleanFileName);
                if (!request.DryRun)
                {
                    if (File.Exists(outputPath) && !request.OverwriteExisting)
                    {
                        warnings.Add($"Skipped existing clean TTS package for plan {package.ContentGenerationPlanId}. Set overwriteExisting=true to replace it.");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? root);
                    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(clean, JsonOptions), cancellationToken);
                    generatedFiles.Add(outputPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to validate TTS package '{path}': {ex.Message}");
                logger.LogWarning(ex, "Phase 9B.1 TTS package validation failed for {Path}", path);
            }
        }

        var validCount = cleanPackages.Count(p => p.ReadyForTts);
        var invalidCount = cleanPackages.Count - validCount;
        var fixedCount = cleanPackages.Count(p => p.SegmentValidationResults.Any(r => r.FixesApplied.Count > 0));
        logger.LogInformation("Phase 9B.1 validated {PlanCount} TTS package(s). Valid={ValidCount} Fixed={FixedCount} Invalid={InvalidCount} DryRun={DryRun}", cleanPackages.Count, validCount, fixedCount, invalidCount, request.DryRun);
        return new TtsPackageValidationResult(cleanPackages.Count, validCount, fixedCount, invalidCount, cleanPackages, generatedFiles, warnings);
    }

    private CleanTtsPackageDocument CleanAndValidatePackage(TtsPackageDocument package)
    {
        var cleanSegments = new List<TtsPackageSegment>();
        var validationResults = new List<TtsSegmentValidationResult>();
        var lastScene = package.Segments.Count == 0 ? 0 : package.Segments.Max(s => s.SceneNumber);

        foreach (var segment in package.Segments)
        {
            var (cleanSegment, result) = CleanAndValidateSegment(segment, package.VoiceProfile, segment.SceneNumber == lastScene);
            cleanSegments.Add(cleanSegment);
            validationResults.Add(result);
        }

        var ready = cleanSegments.Count > 0 && validationResults.All(r => r.IsValid);
        return new CleanTtsPackageDocument(
            package.ContentGenerationPlanId,
            package.RegionId,
            package.Language,
            package.ContentCategory,
            package.PlannedFormat,
            package.Title,
            package.TtsProvider,
            package.VoiceProfile,
            package.MusicProfile,
            cleanSegments,
            package.TotalEstimatedDurationSeconds,
            ready,
            package.GenerationSource,
            package.GeneratedUtc,
            ready ? "Valid" : "Invalid",
            DateTimeOffset.UtcNow,
            ready,
            validationResults);
    }

    private static (TtsPackageSegment Segment, TtsSegmentValidationResult Result) CleanAndValidateSegment(TtsPackageSegment segment, TtsVoiceProfile voiceProfile, bool isFinalScene)
    {
        var issues = new List<string>();
        var fixes = new List<string>();
        var cleanSsml = CleanupSentenceBoundaries(segment.Ssml, fixes);
        XDocument? document = null;

        try
        {
            document = XDocument.Parse(cleanSsml, LoadOptions.PreserveWhitespace);
            CleanupBreaks(document, isFinalScene, fixes);
            CleanupEmphasis(document, segment.Text, fixes);
            cleanSsml = document.Root!.ToString(SaveOptions.DisableFormatting);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            issues.Add($"SSML XML parse failed: {ex.Message}");
        }

        if (document is not null)
            ValidateXml(document, segment, voiceProfile, issues);

        ValidateOutputAudioPath(segment, issues);

        var cleanSegment = segment with { Ssml = cleanSsml };
        return (cleanSegment, new TtsSegmentValidationResult(segment.SceneNumber, issues.Count == 0, issues, fixes));
    }

    private static string CleanupSentenceBoundaries(string ssml, List<string> fixes)
    {
        if (string.IsNullOrWhiteSpace(ssml))
            return ssml;

        var matchCount = SentenceBoundaryRegex().Matches(ssml).Count;
        if (matchCount == 0)
            return ssml;

        fixes.Add($"Inserted 300ms SSML sentence break(s) at {matchCount} merged or unbroken sentence boundary/boundaries.");
        return SentenceBoundaryRegex().Replace(ssml, "<break time=\"300ms\" />");
    }

    private static void CleanupBreaks(XDocument document, bool isFinalScene, List<string> fixes)
    {
        foreach (var breakElement in document.Descendants().Where(IsBreakElement).ToList())
        {
            var time = breakElement.Attribute("time")?.Value;
            if (!isFinalScene && TryParseBreakMilliseconds(time, out var milliseconds) && milliseconds > 1500)
            {
                breakElement.SetAttributeValue("time", "1500ms");
                fixes.Add($"Capped non-final-scene break from {milliseconds}ms to 1500ms.");
            }
        }

        foreach (var parent in document.Descendants().ToList())
        {
            var consecutiveBreaks = 0;
            foreach (var node in parent.Nodes().ToList())
            {
                if (node is XElement element && string.Equals(element.Name.LocalName, "break", StringComparison.OrdinalIgnoreCase))
                {
                    consecutiveBreaks++;
                    if (consecutiveBreaks > 2)
                    {
                        element.Remove();
                        fixes.Add("Removed break beyond two consecutive breaks.");
                    }
                }
                else if (node is XText text && string.IsNullOrWhiteSpace(text.Value))
                {
                    continue;
                }
                else
                {
                    consecutiveBreaks = 0;
                }
            }
        }
    }

    private static void CleanupEmphasis(XDocument document, string text, List<string> fixes)
    {
        var allowed = WordRegex().Matches(text ?? string.Empty).Count > 25 ? int.MaxValue : 2;
        var emphasisElements = document.Descendants().Where(IsEmphasisElement).ToList();
        var kept = 0;
        foreach (var emphasis in emphasisElements)
        {
            var emphasizedText = NormalizeWhitespace(ExtractPlainText(emphasis));
            var existsInText = !string.IsNullOrWhiteSpace(emphasizedText) && ContainsNormalized(text, emphasizedText);
            kept++;
            if (!existsInText || kept > allowed)
            {
                emphasis.ReplaceWith(emphasis.Nodes());
                fixes.Add(!existsInText
                    ? $"Removed emphasis for text not present in segment text: '{emphasizedText}'."
                    : "Removed excess emphasis tag beyond the segment limit.");
            }
        }
    }

    private static void ValidateXml(XDocument document, TtsPackageSegment segment, TtsVoiceProfile voiceProfile, List<string> issues)
    {
        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "speak", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("SSML must have a <speak> root element.");
            return;
        }

        var voice = root.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "voice", StringComparison.OrdinalIgnoreCase));
        if (voice is null)
            issues.Add("SSML must include a <voice> element.");

        var prosody = root.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "prosody", StringComparison.OrdinalIgnoreCase));
        if (prosody is null)
            issues.Add("SSML must include a <prosody> element.");

        var voiceName = voice?.Attribute("name")?.Value;
        if (string.IsNullOrWhiteSpace(voiceName) || !IsValidVoiceName(voiceName))
            issues.Add($"SSML voice name is invalid: '{voiceName ?? string.Empty}'.");

        ValidateProsodyAttribute(prosody, "rate", voiceProfile.Rate, IsValidRate, issues);
        ValidateProsodyAttribute(prosody, "pitch", voiceProfile.Pitch, IsValidPitch, issues);
        ValidateProsodyAttribute(prosody, "volume", voiceProfile.Volume, IsValidVolume, issues);
        ValidateTextConsistency(document, segment.Text, issues);
        ValidateBreaks(document, segment.Text, issues);
        ValidateEmphasis(document, segment.Text, issues);
    }

    private static void ValidateProsodyAttribute(XElement? prosody, string attributeName, string profileValue, Func<string, bool> validator, List<string> issues)
    {
        var value = prosody?.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value) || !validator(value))
            issues.Add($"SSML prosody {attributeName} is invalid: '{value ?? string.Empty}'. Voice profile value='{profileValue ?? string.Empty}'.");
    }

    private static void ValidateTextConsistency(XDocument document, string text, List<string> issues)
    {
        var spoken = NormalizeWhitespace(ExtractSpokenText(document.Root!));
        var normalizedText = NormalizeWhitespace(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedText) || !ContainsNormalized(spoken, normalizedText))
            issues.Add("Segment text is not fully represented in SSML spoken text.");

        foreach (var sentence in SplitSentences(normalizedText))
        {
            if (CountOccurrences(spoken, sentence) > 1)
                issues.Add($"SSML duplicates a full sentence: '{sentence}'.");
        }
    }

    private static void ValidateBreaks(XDocument document, string text, List<string> issues)
    {
        var maxConsecutive = 0;
        foreach (var parent in document.Descendants())
        {
            var consecutive = 0;
            foreach (var node in parent.Nodes())
            {
                if (node is XElement element && string.Equals(element.Name.LocalName, "break", StringComparison.OrdinalIgnoreCase))
                {
                    consecutive++;
                    maxConsecutive = Math.Max(maxConsecutive, consecutive);
                }
                else if (node is XText textNode && string.IsNullOrWhiteSpace(textNode.Value))
                {
                    continue;
                }
                else
                {
                    consecutive = 0;
                }
            }
        }

        if (maxConsecutive > 2)
            issues.Add("SSML contains more than two consecutive breaks.");

        var spokenWithBreakMarkers = ExtractSpokenText(document.Root!, includeBreakMarkers: true);
        if (MissingBreakRegex().IsMatch(spokenWithBreakMarkers))
            issues.Add("SSML is missing a break between separate sentences.");
    }

    private static void ValidateEmphasis(XDocument document, string text, List<string> issues)
    {
        var emphasis = document.Descendants().Where(IsEmphasisElement).ToList();
        var maxAllowed = WordRegex().Matches(text ?? string.Empty).Count > 25 ? int.MaxValue : 2;
        if (emphasis.Count > maxAllowed)
            issues.Add("SSML contains too many emphasis tags for the segment length.");

        foreach (var element in emphasis)
        {
            var emphasizedText = NormalizeWhitespace(ExtractPlainText(element));
            if (string.IsNullOrWhiteSpace(emphasizedText) || !ContainsNormalized(text, emphasizedText))
                issues.Add($"Emphasis text is not present in segment text: '{emphasizedText}'.");
        }
    }

    private static void ValidateOutputAudioPath(TtsPackageSegment segment, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(segment.OutputAudioPath))
        {
            issues.Add("Segment outputAudioPath is required.");
            return;
        }

        if (!segment.OutputAudioPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            issues.Add("Segment outputAudioPath must use a .wav extension.");

        var fileName = Path.GetFileNameWithoutExtension(segment.OutputAudioPath);
        if (!fileName.Contains(segment.SceneNumber.ToString("00"), StringComparison.OrdinalIgnoreCase) && !fileName.Contains(segment.SceneNumber.ToString(), StringComparison.OrdinalIgnoreCase))
            issues.Add("Segment outputAudioPath filename must include the scene number.");
    }

    private static IEnumerable<string> EnumeratePackageFiles(string root, TtsPackageValidationRequest request)
    {
        var assetsRoot = Path.Combine(root, "assets");
        var region = SanitizePathSegment(request.RegionId);
        var requestedPlanIds = request.PlanIds is { Count: > 0 } ? request.PlanIds.ToHashSet() : null;
        IEnumerable<string> files;

        if (requestedPlanIds is { Count: > 0 })
        {
            var regionRoots = !string.IsNullOrWhiteSpace(region)
                ? new[] { Path.Combine(assetsRoot, region) }
                : Directory.Exists(assetsRoot) ? Directory.EnumerateDirectories(assetsRoot).ToArray() : [];
            files = regionRoots.SelectMany(regionRoot => requestedPlanIds.Select(planId => Path.Combine(regionRoot, "plans", planId.ToString("D"), "tts", InputFileName)));
        }
        else
        {
            var searchRoot = !string.IsNullOrWhiteSpace(region) ? Path.Combine(assetsRoot, region, "plans") : assetsRoot;
            files = Directory.Exists(searchRoot)
                ? Directory.EnumerateFiles(searchRoot, InputFileName, SearchOption.AllDirectories)
                : [];
        }

        return files.Where(File.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(request.MaxPlans ?? int.MaxValue);
    }

    private static bool MatchesFilters(TtsPackageDocument package, TtsPackageValidationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RegionId) && !string.Equals(package.RegionId, request.RegionId.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(request.Language) && !string.Equals(package.Language, request.Language.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (request.PlanIds is { Count: > 0 } planIds && (!Guid.TryParse(package.ContentGenerationPlanId, out var planId) || !planIds.Contains(planId)))
            return false;
        if (request.ContentCategories is { Count: > 0 } categories && !categories.Contains(package.ContentCategory, StringComparer.OrdinalIgnoreCase))
            return false;
        if (request.PlannedFormats is { Count: > 0 } formats && !formats.Contains(package.PlannedFormat, StringComparer.OrdinalIgnoreCase))
            return false;
        return true;
    }


    private static bool IsBreakElement(XElement element)
        => string.Equals(element.Name.LocalName, "break", StringComparison.OrdinalIgnoreCase);

    private static bool IsEmphasisElement(XElement element)
        => string.Equals(element.Name.LocalName, "emphasis", StringComparison.OrdinalIgnoreCase);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static bool IsValidVoiceName(string voiceName)
        => ValidVoices.Contains(voiceName) || VoiceNameRegex().IsMatch(voiceName);

    private static bool IsValidRate(string value)
        => NamedRates.Contains(value) || PercentRegex().IsMatch(value);

    private static bool IsValidPitch(string value)
        => NamedPitches.Contains(value) || PercentRegex().IsMatch(value);

    private static bool IsValidVolume(string value)
        => NamedVolumes.Contains(value) || PercentRegex().IsMatch(value);

    private static bool TryParseBreakMilliseconds(string? value, out int milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var match = BreakMillisecondsRegex().Match(value.Trim());
        return match.Success && int.TryParse(match.Groups[1].Value, out milliseconds);
    }

    private static string ExtractSpokenText(XElement element, bool includeBreakMarkers = false)
    {
        var parts = new List<string>();
        AppendSpokenText(element, parts, includeBreakMarkers);
        return string.Join(' ', parts);
    }

    private static void AppendSpokenText(XNode node, List<string> parts, bool includeBreakMarkers)
    {
        switch (node)
        {
            case XText text:
                if (!string.IsNullOrWhiteSpace(text.Value)) parts.Add(text.Value);
                break;
            case XElement element when string.Equals(element.Name.LocalName, "break", StringComparison.OrdinalIgnoreCase):
                parts.Add(includeBreakMarkers ? " [[BREAK]] " : " ");
                break;
            case XElement element:
                foreach (var child in element.Nodes()) AppendSpokenText(child, parts, includeBreakMarkers);
                break;
        }
    }

    private static string ExtractPlainText(XElement element)
        => string.Join(' ', element.DescendantNodes().OfType<XText>().Select(t => t.Value));

    private static IReadOnlyList<string> SplitSentences(string text)
        => SentenceSplitRegex().Split(text).Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizeWhitespace).ToList();

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return 0;
        var normalizedHaystack = NormalizeForComparison(haystack);
        var normalizedNeedle = NormalizeForComparison(needle);
        var count = 0;
        var index = 0;
        while ((index = normalizedHaystack.IndexOf(normalizedNeedle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += normalizedNeedle.Length;
        }

        return count;
    }

    private static bool ContainsNormalized(string? haystack, string? needle)
        => NormalizeForComparison(haystack ?? string.Empty).Contains(NormalizeForComparison(needle ?? string.Empty), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWhitespace(string text)
        => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizeForComparison(string text)
        => Regex.Replace(NormalizeWhitespace(text), @"\s+", " ").Trim();

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    [GeneratedRegex(@"(?<=[.!?])\s*(?=\p{Lu})")]
    private static partial Regex SentenceBoundaryRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=\p{Lu})")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"[.!?]\s*(?!\[\[BREAK\]\])(?=\p{Lu})")]
    private static partial Regex MissingBreakRegex();

    [GeneratedRegex(@"\b[\p{L}\p{N}']+\b")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"^[a-z]{2}-[A-Z]{2}-[A-Za-z]+Neural$")]
    private static partial Regex VoiceNameRegex();

    [GeneratedRegex(@"^[+-]?\d{1,3}%$")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"^(\d+)ms$", RegexOptions.IgnoreCase)]
    private static partial Regex BreakMillisecondsRegex();
}
