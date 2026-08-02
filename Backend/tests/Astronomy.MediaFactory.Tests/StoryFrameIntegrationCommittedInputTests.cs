using System.Runtime.Serialization;
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

    [Fact]
    public void ArtifactPaths_AreDistinctAndOrdinallySorted()
    {
        var authority = CreateAuthority(["Long"],
            [
                "05-editorial/editorial-contract.json",
                "05-editorial/blueprint-certification.json",
                "05-editorial/editorial-contract.json"
            ]);
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(authority);
        Assert.Equal(paths.Distinct(StringComparer.Ordinal), paths);
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
    }

    private sealed class Phase4(DocumentaryBlueprintAggregate aggregate) : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string a, string b, string c, string d, string e,
            CancellationToken cancellationToken = default) => Task.FromResult(new Phase4CommittedAuthorityEvaluation(true,
            aggregate, "P4REUSE_VALID", [], ["04-blueprint/documentary-blueprint-aggregate.json",
                "validation/phase-04-validation.json", "phase-manifest.json"])
            { CommittedValidationEvidence = ["validation/phase-04-validation.json"], ManifestEvidence = ["phase-manifest.json"] });
    }

    {
        {
            {
        }

    private static Phase6CommittedInputAuthority CreateAuthority(
        IReadOnlyList<string> requestedVariants,
        IReadOnlyList<string>? phase5Paths = null)
    {
#pragma warning disable SYSLIB0050
        var aggregate = (DocumentaryBlueprintAggregate)
            FormatterServices.GetUninitializedObject(typeof(DocumentaryBlueprintAggregate));
        var phase5 = (PublishedBlueprintCertification)
            FormatterServices.GetUninitializedObject(typeof(PublishedBlueprintCertification));
#pragma warning restore SYSLIB0050

        var entries = (phase5Paths ??
            ["05-editorial/blueprint-certification.json", "05-editorial/editorial-contract.json"])
            .Select(Entry).ToArray();

        return new Phase6CommittedInputAuthority(
            aggregate, "aggregate-id", Sha('a'), Sha('b'), Sha('c'), "profile", "1.0",
            ["validation/phase-04-validation.json"], ["phase-manifest.json"],
            phase5, "certification-id", Sha('d'), "editorial-contract-id", Sha('e'),
            "phase5-publication-id", ["validation/phase-05-validation.json"], entries,
            true, ["Long", "Short"], requestedVariants, true, true, true, true, true,
            true, true, [], []);
    }

    private static Phase5ArtifactInventoryEntry Entry(string path) =>
        new(path, "Supporting", Sha('f'), Sha('0'), 1, Sha('a'));

    private static string Sha(char value) => new(value, 64);
}
