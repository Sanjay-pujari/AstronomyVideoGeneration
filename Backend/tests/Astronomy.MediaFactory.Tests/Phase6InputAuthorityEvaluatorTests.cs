using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase6InputAuthorityEvaluatorTests
{
    private static readonly Phase6InputAuthorityRequest Request = new("root", "execution", "plan", "event", "en", ["Long"]);

    [Fact]
    public async Task EvaluateAsync_Phase4EvaluatorThrows_ReturnsPhase4Invalid()
    {
        var evaluator = new Phase6InputAuthorityEvaluator(new ThrowingPhase4(), new UnusedPhase5());
        var result = await evaluator.EvaluateAsync(Request);
        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_CancellationBeforePhase4_ThrowsOperationCanceledException()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var evaluator = new Phase6InputAuthorityEvaluator(new ThrowingPhase4(), new UnusedPhase5());
        await Assert.ThrowsAsync<OperationCanceledException>(() => evaluator.EvaluateAsync(Request, source.Token));
    }

    [Fact]
    public async Task EvaluateAsync_ValidCommittedAuthorities_CallsPhase4ThenPhase5AndReturnsTypedAuthority()
    {
        var calls = new List<string>();
        var fixture = Phase5CertificationFixture.Create();
        var phase4 = new RecordingPhase4(fixture.PublishedPhase4, calls);
        var phase5 = new RecordingPhase5(Published(fixture), calls);
        var evaluator = new Phase6InputAuthorityEvaluator(phase4, phase5);

        var result = await evaluator.EvaluateAsync(Request with
        {
            ExecutionId = fixture.Request.ExecutionId,
            PlanId = fixture.Request.PlanId,
            EventId = fixture.Request.EventId,
            Language = fixture.Request.Language
        });

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.NotNull(result.Authority);
        Assert.Same(fixture.PublishedPhase4, result.Authority.Phase4Aggregate);
        Assert.Equal(["phase4", "phase5"], calls);
        Assert.Equal(["Long"], result.Authority.RequestedVariants);
    }

    [Fact]
    public async Task EvaluateAsync_Phase4Failure_DoesNotCallPhase5()
    {
        var calls = new List<string>();
        var fixture = Phase5CertificationFixture.Create();
        var phase4 = new RecordingPhase4(fixture.PublishedPhase4, calls, valid: false);
        var phase5 = new RecordingPhase5(Published(fixture), calls);

        var result = await new Phase6InputAuthorityEvaluator(phase4, phase5).EvaluateAsync(Request);

        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
        Assert.Equal(["phase4"], calls);
    }

    [Fact]
    public void Phase6InputAuthorityException_PreservesReasonCodeAndDeterministicErrors()
    {
        var exception = new Phase6InputAuthorityException("P6INPUT_PHASE5_INVALID", ["first", "second"]);

        Assert.Equal("P6INPUT_PHASE5_INVALID", exception.ReasonCode);
        Assert.Equal(["first", "second"], exception.Errors);
        Assert.Equal("P6INPUT_PHASE5_INVALID: first; second", exception.Message);
    }

    private sealed class ThrowingPhase4 : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
            string expectedPlanId, string expectedEventId, string expectedLanguage, CancellationToken cancellationToken = default) =>
            throw new IOException("expected read failure");
    }

    private sealed class UnusedPhase5 : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
            string expectedPlanId, string expectedEventId, string expectedLanguage, Phase5ExpectedPhase4Authority expectedPhase4,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("must not be called");
    }

    private sealed class RecordingPhase4(DocumentaryBlueprintAggregate aggregate, List<string> calls, bool valid = true)
        : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
            string expectedPlanId, string expectedEventId, string expectedLanguage, CancellationToken cancellationToken = default)
        {
            calls.Add("phase4");
            return Task.FromResult(new Phase4CommittedAuthorityEvaluation(valid, valid ? aggregate : null,
                valid ? "P4REUSE_VALID" : "P4REUSE_INVALID", [],
                ["04-blueprint/documentary-blueprint-aggregate.json", "validation/phase-04-validation.json", "phase-manifest.json"])
            {
                CommittedValidationEvidence = ["validation/phase-04-validation.json"],
                ManifestEvidence = ["phase-manifest.json"]
            });
        }
    }

    private sealed class RecordingPhase5(PublishedBlueprintCertification authority, List<string> calls)
        : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
            string expectedPlanId, string expectedEventId, string expectedLanguage, Phase5ExpectedPhase4Authority expectedPhase4,
            CancellationToken cancellationToken = default)
        {
            calls.Add("phase5");
            var artifact = new Phase5ArtifactInventoryEntry("05-editorial/blueprint-certification.json", "certification",
                authority.Certification.SemanticChecksum, "physical", 1, expectedPhase4.AggregateChecksum);
            return Task.FromResult(new Phase5CommittedStateEvaluation(true, "P5REUSE_VALID", [], [artifact], authority)
            {
                PublicationTransactionId = "publication",
                PublicationCommitted = true,
                CommittedStateValidationPassed = true,
                CommittedValidationEvidence = ["validation/phase-05-validation.json"],
                ManifestEvidence = ["phase-manifest.json"]
            });
        }
    }

    private static PublishedBlueprintCertification Published(Phase5CertificationFixtureResult fixture) => new(
        fixture.Result.Certification, fixture.Result.EditorialContract, fixture.Result.Validation,
        fixture.Result.SceneIntents, fixture.Result.Coverage, fixture.Result.Transitions, fixture.Result.PauseTest,
        fixture.PublishedPhase4.AggregateId, fixture.PublishedPhase4.DeterministicChecksum, "1.0", "published");
}
