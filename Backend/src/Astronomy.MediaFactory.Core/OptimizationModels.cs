using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class OptimizationPlan
{
    public string LocationName { get; init; } = "";
    public string Platform { get; init; } = "";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? RecommendedPublishTimeLocal { get; set; }
    public string? PreferredShortDurationRange { get; set; }
    public IReadOnlyCollection<string> PreferredContentObjects { get; set; } = [];
    public IReadOnlyCollection<string> AvoidedContentObjects { get; set; } = [];
    public string? RecommendedHookStyle { get; set; }
    public string? RecommendedThumbnailStyle { get; set; }
    public IReadOnlyCollection<string> RecommendedHashtags { get; set; } = [];
    public double ConfidenceScore { get; set; }
    public IReadOnlyCollection<string> Reasons { get; set; } = [];
    public IReadOnlyCollection<string> AppliedRules { get; set; } = [];
}

public sealed class OptimizationApplyPreviewRequest
{
    public RunPipelineRequest Request { get; init; } = new(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Udaipur, India");
    public OptimizationPlan? Plan { get; init; }
    public string Platform { get; init; } = "YouTube";
}

public sealed class OptimizationApplyResult
{
    public RunPipelineRequest OriginalRequest { get; init; } = new(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Udaipur, India");
    public RunPipelineRequest ResultRequest { get; init; } = new(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Udaipur, India");
    public OptimizationPlan Plan { get; init; } = new();
    public IReadOnlyCollection<string> ChangedFields { get; init; } = [];
    public string Mode { get; init; } = OptimizationMode.RecommendOnly.ToString();
}
