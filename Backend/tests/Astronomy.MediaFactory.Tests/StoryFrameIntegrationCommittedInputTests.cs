using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class StoryFrameIntegrationCommittedInputTests
{
    [Fact]
    public async Task BuildAsync_ReportsOnlyDeterministicCommittedInputArtifactPaths()
    {
        var fixture = Phase5CertificationFixture.Create();
        var input = await CreateInputAsync(fixture);
        var builder = new RecordingBuilder();
        var service = new StoryFrameIntegrationService(builder);
        var compatibility = service.GetCompatibilityContext();
        var request = new StoryFrameIntegrationRequest(fixture.Request.ExecutionId, fixture.Request.PlanId,
            fixture.Request.EventId, fixture.Request.Language, fixture.Request.Profile, input,
            compatibility.CurrentBuilderType, compatibility.CurrentBuilderVersion,
            compatibility.CurrentIntegrationServiceType, compatibility.CurrentIntegrationServiceVersion);

        var result = await service.BuildAsync(request, CancellationToken.None);

        Assert.Equal(1, builder.CallCount);
        Assert.Same(input.Phase5Authority.EditorialContract, builder.EditorialContract);
        Assert.Equal(input.RequestedVariants, builder.RequestedVariants);
        Assert.Equal(StoryFrameCommittedInputDiagnostics.ArtifactPaths(input), result.Diagnostics.InputArtifactPaths);
    }

    private static async Task<Phase6CommittedInputAuthority> CreateInputAsync(Phase5CertificationFixtureResult fixture)
    {
        var phase4 = new Phase4(fixture.PublishedPhase4);
        var phase5 = new Phase5(new PublishedBlueprintCertification(fixture.Result.Certification,
            fixture.Result.EditorialContract, fixture.Result.Validation, fixture.Result.SceneIntents,
            fixture.Result.Coverage, fixture.Result.Transitions, fixture.Result.PauseTest,
            fixture.PublishedPhase4.AggregateId, fixture.PublishedPhase4.DeterministicChecksum, "1.0", "published"));
        var result = await new Phase6InputAuthorityEvaluator(phase4, phase5).EvaluateAsync(new("root",
            fixture.Request.ExecutionId, fixture.Request.PlanId, fixture.Request.EventId, fixture.Request.Language, ["Long"]));
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        return Assert.IsType<Phase6CommittedInputAuthority>(result.Authority);
    }

    private sealed class RecordingBuilder : ICertifiedStoryFrameBuilder
    {
        public string BuilderType => "recording-builder";
        public string BuilderVersion => "1";
        public int CallCount { get; private set; }
        public DocumentaryBlueprintEditorialContract? EditorialContract { get; private set; }
        public IReadOnlyList<string>? RequestedVariants { get; private set; }
        public Task<IReadOnlyList<StoryFrameAuthorityFrame>> BuildAsync(DocumentaryBlueprintEditorialContract editorialContract,
            IReadOnlyList<string> requestedVariants, CancellationToken cancellationToken)
        {
            CallCount++;
            EditorialContract = editorialContract;
            RequestedVariants = requestedVariants;
            return Task.FromResult<IReadOnlyList<StoryFrameAuthorityFrame>>([]);
        }
    }

    private sealed class Phase4(DocumentaryBlueprintAggregate aggregate) : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string a, string b, string c, string d, string e,
            CancellationToken cancellationToken = default) => Task.FromResult(new Phase4CommittedAuthorityEvaluation(true,
            aggregate, "P4REUSE_VALID", [], ["04-blueprint/documentary-blueprint-aggregate.json",
                "validation/phase-04-validation.json", "phase-manifest.json"])
            { CommittedValidationEvidence = ["validation/phase-04-validation.json"], ManifestEvidence = ["phase-manifest.json"] });
    }

    private sealed class Phase5(PublishedBlueprintCertification authority) : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(string a, string b, string c, string d, string e,
            Phase5ExpectedPhase4Authority expected, CancellationToken cancellationToken = default)
        {
            var artifact = new Phase5ArtifactInventoryEntry("05-editorial/blueprint-certification.json", "certification",
                authority.Certification.SemanticChecksum, "physical", 1, expected.AggregateChecksum);
            return Task.FromResult(new Phase5CommittedStateEvaluation(true, "P5REUSE_VALID", [], [artifact], authority)
            {
                PublicationTransactionId = "publication", PublicationCommitted = true, CommittedStateValidationPassed = true,
                CommittedValidationEvidence = ["validation/phase-05-validation.json"], ManifestEvidence = ["phase-manifest.json"]
            });
        }
    }
}
