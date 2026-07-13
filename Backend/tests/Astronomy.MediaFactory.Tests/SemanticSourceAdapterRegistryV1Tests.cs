using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Event;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public class SemanticSourceAdapterRegistryV1Tests
{
    private readonly SemanticSourceAdapterRegistryV1 _registry = new();
    [Fact] public void Every_Adapter_Supports_One_Canonical_Capability_And_Unique_Id(){Assert.Equal(_registry.Adapters.Count,_registry.Adapters.Select(a=>a.AdapterId).Distinct(StringComparer.Ordinal).Count());foreach(var a in _registry.Adapters) Assert.Contains(a.SupportedCapabilityId.Value, SemanticCapabilityVocabularyV1.CanonicalIds);}
    [Fact] public void Adapter_Sources_And_Evidence_Are_Approved_By_Sprint3A_Policy(){var policies=new SemanticSourcePolicyCatalogV1();foreach(var a in _registry.Adapters){var p=policies.GetRequired(a.SupportedCapabilityId);var s=Assert.Single(p.ApprovedSources.Where(s=>s.SourceId==a.SourceId));Assert.Equal(s.EvidenceCategory,a.EvidenceCategory);Assert.True(a.MaximumEvidenceStrength>=s.MinimumStrength);}}
    [Fact] public void No_Active_Adapter_Uses_LegacyRawJsonScanner_Or_Recursive_Json(){Assert.DoesNotContain(_registry.Adapters,a=>a.SourceId==SemanticSourcePolicyVocabularyV1.LegacyRawJsonScanner);var files=Directory.GetFiles("../../../../src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Sources/Adapters","*.cs",SearchOption.AllDirectories);foreach(var f in files){var text=File.ReadAllText(f);Assert.DoesNotContain("JsonElement",text);Assert.DoesNotContain("dynamic",text);Assert.DoesNotContain("GetProperties()",text);}}
    [Fact] public void Coverage_Is_Deterministic_And_Reports_Missing_Adapters(){var a=_registry.GetCoverageReport().ToArray();var b=_registry.GetCoverageReport().ToArray();Assert.Equal(a,b);Assert.Contains(a,i=>i.Status==SemanticSourceAdapterCertificationStatusV1.AdapterMissing);}
    [Fact] public void Contracts_Serialize_And_RoundTrip(){var r=new EventWindowSourceAdapterV1().TryExtract(new(ObservationMetadata:new(EventWindow:new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"),DateTimeOffset.Parse("2026-01-01T01:00:00Z"),DateTimeOffset.Parse("2026-01-01T02:00:00Z"),null,null,null,null,"UTC","window"))));var json=JsonSerializer.Serialize(r);var clone=JsonSerializer.Deserialize<SemanticSourceAdapterResultV1>(json);Assert.Equal(r.Status, clone!.Status);}
}
