using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Core;

public sealed class NarrationCleanupService
{
    public static readonly string[] ForbiddenSectionLabels =
    [
        "Opening beat", "What it is", "Cause", "Interesting fact", "Best time",
        "Accurate sky guide", "What you will see", "Practical tips", "Final reminder"
    ];

    public static readonly string[] InstructionPrefixes =
    [
        "Explain", "Describe", "Focus on", "Call out", "Add a distinct", "Give safe", "Give", "Close with"
    ];

    public NarrationCleanupResult Clean(string sectionText)
    {
        var text = (sectionText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var labelsRemoved = 0;
        var instructionsRemoved = 0;
        var output = new List<string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1])) output.Add(string.Empty);
                continue;
            }

            if (IsAuthoringMetadata(line) || IsColonPrefixedTemplateMarker(line))
            {
                labelsRemoved++;
                continue;
            }

            var withoutLabel = RemoveLeadingSectionLabel(line, ref labelsRemoved).Trim();
            if (string.IsNullOrWhiteSpace(withoutLabel)) continue;

            if (IsPromptInstruction(withoutLabel))
            {
                instructionsRemoved++;
                continue;
            }

            output.Add(withoutLabel);
        }

        var cleaned = string.Join("\n", output);
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n").Trim();
        return new NarrationCleanupResult(cleaned, labelsRemoved, instructionsRemoved, labelsRemoved + instructionsRemoved > 0);
    }

    public void ValidateClean(string narrationText)
    {
        var text = narrationText ?? string.Empty;
        var hits = ForbiddenSectionLabels
            .Where(label => Regex.IsMatch(text, $@"(?im)^\s*{Regex.Escape(label)}\s*:"))
            .ToArray();
        if (hits.Length > 0)
            throw new InvalidOperationException("Narration cleanup validation failed: forbidden section labels remain in final narration: " + string.Join(", ", hits));

        var instructionHits = InstructionPrefixes
            .Where(prefix => Regex.IsMatch(text, $@"(?im)^\s*{Regex.Escape(prefix)}(?:\b|\.\.\.)"))
            .ToArray();
        if (instructionHits.Length > 0)
            throw new InvalidOperationException("Narration cleanup validation failed: prompt instructions remain in final narration: " + string.Join(", ", instructionHits));
    }

    private static string RemoveLeadingSectionLabel(string line, ref int labelsRemoved)
    {
        foreach (var label in ForbiddenSectionLabels)
        {
            var updated = Regex.Replace(line, $@"^\s*{Regex.Escape(label)}\s*:\s*", string.Empty, RegexOptions.IgnoreCase);
            if (!string.Equals(updated, line, StringComparison.Ordinal))
            {
                labelsRemoved++;
                return updated;
            }
        }
        return line;
    }

    private static bool IsPromptInstruction(string line)
        => InstructionPrefixes.Any(prefix => Regex.IsMatch(line, $@"^\s*{Regex.Escape(prefix)}(?:\b|\.\.\.)", RegexOptions.IgnoreCase));

    private static bool IsAuthoringMetadata(string line)
        => Regex.IsMatch(line, @"^\s*(scene|section|beat|duration|estimated duration|voice|tone|pace|tts|metadata)\s*[:=]", RegexOptions.IgnoreCase);

    private static bool IsColonPrefixedTemplateMarker(string line)
        => Regex.IsMatch(line, @"^\s*:\s*[-\w ]+\s*:?");
}

public sealed record NarrationCleanupResult(string CleanedText, int LabelsRemovedCount, int InstructionsRemovedCount, bool CleanupApplied);
