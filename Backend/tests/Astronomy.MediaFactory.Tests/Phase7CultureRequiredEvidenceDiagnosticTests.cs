using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7CultureRequiredEvidenceDiagnosticTests
{
    [Fact]
    public void Resolve_WritesTemporaryCultureDiagnosticWithoutChangingResolution()
    {
        var json="""{"cultureAndMythology":{"sourceIds":["source-greek"],"greek":{"summary":"Orion is the hunter in Greek tradition."}}}""";
        var source=new CertifiedNarrationSource("source-greek","Registry","Greek source","Registry","ref",true,true,["orion"],[],["CultureAndMythology"],"en",.95m,"")
        { SupportedApprovedFieldPaths=["cultureAndMythology.greek.summary"],ReviewState="Reviewed",AuthorityState="Authoritative" };
        var payload=new CertifiedKnowledgePayload("payload","orion","CONSTELLATION","CONSTELLATION","en","{}",null,json,"registry",[source.SourceId],"Certified")
        { CertificationStatus="Certified",EvergreenPayloadId="evergreen",AllResolvedSources=[source],CertifiedSupportingSources=[source] };
        var profile=new FamilyNarrationProfileResolver().Resolve("CONSTELLATION","en").Profile!;
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"07-narration","debug","culture-required-evidence-debug.json");

        var resolver=new Phase7KnowledgeResolver();
        var ordinary=resolver.Resolve(payload,profile);
        var diagnosed=resolver.Resolve(payload,profile,path);

        Assert.Equal(ordinary.DeterministicChecksum,diagnosed.DeterministicChecksum);
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("p7-culture-debug.v1",document.RootElement.GetProperty("contractVersion").GetString());
        var candidate=Assert.Single(document.RootElement.GetProperty("cultureCandidates").EnumerateArray());
        Assert.Equal("cultureAndMythology.greek.summary",candidate.GetProperty("approvedFieldPath").GetString());
        Assert.Contains("cultureAndMythology parent: true",candidate.GetProperty("culturalDispositionResult").GetString());
        Assert.True(Assert.Single(document.RootElement.GetProperty("finalCultureClaims").EnumerateArray()).GetProperty("acceptedAsRequiredAuthority").GetBoolean());
    }
}
