namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class PromptQualityEvaluator
{
    public const string Version = "AstroPulse-PromptQuality-v1";
    private static readonly string[] RequiredSections = ["Your Role", "Astro Pulse Editorial Identity", "Story Overview", "Scene Editorial Briefs", "Scientific Guardrails", "Writing Principles", "Output Contract"];
    private static readonly string[] EngineeringLeakage = ["metadata", "prompt", "json", "viewer should", "scene goal", "planning", "checklist", "facts to mention", "available facts"];

    public PromptQualityContract Evaluate(string prompt, int sceneCount, int threshold)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var lower = prompt.ToLowerInvariant();
        var missing = RequiredSections.Where(s => !prompt.Contains(s, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (missing.Length > 0) errors.Add($"Missing editorial brief sections: {string.Join(", ", missing)}.");
        if (sceneCount > 0 && CountOccurrences(prompt, "Scene ") < sceneCount) errors.Add("Not every scene appears to have an editorial brief.");
        var leakage = EngineeringLeakage.Where(p => lower.Contains(p)).ToArray();
        if (leakage.Length > 0) errors.Add($"Engineering language remains in the brief: {string.Join(", ", leakage)}.");
        if (prompt.Contains("##", StringComparison.Ordinal)) warnings.Add("Prompt preview uses section headings for readability; final requested narration remains plain text.");

        var sectionCompleteness = Math.Max(0, 100 - missing.Length * 15 - (sceneCount > 0 && CountOccurrences(prompt, "Scene ") < sceneCount ? 20 : 0));
        var engineering = Math.Max(0, 100 - leakage.Length * 20);
        var editorial = ScoreTerms(prompt, ["documentary", "audience", "wonder", "voice", "scene"], 20);
        var scientific = ScoreTerms(prompt, ["confirmed", "science", "assumptions", "integrity", "unsupported"], 20);
        var writing = ScoreTerms(prompt, ["sentence rhythm", "paragraph rhythm", "transition rhythm", "pacing", "vocabulary"], 20);
        var readability = prompt.Length > 900 && prompt.Split('\n').Average(l => l.Length) < 140 ? 95 : 75;
        var overall = (sectionCompleteness + engineering + editorial + scientific + writing + readability) / 6;
        if (overall < threshold) errors.Add($"Prompt quality score {overall} is below the configured threshold {threshold}.");

        return new PromptQualityContract(Version, overall, sectionCompleteness, editorial, scientific, writing, engineering, readability, warnings, errors, errors.Count == 0 && overall >= threshold);
    }

    private static int ScoreTerms(string text, IReadOnlyList<string> terms, int each) => Math.Min(100, terms.Count(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)) * each);
    private static int CountOccurrences(string text, string value)
    {
        var count = 0; var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0) { count++; index += value.Length; }
        return count;
    }
}
