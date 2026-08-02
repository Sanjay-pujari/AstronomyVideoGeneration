using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelinePhase6RoutingTests
{
    [Fact]
    public void ServiceCollection_RegistersExactlyOnePhase6InputAuthorityEvaluator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=phase6-routing-test.invalid;Port=5432;Database=astronomy_mediafactory_test;Username=test_user;Password=test_password;Pooling=false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddMediaFactory(configuration);
        Assert.Single(services.Where(x => x.ServiceType == typeof(IPhase6InputAuthorityEvaluator)));
    }

    [Fact]
    public async Task RegisteredEvaluator_RejectsMissingPhase4AuthorityBeforePhase5Integration()
    {
        var calls = new List<string>();
        var evaluator = new Phase6InputAuthorityEvaluator(new MissingPhase4(calls), new RecordingPhase5(calls));

        var result = await evaluator.EvaluateAsync(new("missing", "execution", "plan", "event", "en", ["Long"]));

        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
        Assert.Equal(["evaluator"], calls);
    }

    private sealed class MissingPhase4(List<string> calls) : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string a, string b, string c, string d, string e,
            CancellationToken cancellationToken = default)
        {
            calls.Add("evaluator");
            return Task.FromResult(new Phase4CommittedAuthorityEvaluation(false, null, "P4REUSE_AUTHORITY_MISSING", [], []));
        }
    }

    private sealed class RecordingPhase5(List<string> calls) : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(string a, string b, string c, string d, string e,
            Phase5ExpectedPhase4Authority expected, CancellationToken cancellationToken = default)
        {
            calls.Add("integration");
            return Task.FromResult(new Phase5CommittedStateEvaluation(false, "unexpected", [], [], null));
        }
    }
}
