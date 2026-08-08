using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionOutputSelectionTests
{
    [Fact]
    public void ManualRequestedOutputsOverrideReplacesPersistedOutputs()
    {
        var resolved = ContentPlanProductionExecutionService.ResolveRequestedOutputs(
            ["ShortVideo", "LongVideo", "Thumbnail"], ["HeroAsset", "Gallery"]);

        Assert.Equal(["HeroAsset", "Gallery"], resolved.AfterResolution);
        Assert.Equal("ManualOverride", resolved.Source);
    }

    [Fact]
    public void ManualOverrideDoesNotPersistChangesToPlan()
    {
        string[] persisted = ["ShortVideo", "LongVideo", "Thumbnail"];
        _ = ContentPlanProductionExecutionService.ResolveRequestedOutputs(persisted, ["HeroAsset"]);
        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail"], persisted);
    }

    [Fact]
    public void ManualHeroOverrideDoesNotModifyPersistedPlan() => ManualOverrideDoesNotPersistChangesToPlan();

    [Fact]
    public void EmptyManualOverrideUsesPersistedPlan()
    {
        var resolved = ContentPlanProductionExecutionService.ResolveRequestedOutputs(
            ["ShortVideo", "LongVideo", "Thumbnail"], []);

        Assert.Equal(resolved.BeforeOverride, resolved.AfterResolution);
        Assert.Equal("PersistedPlan", resolved.Source);
    }

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
    public void PlannedFormatCannotRemoveHeroAssetOverride() => Assert.Equal(
        ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"],
        Map(["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"], plannedFormat: "ShortVideo").RequestedOutputs);

    [Fact]
    public void NormalizationCannotRemoveHeroAssetOverride()
    {
        var resolved = ContentPlanProductionExecutionService.ResolveRequestedOutputs(
            ["ShortVideo"], ["shortvideo", "LongVideo", "Thumbnail", "heroasset", "HeroAsset"]);

        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"], resolved.AfterResolution);
    }

    [Fact]
    public void ManualRequestedOutputsOverridePreservesHeroAsset()
    {
        var resolved = ContentPlanProductionExecutionService.ResolveRequestedOutputs(
            ["ShortVideo", "LongVideo", "Thumbnail"],
            ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"]);

        Assert.Equal("ManualOverride", resolved.Source);
        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail"], resolved.BeforeOverride);
        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"], resolved.Override);
        Assert.Equal(resolved.Override, resolved.AfterResolution);
    }

    [Fact]
    public void HeroAssetSurvivesProductionPipelineRequestMapping()
    {
        var mapped = Map(["ShortVideo", "LongVideo", "Thumbnail"]);
        var resolved = ContentPlanProductionExecutionService.ResolveRequestedOutputs(
            mapped.RequestedOutputs, ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"]);
        var pipelineRequest = new ProductionPipelineRequest(
            mapped with { RequestedOutputs = resolved.AfterResolution }, Guid.NewGuid(), "/tmp/output", false);

        Assert.Contains("HeroAsset", pipelineRequest.Request.RequestedOutputs);
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
