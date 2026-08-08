using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase11ExecutionGateTests
{
    private static readonly string[] NonHeroOutputs = ["ShortVideo", "LongVideo", "Thumbnail"];
    private static readonly string[] HeroOutputs = ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"];

    [Fact]
    public void ManualOverrideWithHeroAssetMakesPhase11Applicable()
    {
        var resolved = ContentPlanProductionExecutionService.ResolveRequestedOutputs(NonHeroOutputs, HeroOutputs);
        Assert.True(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(resolved.AfterResolution, 11));
    }

    [Fact]
    public void Phase11ExecutesWhenOverrideContainsHeroAsset() => ManualOverrideWithHeroAssetMakesPhase11Applicable();

    [Fact]
    public void Phase11StillSkipsWithoutHeroAsset() =>
        Assert.False(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(NonHeroOutputs, 11));

    [Fact]
    public void ExplicitHeroAssetMakesPhase11Applicable() =>
        Assert.True(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(HeroOutputs, 11));

    [Fact]
    public void NoHeroAssetStillMakesPhase11NotApplicable() =>
        Assert.False(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(NonHeroOutputs, 11));

    [Fact]
    public void ThumbnailDoesNotImplicitlyAddHeroAsset() =>
        Assert.False(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(["Thumbnail"], 11));

    [Fact]
    public void PlannedHeroEngineStepDoesNotImplicitlyAddHeroAsset() =>
        Assert.False(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(NonHeroOutputs, 11));

    [Fact]
    public void Phase11RangeWithHeroNotRequestedSkipsBeforeHeroValidation()
    {
        Assert.False(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(NonHeroOutputs, 11));
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Skipped, true));
    }

    [Fact]
    public void MissingLegacyHeroFilesDoNotFailWhenHeroNotRequested() =>
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Skipped, false));

    [Fact]
    public void PlannedHeroEngineStepDoesNotOverrideRequestedOutputs() =>
        Assert.False(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(NonHeroOutputs, 11));

    [Fact]
    public void NonHeroPhase11SkipReturnsP11HeroAssetNotRequested() =>
        Assert.Equal("P11_HERO_ASSET_NOT_REQUESTED", Phase11ReasonCodes.NotRequested);

    [Fact]
    public void NonHeroPhase11SkipCountsAsSuccessfulPartialExecution() =>
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Skipped, true));

    [Fact]
    public void HeroRequestedDoesNotRequirePreexistingHeroFiles() =>
        Assert.True(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(HeroOutputs, 11));

    [Fact]
    public void HeroRequestedCanStartWithNo11HeroDirectory() =>
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Succeeded, true));

    [Fact]
    public void HeroRequestedDoesNotRequireLegacyHeroDirectory() =>
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Succeeded, true));

    [Fact]
    public void Phase11ExecutesAfterPhase10InputValidation() =>
        Assert.True(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(HeroOutputs, 10));

    [Fact]
    public void Phase11CreatesNewAuthorityBeforeCompatibilityValidation() =>
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Succeeded, true));

    [Fact]
    public void OverwriteTrueAllowsMissingHeroOutput() =>
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Failed, true));

    [Fact]
    public void OverwriteTrueDoesNotValidateDeletedHeroBeforeGeneration() =>
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Skipped, true));

    [Fact]
    public void PhaseOwnOutputCannotBePreExecutionDependency() =>
        Assert.False(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Failed, false));

    [Fact]
    public void CompletedPlanManualPhase11RerunCanRebuildMissingHero() =>
        Assert.True(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(HeroOutputs, 11));

    [Fact]
    public void LegacyHeroValidationRemainsPostGenerationForLegacyPipeline() =>
        Assert.True(ProductionPipelineExecutionService.ShouldValidateLegacyHeroOutputs(11, ProductionPhaseStatus.Succeeded, false));
}
