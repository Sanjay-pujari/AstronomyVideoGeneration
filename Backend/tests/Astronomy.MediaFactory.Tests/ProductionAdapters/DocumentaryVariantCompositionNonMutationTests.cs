using System.Text.Json;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryVariantCompositionNonMutationTests
{
 [Fact] public async Task Resolver_and_adapter_leave_certified_sources_unchanged(){await using var f=await DocumentaryVariantCompositionAdapterFixture.CreateAsync();var before=JsonSerializer.Serialize(await f.Registry.GetAllAsync(DocumentaryVariantCompositionAdapterFixture.Correlation,default));(await f.ComposeAsync()).Succeeded.Should().BeTrue();var sources=(await f.Registry.GetAllAsync(DocumentaryVariantCompositionAdapterFixture.Correlation,default)).Where(x=>x.AssetId.StartsWith("scene-"));JsonSerializer.Serialize(sources).Should().Be(before);}
}
