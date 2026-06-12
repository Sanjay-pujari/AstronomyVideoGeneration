using System.Reflection;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelineExecutionServiceTests
{
    [Fact]
    public void FirstNonEmpty_ReturnsEmptyString_WhenAllCandidatesAreMissing()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("FirstNonEmpty", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object?[] { new string?[] { null, "", "   " } });

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void PhaseGating_NamedFullMoonShortOnly_SkipsLongNarration()
    {
        var context = CreateContext("NamedFullMoon", ["ShortVideo"]);

        Assert.True(IsPhaseRequired(context, 13));
        Assert.False(IsPhaseRequired(context, 14));
        Assert.True(IsPhaseRequired(context, 15));
        Assert.False(IsPhaseRequired(context, 16));
        Assert.True(IsPhaseRequired(context, 17));
        Assert.False(IsPhaseRequired(context, 18));
    }

    [Fact]
    public void PhaseGating_MeteorShortAndLong_RunsBothNarrationPhases()
    {
        var context = CreateContext("MeteorShower", ["ShortVideo", "LongVideo"]);

        Assert.True(IsPhaseRequired(context, 13));
        Assert.True(IsPhaseRequired(context, 14));
        Assert.True(IsPhaseRequired(context, 15));
        Assert.True(IsPhaseRequired(context, 16));
        Assert.True(IsPhaseRequired(context, 17));
        Assert.True(IsPhaseRequired(context, 18));
    }

    [Fact]
    public void PhaseGating_ThumbnailOnly_SkipsVideoAndAssetPhasesNotRequested()
    {
        var context = CreateContext("FutureDomain", ["Thumbnail"]);

        Assert.False(IsPhaseRequired(context, 11));
        Assert.True(IsPhaseRequired(context, 12));
        Assert.False(IsPhaseRequired(context, 13));
        Assert.False(IsPhaseRequired(context, 14));
        Assert.False(IsPhaseRequired(context, 15));
        Assert.False(IsPhaseRequired(context, 16));
        Assert.False(IsPhaseRequired(context, 17));
        Assert.False(IsPhaseRequired(context, 18));
        Assert.True(IsPhaseRequired(context, 19));
    }

    [Fact]
    public void RequestedOutputCompletion_ReportsSkippedForUnrequestedLongVideo()
    {
        var context = CreateContext("PlanetPairing", ["ShortVideo", "Thumbnail"]);
        var now = DateTimeOffset.UtcNow;
        ProductionPhaseResult[] phaseResults =
        [
            new(12, "Generate Thumbnails", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(13, "Generate Short Narration", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(14, "Generate Long Narration", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested"),
            new(15, "Generate Short TTS", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(16, "Generate Long TTS", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested"),
            new(17, "Assemble Short Video", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(18, "Assemble Long Video", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested")
        ];

        var completion = BuildRequestedOutputCompletion(context, phaseResults);

        Assert.Contains(completion, item => item.OutputType == "ShortVideo" && item.Requested && item.Status == "Succeeded");
        Assert.Contains(completion, item => item.OutputType == "LongVideo" && !item.Requested && item.Status == "Skipped");
        Assert.Contains(completion, item => item.OutputType == "Thumbnail" && item.Requested && item.Status == "Succeeded");
    }

    private static bool IsPhaseRequired(ProductionPhaseContext context, int phaseNo)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("IsPhaseRequiredForRequestedOutputs", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method!.Invoke(null, [context, phaseNo])!;
    }

    private static IReadOnlyList<RequestedOutputCompletion> BuildRequestedOutputCompletion(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults)
    {
        var method = typeof(ProductionPipelineExecutionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "BuildRequestedOutputCompletion" && m.GetParameters().Length == 2);
        return (IReadOnlyList<RequestedOutputCompletion>)method.Invoke(null, [context, phaseResults])!;
    }

    private static ProductionPhaseContext CreateContext(string eventType, IReadOnlyList<string> requestedOutputs)
    {
        var planId = Guid.NewGuid();
        var outputRoot = Path.Combine(Path.GetTempPath(), "astro-pulse-phase-gating-tests", planId.ToString("N"));
        var request = new ContentPlanProductionPipelineRequest(
            planId,
            "AstronomyEvent",
            $"Current {eventType} Event",
            $"{eventType} Tonight",
            eventType,
            "us",
            "en",
            [eventType == "PlanetPairing" ? "Venus" : "Moon"],
            eventType == "PlanetPairing" ? ["Jupiter"] : [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow,
            null,
            string.Join("+", requestedOutputs),
            requestedOutputs,
            null,
            null,
            null,
            null,
            "Verified",
            "Test",
            "Current event strategy",
            "9 PM",
            "western sky",
            "United States",
            null,
            "9 PM to midnight",
            null,
            null,
            requestedOutputs,
            [],
            []);
        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            eventType,
            request.Title,
            request.ShortTitle,
            request.StartUtc,
            request.PeakUtc,
            request.LocalPeakTime,
            request.BestViewingWindowLocal,
            request.SkyDirectionHint,
            request.VisibilityRegion,
            request.PrimaryObjects,
            request.SecondaryObjects,
            null,
            request.MoonInterference,
            request.MoonIlluminationPercent,
            null,
            [],
            [],
            [],
            [],
            []);
        var executionContext = new ProductionPipelineExecutionContext(
            true,
            planId,
            Guid.NewGuid(),
            null,
            true,
            true,
            "Approved",
            "Approved",
            true,
            true,
            "Verified",
            request.ContentStrategy,
            request.RegionId,
            request.Language,
            request.RequestedOutputs,
            request.Category,
            request.PlannedFormat,
            DateTimeOffset.UtcNow.Year,
            request.EventType,
            Path.Combine(outputRoot, "plan-input"),
            Path.Combine(outputRoot, "question-engine"),
            Path.Combine(outputRoot, "scene-approval-v3"),
            Path.Combine(outputRoot, "hero"),
            Path.Combine(outputRoot, "thumbnails"),
            Path.Combine(outputRoot, "narration"),
            Path.Combine(outputRoot, "tts"),
            Path.Combine(outputRoot, "video-assembly"),
            Path.Combine(outputRoot, "validation"),
            intelligence,
            new GenericAstronomyEventStrategy(),
            null);
        var pipelineRequest = new ProductionPipelineRequest(request, Guid.NewGuid(), outputRoot, false, ExecutionContext: executionContext);
        return new ProductionPhaseContext(pipelineRequest, request, Guid.NewGuid(), Guid.NewGuid().ToString("D"), outputRoot, executionContext, intelligence, new GenericAstronomyEventStrategy(), false, false, 1, 19, false);
    }
}
