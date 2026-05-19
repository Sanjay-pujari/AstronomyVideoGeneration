using System.Text.Json;
using Astronomy.MediaFactory.AIOptimization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Optimization;

public sealed class AIOptimizationPipelineService : IAIOptimizationPipelineService
{
    private readonly MediaFactoryDbContext _db;
    private readonly IHookOptimizationService _hookOptimizationService;
    private readonly IPublishingOptimizationService _publishingOptimizationService;
    private readonly AIOptimizationOptions _options;

    public AIOptimizationPipelineService(MediaFactoryDbContext db, IHookOptimizationService hookOptimizationService, IPublishingOptimizationService publishingOptimizationService, IOptions<AIOptimizationOptions> options)
    {
        _db = db;
        _hookOptimizationService = hookOptimizationService;
        _publishingOptimizationService = publishingOptimizationService;
        _options = options.Value;
    }

    public async Task<AIOptimizationPipelineResult> RunForPipelineAsync(AIOptimizationPipelineRequest request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return new AIOptimizationPipelineResult(false, 0, 0, 0, []);

        var selectedHook = string.IsNullOrWhiteSpace(request.SelectedHook) ? request.SelectedTitle ?? "Astronomy tonight" : request.SelectedHook;
        var hooks = Enumerable.Range(1, 5).Select(i => $"{selectedHook} (variant {i})").ToArray();
        var scores = _hookOptimizationService.Score(new HookOptimizationRequest(hooks, request.Language, request.Objects, request.EventType, "general"));
        foreach (var score in scores)
        {
            _db.HookOptimizationResults.Add(new HookOptimizationRecord
            {
                PipelineRunId = request.PipelineRunId,
                Hook = score.Hook,
                CuriosityScore = score.CuriosityScore,
                EmotionalImpactScore = score.EmotionalImpactScore,
                ClarityScore = score.ClarityScore,
                ClickProbability = score.ClickProbability,
                FinalScore = score.FinalScore,
                RecommendationReason = score.RecommendationReason,
                Language = request.Language
            });
        }

        var publishing = _publishingOptimizationService.BuildRecommendation(request.PipelineRunId, request.Language, request.EventType);
        _db.PublishingOptimizationResults.Add(new PublishingOptimizationRecord
        {
            PipelineRunId = request.PipelineRunId,
            RecommendedPublishTime = EnsureUtc(publishing.RecommendedPublishTime),
            RecommendedHashtagsCsv = string.Join(",", publishing.RecommendedHashtags),
            RecommendedTagsCsv = string.Join(",", publishing.RecommendedTags),
            RecommendedAudienceType = publishing.RecommendedAudienceType,
            PlatformPriorityCsv = string.Join(",", publishing.PlatformPriority)
        });

        var objectCount = request.Objects.Count;
        if (!string.IsNullOrWhiteSpace(request.LongThumbnailPath))
            _db.ThumbnailOptimizationResults.Add(new ThumbnailOptimizationRecord { PipelineRunId = request.PipelineRunId, ObjectCount = objectCount, Brightness = 0.5, TextLength = selectedHook.Length, Language = request.Language, HookIntensity = 0.6, CompositionScore = 0.7 });
        if (!string.IsNullOrWhiteSpace(request.ShortThumbnailPath))
            _db.ThumbnailOptimizationResults.Add(new ThumbnailOptimizationRecord { PipelineRunId = request.PipelineRunId, ObjectCount = objectCount, Brightness = 0.5, TextLength = selectedHook.Length, Language = request.Language, HookIntensity = 0.6, CompositionScore = 0.7 });

        await _db.SaveChangesAsync(cancellationToken);

        var report = new
        {
            pipelineRunId = request.PipelineRunId,
            generatedHooks = hooks,
            scores,
            selectedRecommendedHook = scores.OrderByDescending(x => x.FinalScore).FirstOrDefault()?.Hook,
            publishingRecommendations = publishing,
            thumbnailMetadataScores = await _db.ThumbnailOptimizationResults.Where(x => x.PipelineRunId == request.PipelineRunId).ToListAsync(cancellationToken),
            mode = "RecommendOnly"
        };
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "ai-hook-optimization-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        return new AIOptimizationPipelineResult(true, scores.Count, 1, string.IsNullOrWhiteSpace(request.ShortThumbnailPath) && string.IsNullOrWhiteSpace(request.LongThumbnailPath) ? 0 : (string.IsNullOrWhiteSpace(request.ShortThumbnailPath) || string.IsNullOrWhiteSpace(request.LongThumbnailPath) ? 1 : 2), []);
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero
            ? value
            : value.ToUniversalTime();
}
