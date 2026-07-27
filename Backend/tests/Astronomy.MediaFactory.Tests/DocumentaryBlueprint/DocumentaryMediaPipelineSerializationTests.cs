using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

/// <summary>Direct Web JSON evidence for every public contract introduced by O2.18.</summary>
public sealed class DocumentaryMediaPipelineSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [MemberData(nameof(PublicContracts))]
    public void Every_public_O218_contract_round_trips_directly_and_byte_identically(object value, Type declaredType)
    {
        var json = JsonSerializer.Serialize(value, declaredType, Web);
        var copy = JsonSerializer.Deserialize(json, declaredType, Web);

        Assert.NotNull(copy);
        Assert.Equal(declaredType, copy.GetType());
        Assert.Equal(json, JsonSerializer.Serialize(copy, declaredType, Web));

        // Aggregate contracts must remain acceptable to their production validator after deserialization.
        switch (copy)
        {
            case DocumentaryMediaPipelineRequest request: DocumentaryMediaPipelineValidator.ValidateRequest(request); break;
            case DocumentaryMediaPipelineExecutionPlan plan: DocumentaryMediaPipelineValidator.ValidateExecutionPlan(plan); break;
            case DocumentaryMediaPipelineExecutionRecord record: DocumentaryMediaPipelineValidator.ValidateExecutionRecord(record); break;
            case DocumentaryMediaPipelineResult { ExecutionRecord: not null } result:
                DocumentaryMediaPipelineValidator.ValidateExecutionRecord(result.ExecutionRecord); break;
            case DocumentaryMediaOutputManifest manifest:
                Assert.Equal(manifest.AssetCount, manifest.Assets.Count); break;
        }
    }

    public static IEnumerable<object[]> PublicContracts()
    {
        var project = DocumentaryMediaPipelineFixture.Orion(); // retained, complete O2.17 graph (Hindi and English included)
        var request = DocumentaryMediaPipelineFixture.Request(project);
        var providers = new DocumentaryMediaPipelineFakeProviders();
        var result = DocumentaryMediaPipelineFixture.Run(project, providers: providers);
        var record = result.ExecutionRecord!;
        var plan = record.ExecutionPlan;
        var assetPlan = plan.AssetPlans[0];
        var generated = record.OutputManifest.Assets.First(x => x.Status is DocumentaryMediaAssetStatus.Generated or DocumentaryMediaAssetStatus.Verified);
        var failed = generated with { Status=DocumentaryMediaAssetStatus.Failed, ProviderId=null, ContentIdentity=null, Checksum=null,
            FailureCode="fixture-failure", FailureMessage="deterministic fixture failure" };

        yield return Case(new DocumentaryMediaChecksumProfile());
        yield return Case(request.Policy);
        yield return Case(request.Metadata); // whitespace creator, non-UTC offset, and seven fractional digits
        yield return Case(request);
        yield return Case(new DocumentaryMediaProviderDescriptor("fixture.provider", "Fixture Provider",
            Enum.GetValues<DocumentaryMediaProviderCapability>(), Enum.GetValues<DocumentaryMediaAssetFormat>(), true, "1.0"));
        yield return Case(plan.AssetDependencies[0]);
        yield return Case(assetPlan with { SceneId=null, Dependencies=Array.Empty<DocumentaryMediaAssetDependency>() });
        yield return Case(failed); // nullable provider/content/checksum plus failure code/message
        yield return Case(providers.VisualRequests[0]);
        yield return Case(new DocumentaryVisualGenerationResult(DocumentaryMediaAssetStatus.Failed, failed, failed.FailureCode, failed.FailureMessage));
        yield return Case(providers.NarrationRequests[0]);
        yield return Case(new DocumentaryNarrationSynthesisResult(DocumentaryMediaAssetStatus.Generated, generated, generated.DurationMilliseconds, null, null));
        yield return Case(providers.SubtitleRequests[0]);
        yield return Case(new DocumentarySubtitleGenerationResult(DocumentaryMediaAssetStatus.Failed, failed, 0, failed.FailureCode, failed.FailureMessage));
        yield return Case(providers.SceneRequests[0]);
        yield return Case(new DocumentarySceneCompositionResult(DocumentaryMediaAssetStatus.Generated, generated, generated.DurationMilliseconds, null, null));
        yield return Case(providers.VariantRequests[0]);
        yield return Case(new DocumentaryVariantCompositionResult(DocumentaryMediaAssetStatus.Generated, generated, project.Variants[0].SceneCount, generated.DurationMilliseconds, null, null));
        yield return Case(providers.VerificationRequests[0]);
        yield return Case(new DocumentaryRenderVerificationResult(false, 0, 0, 0, 0, 0, 0, 0, false, false, false, false, ["deterministic failure"]));
        yield return Case(plan.VariantPlans[0]);
        yield return Case(plan); // nonempty, large dependency graph
        yield return Case(record.VariantRecords[0]);
        yield return Case(record.OutputManifest);
        yield return Case(record);
        yield return Case(result);
        yield return Case(DocumentaryMediaPipelineFixture.Summary(record));
    }

    [Fact]
    public void Contract_inventory_contains_exactly_the_27_public_O218_types() =>
        Assert.Equal(27, PublicContracts().Select(x => (Type)x[1]).Distinct().Count());

    [Fact]
    public void Independently_reconstructed_request_is_byte_identical()
    {
        var project = DocumentaryMediaPipelineFixture.Orion();
        Assert.Equal(JsonSerializer.Serialize(DocumentaryMediaPipelineFixture.Request(project), Web),
            JsonSerializer.Serialize(DocumentaryMediaPipelineFixture.EquivalentRequest(project), Web));
    }

    private static object[] Case<T>(T value) where T : notnull => [value, typeof(T)];
}
