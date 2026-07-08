namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record VisualQualityFramework
{
    public const string Version = "RC1-A.1";

    public required string FrameworkVersion { get; init; }
    public required string VisualRealism { get; init; }
    public required string SubjectPriority { get; init; }
    public required string LightingStyle { get; init; }
    public required string CompositionStyle { get; init; }
    public required string EditorialStyle { get; init; }
    public required string CameraStyle { get; init; }
    public required string DepthStyle { get; init; }
    public required string ColorStyle { get; init; }
    public required string BackgroundPolicy { get; init; }
    public required string NegativePromptPolicy { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> DomainOverrides { get; init; }

    public string BuildPromptPolicyText(string domain = "Astronomy")
    {
        var domainRules = DomainOverrides.TryGetValue(domain, out var rules) ? string.Join(" ", rules) : string.Empty;
        return string.Join(" ", new[]
        {
            $"Visual Quality Framework {FrameworkVersion}.",
            $"Visual realism: {VisualRealism}",
            $"Subject priority: {SubjectPriority}",
            $"Lighting style: {LightingStyle}",
            $"Composition style: {CompositionStyle}",
            $"Editorial style: {EditorialStyle}",
            $"Camera style: {CameraStyle}",
            $"Depth style: {DepthStyle}",
            $"Color style: {ColorStyle}",
            $"Background policy: {BackgroundPolicy}",
            $"Negative prompt policy: {NegativePromptPolicy}",
            domainRules
        }).Trim();
    }

    public VisualQualityFrameworkReview CreateReview(params string[] productsUsingFramework) => new(
        FrameworkVersion,
        productsUsingFramework.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        DomainOverrides,
        [],
        ["Keep VisualQualityFramework domain-independent; add future WildlifeVisualRules, HistoryVisualRules, MarineVisualRules, GeographyVisualRules, OceanVisualRules, and SpaceMissionVisualRules as domain overrides instead of duplicating product prompts."]);

    public static VisualQualityFramework Astronomy() => new()
    {
        FrameworkVersion = Version,
        VisualRealism = "Premium science-documentary realism; primary subjects must be physically recognizable with natural scale cues and no fantasy, painterly, cartoon, or CGI look.",
        SubjectPriority = "One Hero Per Frame: primary subject, then supporting subject, then context, then overlay safe area; decorative elements must never become visual heroes.",
        LightingStyle = "Use twilight, blue hour, astronomical night, or natural sunset lighting; avoid neon, fantasy glow, oversaturated skies, and unrealistic glow.",
        CompositionStyle = "Clear editorial hierarchy with one primary visual subject, supporting subjects subordinate, contextual sky/horizon only when useful, and clean overlay-safe negative space.",
        EditorialStyle = "Premium Science Documentary; never fantasy artwork, concept art, or movie poster styling.",
        CameraStyle = "Premium telescope or documentary camera language appropriate to the subject; no app screenshots, star charts, diagrams, or UI.",
        DepthStyle = "Natural atmospheric depth and documentary separation without artificial bokeh dominating astronomy details.",
        ColorStyle = "Natural color, scientifically respectful contrast, restrained saturation, and correct apparent texture for celestial objects.",
        BackgroundPolicy = "Background supports the primary subject; stars are support only and never dominate; Milky Way appears only when scientifically appropriate.",
        NegativePromptPolicy = "Exclude fantasy rendering, painterly style, cartoon style, CGI look, neon skies, unrealistic glow, oversaturated skies, labels, embedded text, UI, watermarks, unrelated celestial objects, and decorative hero elements.",
        DomainOverrides = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Astronomy"] =
            [
                "Planets must be physically recognizable, circular, premium telescope quality, natural color, with correct apparent texture; no fantasy rendering, painterly style, cartoon style, or CGI look.",
                "Moon imagery must respect phase, illumination, orientation, and maria.",
                "Stars are supporting context only and must never dominate the frame.",
                "Milky Way is allowed only when scientifically appropriate for the event and story context."
            ]
        }
    };
}

public sealed record VisualQualityFrameworkReview(
    string FrameworkVersion,
    IReadOnlyList<string> ProductsUsingFramework,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DomainOverrides,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Recommendations);
