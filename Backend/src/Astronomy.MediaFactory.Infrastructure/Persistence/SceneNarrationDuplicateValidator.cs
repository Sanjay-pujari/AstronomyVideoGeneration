using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static partial class SceneNarrationDuplicateValidator
{
    public static QuestionDrivenNarrationDto ValidateAndRepair(QuestionDrivenNarrationDto narration)
    {
        var scenes = narration.Scenes.Select(ValidateAndRepair).ToArray();
        return narration with
        {
            Scenes = scenes,
            TotalEstimatedDurationSeconds = scenes.Sum(scene => scene.EstimatedDurationSeconds)
        };
    }

    public static QuestionDrivenNarrationSceneDto ValidateAndRepair(QuestionDrivenNarrationSceneDto scene)
    {
        var cleaned = RemoveDuplicates(scene.NarrationText);
        return string.Equals(cleaned, scene.NarrationText, StringComparison.Ordinal)
            ? scene
            : scene with { NarrationText = cleaned };
    }

    public static bool HasDuplicateNarration(string? text)
        => FindDuplicateKeys(text).Count > 0;

    private static string RemoveDuplicates(string? text)
    {
        var sentences = SplitSentences(text).ToArray();
        if (sentences.Length <= 1) return Clean(text);

        var seenExact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSemantic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();

        foreach (var sentence in sentences)
        {
            var exactKey = NormalizeSentence(sentence);
            if (string.IsNullOrWhiteSpace(exactKey) || !seenExact.Add(exactKey)) continue;

            var semanticKey = BuildSemanticKey(sentence);
            if (!string.IsNullOrWhiteSpace(semanticKey) && !seenSemantic.Add(semanticKey)) continue;

            kept.Add(EnsureTerminalPunctuation(sentence));
        }

        return Clean(string.Join(" ", kept));
    }

    private static IReadOnlyList<string> FindDuplicateKeys(string? text)
    {
        var duplicates = new List<string>();
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var semantic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sentence in SplitSentences(text))
        {
            var exactKey = NormalizeSentence(sentence);
            if (!string.IsNullOrWhiteSpace(exactKey) && !exact.Add(exactKey)) duplicates.Add(exactKey);
            var semanticKey = BuildSemanticKey(sentence);
            if (!string.IsNullOrWhiteSpace(semanticKey) && !semantic.Add(semanticKey)) duplicates.Add(semanticKey);
        }
        return duplicates;
    }

    private static IEnumerable<string> SplitSentences(string? text)
    {
        var source = Clean(text).Replace('\n', ' ');
        if (string.IsNullOrWhiteSpace(source)) yield break;
        foreach (var part in SentenceRegex().Split(source))
        {
            var sentence = Clean(part);
            if (!string.IsNullOrWhiteSpace(sentence)) yield return sentence;
        }
    }

    private static string BuildSemanticKey(string sentence)
    {
        var normalized = NormalizeSentence(sentence);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        if (TimingRegex().IsMatch(sentence)) return "timing:" + NormalizeTiming(sentence);
        if (TransitionRegex().IsMatch(sentence)) return "transition:" + NormalizeCoreTerms(sentence);
        if (FactRegex().IsMatch(sentence)) return "fact:" + NormalizeFact(sentence);
        return normalized;
    }

    private static string NormalizeTiming(string value)
    {
        var normalized = NormalizeSentence(value);
        normalized = Regex.Replace(normalized, @"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\b", "date", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\b\d{1,2}(?::\d{2})?\s*(?:am|pm|utc|gmt|ist|est|edt|cst|cdt|mst|mdt|pst|pdt)?\b", "time", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static string NormalizeFact(string value)
    {
        var normalized = NormalizeSentence(value);
        var originMatch = Regex.Match(normalized, @"\b(originates?|originate|comes?|come|linked|left|debris|asteroid|comet|phaethon)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (originMatch.Success)
        {
            var identifiers = Regex.Matches(normalized, @"\b(?:[0-9]+|phaethon|geminids?|asteroid|comet|debris|meteor|shower)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(word => word, StringComparer.OrdinalIgnoreCase);
            return "origin " + string.Join(' ', identifiers);
        }

        var numbers = Regex.Matches(normalized, @"\b\d+(?:\.\d+)?\b", RegexOptions.CultureInvariant).Select(match => match.Value).ToArray();
        if (numbers.Length > 0) return string.Join(' ', NormalizeCoreTerms(value), string.Join(' ', numbers));
        return NormalizeCoreTerms(value);
    }

    private static string NormalizeCoreTerms(string value)
    {
        var words = Regex.Matches(NormalizeSentence(value), @"[a-z0-9]{4,}")
            .Select(match => match.Value)
            .Where(word => !StopWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(word => word, StringComparer.OrdinalIgnoreCase);
        return string.Join(' ', words);
    }

    private static string NormalizeSentence(string value)
        => Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static string EnsureTerminalPunctuation(string value)
    {
        var cleaned = Clean(value).Trim(' ', ',', ';', ':');
        return cleaned.EndsWith('.') || cleaned.EndsWith('!') || cleaned.EndsWith('?') ? cleaned : cleaned + ".";
    }

    private static string Clean(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "that", "this", "with", "from", "into", "your", "their", "there", "about", "because", "during", "around", "after", "before", "view", "sky", "event", "tonight", "observers"
    };

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\b(time|timing|window|peak|peaks|during|around|before|after|sunset|midnight|pre[- ]dawn|evening|morning|\d{1,2}:\d{2}|\d{4}[-/]\d{1,2}[-/]\d{1,2}|\b(?:am|pm|utc|gmt|ist|est|edt|cst|cdt|mst|mdt|pst|pdt)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex TimingRegex();

    [GeneratedRegex(@"\b(fact|because|means|unusual|origin|originate|debris|asteroid|comet|moon|planet|eclipse|meteor|shower|distance|apart|alignment|tradition|named|called|scientific|science)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FactRegex();

    [GeneratedRegex(@"\b(then|next|finally|meanwhile|after that|as the scene|the moment passes|keep watching|follow for more|until then|step outside)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TransitionRegex();
}
