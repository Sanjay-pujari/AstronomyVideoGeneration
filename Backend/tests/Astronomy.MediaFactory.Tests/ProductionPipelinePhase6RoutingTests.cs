using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelinePhase6RoutingTests
{
    private static string PipelineSource => File.ReadAllText(
        RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));

    [Fact]
    public void ServiceCollection_RegistersExactlyOnePhase6InputAuthorityEvaluator()
    {
        var registration = Assert.Single(BuildServices().Where(x =>
            x.ServiceType == typeof(IPhase6InputAuthorityEvaluator)));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(Phase6InputAuthorityEvaluator), registration.ImplementationType);
    }

    [Fact]
    public void ServiceCollection_RegistersExactlyOneStoryFrameIntegrationService()
    {
        var services = BuildServices();
        Assert.Single(services.Where(x => x.ServiceType == typeof(IStoryFrameIntegrationService)));
        Assert.Single(services.Where(x => x.ServiceType == typeof(StoryFrameIntegrationService)));
    }

    [Fact]
    public void ProductionPipelineConstruction_RequiresPhase6InputAuthorityEvaluator()
    {
        var parameter = GetEvaluatorParameter();
        Assert.False(parameter.HasDefaultValue);
        Assert.False(parameter.IsOptional);
        Assert.NotEqual(NullabilityState.Nullable, new NullabilityInfoContext().Create(parameter).ReadState);
    }

    [Fact]
    public void ProductionPipeline_HoldsNonNullableEvaluatorField()
    {
        var field = typeof(ProductionPipelineExecutionService)
            .GetField("_phase6InputAuthorityEvaluator", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Equal(typeof(IPhase6InputAuthorityEvaluator), field!.FieldType);
    }

    [Fact]
    public void Phase6Route_UsesDedicatedExecutionMethods()
    {
        Assert.Contains("ExecutePhase6Async", PipelineSource, StringComparison.Ordinal);
        Assert.Contains("ExecuteLockedPhase6Async", PipelineSource, StringComparison.Ordinal);
        Assert.Contains("PhaseChronicleDocumentaryArchitectCoreAsync", PipelineSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase6Route_InvokesCommittedInputEvaluator() =>
        Assert.Contains("_phase6InputAuthorityEvaluator.EvaluateAsync", PipelineSource, StringComparison.Ordinal);

    [Fact]
    public void Phase6Route_InvokesStoryFrameIntegration() =>
        Assert.Contains("storyFrameIntegrationService.BuildAsync", PipelineSource, StringComparison.Ordinal);

    [Fact]
    public void Phase6Route_UsesTypedInputFailureException()
    {
        Assert.Contains("throw new Phase6InputAuthorityException", PipelineSource, StringComparison.Ordinal);
        Assert.Contains("catch (Phase6InputAuthorityException", PipelineSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("P6INPUT_PHASE4_INVALID")]
    [InlineData("P6INPUT_PHASE5_INVALID")]
    [InlineData("P6INPUT_PHASE4_LINEAGE_MISMATCH")]
    [InlineData("P6INPUT_LONG_LINEAGE_MISMATCH")]
    [InlineData("P6INPUT_SHORT_LINEAGE_MISMATCH")]
    [InlineData("P6INPUT_CERTIFICATION_REJECTED")]
    [InlineData("P6INPUT_STORY_FRAME_NOT_ELIGIBLE")]
    [InlineData("P6INPUT_VARIANT_INVALID")]
    [InlineData("P6INPUT_VARIANT_NOT_ALLOWED")]
    [InlineData("P6INPUT_SCENE_EVIDENCE_INVALID")]
    public void Phase6InputAuthorityException_PreservesEveryDefinedReasonCode(string reasonCode)
    {
        var exception = new Phase6InputAuthorityException(reasonCode, ["deterministic error"]);
        Assert.Equal(reasonCode, exception.ReasonCode);
        Assert.Equal($"{reasonCode}: deterministic error", exception.Message);
    }

    [Fact]
    public void Phase6Route_DoesNotParseReasonCodeFromExceptionMessage()
    {
        Assert.DoesNotContain("Split(':')", PipelineSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Substring(0", PipelineSource, StringComparison.Ordinal);
        Assert.Contains(".ReasonCode", PipelineSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase6Route_DoesNotUseOptionalCertificationDiagnosticsAsAuthority()
    {
        var evaluator = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "DocumentaryBlueprint", "Phase6InputAuthorityEvaluator.cs"));
        Assert.DoesNotContain("certification-diagnostics.json", evaluator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase6Route_DoesNotReadLegacyStoryGraph()
    {
        var evaluator = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "DocumentaryBlueprint", "Phase6InputAuthorityEvaluator.cs"));
        Assert.DoesNotContain("editorial/story-graph.json", evaluator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase6Route_DoesNotInvokeLegacyCreativeBuilderBuildAsync() =>
        Assert.DoesNotContain("creativeStoryboardBuilder.BuildAsync", PipelineSource, StringComparison.Ordinal);

    [Fact]
    public void Phase6Route_AcquiresExecutionLock()
    {
        Assert.Contains("_storyFrameExecutionLock", PipelineSource, StringComparison.Ordinal);
        Assert.Contains("AcquireAsync", PipelineSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase6Route_PerformsRecoveryBeforeBuild()
    {
        var recoveryIndex = PipelineSource.IndexOf("_storyFrameTemporaryDirectoryRecovery", StringComparison.Ordinal);
        var buildIndex = PipelineSource.IndexOf("storyFrameIntegrationService.BuildAsync", StringComparison.Ordinal);
        Assert.True(recoveryIndex >= 0 && buildIndex >= 0 && recoveryIndex < buildIndex);
    }

    [Fact]
    public void Phase6Route_ValidatesBeforeCommit()
    {
        var validateIndex = PipelineSource.IndexOf("StoryFrameArtifactValidator", StringComparison.Ordinal);
        var commitIndex = PipelineSource.IndexOf("_storyFrameAuthorityCommitter", StringComparison.Ordinal);
        Assert.True(validateIndex >= 0 && commitIndex >= 0 && validateIndex < commitIndex);
    }

    [Fact]
    public void Phase6Route_HasExplicitLongAndShortVariantResolver()
    {
        Assert.Contains("ResolvePhase6RequestedVariants", PipelineSource, StringComparison.Ordinal);
        Assert.Contains("\"Long\"", PipelineSource, StringComparison.Ordinal);
        Assert.Contains("\"Short\"", PipelineSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase6Validation_UsesExactTypedFailureReasonCode() =>
        Assert.Contains("inputFailure.ReasonCode", PipelineSource, StringComparison.Ordinal);

    [Fact]
    public void Phase6Route_DoesNotCatchOperationCanceledExceptionAsInputFailure()
    {
        var evaluator = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "DocumentaryBlueprint", "Phase6InputAuthorityEvaluator.cs"));
        Assert.DoesNotContain("OperationCanceledException or", evaluator, StringComparison.Ordinal);
        Assert.DoesNotContain("or OperationCanceledException", evaluator, StringComparison.Ordinal);
    }

    private static IServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] =
                    "Host=phase6-routing-test.invalid;Port=5432;Database=astronomy_mediafactory_test;" +
                    "Username=test_user;Password=test_password;Pooling=false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddMediaFactory(configuration);
        return services;
    }

    private static ParameterInfo GetEvaluatorParameter() =>
        typeof(ProductionPipelineExecutionService).GetConstructors().Single().GetParameters()
            .Single(x => x.ParameterType == typeof(IPhase6InputAuthorityEvaluator));
}
