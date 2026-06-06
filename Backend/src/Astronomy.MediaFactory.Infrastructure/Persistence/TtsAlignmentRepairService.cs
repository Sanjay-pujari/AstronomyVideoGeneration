using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed partial class TtsAlignmentRepairService(
    IOptions<RenderingOptions> renderingOptions,
    ILogger<TtsAlignmentRepairService> logger) : ITtsAlignmentRepairService
{
    private const string RawFileName = "tts-package.json";
    private const string CleanFileName = "tts-package-clean.json";
    private const string FinalFileName = "tts-package-final.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<TtsAlignmentRepairResult> RepairTtsAlignmentAsync(TtsAlignmentRepairRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var finalPackages = new List<FinalTtsPackageDocument>();
        var candidates = EnumeratePackageFiles(root, request).ToList();

        foreach (var path in candidates)
        {
            try
            {
                var package = await ReadPackageAsync(path, cancellationToken);
                if (package is null)
                {
                    warnings.Add($"Skipped invalid TTS package JSON at {path}.");
                    continue;
                }

                if (!MatchesFilters(package.Value, request))
                    continue;

                var finalPackage = RepairPackage(package.Value);
                finalPackages.Add(finalPackage);

                var outputPath = Path.Combine(Path.GetDirectoryName(path) ?? root, FinalFileName);
                if (!request.DryRun)
                {
                    if (File.Exists(outputPath) && !request.OverwriteExisting)
                    {
                        warnings.Add($"Skipped existing final TTS package for plan {finalPackage.ContentGenerationPlanId}. Set overwriteExisting=true to replace it.");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? root);
                    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(finalPackage, JsonOptions), cancellationToken);
                    generatedFiles.Add(outputPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to repair TTS package '{path}': {ex.Message}");
                logger.LogWarning(ex, "Phase 9B.3 TTS alignment normalization failed for {Path}", path);
            }
        }

        var normalizedValidCount = finalPackages.Count(p => string.Equals(p.AlignmentRepairStatus, "NormalizedValid", StringComparison.OrdinalIgnoreCase));
        var alreadyValidCount = finalPackages.Count(p => string.Equals(p.AlignmentRepairStatus, "AlreadyValid", StringComparison.OrdinalIgnoreCase));
        var failedCount = finalPackages.Count(p => string.Equals(p.AlignmentRepairStatus, "Failed", StringComparison.OrdinalIgnoreCase));
        var readyForAudioCount = finalPackages.Count(p => p.ReadyForAudioGeneration);
        logger.LogInformation("Phase 9B.3 normalized {PlanCount} TTS package(s). NormalizedValid={NormalizedValidCount} AlreadyValid={AlreadyValidCount} Failed={FailedCount} ReadyForAudio={ReadyForAudioCount} DryRun={DryRun}", finalPackages.Count, normalizedValidCount, alreadyValidCount, failedCount, readyForAudioCount, request.DryRun);

        return new TtsAlignmentRepairResult(finalPackages.Count, normalizedValidCount, alreadyValidCount, failedCount, readyForAudioCount, finalPackages, generatedFiles, warnings);
    }

    private static async Task<RepairableTtsPackage?> ReadPackageAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        if (string.Equals(Path.GetFileName(path), CleanFileName, StringComparison.OrdinalIgnoreCase))
        {
            var clean = await JsonSerializer.DeserializeAsync<CleanTtsPackageDocument>(stream, JsonOptions, cancellationToken);
            return clean is null ? null : RepairableTtsPackage.FromClean(clean);
        }

        var raw = await JsonSerializer.DeserializeAsync<TtsPackageDocument>(stream, JsonOptions, cancellationToken);
        return raw is null ? null : RepairableTtsPackage.FromRaw(raw);
    }

    private static FinalTtsPackageDocument RepairPackage(RepairableTtsPackage package)
    {
        var repairedSegments = new List<TtsPackageSegment>();
        var validationResults = new List<TtsSegmentValidationResult>();
        var lastScene = package.Segments.Count == 0 ? 0 : package.Segments.Max(s => s.SceneNumber);
        var allOriginalSegmentsAligned = true;

        foreach (var segment in package.Segments)
        {
            var result = RepairSegment(segment, package.Language, package.VoiceProfile, segment.SceneNumber == 1, segment.SceneNumber == lastScene);
            repairedSegments.Add(result.Segment);
            validationResults.Add(result.Validation);
            if (!result.WasOriginallyAligned)
                allOriginalSegmentsAligned = false;
        }

        var ready = repairedSegments.Count > 0 && validationResults.All(r => r.IsValid);
        var repairStatus = ready
            ? allOriginalSegmentsAligned ? "AlreadyValid" : "NormalizedValid"
            : "Failed";

        return new FinalTtsPackageDocument(
            package.ContentGenerationPlanId,
            package.RegionId,
            package.Language,
            package.ContentCategory,
            package.PlannedFormat,
            package.Title,
            package.TtsProvider,
            package.VoiceProfile,
            package.MusicProfile,
            repairedSegments,
            package.TotalEstimatedDurationSeconds,
            ready,
            "Phase9B.3",
            package.GeneratedUtc,
            ready ? "Valid" : "Invalid",
            DateTimeOffset.UtcNow,
            ready,
            repairStatus,
            DateTimeOffset.UtcNow,
            validationResults);
    }

    private static SegmentRepairResult RepairSegment(TtsPackageSegment segment, string language, TtsVoiceProfile voiceProfile, bool isOpeningScene, bool isFinalScene)
    {
        var issues = new List<string>();
        var fixes = new List<string>();
        TtsAlignmentMismatchDetail? mismatch = null;
        var normalizedAligned = TryValidateAlignment(segment.Ssml, segment.Text, out mismatch);
        var legacyAligned = IsLegacyXmlAligned(segment.Ssml, segment.Text);
        var originalAligned = normalizedAligned && legacyAligned;
        var repairedSegment = segment;

        if (normalizedAligned)
        {
            if (!legacyAligned)
                fixes.Add("Accepted SSML/text alignment after inline-tag-aware normalization.");
        }
        else if (IsXmlParseable(segment.Ssml))
        {
            issues.Add("SSML/text alignment mismatch after normalization.");
            if (mismatch is not null)
            {
                issues.Add($"sourceNormalized={mismatch.SourceNormalized}");
                issues.Add($"spokenNormalized={mismatch.SpokenNormalized}");
                issues.Add($"missingWords={string.Join(',', mismatch.MissingWords)}");
            }
        }
        else
        {
            var voiceSettings = ExtractVoiceSettings(segment.Ssml, voiceProfile);
            var rebuiltSsml = BuildSsmlFromText(segment.Text, language, voiceSettings, segment.EmphasisWords, isOpeningScene, isFinalScene, fixes);
            repairedSegment = segment with { Ssml = rebuiltSsml };
            if (!TryValidateAlignment(rebuiltSsml, segment.Text, out mismatch))
            {
                issues.Add("Final SSML/text alignment mismatch.");
                if (mismatch is not null)
                {
                    issues.Add($"sourceNormalized={mismatch.SourceNormalized}");
                    issues.Add($"spokenNormalized={mismatch.SpokenNormalized}");
                    issues.Add($"missingWords={string.Join(',', mismatch.MissingWords)}");
                }
            }
        }

        ValidateOutputAudioPath(segment, issues);

        return new SegmentRepairResult(
            repairedSegment,
            new TtsSegmentValidationResult(segment.SceneNumber, issues.Count == 0, issues, fixes, issues.Count == 0 ? null : mismatch),
            originalAligned);
    }

    private static VoiceSettings ExtractVoiceSettings(string ssml, TtsVoiceProfile profile)
    {
        var voiceName = profile.VoiceName;
        var rate = profile.Rate;
        var pitch = NormalizePitch(profile.Pitch);
        var volume = profile.Volume;

        try
        {
            var document = XDocument.Parse(ssml, LoadOptions.PreserveWhitespace);
            var voice = document.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "voice", StringComparison.OrdinalIgnoreCase));
            var prosody = document.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "prosody", StringComparison.OrdinalIgnoreCase));
            voiceName = NonEmpty(voice?.Attribute("name")?.Value, voiceName);
            rate = NonEmpty(prosody?.Attribute("rate")?.Value, rate);
            pitch = NonEmpty(prosody?.Attribute("pitch")?.Value, pitch);
            volume = NonEmpty(prosody?.Attribute("volume")?.Value, volume);
        }
        catch
        {
            // Invalid source SSML is expected during repair; preserve package-level voice profile values.
        }

        return new VoiceSettings(voiceName, rate, pitch, volume);
    }

    private static string BuildSsmlFromText(
        string text,
        string language,
        VoiceSettings voiceSettings,
        IReadOnlyList<string> emphasisWords,
        bool isOpeningScene,
        bool isFinalScene,
        List<string> fixes)
    {
        fixes.Add("Rebuilt SSML from approved narration text.");

        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        XNamespace xml = XNamespace.Xml;
        var speak = new XElement(ns + "speak",
            new XAttribute("version", "1.0"),
            new XAttribute(xml + "lang", NormalizeLanguage(language)),
            new XElement(ns + "voice",
                new XAttribute("name", voiceSettings.VoiceName),
                new XElement(ns + "prosody",
                    new XAttribute("rate", voiceSettings.Rate),
                    new XAttribute("pitch", voiceSettings.Pitch),
                    new XAttribute("volume", voiceSettings.Volume))));

        var prosody = speak.Descendants(ns + "prosody").Single();
        var insertedBreaks = false;
        if (isOpeningScene && HasDramaticPauseHint(text, emphasisWords))
        {
            prosody.Add(new XElement(ns + "break", new XAttribute("time", "700ms")));
            insertedBreaks = true;
        }

        foreach (var part in SplitTextForBreaks(text))
        {
            AddTextWithEmphasis(prosody, part.Text, emphasisWords, ns);
            if (part.BreakMilliseconds is { } milliseconds)
            {
                prosody.Add(new XElement(ns + "break", new XAttribute("time", $"{milliseconds}ms")));
                insertedBreaks = true;
            }
        }

        if (isFinalScene && HasDramaticPauseHint(text, emphasisWords))
        {
            prosody.Add(new XElement(ns + "break", new XAttribute("time", "900ms")));
            insertedBreaks = true;
        }

        if (insertedBreaks)
            fixes.Add("Inserted sentence boundary breaks.");

        return speak.ToString(SaveOptions.DisableFormatting);
    }

    private static void AddTextWithEmphasis(XElement parent, string text, IReadOnlyList<string> emphasisWords, XNamespace ns)
    {
        var applicable = emphasisWords
            .Where(word => !string.IsNullOrWhiteSpace(word) && text.Contains(word, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(word => word.Length)
            .ToList();

        if (applicable.Count == 0)
        {
            parent.Add(new XText(text));
            return;
        }

        var pattern = string.Join('|', applicable.Select(Regex.Escape));
        var index = 0;
        foreach (Match match in Regex.Matches(text, pattern))
        {
            if (match.Index > index)
                parent.Add(new XText(text[index..match.Index]));

            parent.Add(new XElement(ns + "emphasis", new XAttribute("level", "moderate"), new XText(match.Value)));
            index = match.Index + match.Length;
        }

        if (index < text.Length)
            parent.Add(new XText(text[index..]));
    }

    private static IReadOnlyList<TextBreakPart> SplitTextForBreaks(string text)
    {
        var normalized = text ?? string.Empty;
        var matches = SentenceBoundaryRegex().Matches(normalized).Cast<Match>().ToList();
        if (matches.Count == 0)
            return [new TextBreakPart(normalized, null)];

        var parts = new List<TextBreakPart>();
        var start = 0;
        foreach (var match in matches)
        {
            var sentence = normalized[start..match.Index];
            parts.Add(new TextBreakPart(sentence, SelectBreakMilliseconds(sentence)));
            start = match.Index + match.Length;
        }

        parts.Add(new TextBreakPart(normalized[start..], null));
        return parts;
    }

    private static int SelectBreakMilliseconds(string sentence)
    {
        var wordCount = WordRegex().Matches(sentence ?? string.Empty).Count;
        return wordCount <= 6 ? 300 : 500;
    }

    private static bool HasDramaticPauseHint(string text, IReadOnlyList<string> emphasisWords)
    {
        var combined = string.Join(' ', emphasisWords ?? []) + " " + (text ?? string.Empty);
        return DramaticHintRegex().IsMatch(combined);
    }

    private static bool TryValidateAlignment(string ssml, string text, out TtsAlignmentMismatchDetail? mismatch)
    {
        mismatch = null;
        try
        {
            var document = XDocument.Parse(ssml, LoadOptions.PreserveWhitespace);
            var spoken = ExtractSpokenText(document.Root!);
            var sourceNormalized = NormalizeForAlignment(text);
            var spokenNormalized = NormalizeForAlignment(spoken);
            if (string.Equals(spokenNormalized, sourceNormalized, StringComparison.Ordinal))
                return true;

            mismatch = new TtsAlignmentMismatchDetail(sourceNormalized, spokenNormalized, FindMissingWords(sourceNormalized, spokenNormalized));
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            mismatch = new TtsAlignmentMismatchDetail(NormalizeForAlignment(text), string.Empty, TokenizeNormalized(NormalizeForAlignment(text)));
            return false;
        }
    }


    private static bool IsXmlParseable(string ssml)
    {
        try
        {
            XDocument.Parse(ssml, LoadOptions.PreserveWhitespace);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLegacyXmlAligned(string ssml, string text)
    {
        try
        {
            var document = XDocument.Parse(ssml, LoadOptions.PreserveWhitespace);
            var legacySpoken = string.Join(' ', document.Root!.DescendantNodes().OfType<XText>().Select(t => t.Value));
            return string.Equals(NormalizeForLegacyAlignment(legacySpoken), NormalizeForLegacyAlignment(text), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractSpokenText(XElement element)
    {
        var builder = new StringBuilder();
        AppendSpokenText(element, builder);
        return builder.ToString();
    }

    private static void AppendSpokenText(XNode node, StringBuilder builder)
    {
        switch (node)
        {
            case XText text:
                builder.Append(text.Value);
                break;
            case XElement element when string.Equals(element.Name.LocalName, "break", StringComparison.OrdinalIgnoreCase):
                builder.Append(' ');
                break;
            case XElement element:
                foreach (var child in element.Nodes())
                    AppendSpokenText(child, builder);
                break;
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
    }

    private static IEnumerable<string> EnumeratePackageFiles(string root, TtsAlignmentRepairRequest request)
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
            files = regionRoots.SelectMany(regionRoot => requestedPlanIds.Select(planId => ResolvePlanInputPath(regionRoot, planId.ToString("D"))));
        }
        else
        {
            var searchRoot = !string.IsNullOrWhiteSpace(region) ? Path.Combine(assetsRoot, region, "plans") : assetsRoot;
            files = Directory.Exists(searchRoot)
                ? Directory.EnumerateDirectories(searchRoot, "tts", SearchOption.AllDirectories).Select(ResolveTtsInputPath)
                : [];
        }

        return files.Where(File.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(request.MaxPlans ?? int.MaxValue);
    }

    private static string ResolvePlanInputPath(string regionRoot, string planId)
        => ResolveTtsInputPath(Path.Combine(regionRoot, "plans", planId, "tts"));

    private static string ResolveTtsInputPath(string ttsRoot)
    {
        var cleanPath = Path.Combine(ttsRoot, CleanFileName);
        return File.Exists(cleanPath) ? cleanPath : Path.Combine(ttsRoot, RawFileName);
    }

    private static bool MatchesFilters(RepairableTtsPackage package, TtsAlignmentRepairRequest request)
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

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string NonEmpty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeLanguage(string language)
        => string.IsNullOrWhiteSpace(language) ? "en-US" : string.Equals(language.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en-US" : language.Trim();

    private static string NormalizePitch(string pitch)
        => string.Equals(pitch, "neutral", StringComparison.OrdinalIgnoreCase) ? "+0%" : pitch;

    private static string NormalizeWhitespace(string text)
        => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizeForAlignment(string text)
    {
        var normalized = NormalizeQuotesAndDashes(WebUtility.HtmlDecode(text ?? string.Empty)).Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var withoutPunctuation = PunctuationRegex().Replace(normalized, " ");
        return NormalizeWhitespace(withoutPunctuation).Trim();
    }

    private static string NormalizeForLegacyAlignment(string text)
    {
        var withoutTagsNormalized = NormalizeWhitespace(text);
        var withoutPunctuation = PunctuationRegex().Replace(withoutTagsNormalized, " ");
        return NormalizeWhitespace(withoutPunctuation).Trim();
    }

    private static string NormalizeQuotesAndDashes(string text)
        => text
            .Replace('‘', '\'')
            .Replace('’', '\'')
            .Replace('‚', '\'')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('„', '"')
            .Replace('‐', '-')
            .Replace('‑', '-')
            .Replace('‒', '-')
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace('―', '-');

    private static IReadOnlyList<string> FindMissingWords(string sourceNormalized, string spokenNormalized)
    {
        var remaining = TokenizeNormalized(spokenNormalized).GroupBy(word => word).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var word in TokenizeNormalized(sourceNormalized))
        {
            if (remaining.TryGetValue(word, out var count) && count > 0)
                remaining[word] = count - 1;
            else
                missing.Add(word);
        }

        return missing;
    }

    private static IReadOnlyList<string> TokenizeNormalized(string normalized)
        => NormalizeWhitespace(normalized).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private readonly record struct RepairableTtsPackage(
        string ContentGenerationPlanId,
        string RegionId,
        string Language,
        string ContentCategory,
        string PlannedFormat,
        string Title,
        string TtsProvider,
        TtsVoiceProfile VoiceProfile,
        TtsMusicProfile MusicProfile,
        IReadOnlyList<TtsPackageSegment> Segments,
        int TotalEstimatedDurationSeconds,
        string GenerationSource,
        DateTimeOffset GeneratedUtc)
    {
        public static RepairableTtsPackage FromRaw(TtsPackageDocument document)
            => new(document.ContentGenerationPlanId, document.RegionId, document.Language, document.ContentCategory, document.PlannedFormat, document.Title, document.TtsProvider, document.VoiceProfile, document.MusicProfile, document.Segments, document.TotalEstimatedDurationSeconds, document.GenerationSource, document.GeneratedUtc);

        public static RepairableTtsPackage FromClean(CleanTtsPackageDocument document)
            => new(document.ContentGenerationPlanId, document.RegionId, document.Language, document.ContentCategory, document.PlannedFormat, document.Title, document.TtsProvider, document.VoiceProfile, document.MusicProfile, document.Segments, document.TotalEstimatedDurationSeconds, document.GenerationSource, document.GeneratedUtc);

    }

    private sealed record VoiceSettings(string VoiceName, string Rate, string Pitch, string Volume);

    private sealed record SegmentRepairResult(TtsPackageSegment Segment, TtsSegmentValidationResult Validation, bool WasOriginallyAligned);

    private sealed record TextBreakPart(string Text, int? BreakMilliseconds);

    [GeneratedRegex(@"(?<=[.!?])\s+(?=\S)")]
    private static partial Regex SentenceBoundaryRegex();

    [GeneratedRegex(@"\b[\p{L}\p{N}']+\b")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"(?:[\p{P}\p{S}]|\p{Cf})+")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"\b(dramatic|pause|final|opening|hook|reveal|wonder|awe)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DramaticHintRegex();
}
