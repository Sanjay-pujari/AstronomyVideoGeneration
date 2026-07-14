using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
namespace Astronomy.MediaFactory.Tests;
public sealed class SemanticSourcePolicyContractsV1Tests
{
    [Fact] public void Contracts_Serialize_And_RoundTrip(){var policy=new SemanticSourcePolicyCatalogV1().Policies.First(); var json=JsonSerializer.Serialize(policy); var round=JsonSerializer.Deserialize<SemanticSourcePolicyV1>(json); Assert.Equal(policy,round);}
    [Fact] public void Collections_Are_Immutable_And_Structurally_Equal(){var c1=new SemanticSourcePolicyCatalogV1(); var c2=new SemanticSourcePolicyCatalogV1(); Assert.IsAssignableFrom<IReadOnlyCollection<SemanticSourcePolicyV1>>(c1.Policies); Assert.False(c1.Policies is ICollection<SemanticSourcePolicyV1>{IsReadOnly:false}); Assert.Equal(c1.Policies.ToArray(),c2.Policies.ToArray());}
    [Fact] public void Runtime_Orchestration_Does_Not_Duplicate_Source_Policy_Logic(){var root=RepositoryTestPaths.Root(); var files=new[]{"Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs","Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"}; var forbidden=new[]{"ISemanticSourcePolicyCatalogV1","SemanticSourcePolicyCatalogV1","SemanticSourcePolicyCertifierV1","SemanticSourcePolicyV1"}; foreach(var f in files.Where(f=>File.Exists(Path.Combine(root,f)))){var text=File.ReadAllText(Path.Combine(root,f)); foreach(var token in forbidden) Assert.DoesNotContain(token,text);} var services=File.ReadAllText(Path.Combine(root,"Backend/src/Astronomy.MediaFactory.Infrastructure/Extensions/ServiceCollectionExtensions.cs")); Assert.Contains("ISemanticSourcePolicyCatalogV1",services); Assert.Contains("SemanticSourcePolicyCatalogV1",services);}
}
