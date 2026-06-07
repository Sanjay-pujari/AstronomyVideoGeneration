using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DirectorTimelineService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<DirectorTimelineService> logger) : IDirectorTimelineService
{
    private const string GenerationSource = "Phase9D";
    private const string FinalPackageFileName = "tts-package-final.json";
    private const string PolishedNarrationFileName = "narration-polished.json";
    private const string AudioManifestFileName = "tts-audio-manifest.json";
    private const string TimelineFileName = "director-timeline.json";
    private const string AiPromptMissingWarning = "AI image prompt exists but generated image is not available yet.";
    private const string MissingStellariumCaptureWarning = "SSC script exists but captured PNG is missing.";
    private const string ClosingFallbackQualityNote = "Fallback visual selected for closing scene.";
    private const string RecoveredFallbackQualityNote = "Recovered fallback visual for render readiness.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<DirectorTimelineResult> GenerateDirectorTimelinesAsync(DirectorTimelineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var timelines = new List<DirectorTimelineDocument>();
        var candidates = await ResolveCandidatesAsync(request, cancellationToken);

        foreach (var plan in candidates)
        {
            try
            {
                var timelinePath = BuildTimelinePath(root, plan.RegionId, plan.Id);
                if (!request.DryRun && File.Exists(timelinePath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing director timeline for plan {plan.Id}. Set overwriteExisting=true to replace it.");
                    continue;
                }

                var timeline = await BuildTimelineAsync(root, plan, cancellationToken);
                timelines.Add(timeline);
                warnings.AddRange(timeline.RenderReadiness.Warnings.Select(w => $"Plan {plan.Id}: {w}"));

                if (!request.DryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(timelinePath) ?? root);
                    await File.WriteAllTextAsync(timelinePath, JsonSerializer.Serialize(timeline, JsonOptions), cancellationToken);
                    generatedFiles.Add(timelinePath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to generate director timeline for plan {plan.Id}: {ex.Message}");
                logger.LogWarning(ex, "Phase 9D director timeline generation failed for plan {PlanId}", plan.Id);
            }
        }

        var readyCount = timelines.Count(t => t.RenderReadiness.ReadyForRender);
        logger.LogInformation("Phase 9D processed {PlanCount} content generation plan(s). Generated={GeneratedCount} ReadyForRender={ReadyForRenderCount} DryRun={DryRun}", candidates.Count, timelines.Count, readyCount, request.DryRun);
        return new DirectorTimelineResult(candidates.Count, timelines.Count, readyCount, timelines.Count - readyCount, timelines, generatedFiles, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<IReadOnlyList<ContentGenerationPlan>> ResolveCandidatesAsync(DirectorTimelineRequest request, CancellationToken cancellationToken)
    {
        var requestedCategories = ToSet(request.ContentCategories);
        var requestedFormats = ToSet(request.PlannedFormats);
        var query = db.ContentGenerationPlans.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.RegionId))
        {
            var region = request.RegionId.Trim();
            query = query.Where(p => p.RegionId == region);
        }

        if (request.PlanIds is { Count: > 0 })
        {
            var ids = request.PlanIds.ToHashSet();
            query = query.Where(p => ids.Contains(p.Id));
        }

        if (requestedCategories is not null)
            query = query.Where(p => requestedCategories.Contains(p.ContentCategoryCode));
        if (requestedFormats is not null)
            query = query.Where(p => p.PlannedFormat != null && requestedFormats.Contains(p.PlannedFormat));

        query = query.Where(p => p.AstronomyContentOpportunityId != null || p.AstronomyEventIntelligenceId != null);

        var root = ResolveWorkingDirectoryRoot();
        var plans = await query
            .OrderByDescending(p => p.ScheduledUtc ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return plans
            .Where(p => HasRequiredProductionAudio(root, p))
            .Take(request.MaxPlans ?? int.MaxValue)
            .ToList();
    }

    private static bool HasRequiredProductionAudio(string root, ContentGenerationPlan plan)
    {
        var planRoot = BuildPlanRoot(root, plan.RegionId, plan.Id);
        return File.Exists(Path.Combine(planRoot, "tts", FinalPackageFileName))
            && File.Exists(Path.Combine(planRoot, "tts", "audio", AudioManifestFileName))
            && File.Exists(Path.Combine(planRoot, "tts", "audio", "narration-combined.wav"));
    }

    private async Task<DirectorTimelineDocument> BuildTimelineAsync(string root, ContentGenerationPlan plan, CancellationToken cancellationToken)
    {
        var planRoot = BuildPlanRoot(root, plan.RegionId, plan.Id);
        var finalPackage = await ReadJsonAsync<FinalTtsPackageDocument>(Path.Combine(planRoot, "tts", FinalPackageFileName), cancellationToken);
        var manifest = await ReadJsonAsync<TtsAudioManifest>(Path.Combine(planRoot, "tts", "audio", AudioManifestFileName), cancellationToken);
        var polished = await ReadJsonAsync<PolishedNarrationDocument>(Path.Combine(planRoot, "narration", PolishedNarrationFileName), cancellationToken);
        var assetJobs = await db.AstronomyAssetProductionJobs
            .AsNoTracking()
            .Where(j => j.ContentGenerationPlanId == plan.Id && j.Status == AstronomyAssetProductionJobStatuses.Completed)
            .OrderBy(j => j.SceneNumber)
            .ThenBy(j => j.Priority)
            .ThenBy(j => j.AssetType)
            .ToListAsync(cancellationToken);
        List<AstronomyAssetProductionJob> eventFallbackJobs = plan.AstronomyEventIntelligenceId is null
            ? []
            : await db.AstronomyAssetProductionJobs
                .AsNoTracking()
                .Where(j => j.ContentGenerationPlanId != plan.Id
                    && j.AstronomyEventIntelligenceId == plan.AstronomyEventIntelligenceId
                    && j.Status == AstronomyAssetProductionJobStatuses.Completed)
                .OrderBy(j => j.SceneNumber)
                .ThenBy(j => j.Priority)
                .ThenBy(j => j.AssetType)
                .ToListAsync(cancellationToken);

        var missingRequiredAssets = new List<string>();
        var warnings = new List<string>();
        if (finalPackage is null)
            missingRequiredAssets.Add($"Missing final TTS package: {Path.Combine(planRoot, "tts", FinalPackageFileName)}");
        if (manifest is null)
            missingRequiredAssets.Add($"Missing TTS audio manifest: {Path.Combine(planRoot, "tts", "audio", AudioManifestFileName)}");

        var combinedPath = manifest?.CombinedAudioPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(combinedPath) || !File.Exists(combinedPath))
            missingRequiredAssets.Add("Missing required combined narration audio.");

        var segments = finalPackage?.Segments.OrderBy(s => s.SceneNumber).ToList() ?? [];
        if (segments.Count == 0 && polished is not null)
        {
            segments = polished.Segments.OrderBy(s => s.SceneNumber)
                .Select(s => new TtsPackageSegment(s.SceneNumber, s.SceneName, s.FinalNarration, string.Empty, 0, s.PauseHints, s.EmphasisWords, s.VoicePerformance, string.Empty))
                .ToList();
        }

        if (segments.Count == 0)
            missingRequiredAssets.Add("No narration scenes were found in the final TTS package or polished narration.");

        var manifestDurations = (manifest?.Segments ?? [])
            .GroupBy(s => s.SceneNumber)
            .ToDictionary(g => g.Key, g => g.First(), EqualityComparer<int>.Default);
        var sceneCount = segments.Count;
        var fallbackTotal = finalPackage?.TotalEstimatedDurationSeconds ?? segments.Sum(s => s.EstimatedDurationSeconds);
        var narrationTotal = manifest?.TotalDurationSeconds > 0 ? manifest.TotalDurationSeconds : fallbackTotal;
        var rawSceneDurations = segments.Select(s => ResolveSceneDuration(s, manifestDurations)).ToList();
        var totalRawDuration = rawSceneDurations.Sum();
        var breathers = CalculateBreathers(sceneCount, narrationTotal, totalRawDuration);
        var scenes = new List<DirectorTimelineScene>();
        var cursor = 0d;
        var lastSceneNumber = segments.Count == 0 ? 0 : segments.Max(s => s.SceneNumber);

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var duration = rawSceneDurations[i];
            var sceneStart = Round(cursor);
            var sceneEnd = Round(cursor + duration);
            cursor = sceneEnd + breathers.ElementAtOrDefault(i);

            var jobsForScene = assetJobs.Where(j => j.SceneNumber == segment.SceneNumber).ToList();
            var selection = SelectVisualAssets(segment.SceneNumber, lastSceneNumber, jobsForScene, assetJobs, eventFallbackJobs, scenes, warnings);
            var selectedAssets = selection.RenderableAssets;
            var textOverlay = jobsForScene.FirstOrDefault(j => IsAssetType(j, "TextOverlayCard") && HasUsablePath(j));
            var qualityNotes = new List<string>();
            if (selection.UsedClosingFallback)
                qualityNotes.Add(ClosingFallbackQualityNote);
            if (selection.UsedRecoveredFallback)
                qualityNotes.Add(RecoveredFallbackQualityNote);
            if (selection.ReusedFromSceneNumber is not null)
                qualityNotes.Add($"Fallback reused from scene {selection.ReusedFromSceneNumber}.");
            if (selectedAssets.Count == 0)
            {
                missingRequiredAssets.Add($"Scene {segment.SceneNumber} has no usable visual asset.");
                qualityNotes.Add("No primary visual source is currently available for this scene.");
            }

            var sceneAudioPath = manifestDurations.TryGetValue(segment.SceneNumber, out var manifestSegment) && !string.IsNullOrWhiteSpace(manifestSegment.AudioPath)
                ? manifestSegment.AudioPath
                : segment.OutputAudioPath;
            if (string.IsNullOrWhiteSpace(sceneAudioPath))
                missingRequiredAssets.Add($"Scene {segment.SceneNumber} has no audio path in the TTS audio manifest or final TTS package.");

            scenes.Add(new DirectorTimelineScene(
                segment.SceneNumber,
                string.IsNullOrWhiteSpace(segment.SceneName) ? $"Scene {segment.SceneNumber}" : segment.SceneName,
                sceneStart,
                sceneEnd,
                Round(sceneEnd - sceneStart),
                segment.Text,
                sceneAudioPath,
                selectedAssets.FirstOrDefault() ?? new DirectorTimelineAsset(string.Empty, string.Empty, string.Empty),
                selectedAssets.Skip(1).Take(4).ToList(),
                selection.TechnicalReferences,
                new DirectorTimelineOverlayPlan(textOverlay?.OutputPath ?? string.Empty, "center-safe 80% width / 70% height; keep lower captions clear", true),
                ResolveCameraMotion(plan.ContentCategoryCode, segment.SceneNumber, lastSceneNumber),
                segment.SceneNumber == 1 ? "fade from black" : "soft crossfade",
                segment.SceneNumber == lastSceneNumber ? "fade to black" : "soft crossfade",
                ResolveVisualMood(plan.ContentCategoryCode, segment.SceneNumber, lastSceneNumber),
                ResolveMusicCue(plan.ContentCategoryCode, segment.SceneNumber, lastSceneNumber),
                qualityNotes));
        }

        var timelineDuration = scenes.Count == 0 ? 0 : scenes.Max(s => s.EndSecond);
        if (timelineDuration <= 0)
            missingRequiredAssets.Add("Timeline duration is invalid.");
        if (narrationTotal > 0 && timelineDuration > 0 && Math.Abs(timelineDuration - narrationTotal) > Math.Max(1.5, narrationTotal * 0.08))
            warnings.Add($"Timeline duration {timelineDuration:0.###}s differs from combined narration duration {narrationTotal:0.###}s.");

        foreach (var scene in scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.PrimaryAsset.AssetType) || string.IsNullOrWhiteSpace(scene.PrimaryAsset.Path))
                missingRequiredAssets.Add($"Scene {scene.SceneNumber} has no usable primary visual asset.");
            if (IsSscPath(scene.PrimaryAsset.Path))
                missingRequiredAssets.Add($"Scene {scene.SceneNumber} primary visual cannot be an SSC script.");
            if ((string.IsNullOrWhiteSpace(scene.PrimaryAsset.AssetType) || string.IsNullOrWhiteSpace(scene.PrimaryAsset.Path))
                && scene.TechnicalReferences.Any(reference => IsSscPath(reference.Path)))
                missingRequiredAssets.Add($"Scene {scene.SceneNumber} has only an SSC script reference and no render visual.");
        }

        var renderReady = missingRequiredAssets.Count == 0
            && timelineDuration > 0
            && !string.IsNullOrWhiteSpace(combinedPath)
            && File.Exists(combinedPath)
            && scenes.All(scene => !string.IsNullOrWhiteSpace(scene.PrimaryAsset.AssetType)
                && !string.IsNullOrWhiteSpace(scene.PrimaryAsset.Path)
                && !IsSscPath(scene.PrimaryAsset.Path));
        return new DirectorTimelineDocument(
            plan.Id.ToString("D"),
            plan.RegionId,
            plan.ContentCategoryCode,
            finalPackage?.PlannedFormat ?? plan.PlannedFormat ?? string.Empty,
            finalPackage?.Title ?? polished?.Title ?? plan.Title ?? string.Empty,
            Round(narrationTotal),
            new DirectorTimelineAudio(combinedPath, Round(narrationTotal), finalPackage?.VoiceProfile.VoiceName ?? manifest?.VoiceName ?? string.Empty, finalPackage?.MusicProfile.Mood ?? string.Empty, finalPackage?.MusicProfile.Intensity ?? string.Empty),
            scenes,
            new DirectorTimelineRenderReadiness(renderReady, missingRequiredAssets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
            GenerationSource,
            DateTimeOffset.UtcNow);
    }

    private sealed record VisualAssetSelection(
        IReadOnlyList<DirectorTimelineAsset> RenderableAssets,
        IReadOnlyList<DirectorTimelineAsset> TechnicalReferences,
        bool UsedClosingFallback,
        bool UsedRecoveredFallback,
        int? ReusedFromSceneNumber);

    private sealed record CandidateTimelineAsset(
        DirectorTimelineAsset Asset,
        VisualAssetKind Kind,
        int SceneNumber,
        int Priority);

    private sealed record ClassifiedTimelineAsset(DirectorTimelineAsset? Asset, VisualAssetKind Kind)
    {
        public static ClassifiedTimelineAsset Empty { get; } = new(null, VisualAssetKind.None);
    }

    private enum VisualAssetKind
    {
        None,
        StellariumCapturePng,
        AiImageActual,
        AiCinematicActual,
        AiImagePlanned,
        AiCinematicPlanned,
        AiHeroPlanned,
        SkyMapCard,
        ConstellationGuide,
        TextOverlayCard,
        NasaMetadata,
        ThumbnailConcept,
        JsonVisualPackage,
        OtherRenderable
    }

    private static double ResolveSceneDuration(TtsPackageSegment segment, IReadOnlyDictionary<int, TtsAudioManifestSegment> manifestDurations)
    {
        if (manifestDurations.TryGetValue(segment.SceneNumber, out var manifestSegment) && manifestSegment.DurationSeconds > 0)
            return Round(manifestSegment.DurationSeconds);
        return Math.Max(0.1, segment.EstimatedDurationSeconds);
    }

    private static IReadOnlyList<double> CalculateBreathers(int sceneCount, double narrationTotal, double rawDuration)
    {
        if (sceneCount <= 1 || narrationTotal <= rawDuration + 0.3)
            return Enumerable.Repeat(0d, Math.Max(sceneCount, 0)).ToArray();

        var gapCount = sceneCount - 1;
        var gap = Math.Clamp((narrationTotal - rawDuration) / gapCount, 0.3, 0.7);
        return Enumerable.Range(0, sceneCount).Select(i => i < gapCount ? Round(gap) : 0d).ToArray();
    }

    private static VisualAssetSelection SelectVisualAssets(
        int sceneNumber,
        int lastSceneNumber,
        IReadOnlyList<AstronomyAssetProductionJob> jobs,
        IReadOnlyList<AstronomyAssetProductionJob> planJobs,
        IReadOnlyList<AstronomyAssetProductionJob> eventFallbackJobs,
        IReadOnlyList<DirectorTimelineScene> existingScenes,
        List<string> warnings)
    {
        var technicalReferences = SelectTechnicalReferences(jobs, warnings).ToList();
        var renderable = SelectRenderableAssetCandidates(jobs, sceneNumber, lastSceneNumber, warnings, AssetRecoveryScope.SameSceneExact).ToList();
        var usedRecoveredFallback = false;
        int? reusedFromSceneNumber = null;

        if (renderable.Count == 0)
        {
            var samePlanFallback = SelectRenderableAssetCandidates(planJobs, sceneNumber, lastSceneNumber, warnings, AssetRecoveryScope.SamePlan)
                .Where(candidate => candidate.SceneNumber != sceneNumber)
                .ToList();
            if (samePlanFallback.Count > 0)
            {
                renderable.AddRange(samePlanFallback);
                usedRecoveredFallback = true;
                reusedFromSceneNumber = samePlanFallback.First().SceneNumber;
            }
        }

        if (renderable.Count == 0 && eventFallbackJobs.Count > 0)
        {
            var sameEventFallback = SelectRenderableAssetCandidates(eventFallbackJobs, sceneNumber, lastSceneNumber, warnings, AssetRecoveryScope.SameEvent).ToList();
            if (sameEventFallback.Count > 0)
            {
                renderable.AddRange(sameEventFallback);
                usedRecoveredFallback = true;
                reusedFromSceneNumber = sameEventFallback.First().SceneNumber == sceneNumber ? null : sameEventFallback.First().SceneNumber;
            }
        }

        if (renderable.Count == 0)
        {
            var reused = existingScenes
                .Where(scene => !string.IsNullOrWhiteSpace(scene.PrimaryAsset.AssetType)
                    && !string.IsNullOrWhiteSpace(scene.PrimaryAsset.Path)
                    && !IsSscPath(scene.PrimaryAsset.Path))
                .Select(scene => new CandidateTimelineAsset(scene.PrimaryAsset, ClassifyAssetKind(scene.PrimaryAsset), scene.SceneNumber, 0))
                .OrderBy(candidate => RankRenderableAsset(candidate.Kind, false, AssetRecoveryScope.LastResortTimelineReuse))
                .ThenBy(candidate => candidate.SceneNumber)
                .FirstOrDefault();

            if (reused is not null)
            {
                renderable.Add(reused);
                usedRecoveredFallback = true;
                reusedFromSceneNumber = reused.SceneNumber;
            }
        }

        var assets = renderable
            .Where(candidate => !IsSscPath(candidate.Asset.Path))
            .DistinctBy(candidate => $"{candidate.Asset.AssetType}|{candidate.Asset.Path}", StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Asset)
            .ToList();

        return new VisualAssetSelection(
            assets,
            technicalReferences.DistinctBy(a => $"{a.AssetType}|{a.Path}", StringComparer.OrdinalIgnoreCase).ToList(),
            sceneNumber == lastSceneNumber && usedRecoveredFallback,
            usedRecoveredFallback,
            reusedFromSceneNumber);
    }

    private static IEnumerable<CandidateTimelineAsset> SelectRenderableAssetCandidates(
        IReadOnlyList<AstronomyAssetProductionJob> jobs,
        int sceneNumber,
        int lastSceneNumber,
        List<string> warnings,
        AssetRecoveryScope scope)
        => jobs
            .Where(IsVisualJob)
            .Select(job => new { Job = job, Classified = ToRenderableTimelineAsset(job, warnings) })
            .Where(x => x.Classified.Asset is not null && !IsSscPath(x.Classified.Asset.Path))
            .Select(x => new CandidateTimelineAsset(x.Classified.Asset!, NormalizeVisualAssetKind(x.Classified), x.Job.SceneNumber, x.Job.Priority))
            .OrderBy(x => RankRenderableAsset(x.Kind, sceneNumber == lastSceneNumber, scope))
            .ThenBy(x => x.Priority)
            .ThenBy(x => Math.Abs(x.SceneNumber - sceneNumber));

    private static IEnumerable<DirectorTimelineAsset> SelectTechnicalReferences(IReadOnlyList<AstronomyAssetProductionJob> jobs, List<string> warnings)
        => jobs
            .Select(job => ToTechnicalReference(job, warnings))
            .Where(asset => asset is not null)
            .Select(asset => asset!);

    private enum AssetRecoveryScope
    {
        SameSceneExact,
        SamePlan,
        SameEvent,
        LastResortTimelineReuse
    }

    private static int RankRenderableAsset(VisualAssetKind kind, bool isClosingScene, AssetRecoveryScope scope)
    {
        if (scope == AssetRecoveryScope.SameSceneExact)
        {
            return kind switch
            {
                VisualAssetKind.TextOverlayCard => 0,
                VisualAssetKind.SkyMapCard => 1,
                VisualAssetKind.ConstellationGuide => 2,
                VisualAssetKind.NasaMetadata => 3,
                VisualAssetKind.StellariumCapturePng => 4,
                VisualAssetKind.AiImageActual or VisualAssetKind.AiCinematicActual => 5,
                VisualAssetKind.AiImagePlanned or VisualAssetKind.AiCinematicPlanned or VisualAssetKind.AiHeroPlanned => 6,
                VisualAssetKind.JsonVisualPackage => 7,
                _ => 8
            };
        }

        if (scope == AssetRecoveryScope.SamePlan)
        {
            return kind switch
            {
                VisualAssetKind.TextOverlayCard => 0,
                VisualAssetKind.SkyMapCard => 1,
                VisualAssetKind.ThumbnailConcept => 2,
                VisualAssetKind.AiImagePlanned or VisualAssetKind.AiCinematicPlanned or VisualAssetKind.AiHeroPlanned => 3,
                VisualAssetKind.AiImageActual or VisualAssetKind.AiCinematicActual => 4,
                VisualAssetKind.JsonVisualPackage => 5,
                _ => 6
            };
        }

        if (scope == AssetRecoveryScope.SameEvent)
        {
            return kind switch
            {
                VisualAssetKind.StellariumCapturePng => 0,
                VisualAssetKind.ConstellationGuide => 1,
                VisualAssetKind.NasaMetadata => 2,
                VisualAssetKind.AiImagePlanned or VisualAssetKind.AiCinematicPlanned or VisualAssetKind.AiHeroPlanned => 3,
                VisualAssetKind.AiImageActual or VisualAssetKind.AiCinematicActual => 4,
                VisualAssetKind.JsonVisualPackage => 5,
                _ => 6
            };
        }

        if (isClosingScene)
        {
            return kind switch
            {
                VisualAssetKind.AiCinematicActual => 0,
                VisualAssetKind.AiCinematicPlanned => 1,
                VisualAssetKind.TextOverlayCard => 2,
                VisualAssetKind.SkyMapCard => 3,
                VisualAssetKind.StellariumCapturePng => 4,
                VisualAssetKind.ConstellationGuide => 5,
                VisualAssetKind.NasaMetadata => 6,
                VisualAssetKind.AiHeroPlanned => 7,
                _ => 8
            };
        }

        return kind switch
        {
            VisualAssetKind.StellariumCapturePng => 0,
            VisualAssetKind.AiImageActual => 1,
            VisualAssetKind.SkyMapCard => 2,
            VisualAssetKind.ConstellationGuide => 3,
            VisualAssetKind.TextOverlayCard => 4,
            VisualAssetKind.NasaMetadata => 5,
            VisualAssetKind.AiImagePlanned => 6,
            VisualAssetKind.JsonVisualPackage => 7,
            _ => 8
        };
    }

    private static ClassifiedTimelineAsset ToRenderableTimelineAsset(AstronomyAssetProductionJob job, List<string> warnings)
    {
        if (IsStellariumJob(job))
        {
            var pngPath = FirstExistingPng(
                job.OutputPath,
                ExtractMetadataValue(job.MetadataJson, "CapturePath"),
                ExtractMetadataValue(job.MetadataJson, "capturePath"),
                ExtractMetadataValue(job.MetadataJson, "screenshotPath"),
                ExtractMetadataValue(job.MetadataJson, "imagePath"),
                ExtractMetadataValue(job.MetadataJson, "assetPath"),
                ExtractMetadataValue(job.MetadataJson, "outputPath"));
            if (!string.IsNullOrWhiteSpace(pngPath))
                return new ClassifiedTimelineAsset(new DirectorTimelineAsset(job.AssetType, pngPath, BuildUsage(job)), VisualAssetKind.StellariumCapturePng);

            if (HasSscReference(job))
                warnings.Add(MissingStellariumCaptureWarning);
            return ClassifiedTimelineAsset.Empty;
        }

        if (IsAiImageType(job.AssetType))
        {
            var actualPath = FirstNonJsonPath(ExtractMetadataValue(job.MetadataJson, "generatedImagePath"), ExtractMetadataValue(job.MetadataJson, "imagePath"), ExtractMetadataValue(job.MetadataJson, "assetPath"), job.OutputPath);
            if (!string.IsNullOrWhiteSpace(actualPath))
            {
                return new ClassifiedTimelineAsset(
                    new DirectorTimelineAsset(job.AssetType, actualPath, BuildUsage(job)),
                    job.AssetType.Contains("Cinematic", StringComparison.OrdinalIgnoreCase) ? VisualAssetKind.AiCinematicActual : VisualAssetKind.AiImageActual);
            }

            warnings.Add(AiPromptMissingWarning);
            var plannedPath = FirstNonBlank(job.OutputPath, job.PromptOrInstruction, ExtractMetadataValue(job.MetadataJson, "imagePrompt"), ExtractMetadataValue(job.MetadataJson, "prompt"));
            if (string.IsNullOrWhiteSpace(plannedPath))
                return ClassifiedTimelineAsset.Empty;

            return new ClassifiedTimelineAsset(
                new DirectorTimelineAsset("PlannedVisual", plannedPath, BuildUsage(job)),
                job.AssetType.Contains("Cinematic", StringComparison.OrdinalIgnoreCase)
                    ? VisualAssetKind.AiCinematicPlanned
                    : job.AssetType.Contains("Hero", StringComparison.OrdinalIgnoreCase) ? VisualAssetKind.AiHeroPlanned : VisualAssetKind.AiImagePlanned);
        }

        var path = FirstNonBlank(job.OutputPath, ExtractMetadataValue(job.MetadataJson, "outputPath"), ExtractMetadataValue(job.MetadataJson, "assetPath"));
        if (string.IsNullOrWhiteSpace(path))
            return ClassifiedTimelineAsset.Empty;

        var kind = job.AssetType switch
        {
            var t when t.Equals("SkyMapCard", StringComparison.OrdinalIgnoreCase) => VisualAssetKind.SkyMapCard,
            var t when t.Equals("ConstellationGuide", StringComparison.OrdinalIgnoreCase) => VisualAssetKind.ConstellationGuide,
            var t when t.Equals("TextOverlayCard", StringComparison.OrdinalIgnoreCase) => VisualAssetKind.TextOverlayCard,
            var t when t.Equals("ThumbnailConcept", StringComparison.OrdinalIgnoreCase) => VisualAssetKind.ThumbnailConcept,
            var t when t.Contains("Nasa", StringComparison.OrdinalIgnoreCase) => VisualAssetKind.NasaMetadata,
            _ when IsJsonPath(path) => VisualAssetKind.JsonVisualPackage,
            _ => VisualAssetKind.OtherRenderable
        };
        return new ClassifiedTimelineAsset(new DirectorTimelineAsset(job.AssetType, path, BuildUsage(job)), kind);
    }

    private static DirectorTimelineAsset? ToTechnicalReference(AstronomyAssetProductionJob job, List<string> warnings)
    {
        if (!HasSscReference(job))
            return null;

        var path = FirstNonBlank(job.OutputPath, ExtractMetadataValue(job.MetadataJson, "SscPath"), ExtractMetadataValue(job.MetadataJson, "sscPath"), ExtractMetadataValue(job.MetadataJson, "sscFile"));
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase))
            return null;

        if (FirstExistingPng(job.OutputPath, ExtractMetadataValue(job.MetadataJson, "CapturePath"), ExtractMetadataValue(job.MetadataJson, "capturePath")) is null)
            warnings.Add(MissingStellariumCaptureWarning);

        return new DirectorTimelineAsset("StellariumScriptReference", path, "Source script used to produce or regenerate capture.");
    }


    private static VisualAssetKind NormalizeVisualAssetKind(ClassifiedTimelineAsset classified)
        => classified.Asset is null ? VisualAssetKind.None : ClassifyAssetKind(classified.Asset, classified.Kind);

    private static VisualAssetKind ClassifyAssetKind(DirectorTimelineAsset asset, VisualAssetKind? knownKind = null)
    {
        if (knownKind is { } kind && kind != VisualAssetKind.OtherRenderable)
            return kind;

        if (asset.AssetType.Equals("SkyMapCard", StringComparison.OrdinalIgnoreCase))
            return VisualAssetKind.SkyMapCard;
        if (asset.AssetType.Equals("ConstellationGuide", StringComparison.OrdinalIgnoreCase))
            return VisualAssetKind.ConstellationGuide;
        if (asset.AssetType.Equals("TextOverlayCard", StringComparison.OrdinalIgnoreCase))
            return VisualAssetKind.TextOverlayCard;
        if (asset.AssetType.Equals("ThumbnailConcept", StringComparison.OrdinalIgnoreCase))
            return VisualAssetKind.ThumbnailConcept;
        if (asset.AssetType.Contains("Nasa", StringComparison.OrdinalIgnoreCase))
            return VisualAssetKind.NasaMetadata;
        if (asset.AssetType.Equals("PlannedVisual", StringComparison.OrdinalIgnoreCase))
            return VisualAssetKind.AiImagePlanned;
        if (asset.AssetType.Contains("Stellarium", StringComparison.OrdinalIgnoreCase) && asset.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return VisualAssetKind.StellariumCapturePng;
        if (asset.AssetType.Contains("Ai", StringComparison.OrdinalIgnoreCase) && asset.AssetType.Contains("Image", StringComparison.OrdinalIgnoreCase))
            return VisualAssetKind.AiImageActual;
        return IsJsonPath(asset.Path) ? VisualAssetKind.JsonVisualPackage : VisualAssetKind.OtherRenderable;
    }

    private static bool IsVisualJob(AstronomyAssetProductionJob job)
        => IsAiImageType(job.AssetType) || IsStellariumJob(job) || HasUsablePath(job) || !string.IsNullOrWhiteSpace(job.PromptOrInstruction);

    private static bool HasUsablePath(AstronomyAssetProductionJob job)
        => !string.IsNullOrWhiteSpace(job.OutputPath) || !string.IsNullOrWhiteSpace(ExtractMetadataValue(job.MetadataJson, "outputPath")) || !string.IsNullOrWhiteSpace(ExtractMetadataValue(job.MetadataJson, "assetPath"));

    private static bool HasSscReference(AstronomyAssetProductionJob job)
        => FirstNonBlank(job.OutputPath, ExtractMetadataValue(job.MetadataJson, "SscPath"), ExtractMetadataValue(job.MetadataJson, "sscPath"), ExtractMetadataValue(job.MetadataJson, "sscFile"))?.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsStellariumJob(AstronomyAssetProductionJob job)
        => job.AssetType.Contains("Stellarium", StringComparison.OrdinalIgnoreCase) || HasSscReference(job);

    private static bool IsAiImageType(string assetType)
        => assetType.Contains("Ai", StringComparison.OrdinalIgnoreCase) && assetType.Contains("Image", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssetType(AstronomyAssetProductionJob job, string assetType)
        => string.Equals(job.AssetType, assetType, StringComparison.OrdinalIgnoreCase);

    private static string BuildUsage(AstronomyAssetProductionJob job)
        => string.IsNullOrWhiteSpace(job.AssetPurpose) ? $"Primary visual for {job.SceneName}" : job.AssetPurpose;

    private static string ResolveCameraMotion(string category, int sceneNumber, int lastSceneNumber)
        => category switch
        {
            "RareEventAlert" when sceneNumber == 1 => "slow push-in",
            "RareEventAlert" when sceneNumber == lastSceneNumber => "slow fade out",
            "RareEventAlert" => "subtle pan / hold",
            "PlanetConjunction" when sceneNumber == 1 => "slow zoom toward pairing",
            "PlanetConjunction" when sceneNumber == lastSceneNumber => "slow pull-back",
            "PlanetConjunction" => "gentle orbit / line-of-sight style",
            "PlanetGrouping" when sceneNumber == lastSceneNumber => "closing hold",
            "PlanetGrouping" => "guided pan across group with object sequence emphasis",
            "WeeklySkyForecast" => "episode montage crossfade with night-by-night progression",
            _ when sceneNumber == lastSceneNumber => "slow pull-back",
            _ => "subtle push-in"
        };

    private static string ResolveVisualMood(string category, int sceneNumber, int lastSceneNumber)
        => category switch
        {
            "RareEventAlert" => sceneNumber == 1 ? "urgent cinematic skywatch" : "focused observational clarity",
            "PlanetConjunction" => "calm wonder with clear positional emphasis",
            "PlanetGrouping" => "guided discovery across multiple bright objects",
            "WeeklySkyForecast" => "weekly night-sky guide montage",
            _ => sceneNumber == lastSceneNumber ? "reflective closing sky" : "calm astronomy explainer"
        };

    private static string ResolveMusicCue(string category, int sceneNumber, int lastSceneNumber)
        => sceneNumber == 1
            ? category == "RareEventAlert" ? "low tension bed enters" : "ambient bed fades in"
            : sceneNumber == lastSceneNumber ? "music resolves and fades" : "maintain gentle underscore";

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string BuildPlanRoot(string root, string regionId, Guid planId)
        => Path.Combine(root, "assets", SanitizePathSegment(regionId) ?? "unknown-region", "plans", planId.ToString("D"));

    private static string BuildTimelinePath(string root, string regionId, Guid planId)
        => Path.Combine(BuildPlanRoot(root, regionId, planId), "timeline", TimelineFileName);

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
            ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static string? ExtractMetadataValue(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJsonPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsSscPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).Equals(".ssc", StringComparison.OrdinalIgnoreCase);

    private static string? FirstExistingPng(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(value) || values[0] == value)
                return value;
        }
        return null;
    }

    private static string? FirstNonJsonPath(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && !Path.GetExtension(v).Equals(".json", StringComparison.OrdinalIgnoreCase));

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static double Round(double value)
        => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
