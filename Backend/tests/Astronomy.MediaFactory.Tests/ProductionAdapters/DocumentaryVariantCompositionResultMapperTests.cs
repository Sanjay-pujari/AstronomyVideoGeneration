using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryVariantCompositionResultMapperTests
{
 [Fact] public async Task Success_mapping_preserves_certified_artifact_metadata(){await using var f=await DocumentaryVariantCompositionAdapterFixture.CreateAsync();var adapter=await f.ComposeAsync();var mapped=new DocumentaryVariantCompositionResultMapper().Map(f.Request,adapter);mapped.Status.Should().Be(DocumentaryMediaAssetStatus.Generated);mapped.AssetResult.AssetType.Should().Be(DocumentaryMediaAssetType.VariantVideo);mapped.AssetResult.Checksum.Should().Be(adapter.Artifact!.Checksum);mapped.SceneCount.Should().Be(3);mapped.AssetResult.CorrelationId.Should().Be(f.Request.CorrelationId);}
 [Fact] public async Task Failure_mapping_preserves_safe_failure_and_request_context(){await using var f=await DocumentaryVariantCompositionAdapterFixture.CreateAsync(enabled:false);var mapped=new DocumentaryVariantCompositionResultMapper().Map(f.Request,await f.ComposeAsync());mapped.Status.Should().Be(DocumentaryMediaAssetStatus.Failed);mapped.FailureCode.Should().Be(nameof(DocumentaryProductionFailureCode.AdapterUnavailable));mapped.AssetResult.AttemptCount.Should().Be(f.Request.Attempt);}
}
