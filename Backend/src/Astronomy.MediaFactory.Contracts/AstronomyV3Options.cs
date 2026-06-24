namespace Astronomy.MediaFactory.Contracts;

public sealed class AstronomyV3Options
{
    public const string SectionName = "AstronomyV3Options";
    public bool EnableV31NarrationIntelligence { get; set; } = true;
    public bool EnableFactExpansion { get; set; } = true;
    public bool EnableHindiNaturalization { get; set; } = true;
    public string AudienceLevel { get; set; } = "Beginner";
    public string NarrationTone { get; set; } = "Documentary";
    public int MaxInterestingFactsPerVideo { get; set; } = 2;
    public bool AllowGenericFallbackFacts { get; set; } = true;
}
