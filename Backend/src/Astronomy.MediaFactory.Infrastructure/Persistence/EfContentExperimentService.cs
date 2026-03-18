using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EfContentExperimentService : IContentExperimentService
{
    private static readonly TimeSpan RotationInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan CompletionWindow = TimeSpan.FromHours(24);
    private static readonly Regex TokenSplitRegex = new("[^a-zA-Z0-9]+", RegexOptions.Compiled);

    private readonly MediaFactoryDbContext _db;

    public EfContentExperimentService(MediaFactoryDbContext db)
    {
        _db = db;
    }

    public async Task InitializeExperimentsAsync(PublishedVideo publishedVideo, OptimizedVideoMetadata metadata, ThumbnailPlan thumbnailPlan, MonetizationPlan? monetizationPlan, CancellationToken cancellationToken)
    {
        var titleValues = new[] { metadata.PrimaryTitle }
            .Concat(metadata.AlternateTitles)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (titleValues.Length > 0)
        {
            var experiment = BuildExperiment(
                publishedVideo.Id,
                ContentExperimentType.Title,
                ContentVariantType.TitleText,
                titleValues);

            publishedVideo.TitleExperimentId = experiment.Id;
            publishedVideo.SelectedTitleVariantId = experiment.SelectedVariantId;
            await _db.ContentExperiments.AddAsync(experiment, cancellationToken);
        }

        var thumbnailValues = thumbnailPlan.Variants
            .Select(x => x.Value)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        if (thumbnailValues.Length > 0)
        {
            var experiment = BuildExperiment(
                publishedVideo.Id,
                ContentExperimentType.Thumbnail,
                ContentVariantType.ThumbnailTextAndLayout,
                thumbnailValues);

            publishedVideo.ThumbnailExperimentId = experiment.Id;
            publishedVideo.SelectedThumbnailVariantId = experiment.SelectedVariantId;
            await _db.ContentExperiments.AddAsync(experiment, cancellationToken);
        }

        var ctaValues = BuildCtaVariants(monetizationPlan)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (ctaValues.Length > 0)
        {
            var experiment = BuildExperiment(
                publishedVideo.Id,
                ContentExperimentType.CTA,
                ContentVariantType.CallToActionText,
                ctaValues);

            publishedVideo.CtaExperimentId = experiment.Id;
            publishedVideo.SelectedCtaVariantId = experiment.SelectedVariantId;
            await _db.ContentExperiments.AddAsync(experiment, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExperimentVariantAssignment> ResolveAssignmentsAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var video = await _db.PublishedVideos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == videoId, cancellationToken);

        if (video is null)
        {
            return new ExperimentVariantAssignment();
        }

        return new ExperimentVariantAssignment
        {
            TitleExperimentId = video.TitleExperimentId,
            TitleVariantId = video.SelectedTitleVariantId,
            ThumbnailExperimentId = video.ThumbnailExperimentId,
            ThumbnailVariantId = video.SelectedThumbnailVariantId,
            CtaExperimentId = video.CtaExperimentId,
            CtaVariantId = video.SelectedCtaVariantId
        };
    }

    public async Task EvaluateRecentExperimentsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var experiments = await _db.ContentExperiments
            .Include(x => x.Variants)
            .Where(x => x.Status == ContentExperimentStatus.Running)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var experiment in experiments)
        {
            var video = await _db.PublishedVideos.FirstOrDefaultAsync(x => x.Id == experiment.VideoId, cancellationToken);
            if (video is null)
            {
                experiment.Status = ContentExperimentStatus.Cancelled;
                experiment.CompletedAt = now;
                experiment.Touch();
                continue;
            }

            var snapshots = await _db.VideoAnalytics
                .Where(x => x.PublishedVideoId == experiment.VideoId && x.RetrievedAt >= experiment.CreatedAt)
                .OrderBy(x => x.RetrievedAt)
                .ToListAsync(cancellationToken);

            ApplyMetrics(experiment, snapshots);

            if (snapshots.Count == 0)
            {
                if (now - experiment.CreatedAt >= CompletionWindow)
                {
                    CompleteWithWinner(experiment, video, SelectFallbackVariant(experiment), now);
                    await ApplyWinningVariantAsync(experiment, video, cancellationToken);
                }

                continue;
            }

            var currentVariant = experiment.Variants.FirstOrDefault(x => x.Id == experiment.SelectedVariantId)
                ?? experiment.Variants.OrderBy(x => x.CreatedUtc).First();
            var currentHasData = GetRelevantSnapshots(experiment.ExperimentType, snapshots, currentVariant.Id).Count > 0;
            var untestedVariant = experiment.Variants
                .OrderBy(x => x.CreatedUtc)
                .FirstOrDefault(x => GetRelevantSnapshots(experiment.ExperimentType, snapshots, x.Id).Count == 0);

            var lastSwitchAt = experiment.UpdatedUtc ?? experiment.CreatedUtc;
            if (untestedVariant is not null && currentHasData && now - lastSwitchAt >= RotationInterval)
            {
                experiment.SelectedVariantId = untestedVariant.Id;
                experiment.Touch();
                ApplySelectedVariant(experiment, video, untestedVariant.Value);
                continue;
            }

            if (untestedVariant is null || now - experiment.CreatedAt >= CompletionWindow)
            {
                var winner = SelectWinner(experiment);
                CompleteWithWinner(experiment, video, winner, now);
                await ApplyWinningVariantAsync(experiment, video, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ContentExperiment>> GetRecentExperimentsAsync(int take, CancellationToken cancellationToken)
        => await _db.ContentExperiments
            .AsNoTracking()
            .Include(x => x.Variants)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Max(take, 1))
            .ToListAsync(cancellationToken);

    public async Task<ContentExperiment?> GetExperimentAsync(Guid id, CancellationToken cancellationToken)
        => await _db.ContentExperiments
            .AsNoTracking()
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<ContentExperiment>> GetTopPerformingExperimentsAsync(int take, CancellationToken cancellationToken)
        => await _db.ContentExperiments
            .AsNoTracking()
            .Include(x => x.Variants)
            .Where(x => x.Status == ContentExperimentStatus.Completed)
            .OrderByDescending(x => x.Variants.Any(v => v.Ctr.HasValue))
            .ThenByDescending(x => x.Variants.Where(v => v.IsWinner).Select(v => v.Ctr ?? 0d).FirstOrDefault())
            .ThenByDescending(x => x.Variants.Where(v => v.IsWinner).Select(v => v.Views).FirstOrDefault())
            .ThenByDescending(x => x.Variants.Where(v => v.IsWinner).Select(v => v.EngagementScore).FirstOrDefault())
            .Take(Math.Max(take, 1))
            .ToListAsync(cancellationToken);

    public async Task<ExperimentFeedbackSnapshot> GetFeedbackSnapshotAsync(CancellationToken cancellationToken)
    {
        var completed = await _db.ContentExperiments
            .AsNoTracking()
            .Include(x => x.Variants)
            .Where(x => x.Status == ContentExperimentStatus.Completed && x.CompletedAt >= DateTimeOffset.UtcNow.AddDays(-60))
            .OrderByDescending(x => x.CompletedAt)
            .Take(40)
            .ToListAsync(cancellationToken);

        var winners = completed
            .Select(x => x.Variants.FirstOrDefault(v => v.IsWinner))
            .Where(static x => x is not null)
            .Cast<ContentVariant>()
            .ToArray();

        return new ExperimentFeedbackSnapshot
        {
            WinningTitlePatterns = winners
                .Where(x => x.VariantType == ContentVariantType.TitleText)
                .Select(x => ExtractPattern(x.Value))
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray(),
            WinningHooks = winners
                .Where(x => x.VariantType == ContentVariantType.TitleText)
                .Select(x => ExtractOpeningHook(x.Value))
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray(),
            WinningThumbnailPatterns = winners
                .Where(x => x.VariantType == ContentVariantType.ThumbnailTextAndLayout)
                .Select(x => x.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray(),
            WinningCallToActions = winners
                .Where(x => x.VariantType == ContentVariantType.CallToActionText)
                .Select(x => x.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray()
        };
    }

    private static ContentExperiment BuildExperiment(Guid videoId, ContentExperimentType experimentType, ContentVariantType variantType, IReadOnlyCollection<string> values)
    {
        var variants = values
            .Select(value => new ContentVariant
            {
                ContentExperimentId = Guid.Empty,
                VariantType = variantType,
                Value = value.Trim()
            })
            .ToList();

        var experiment = new ContentExperiment
        {
            VideoId = videoId,
            ExperimentType = experimentType,
            Status = ContentExperimentStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            Variants = variants
        };

        foreach (var variant in variants)
        {
            variant.ContentExperimentId = experiment.Id;
        }

        experiment.SelectedVariantId = variants.FirstOrDefault()?.Id;
        return experiment;
    }

    private static IEnumerable<string> BuildCtaVariants(MonetizationPlan? monetizationPlan)
    {
        if (string.IsNullOrWhiteSpace(monetizationPlan?.PinnedCommentText))
            yield break;

        yield return monetizationPlan.PinnedCommentText.Trim();
        yield return "See the pinned resources for beginner-friendly gear and tonight's observing picks.";
        yield return "Open the pinned recommendations to compare the telescope and accessory links mentioned in this video.";
    }

    private static void ApplyMetrics(ContentExperiment experiment, IReadOnlyCollection<VideoAnalytics> snapshots)
    {
        foreach (var variant in experiment.Variants)
        {
            var relevant = GetRelevantSnapshots(experiment.ExperimentType, snapshots, variant.Id);
            if (relevant.Count == 0)
            {
                variant.Views = 0;
                variant.Ctr = null;
                variant.EngagementScore = 0;
                continue;
            }

            variant.Views = relevant.Max(x => x.Views);
            variant.Ctr = relevant.Any(x => x.CtrPercent.HasValue)
                ? relevant.Where(x => x.CtrPercent.HasValue).Average(x => x.CtrPercent!.Value)
                : null;
            variant.EngagementScore = relevant.Average(ComputeEngagementScore);
        }
    }

    private static List<VideoAnalytics> GetRelevantSnapshots(ContentExperimentType experimentType, IReadOnlyCollection<VideoAnalytics> snapshots, Guid variantId)
        => experimentType switch
        {
            ContentExperimentType.Title => snapshots.Where(x => x.TitleVariantId == variantId).ToList(),
            ContentExperimentType.Thumbnail => snapshots.Where(x => x.ThumbnailVariantId == variantId).ToList(),
            ContentExperimentType.CTA => snapshots.Where(x => x.CtaVariantId == variantId).ToList(),
            _ => []
        };

    private static double ComputeEngagementScore(VideoAnalytics analytics)
    {
        var interactions = analytics.Views <= 0 ? 0 : ((double)(analytics.Likes + analytics.Comments) / analytics.Views) * 100d;
        var retention = analytics.DurationSeconds > 0 && analytics.AverageViewDurationSeconds.HasValue
            ? (analytics.AverageViewDurationSeconds.Value / analytics.DurationSeconds) * 100d
            : 0d;

        return Math.Round(interactions + retention, 4);
    }

    private static ContentVariant SelectWinner(ContentExperiment experiment)
    {
        var variants = experiment.Variants.OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (experiment.Variants.Any(x => x.Ctr.HasValue))
        {
            return variants
                .OrderByDescending(x => x.Ctr ?? 0d)
                .ThenByDescending(x => x.Views)
                .ThenByDescending(x => x.EngagementScore)
                .First();
        }

        if (experiment.Variants.Any(x => x.Views > 0))
        {
            return variants
                .OrderByDescending(x => x.Views)
                .ThenByDescending(x => x.EngagementScore)
                .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        return variants
            .OrderByDescending(x => x.EngagementScore)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static ContentVariant SelectFallbackVariant(ContentExperiment experiment)
        => experiment.Variants
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .First();

    private static void CompleteWithWinner(ContentExperiment experiment, PublishedVideo video, ContentVariant winner, DateTimeOffset completedAt)
    {
        foreach (var variant in experiment.Variants)
        {
            variant.IsWinner = variant.Id == winner.Id;
            if (variant.IsWinner)
            {
                experiment.SelectedVariantId = variant.Id;
            }
        }

        experiment.Status = ContentExperimentStatus.Completed;
        experiment.CompletedAt = completedAt;
        experiment.Touch();
        ApplySelectedVariant(experiment, video, winner.Value);
    }

    private async Task ApplyWinningVariantAsync(ContentExperiment experiment, PublishedVideo video, CancellationToken cancellationToken)
    {
        if (video.PipelineRunId is Guid pipelineRunId)
        {
            var script = await _db.GeneratedScripts
                .OrderByDescending(x => x.CreatedUtc)
                .FirstOrDefaultAsync(x => x.PipelineRunId == pipelineRunId, cancellationToken);

            if (script is not null)
            {
                var winnerValue = experiment.Variants.FirstOrDefault(x => x.IsWinner)?.Value;
                if (!string.IsNullOrWhiteSpace(winnerValue))
                {
                    switch (experiment.ExperimentType)
                    {
                        case ContentExperimentType.Title:
                            script.OptimizedTitle = winnerValue;
                            script.Title = winnerValue;
                            script.AlternateTitlesCsv = string.Join('|', experiment.Variants.Where(x => x.Id != experiment.SelectedVariantId).Select(x => x.Value));
                            break;
                        case ContentExperimentType.Thumbnail:
                            script.ThumbnailTextSuggestionsCsv = string.Join('|', experiment.Variants.OrderByDescending(x => x.IsWinner).ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase).Select(x => x.Value));
                            break;
                    }
                }
            }
        }

        if (experiment.ExperimentType == ContentExperimentType.CTA)
        {
            var record = await _db.MonetizationRecords
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.VideoId == video.Id, cancellationToken);
            var winner = experiment.Variants.FirstOrDefault(x => x.IsWinner)?.Value;
            if (record is not null && !string.IsNullOrWhiteSpace(winner))
            {
                record.PinnedCommentText = winner;
            }
        }
    }

    private static void ApplySelectedVariant(ContentExperiment experiment, PublishedVideo video, string value)
    {
        switch (experiment.ExperimentType)
        {
            case ContentExperimentType.Title:
                video.SelectedTitleVariantId = experiment.SelectedVariantId;
                video.Title = value;
                video.OptimizedTitle = value;
                break;
            case ContentExperimentType.Thumbnail:
                video.SelectedThumbnailVariantId = experiment.SelectedVariantId;
                break;
            case ContentExperimentType.CTA:
                video.SelectedCtaVariantId = experiment.SelectedVariantId;
                break;
        }

        video.Touch();
    }

    private static string ExtractPattern(string value)
    {
        var normalized = Regex.Replace(value, "\\d+", "<N>");
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        return normalized.Length <= 72 ? normalized : normalized[..72].TrimEnd() + "...";
    }

    private static string ExtractOpeningHook(string value)
    {
        var tokens = TokenSplitRegex.Split(value)
            .Where(x => x.Length >= 3)
            .Take(6)
            .ToArray();
        return tokens.Length == 0 ? string.Empty : string.Join(' ', tokens);
    }
}
