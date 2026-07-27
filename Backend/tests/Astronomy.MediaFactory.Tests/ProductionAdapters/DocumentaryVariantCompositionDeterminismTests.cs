using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryVariantCompositionDeterminismTests
{
 [Fact] public async Task Same_logical_variant_produces_same_final_path_checksum_and_identity(){await using var f=await DocumentaryVariantCompositionAdapterFixture.CreateAsync();var first=await f.ComposeAsync();var second=await f.ComposeAsync();second.Artifact!.PhysicalPath.Should().Be(first.Artifact!.PhysicalPath);second.Artifact.Checksum.Should().Be(first.Artifact.Checksum);second.Artifact.ContentIdentity.Should().Be(first.Artifact.ContentIdentity);}
}
