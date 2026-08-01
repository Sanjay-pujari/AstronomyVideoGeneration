using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase5CommittedAuthorityEvaluatorTests
{
    [Fact] public async Task EvaluateAsync_ValidCommittedAuthority_ReturnsPublishedAuthority() { using var f=await Valid(); var r=await f.EvaluateAsync(); Assert.True(r.IsValid); Assert.NotNull(r.PublishedAuthority); Assert.Equal("P5REUSE_VALID",r.ReasonCode); }
    [Fact] public async Task EvaluateAsync_MissingCanonicalCertification_ReturnsAuthorityMissing() { using var f=await Valid(); File.Delete(f.Editorial("blueprint-certification.json")); await Reason(f,"P5REUSE_AUTHORITY_MISSING"); }
    [Fact] public async Task EvaluateAsync_MissingSupportingArtifact_ReturnsArtifactMissing() { using var f=await Valid(); File.Delete(f.Editorial("coverage-report.json")); await Reason(f,"P5REUSE_ARTIFACT_MISSING"); }
    [Fact] public async Task EvaluateAsync_MissingValidation_ReturnsValidationMissing() { using var f=await Valid(); File.Delete(f.Validation); await Reason(f,"P5REUSE_VALIDATION_MISSING"); }
    [Fact] public async Task EvaluateAsync_MissingManifest_ReturnsManifestMissing() { using var f=await Valid(); File.Delete(f.Manifest); await Reason(f,"P5REUSE_MANIFEST_MISSING"); }
    [Fact] public async Task EvaluateAsync_ExecutionIdentityMismatch_ReturnsIdentityMismatch() { using var f=await Valid(); await Reason(await f.EvaluateAsync(execution:"other"),"P5REUSE_IDENTITY_MISMATCH"); }
    [Fact] public async Task EvaluateAsync_PlanIdentityMismatch_ReturnsIdentityMismatch() { using var f=await Valid(); await Reason(await f.EvaluateAsync(plan:"other"),"P5REUSE_IDENTITY_MISMATCH"); }
    [Fact] public async Task EvaluateAsync_EventIdentityMismatch_ReturnsIdentityMismatch() { using var f=await Valid(); await Reason(await f.EvaluateAsync(eventId:"other"),"P5REUSE_IDENTITY_MISMATCH"); }
    [Fact] public async Task EvaluateAsync_LanguageMismatch_ReturnsIdentityMismatch() { using var f=await Valid(); await Reason(await f.EvaluateAsync(language:"fr"),"P5REUSE_IDENTITY_MISMATCH"); }
    [Fact] public async Task EvaluateAsync_Phase4AggregateIdMismatch_ReturnsLineageMismatch() { using var f=await Valid(); await Reason(await f.EvaluateAsync(f.Expected with { AggregateId="other" }),"P5REUSE_SOURCE_PHASE4_MISMATCH"); }
    [Fact] public async Task EvaluateAsync_Phase4AggregateChecksumMismatch_ReturnsLineageMismatch() { using var f=await Valid(); await Reason(await f.EvaluateAsync(f.Expected with { AggregateChecksum=Sha('a') }),"P5REUSE_SOURCE_PHASE4_MISMATCH"); }
    [Fact] public async Task EvaluateAsync_LongOrShortChecksumMismatch_ReturnsLineageMismatch() { using var f=await Valid(); await Reason(await f.EvaluateAsync(f.Expected with { LongChecksum=Sha('a') }),"P5REUSE_SOURCE_PHASE4_MISMATCH"); }
    [Fact] public async Task EvaluateAsync_ManifestMissingRequiredEntry_ReturnsManifestMismatch() { using var f=await Valid(); Edit(f.Manifest,n=>n["phase5Artifacts"]!.AsArray().RemoveAt(0)); await Reason(f,"P5REUSE_MANIFEST_INVALID"); }
    [Fact] public async Task EvaluateAsync_ManifestRoleMismatch_ReturnsManifestMismatch() { using var f=await Valid(); Edit(f.Manifest,n=>n["phase5Artifacts"]![0]!["role"]="wrong"); await Reason(f,"P5REUSE_MANIFEST_INVALID"); }
    [Fact] public async Task EvaluateAsync_ManifestChecksumMismatch_ReturnsChecksumMismatch() { using var f=await Valid(); Edit(f.Manifest,n=>n["phase5Artifacts"]![0]!["physicalSha256"]=Sha('a')); await Reason(f,"P5REUSE_CHECKSUM_INVALID"); }
    [Fact] public async Task EvaluateAsync_ManifestAbsolutePath_ReturnsPathInvalid() { using var f=await Valid(); Edit(f.Manifest,n=>n["phase5Artifacts"]![0]!["relativePath"]=Path.GetFullPath("bad")); await Reason(f,"P5REUSE_MANIFEST_INVALID"); }
    [Fact] public async Task EvaluateAsync_ManifestTraversalPath_ReturnsPathInvalid() { using var f=await Valid(); Edit(f.Manifest,n=>n["phase5Artifacts"]![0]!["relativePath"]="05-editorial/../bad"); await Reason(f,"P5REUSE_MANIFEST_INVALID"); }
    [Fact] public async Task EvaluateAsync_CertificationSemanticChecksumMismatch_ReturnsChecksumMismatch() { using var f=await Valid(); Edit(f.Editorial("blueprint-certification.json"),n=>n["semanticChecksum"]=Sha('a')); await Reason(f,"P5REUSE_CHECKSUM_INVALID"); }
    [Fact] public async Task EvaluateAsync_EditorialChecksumMismatch_ReturnsChecksumMismatch() { using var f=await Valid(); Edit(f.Editorial("editorial-contract.json"),n=>n["checksum"]=Sha('a')); await Reason(f,"P5REUSE_CHECKSUM_INVALID"); }
    [Fact] public async Task EvaluateAsync_CertificationNotAccepted_ReturnsCertificationInvalid() { using var f=await Valid(); Edit(f.Editorial("blueprint-certification.json"),n=>n["passed"]=false); await Reason(f,"P5REUSE_CERTIFICATION_REJECTED"); }
    [Fact] public async Task EvaluateAsync_InvalidCoverage_ReturnsCommittedStateInvalid() { using var f=await Valid(); Edit(f.Editorial("coverage-report.json"),n=>n["isValid"]=false); await Reason(f,"P5REUSE_CHECKSUM_INVALID"); }
    [Fact] public async Task EvaluateAsync_InvalidTransitions_ReturnsCommittedStateInvalid() { using var f=await Valid(); Edit(f.Editorial("transition-report.json"),n=>n["isValid"]=false); await Reason(f,"P5REUSE_CHECKSUM_INVALID"); }
    [Fact] public async Task EvaluateAsync_FailedPauseTest_ReturnsCommittedStateInvalid() { using var f=await Valid(); Edit(f.Editorial("pause-test-report.json"),n=>n["isValid"]=false); await Reason(f,"P5REUSE_CHECKSUM_INVALID"); }
    [Fact] public async Task EvaluateAsync_MalformedArtifactJson_ReturnsUnreadable() { using var f=await Valid(); await File.WriteAllTextAsync(f.Editorial("coverage-report.json"),"{"); await Reason(f,"P5REUSE_COMMITTED_STATE_INVALID"); }
    [Fact] public async Task EvaluateAsync_ValidState_ReturnsCompleteRelativeInventory() { using var f=await Valid(); var r=await f.EvaluateAsync(); Assert.Equal(7,r.Artifacts.Count); Assert.All(r.Artifacts,x=>Assert.StartsWith("05-editorial/",x.RelativePath)); Assert.All(r.Artifacts,x=>Assert.False(Path.IsPathRooted(x.RelativePath))); }

    private static async Task<Phase5PublicationTestFixture> Valid(){var f=new Phase5PublicationTestFixture();await f.PublishValidAsync();return f;}
    private static async Task Reason(Phase5PublicationTestFixture f,string reason)=>await Reason(await f.EvaluateAsync(),reason);
    private static Task Reason(Phase5CommittedStateEvaluation r,string reason){Assert.False(r.IsValid);Assert.Equal(reason,r.ReasonCode);return Task.CompletedTask;}
    private static void Edit(string path,Action<JsonObject> edit){var n=JsonNode.Parse(File.ReadAllText(path))!.AsObject();edit(n);File.WriteAllText(path,n.ToJsonString());}
    private static string Sha(char c)=>new(c,64);
}
