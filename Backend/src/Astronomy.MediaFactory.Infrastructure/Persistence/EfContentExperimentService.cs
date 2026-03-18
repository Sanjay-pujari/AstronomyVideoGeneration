using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EfContentExperimentService : IContentExperimentService
{
    private static readonly TimeSpan RotationInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan CompletionWindow = TimeSpan.FromHours(24);
    private const double CtrTieTolerance = 0.15d;
    private const double EngagementTieTolerance = 0.5d;
    private static readonly Regex TokenSplitRegex = new("[^a-zA-Z0-9]+", RegexOptions.Compiled);

    private readonly MediaFactoryDbContext _db;

    public EfContentExperimentService(MediaFactoryDbContext db)
    {
        _db = db;
    }

    public async Task InitializeExperimentsAsync(PublishedVideo publishedVideo, OptimizedVideoMetadata metadata, ThumbnailPlan thumbnailPlan, MonetizationPlan? monetizationPlan, CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            CreateDefinition(
                ContentExperimentType.Title,
                ContentVariantType.TitleText,
                new[] { metadata.PrimaryTitle }
                    .Concat(metadata.AlternateTitles)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToArray(),
                static (video, experiment) =>
                {
                    video.TitleExperimentId = experiment.Id;
                    video.SelectedTitleVariantId = experiment.SelectedVariantId;
                }),
            CreateDefinition(
                ContentExperimentType.Thumbnail,
                ContentVariantType.ThumbnailTextAndLayout,
                thumbnailPlan.Variants
                    .Select(x => x.Value)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToArray(),
                static (video, experiment) =>
                {
                    video.ThumbnailExperimentId = experiment.Id;
                    video.SelectedThumbnailVariantId = experiment.SelectedVariantId;
                }),
            CreateDefinition(
                ContentExperimentType.CTA,
                ContentVariantType.CallToActionText,
                BuildCtaVariants(monetizationPlan)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToArray(),
                static (video, experiment) =>
                {
                    video.CtaExperimentId = experiment.Id;
                    video.SelectedCtaVariantId = experiment.SelectedVariantId;
                })
        };

        foreach (var definition in definitions)
        {
            if (definition.Values.Count == 0)
            {
                continue;
            }

            var experiment = BuildExperiment(
                publishedVideo.Id,
                definition.ExperimentType,
                definition.VariantType,
                definition.Values);

            definition.ApplySelection(publishedVideo, experiment);
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

        if (experiments.Count == 0)
        {
            return;
        }

        var videoIds = experiments.Select(x => x.VideoId).Distinct().ToArray();
        var earliestCreatedAt = experiments.Min(x => x.CreatedAt);

        var videosById = await _db.PublishedVideos
            .Where(x => videoIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var analyticsByVideoId = (await _db.VideoAnalytics
                .Where(x => videoIds.Contains(x.PublishedVideoId) && x.RetrievedAt >= earliestCreatedAt)
                .OrderBy(x => x.RetrievedAt)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.PublishedVideoId)
            .ToDictionary(x => x.Key, x => (IReadOnlyCollection<VideoAnalytics>)x.ToList());

        foreach (var experiment in experiments)
        {
            await EvaluateExperimentLifecycleAsync(
                experiment,
                videosById.GetValueOrDefault(experiment.VideoId),
                analyticsByVideoId.GetValueOrDefault(experiment.VideoId) ?? [],
                now,
                cancellationToken);
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
                .ToArray(),
            Insights = completed
                .Select(BuildFeedbackInsight)
                .Where(static x => x is not null)
                .Cast<ExperimentFeedbackInsight>()
                .Take(10)
                .ToArray()
        };
    }

    private async Task EvaluateExperimentLifecycleAsync(
        ContentExperiment experiment,
        PublishedVideo? video,
        IReadOnlyCollection<VideoAnalytics> videoSnapshots,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (video is null)
        {
            experiment.Status = ContentExperimentStatus.Cancelled;
            experiment.CompletedAt = now;
            experiment.Touch();
            return;
        }

        var lifecycle = BuildLifecycleState(experiment, videoSnapshots, now);
        ApplyMetrics(experiment, lifecycle.Snapshots);

        if (lifecycle.Snapshots.Count == 0)
        {
            if (lifecycle.ShouldCompleteWithoutData)
            {
                await FinalizeExperimentAsync(experiment, video, SelectFallbackVariant(experiment), now, cancellationToken);
            }

            return;
        }

        if (lifecycle.ShouldRotate)
        {
            RotateToVariant(experiment, video, lifecycle.NextVariant!);
            return;
        }

        if (lifecycle.ShouldComplete)
        {
            await FinalizeExperimentAsync(experiment, video, SelectWinner(experiment), now, cancellationToken);
        }
    }

    private ExperimentLifecycleState BuildLifecycleState(ContentExperiment experiment, IReadOnlyCollection<VideoAnalytics> videoSnapshots, DateTimeOffset now)
    {
        var snapshots = videoSnapshots
            .Where(x => x.RetrievedAt >= experiment.CreatedAt)
            .OrderBy(x => x.RetrievedAt)
            .ToList();

        var currentVariant = experiment.Variants.FirstOrDefault(x => x.Id == experiment.SelectedVariantId)
            ?? experiment.Variants.OrderBy(x => x.CreatedUtc).First();

        var lastSwitchAt = experiment.UpdatedUtc ?? experiment.CreatedUtc;
        var currentHasData = GetRelevantSnapshots(experiment.ExperimentType, snapshots, currentVariant.Id).Count > 0;
        var nextVariant = experiment.Variants
            .OrderBy(x => x.CreatedUtc)
            .FirstOrDefault(x => GetRelevantSnapshots(experiment.ExperimentType, snapshots, x.Id).Count == 0);

        var hasExpired = now - experiment.CreatedAt >= CompletionWindow;
        var canRotate = nextVariant is not null && currentHasData && now - lastSwitchAt >= RotationInterval;

        return new ExperimentLifecycleState(
            snapshots,
            currentVariant,
            nextVariant,
            ShouldRotate: canRotate,
            ShouldComplete: nextVariant is null || hasExpired,
            ShouldCompleteWithoutData: hasExpired);
    }

    private static ContentExperimentDefinition CreateDefinition(
        ContentExperimentType experimentType,
        ContentVariantType variantType,
        IReadOnlyCollection<string> values,
        Action<PublishedVideo, ContentExperiment> applySelection)
        => new(experimentType, variantType, values, applySelection);

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
        => experiment.Variants
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .Aggregate((best, candidate) => CompareVariants(candidate, best) > 0 ? candidate : best);

    private static int CompareVariants(ContentVariant candidate, ContentVariant incumbent)
    {
        var signalComparison = CompareSignalStrength(candidate, incumbent);
        if (signalComparison != 0)
        {
            return signalComparison;
        }

        if (candidate.Ctr.HasValue || incumbent.Ctr.HasValue)
        {
            var ctrComparison = CompareMetric(candidate.Ctr ?? 0d, incumbent.Ctr ?? 0d, CtrTieTolerance);
            if (ctrComparison != 0)
            {
                return ctrComparison;
            }
        }

        var engagementComparison = CompareMetric(candidate.EngagementScore, incumbent.EngagementScore, EngagementTieTolerance);
        if (engagementComparison != 0)
        {
            return engagementComparison;
        }

        var viewsComparison = candidate.Views.CompareTo(incumbent.Views);
        if (viewsComparison != 0)
        {
            return viewsComparison;
        }

        return -StringComparer.OrdinalIgnoreCase.Compare(candidate.Value, incumbent.Value);
    }

    private static int CompareSignalStrength(ContentVariant candidate, ContentVariant incumbent)
    {
        var candidateSignals = (candidate.Ctr.HasValue ? 2 : 0) + (candidate.Views > 0 ? 1 : 0) + (candidate.EngagementScore > 0 ? 1 : 0);
        var incumbentSignals = (incumbent.Ctr.HasValue ? 2 : 0) + (incumbent.Views > 0 ? 1 : 0) + (incumbent.EngagementScore > 0 ? 1 : 0);
        return candidateSignals.CompareTo(incumbentSignals);
    }

    private static int CompareMetric(double candidate, double incumbent, double tolerance)
    {
        var delta = candidate - incumbent;
        if (Math.Abs(delta) <= tolerance)
        {
            return 0;
        }

        return delta > 0 ? 1 : -1;
    }

    private static ContentVariant SelectFallbackVariant(ContentExperiment experiment)
        => experiment.Variants
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .First();

    private static void RotateToVariant(ContentExperiment experiment, PublishedVideo video, ContentVariant nextVariant)
    {
        experiment.SelectedVariantId = nextVariant.Id;
        experiment.Touch();
        ApplySelectedVariant(experiment, video, nextVariant.Value);
    }

    private async Task FinalizeExperimentAsync(ContentExperiment experiment, PublishedVideo video, ContentVariant winner, DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        CompleteWithWinner(experiment, video, winner, completedAt);
        await ApplyWinningVariantAsync(experiment, video, cancellationToken);
    }

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

    private static ExperimentFeedbackInsight? BuildFeedbackInsight(ContentExperiment experiment)
    {
        var winner = experiment.Variants.FirstOrDefault(x => x.IsWinner);
        if (winner is null)
        {
            return null;
        }

        return new ExperimentFeedbackInsight
        {
            ExperimentType = experiment.ExperimentType,
            WinningValue = winner.Value,
            WinningPattern = experiment.ExperimentType == ContentExperimentType.Title ? ExtractPattern(winner.Value) : winner.Value,
            WinningHook = experiment.ExperimentType == ContentExperimentType.Title ? ExtractOpeningHook(winner.Value) : string.Empty,
            Metrics = new VariantPerformanceMetrics
            {
                Views = winner.Views,
                Ctr = winner.Ctr,
                EngagementScore = winner.EngagementScore
            }
        };
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

    private sealed record ContentExperimentDefinition(
        ContentExperimentType ExperimentType,
        ContentVariantType VariantType,
        IReadOnlyCollection<string> Values,
        Action<PublishedVideo, ContentExperiment> ApplySelection);

    private sealed record ExperimentLifecycleState(
        IReadOnlyCollection<VideoAnalytics> Snapshots,
        ContentVariant CurrentVariant,
        ContentVariant? NextVariant,
        bool ShouldRotate,
        bool ShouldComplete,
        bool ShouldCompleteWithoutData);
}
