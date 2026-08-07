using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionOutputSelectionTests
{
    [Fact]
    public void FullAstronomyProfileRequestsShortLongThumbnail()
    {
        var request = Map(["ShortVideo", "Thumbnail"], fullProfile: true);

        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail"], request.RequestedOutputs);
    }

    [Fact]
    public void ExplicitShortOnlyPlanRemainsShortOnly()
        => Assert.Equal(["ShortVideo"], Map(["ShortVideo"]).RequestedOutputs);

    [Fact]
    public void ExplicitLongOnlyPlanRemainsLongOnly()
        => Assert.Equal(["LongVideo"], Map(["LongVideo"]).RequestedOutputs);

    [Fact]
    public void PlannedFormatShortDoesNotSuppressExplicitLongRequestedOutput()
        => Assert.Equal(
            ["ShortVideo", "LongVideo", "Thumbnail"],
            Map(["ShortVideo", "LongVideo", "Thumbnail"], plannedFormat: "ShortVideo").RequestedOutputs);

    [Fact]
    public void RequestedOutputsReachProductionPipelineRequestUnchanged()
    {
        string[] outputs = ["LongVideo", "ShortVideo", "HeroAsset"];

        Assert.Equal(outputs, Map(outputs).RequestedOutputs);
    }

    [Fact]
    public void LegacyShortProjectionWithBothVariantWorkflowResolvesConfiguredProductionProfile()
    {
        var request = Map(["ShortVideo", "Thumbnail"], assetPlanJson: "{\"plannedProductionSteps\":[\"Scene Engine Short\",\"Scene Engine Long\"]}");

        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail"], request.RequestedOutputs);
    }

    private static ContentPlanProductionPipelineRequest Map(
        IReadOnlyList<string> outputs,
        bool fullProfile = false,
        string plannedFormat = "ShortVideo",
        string? assetPlanJson = null)
    {
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = fullProfile ? "AstronomyEducation" : "RareEventAlert",
            PrimaryAstronomyEventTypeCode = fullProfile ? "CONSTELLATION" : "PLANET_CONJUNCTION",
            Title = "Test astronomy plan",
            RegionId = "GLOBAL",
            Language = "en",
            PlannedFormat = plannedFormat,
            RequestedOutputTypesJson = JsonSerializer.Serialize(outputs),
            AssetPlanJson = assetPlanJson
        };
        var intelligence = new AstronomyEventIntelligence
        {
            EventType = plan.PrimaryAstronomyEventTypeCode!,
            EventCode = "test-event",
            ExternalEventId = "test-event",
            Title = plan.Title!,
            RegionId = plan.RegionId,
            Language = plan.Language,
            StartUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            VerificationStatus = "Verified",
            ContentStrategy = fullProfile ? "EvergreenConstellationEducation" : "LocalViewingGuide"
        };

        return new ContentPlanProductionRequestMapper().Map(plan, intelligence);
    }
}
