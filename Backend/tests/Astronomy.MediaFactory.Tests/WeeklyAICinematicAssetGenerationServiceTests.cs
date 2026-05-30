using System.Buffers.Binary;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklyAICinematicAssetGenerationServiceTests
{
    [Fact]
    public async Task GenerateAndPersistAsync_SelectsTopBatchAndDefersRemainder_WhenProviderConfigured()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var generator = new RecordingAICinematicImageGenerator();
        var service = CreateService(generator, maxAssetsPerRun: 2);

        try
        {
            var summary = await service.GenerateAndPersistAsync(
                CreateVisualAssetPlan([
                    CreateSegment("seg-normal", priority: 50, emotionalReset: false, pacingReset: false),
                    CreateSegment("seg-pacing", priority: 50, emotionalReset: false, pacingReset: true),
                    CreateSegment("seg-emotional", priority: 50, emotionalReset: true, pacingReset: false),
                    CreateSegment("seg-lower", priority: 10, emotionalReset: true, pacingReset: true)
                ]),
                CreateBalanceReport(),
                CreateDiversificationPlan(),
                CreateEpisodeArchitecture(),
                weeklyContext: null,
                workingDirectoryRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            Assert.Equal(4, summary.PlannedCount);
            Assert.Equal(2, summary.GeneratedCount);
            Assert.Equal(2, summary.ProductionReadyCount);
            Assert.Equal(2, summary.DeferredCount);
            Assert.Equal(2, generator.AssetCodes.Count);
            Assert.Equal("asset_seg_emotional", generator.AssetCodes[0]);
            Assert.Equal("asset_seg_pacing", generator.AssetCodes[1]);
            Assert.Equal(2, summary.Results.Count(result => result.GenerationStatus == "Deferred"));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static WeeklyAICinematicAssetGenerationService CreateService(IAICinematicImageGenerator generator, int maxAssetsPerRun) => new(
        new AICinematicPromptBuilder(new AICinematicStylePolicy(), NullLogger<AICinematicPromptBuilder>.Instance),
        new AICinematicAssetPersister(),
        new AICinematicAssetValidator(),
        generator,
        Options.Create(new WeeklySkyForecastAICinematicAssetsOptions
        {
            Enabled = true,
            MaxAssetsPerRun = maxAssetsPerRun,
            ContinueOnFailure = true
        }),
        NullLogger<WeeklyAICinematicAssetGenerationService>.Instance);

    private static WeeklyVisualAssetPlan CreateVisualAssetPlan(IReadOnlyList<SegmentVisualAssetPlan> longformSegments) => new(
        Guid.NewGuid(),
        "US",
        "en",
        new DateOnly(2026, 5, 25),
        DateTime.UtcNow,
        [],
        longformSegments,
        [],
        VisualAssetPlanningReady: true,
        PlannedVisualAssetCount: longformSegments.Count,
        PlannedMotionGraphicsCount: 0,
        PlannedEducationalOverlayCount: 0,
        PlannedAICinematicCount: longformSegments.Count,
        PlannedNASAAssetCount: 0,
        PlannedJWSTAssetCount: 0,
        ValidationWarnings: []);

    private static SegmentVisualAssetPlan CreateSegment(string segmentId, int priority, bool emotionalReset, bool pacingReset) => new(
        segmentId,
        "CustomSegment",
        VisualAssetSourceType.AICinematic,
        null,
        null,
        [new VisualAssetRequirement($"req-{segmentId}", $"asset-{segmentId}", "support", "cinematic_segment_support", VisualAssetSourceType.AICinematic, false, true, "generate")],
        [],
        RequiresNewAssets: true,
        AssetPriority: priority,
        EducationalImportance: 0,
        EmotionalImportance: 0,
        CinematicImportance: 0,
        RetentionImportance: 0,
        EstimatedScreenTimeSeconds: 10,
        TransitionStyle: "cut",
        VisualComplexity: "simple",
        ProductionStatus: "Planned",
        AssignedObjects: [],
        SourcePlans: [new VisualAssetSourcePlan(VisualAssetSourceType.AICinematic, "cinematic_segment_support", "support")],
        RetentionMetadata: new SegmentRetentionMetadata(pacingReset, emotionalReset, false, false, false),
        Warnings: []);

    private static WeeklyVisualBalanceReport CreateBalanceReport() => new(
        Guid.NewGuid(),
        DateTime.UtcNow,
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        VisualBalanceHealthy: false);

    private static WeeklySegmentDiversificationPlan CreateDiversificationPlan() => new(
        Guid.NewGuid(),
        "US",
        "en",
        new DateOnly(2026, 5, 25),
        DateTime.UtcNow,
        [],
        [],
        [],
        SegmentDiversificationReady: true,
        DiversifiedLongformSegmentCount: 0,
        DiversifiedShortformSegmentCount: 0,
        AssetExpansionRequired: true,
        HighestRetentionRiskScore: 0,
        HighestRepetitionRiskScore: 0,
        ValidationWarnings: []);

    private static WeeklyEpisodeArchitectureResult CreateEpisodeArchitecture()
    {
        var plan = new WeeklyEpisodePlan(
            Guid.NewGuid(),
            "US",
            "en",
            new DateOnly(2026, 5, 25),
            WeeklyEpisodeType.LongFormWeeklyForecast,
            60,
            [],
            [],
            "Planned");
        return new WeeklyEpisodeArchitectureResult(plan, plan, plan, string.Empty, string.Empty, string.Empty, true);
    }

    private sealed class RecordingAICinematicImageGenerator : IAICinematicImageGenerator
    {
        public List<string> AssetCodes { get; } = [];
        public bool IsConfigured => true;
        public string DeploymentName => "test-image-deployment";

        public async Task<AICinematicProviderResult> GenerateAsync(AICinematicAssetRequest request, CancellationToken cancellationToken)
        {
            AssetCodes.Add(request.AssetCode);
            Directory.CreateDirectory(Path.GetDirectoryName(request.PlannedImagePath)!);
            await File.WriteAllBytesAsync(request.PlannedImagePath, CreateMinimalPngBytes(), cancellationToken);
            return new AICinematicProviderResult("Generated", request.PlannedImagePath, ProviderConfigured: true, []);
        }

        private static byte[] CreateMinimalPngBytes()
        {
            var bytes = new byte[(50 * 1024) + 1];
            bytes[0] = 0x89;
            bytes[1] = (byte)'P';
            bytes[2] = (byte)'N';
            bytes[3] = (byte)'G';
            bytes[4] = 0x0D;
            bytes[5] = 0x0A;
            bytes[6] = 0x1A;
            bytes[7] = 0x0A;
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), 1920);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), 1080);
            return bytes;
        }
    }
}
