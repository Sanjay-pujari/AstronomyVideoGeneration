using System.Text.Json;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintDeterminismTests
{
 private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
 [Fact] public void Equivalent_external_inputs_produce_identical_json() { Assert.Equal(JsonSerializer.Serialize(OrionDocumentaryBlueprintFixture.CreateOrdered(),JsonOptions),JsonSerializer.Serialize(OrionDocumentaryBlueprintFixture.CreateOrdered(),JsonOptions)); }
 [Fact] public void Fixture_preserves_external_timestamp_and_ids() { var x=OrionDocumentaryBlueprintFixture.Create(); Assert.Equal(new DateTimeOffset(2026,1,15,12,0,0,TimeSpan.Zero),x.Metadata.CreatedUtc); Assert.Equal("correlation-orion-001",x.Metadata.CorrelationId); }
}
