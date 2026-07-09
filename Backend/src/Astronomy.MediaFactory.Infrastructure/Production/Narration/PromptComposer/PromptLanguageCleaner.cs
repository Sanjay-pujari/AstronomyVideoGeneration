using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class PromptLanguageCleaner
{
    private static readonly (string Bad, string Good)[] Replacements =
    [
        ("metadata", "production notes"), ("prompt", "brief"), ("JSON", "structured notes"),
        ("Viewer should", "The audience should come away able to"), ("viewer should", "the audience should come away able to"),
        ("Scene goal", "Editorial aim"), ("scene goal", "editorial aim"), ("Planning", "Editorial preparation"), ("planning", "editorial preparation"),
        ("Checklist", "Creative priorities"), ("checklist", "creative priorities"), ("Facts to mention", "Natural sky details"), ("facts to mention", "natural sky details"),
        ("Available facts", "Confirmed sky details"), ("available facts", "confirmed sky details"), ("Verified details", "confirmed sky details"),
        ("event identity", "what the audience is seeing")
    ];

    public string Clean(string text) => CleanText(text);

    public static string CleanText(string text)
    {
        var cleaned = text ?? string.Empty;
        foreach (var (bad, good) in Replacements) cleaned = cleaned.Replace(bad, good, StringComparison.OrdinalIgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        return cleaned.Trim();
    }
}
