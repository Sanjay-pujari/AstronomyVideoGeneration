using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
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
    [Fact]
    public void CurrentPhase6_HasNoLegacyAuthorityDependenciesInRc2ApiOrchestrator()
    {
        var constructor = typeof(Rc2ContentPlanningBatchOrchestrator).GetConstructors().Single();

        Assert.DoesNotContain(constructor.GetParameters(), parameter =>
            parameter.ParameterType == typeof(CreativeStoryboardBuilder));
        Assert.DoesNotContain("Creative Intelligence", constructor.ToString(), StringComparison.OrdinalIgnoreCase);
    }

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
        var parameter = typeof(ProductionPipelineExecutionService).GetConstructors().Single().GetParameters()
            .Single(x => x.ParameterType == typeof(IPhase6InputAuthorityEvaluator));
        Assert.False(parameter.HasDefaultValue);
        Assert.False(parameter.IsOptional);
        Assert.NotEqual(NullabilityState.Nullable, new NullabilityInfoContext().Create(parameter).ReadState);
    }

    [Fact]
    public async Task EvaluatorFailure_StopsBeforeDownstreamIntegration()
    {
        var calls = new List<string>();
        var evaluator = new Phase6InputAuthorityEvaluator(
            new MissingPhase4(calls), new RecordingPhase5(calls));

        var result = await evaluator.EvaluateAsync(
            new("missing", "execution", "plan", "event", "en", ["Long"]));

        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
        Assert.Equal(["evaluator"], calls);
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
    public async Task TypedFailureReasonCode_IsPreservedInPhaseResultAndValidationArtifact(string reasonCode)
    {
        var result = new ProductionPhaseResult(
            6, "Story Frames", ProductionPhaseStatus.Failed,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0,
            [], [], "phase-06-validation.json", [], ["deterministic error"], true,
            $"{reasonCode}: deterministic error")
        {
            ReasonCode = reasonCode
        };
        var root = Path.Combine(Path.GetTempPath(), $"phase6-routing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var validationPath = Path.Combine(root, "phase-06-validation.json");
        try
        {
            await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(result));
            var committed = JsonSerializer.Deserialize<ProductionPhaseResult>(
                await File.ReadAllTextAsync(validationPath));

            Assert.Equal(reasonCode, result.ReasonCode);
            Assert.NotNull(committed);
            Assert.Equal(reasonCode, committed!.ReasonCode);
            Assert.Equal("phase-06-validation.json", committed.ValidationReportPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
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

    private sealed class MissingPhase4(List<string> calls) : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(
            string executionRoot, string expectedExecutionId, string expectedPlanId,
            string expectedEventId, string expectedLanguage,
            CancellationToken cancellationToken = default)
        {
            calls.Add("evaluator");
            return Task.FromResult(new Phase4CommittedAuthorityEvaluation(
                false, null, "P4REUSE_AUTHORITY_MISSING", [], []));
        }
    }

    private sealed class RecordingPhase5(List<string> calls) : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(
            string executionRoot, string expectedExecutionId, string expectedPlanId,
            string expectedEventId, string expectedLanguage,
            Phase5ExpectedPhase4Authority expectedPhase4,
            CancellationToken cancellationToken = default)
        {
            calls.Add("integration");
            return Task.FromResult(new Phase5CommittedStateEvaluation(
                false, "unexpected", [], [], null));
        }
    }
}
