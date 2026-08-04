using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7CultureRequiredEvidenceDiagnosticTests
{
    [Fact]
    public void DiagnosticsOption_IsDisabledByDefault()
        => Assert.False(new Phase7KnowledgeDiagnosticsOptions().EnableCultureEvidenceDebug);

    [Fact]
    public void DiagnosticDirectory_IsCreated()
    {
        var (root,path)=NewPath();
        Resolve(path);
        Assert.True(Directory.Exists(Path.Combine(root,"07-narration","debug")));
    }

    [Fact]
    public void DiagnosticFile_IsWrittenOnSuccessfulResolution()
    {
        var (_,path)=NewPath();
        Resolve(path);

        using var document=JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("p7-culture-debug.v1",document.RootElement.GetProperty("contractVersion").GetString());
        var candidate=Assert.Single(document.RootElement.GetProperty("cultureCandidates").EnumerateArray());
        Assert.Equal("cultureAndMythology.greek.summary",candidate.GetProperty("approvedFieldPath").GetString());
        Assert.Contains("cultureAndMythology parent: true",candidate.GetProperty("culturalDispositionResult").GetString());
        Assert.True(Assert.Single(document.RootElement.GetProperty("finalCultureClaims").EnumerateArray()).GetProperty("acceptedAsRequiredAuthority").GetBoolean());
    }

    [Fact]
    public void DiagnosticFile_IsWrittenWhenKnowledgeHasBlockingIssues()
    {
        var (_,path)=NewPath();
        var result=Resolve(path,certified:false);

        Assert.NotEmpty(result.BlockingIssues);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length>0);
    }

    [Fact]
    public void DiagnosticFailure_DoesNotChangeResolutionChecksum()
    {
        var (_,failingPath)=NewFailingPath();
        var ordinary=Resolve(null);
        var diagnosed=Resolve(failingPath);
        Assert.Equal(ordinary.DeterministicChecksum,diagnosed.DeterministicChecksum);
    }

    [Fact]
    public void DiagnosticFailure_DoesNotChangeBlockingIssues()
    {
        var (_,failingPath)=NewFailingPath();
        var ordinary=Resolve(null,certified:false);
        var diagnosed=Resolve(failingPath,certified:false);
        Assert.Equal(ordinary.BlockingIssues,diagnosed.BlockingIssues);
    }

    [Fact]
    public void DiagnosticFailure_ProducesSafeWarning()
    {
        var (root,failingPath)=NewFailingPath();
        var result=Resolve(failingPath);
        var warning=Assert.Single(result.Warnings.Where(x=>x.StartsWith("P7CULTURE_DEBUG_WRITE_FAILED:",StringComparison.Ordinal)));
        Assert.Equal("P7CULTURE_DEBUG_WRITE_FAILED:DirectoryNotFoundException",warning);
        Assert.DoesNotContain(root,warning,StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticArtifact_IsNotPartOfKnowledgeAuthorityInventory()
    {
        var (root,path)=NewPath();
        Resolve(path);

        var authorityDirectory=Path.Combine(root,"07-narration","knowledge");
        var authorityInventory=Directory.Exists(authorityDirectory)
            ? Directory.EnumerateFiles(authorityDirectory,"*",SearchOption.AllDirectories).Select(Path.GetFullPath).ToArray()
            : [];
        Assert.True(File.Exists(path));
        Assert.DoesNotContain(Path.GetFullPath(path),authorityInventory);
    }

    private static ResolvedNarrationKnowledge Resolve(string? path,bool certified=true)
    {
        var json="""{"cultureAndMythology":{"sourceIds":["source-greek"],"greek":{"summary":"Orion is the hunter in Greek tradition."}}}""";
        var source=new CertifiedNarrationSource("source-greek","Registry","Greek source","Registry","ref",true,true,["orion"],[],["CultureAndMythology"],"en",.95m,"")
        { SupportedApprovedFieldPaths=["cultureAndMythology.greek.summary"],ReviewState="Reviewed",AuthorityState="Authoritative" };
        var payload=new CertifiedKnowledgePayload("payload","orion","CONSTELLATION","CONSTELLATION","en","{}",null,json,"registry",[source.SourceId],certified?"Certified":"Rejected")
        { CertificationStatus=certified?"Certified":"Rejected",EvergreenPayloadId="evergreen",AllResolvedSources=[source],CertifiedSupportingSources=[source] };
        var profile=new FamilyNarrationProfileResolver().Resolve("CONSTELLATION","en").Profile!;
        return new Phase7KnowledgeResolver().Resolve(payload,profile,path);
    }

    private static (string Root,string Path) NewPath()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        return (root,Path.Combine(root,"07-narration","debug","culture-required-evidence-debug.json"));
    }

    private static (string Root,string Path) NewFailingPath()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var blocker=Path.Combine(root,"07-narration");
        File.WriteAllText(blocker,"not a directory");
        return (root,Path.Combine(blocker,"debug","culture-required-evidence-debug.json"));
    }
}
