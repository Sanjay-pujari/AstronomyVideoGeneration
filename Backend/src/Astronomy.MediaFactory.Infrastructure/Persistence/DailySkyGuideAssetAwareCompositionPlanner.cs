using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuideAssetAwareCompositionPlanner(
    IDailySkyGuideAssetAwareContextService contextService) : IDailySkyGuideAssetAwareCompositionPlanner, IAssetAwareCompositionPlanner
{
    private static readonly SegmentTemplate[] Templates =
    [
        new(1, "DailySkyGuide_Intro", "Intro", "IntroBackground", "Introduce tonight's sky and location.", 6d, "FadeIn"),
        new(2, "DailySkyGuide_PrimaryObject", "PrimaryObject", "ThumbnailCandidate", "Explain the primary celestial object.", 8d, "SlowZoom"),
        new(3, "DailySkyGuide_SupportingSkyMap", "SupportingSkyMap", "SupportingSkyMap", "Provide a supporting sky map for orientation.", 8d, "PanAndZoom"),
        new(4, "DailySkyGuide_Outro", "Outro", "OutroBackground", "Close with viewing tips and next steps.", 5d, "CrossFade")
    ];

    public string ContentCategoryCode => "DailySkyGuide";

    public async Task<AssetAwareVideoCompositionPlan> BuildAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
    {
        var context = await contextService.BuildAsync(contentGenerationPlanId, cancellationToken);

        var warnings = new List<string>(context.Warnings);
        var segments = new List<AssetAwareVideoSegment>(Templates.Length);

        foreach (var template in Templates)
        {
            var asset = context.VisualAssets.FirstOrDefault(a => a.Role.Equals(template.VisualRole, StringComparison.OrdinalIgnoreCase));
            var exists = asset?.Exists ?? false;
            if (!exists)
            {
                warnings.Add($"Missing required visual asset for role '{template.VisualRole}'.");
            }

            segments.Add(new AssetAwareVideoSegment(
                template.SortOrder,
                template.SegmentCode,
                template.SegmentType,
                template.VisualRole,
                asset?.Path,
                exists,
                template.SuggestedNarrationPurpose,
                template.SuggestedDurationSeconds,
                template.TransitionType,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["contentCategoryCode"] = context.ContentCategoryCode,
                    ["locationName"] = context.LocationName
                }));
        }

        var ready = segments.All(s => s.ImageExists);

        return new AssetAwareVideoCompositionPlan(
            context.ContentGenerationPlanId,
            context.ContentCategoryCode,
            context.LocationName,
            context.TargetDate,
            context.Language,
            context.Title,
            segments.Count,
            segments,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ready);
    }

    private sealed record SegmentTemplate(
        int SortOrder,
        string SegmentCode,
        string SegmentType,
        string VisualRole,
        string SuggestedNarrationPurpose,
        double SuggestedDurationSeconds,
        string TransitionType);
}

public sealed class AssetAwareCompositionPlannerResolver(
    IEnumerable<IAssetAwareCompositionPlanner> planners) : IAssetAwareCompositionPlannerResolver
{
    public IAssetAwareCompositionPlanner? Resolve(string contentCategoryCode)
        => planners.FirstOrDefault(x => x.ContentCategoryCode.Equals(contentCategoryCode, StringComparison.OrdinalIgnoreCase));
}
