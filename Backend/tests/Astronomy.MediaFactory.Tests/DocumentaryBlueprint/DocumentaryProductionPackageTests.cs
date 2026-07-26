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
        Assert.Equal(Enumerable.Range(0,6),package.Manifest.Entries.Select(x=>x.Sequence));Assert.Same(candidate.NarrativeDraft,package.NarrativeDraft);Assert.Same(candidate.FinalValidationResult,package.FinalValidationResult);Assert.Same(candidate.ConvergenceState,package.ConvergenceState);Assert.Same(candidate.AcceptanceDecision,package.AcceptanceDecision);Assert.Same(candidate.ConvergenceState.Cycles,package.RevisionCycles);
        Assert.Equal(before,JsonSerializer.Serialize(request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var summary=new DocumentaryProductionPackageSummarizer().Summarize(package);Assert.Equal(cycleCount,summary.CompletedCycleCount);Assert.Equal(0,summary.FinalFindingCount);Assert.Equal(0,summary.UnresolvedRevisionItemCount);Assert.True(summary.IsComplete);
    }

    [Fact] public void Complete_package_has_byte_identical_json_reconstruction()
    {
        var original=Assert.IsType<DocumentaryProductionPackage>(Assemble(OrionDocumentaryNarrativeAcceptanceFixture.MultiCycleReleaseCandidate()).Package);
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var json=JsonSerializer.Serialize(original,options);
        var reconstructed=Assert.IsType<DocumentaryProductionPackage>(JsonSerializer.Deserialize<DocumentaryProductionPackage>(json,options));
        Assert.Equal(json,JsonSerializer.Serialize(reconstructed,options));Assert.Equal(original.PackageId,reconstructed.PackageId);
        Assert.Equal(JsonSerializer.Serialize(original.Manifest,options),JsonSerializer.Serialize(reconstructed.Manifest,options));
        Assert.Equal(JsonSerializer.Serialize(original.ReleaseCandidate,options),JsonSerializer.Serialize(reconstructed.ReleaseCandidate,options));
        Assert.Equal(JsonSerializer.Serialize(original.NarrativeDraft,options),JsonSerializer.Serialize(reconstructed.NarrativeDraft,options));
        Assert.Equal(JsonSerializer.Serialize(original.FinalValidationResult,options),JsonSerializer.Serialize(reconstructed.FinalValidationResult,options));
        Assert.Equal(original.RevisionCycles.Select(x=>x.CycleId),reconstructed.RevisionCycles.Select(x=>x.CycleId));
        Assert.Equal(JsonSerializer.Serialize(original.ConvergenceState,options),JsonSerializer.Serialize(reconstructed.ConvergenceState,options));
        Assert.Equal(JsonSerializer.Serialize(original.AcceptanceDecision,options),JsonSerializer.Serialize(reconstructed.AcceptanceDecision,options));
        Assert.Equal(JsonSerializer.Serialize(original.Policy,options),JsonSerializer.Serialize(reconstructed.Policy,options));
        Assert.Equal(JsonSerializer.Serialize(original.Metadata,options),JsonSerializer.Serialize(reconstructed.Metadata,options));
        Assert.Equal(original.IncludedSections,reconstructed.IncludedSections);Assert.True(reconstructed.IsComplete);
    }

    [Theory]
    [InlineData(0,0)] [InlineData(0,1)] [InlineData(0,2)]
    [InlineData(1,0)] [InlineData(1,1)] [InlineData(1,2)]
    [InlineData(2,0)] [InlineData(2,1)] [InlineData(2,2)]
    [InlineData(3,0)] [InlineData(3,1)] [InlineData(3,2)]
    [InlineData(4,0)] [InlineData(4,1)] [InlineData(4,2)]
    public void Package_rejects_each_structurally_valid_manifest_semantic_mismatch(int entryIndex,int field)
    {
        var package=Assert.IsType<DocumentaryProductionPackage>(Assemble(OrionDocumentaryNarrativeAcceptanceFixture.OneCycleReleaseCandidate()).Package);
        var entries=package.Manifest.Entries.ToArray();var old=entries[entryIndex];
        entries[entryIndex]=new(old.Section,field==0?old.ArtifactType+"X":old.ArtifactType,
            field==1?old.ArtifactIdentity+" ":old.ArtifactIdentity,field==2?old.ArtifactVersion.ToUpperInvariant()+"X":old.ArtifactVersion,
            old.Sequence,old.IsRequired);
        var manifest=new DocumentaryProductionPackageManifest(package.Manifest.ManifestId,package.PackageId,entries,"1.0",package.Manifest.CorrelationId);
        Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackage(package.PackageId,package.ReleaseCandidate,package.NarrativeDraft,
            package.FinalValidationResult,package.RevisionCycles,package.ConvergenceState,package.AcceptanceDecision,manifest,package.Policy,package.Metadata,package.IncludedSections));
    }

    [Fact] public void Manifest_entry_rejects_noncanonical_structure_and_manifest_evidence()
    {
        Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.AcceptedNarrative,"type","id","1",1,true));
        Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.AcceptedNarrative,"type","id","1",0,false));
        var package=Assert.IsType<DocumentaryProductionPackage>(Assemble(OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanReleaseCandidate()).Package);
        var entries=package.Manifest.Entries.ToArray();var last=entries[5];entries[5]=new(last.Section,last.ArtifactType+"X",last.ArtifactIdentity,last.ArtifactVersion,last.Sequence,true);
        Assert.Throws<ArgumentException>(()=>new DocumentaryProductionPackageManifest(package.Manifest.ManifestId,package.PackageId,entries,"1.0",package.Manifest.CorrelationId));
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
