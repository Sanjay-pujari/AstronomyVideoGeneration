using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryProductionPackageTests
{
    private static DocumentaryProductionPackagePolicy Policy() => new(true,true,true,true,true,true,true,Enum.GetValues<DocumentaryProductionPackageSection>(),"1.0");
    private static DocumentaryProductionPackageMetadata Metadata(string? correlation=null) => new(DateTimeOffset.Parse("2026-07-26T11:12:13.1234567+05:30")," package certifier ","1.0",correlation??OrionDocumentaryNarrativeAcceptanceFixture.Correlation);
    private static DocumentaryProductionPackageAssemblyResult Assemble(DocumentaryNarrativeReleaseCandidate candidate) => new DocumentaryProductionPackageAssembler().Assemble(new(candidate,Policy(),Metadata()));

    [Fact] public void Inventories_are_exact()
    {
        Assert.Equal(["Complete","Rejected"],Enum.GetNames<DocumentaryProductionPackageStatus>());
        Assert.Equal(["ReleaseCandidateNotAccepted","ReleaseCandidateNotClean","ReleaseCandidateNotFullyResolved","ReleaseCandidateIdentityMismatch","NarrativeDraftLineageMismatch","ValidationLineageMismatch","ConvergenceLineageMismatch","AcceptanceLineageMismatch","CorrelationMismatch","RequiredSectionMissing","RequiredEvidenceMissing","PolicyRejected"],Enum.GetNames<DocumentaryProductionPackageRejectionReason>());
        Assert.Equal(["AcceptedNarrative","FinalValidationEvidence","RevisionHistory","ConvergenceEvidence","AcceptanceEvidence","PackageManifest"],Enum.GetNames<DocumentaryProductionPackageSection>());
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Assembler_certifies_zero_one_and_multi_cycle_packages(int cycleCount)
    {
        var candidate=cycleCount switch {0=>OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanReleaseCandidate(),1=>OrionDocumentaryNarrativeAcceptanceFixture.OneCycleReleaseCandidate(),_=>OrionDocumentaryNarrativeAcceptanceFixture.MultiCycleReleaseCandidate()};
        var request=new DocumentaryProductionPackageRequest(candidate,Policy(),Metadata());
        var before=JsonSerializer.Serialize(request,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var result=new DocumentaryProductionPackageAssembler().Assemble(request);var package=Assert.IsType<DocumentaryProductionPackage>(result.Package);
        Assert.True(result.IsComplete);Assert.True(result.HasPackage);Assert.Empty(result.RejectionReasons);Assert.Equal(cycleCount,package.CompletedCycleCount);
        Assert.Equal($"{candidate.ReleaseCandidateId}.production-package",package.PackageId);Assert.Equal($"{package.PackageId}.manifest",package.Manifest.ManifestId);
        Assert.Equal(Enumerable.Range(0,6),package.Manifest.Entries.Select(x=>x.Sequence));Assert.Same(candidate.NarrativeDraft,package.NarrativeDraft);Assert.Same(candidate.FinalValidationResult,package.FinalValidationResult);Assert.Same(candidate.ConvergenceState.Cycles,package.RevisionCycles);
        Assert.Equal(before,JsonSerializer.Serialize(request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var summary=new DocumentaryProductionPackageSummarizer().Summarize(package);Assert.Equal(cycleCount,summary.CompletedCycleCount);Assert.Equal(0,summary.FinalFindingCount);Assert.Equal(0,summary.UnresolvedRevisionItemCount);Assert.True(summary.IsComplete);
    }

    [Fact] public void Correlation_mismatch_is_rejected_without_package()
    {
        var candidate=OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanReleaseCandidate();
        var result=new DocumentaryProductionPackageAssembler().Assemble(new(candidate,Policy(),Metadata(candidate.Metadata.CorrelationId.ToUpperInvariant())));
        Assert.Equal(DocumentaryProductionPackageStatus.Rejected,result.Status);Assert.Equal([DocumentaryProductionPackageRejectionReason.CorrelationMismatch],result.RejectionReasons);Assert.Null(result.Package);Assert.False(result.HasPackage);
    }

    [Fact] public void Contracts_enforce_strict_boundaries_and_defensive_collections()
    {
        var sections=Enum.GetValues<DocumentaryProductionPackageSection>();var policy=Policy();sections[0]=DocumentaryProductionPackageSection.PackageManifest;Assert.Equal(DocumentaryProductionPackageSection.AcceptedNarrative,policy.RequiredSections[0]);
        Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackagePolicy(false,true,true,true,true,true,true,Enum.GetValues<DocumentaryProductionPackageSection>(),"1.0"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackagePolicy(true,true,true,true,true,true,true,Enum.GetValues<DocumentaryProductionPackageSection>().Reverse().ToArray(),"1.0"));
        Assert.Throws<ArgumentException>(()=>Metadata(" "));Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackageMetadata(default,"x","1.0","c"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.AcceptedNarrative,"type","id","1",1,true));
        Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.AcceptedNarrative,"type","id","1",0,false));
        Assert.Throws<ArgumentNullException>(()=>new DocumentaryProductionPackageAssembler().Assemble(null!));Assert.Throws<ArgumentNullException>(()=>new DocumentaryProductionPackageSummarizer().Summarize(null!));
    }

    [Fact] public void Operations_are_sealed_parameterless_stateless_and_synchronous()
    {
        foreach(var pair in new[]{(typeof(DocumentaryProductionPackageAssembler),"Assemble"),(typeof(DocumentaryProductionPackageSummarizer),"Summarize")})
        {Assert.True(pair.Item1.IsSealed);Assert.Empty(pair.Item1.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic));Assert.Single(pair.Item1.GetConstructors());var method=Assert.Single(pair.Item1.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly));Assert.Equal(pair.Item2,method.Name);Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));}
    }
}
