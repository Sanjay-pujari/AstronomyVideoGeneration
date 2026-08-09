using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase13GallerySemanticAuthorityTests
{
    [Fact] public void IdentificationCanResolveFromExplicitPhase2Fact() => AssertStrategy(
        [Claim("id", "Identification", "Recognize Orion by its three Belt stars.")], EmptyPhase4(), EmptyPhase6(), "ExplicitCertifiedPhase2IdentificationFact");
    [Fact] public void IdentificationCanResolveFromCertifiedObservationFact() => AssertStrategy(
        [Claim("obs", "Observation", "Look for Orion's three distinctive Belt stars.")], EmptyPhase4(), EmptyPhase6(), "CertifiedPhase2ObservationRecognitionFact");
    [Fact] public void IdentificationCanResolveFromPhase6ViewerTakeaway() => AssertStrategy(
        [], EmptyPhase4(), Phase6("Locate Orion by looking for three Belt stars."), "CertifiedPhase6RecognitionCue");
    [Fact] public void IdentificationCanResolveFromPhase4LearningObjective() => AssertStrategy(
        [], Json("""{"learningObjectives":["Recognize Orion from its three Belt stars."]}"""), EmptyPhase6(), "CertifiedPhase4RecognitionObjective");

    [Fact] public void ObjectNamesAloneDoNotProveIdentificationRelationship() => Assert.False(Resolve([], EmptyPhase4(), EmptyPhase6()).Certified);
    [Fact] public void BeltRelationshipRequiresExplicitCertifiedAuthority() => Assert.True(Resolve(
        [Claim("belt", "Identification", "Alnitak, Alnilam, and Mintaka are the three stars of Orion's Belt.")], EmptyPhase4(), EmptyPhase6()).Certified);
    [Fact] public void UncertifiedObjectRelationshipIsRejected() => Assert.False(Resolve(
        [Claim("rel", "Relationship", "Mintaka and Alnilam", "Draft")], EmptyPhase4(), EmptyPhase6()).Certified);
    [Fact] public void GenericIdentityClaimCannotSubstituteIdentification() => Assert.Throws<InvalidOperationException>(() =>
        Phase13GalleryAuthority.SelectCertifiedContentForGalleryRole("CONSTELLATION_ORION", "how-to-identify",
            [Claim("identity", "Identity", "Orion is a constellation.")], ["Orion"], ["Alnitak", "Alnilam", "Mintaka"], [], []));
    [Fact] public void QuestionEngineIsNotUsedAsGalleryAuthority() => Assert.DoesNotContain("question", Resolve([], EmptyPhase4(), EmptyPhase6()).ResolutionStrategy, StringComparison.OrdinalIgnoreCase);
    [Fact] public void SourceNotesAreNotPublishedAsFacts() => Assert.False(Resolve([], Json("""{"sourceNotes":["Recognize Orion from a URL title"]}"""), EmptyPhase6()).Certified);

    [Fact] public void UnsupportedIdentificationRoleUsesCertifiedSubstitute() => Assert.True(SubstitutionPlan().Diagnostics[1].RoleSubstitutionApplied);
    [Fact] public void RoleSubstitutionDoesNotDuplicateExistingRole() => Assert.Equal(6, SubstitutionPlan().Selections.Select(x => x.ResolvedRoleId).Distinct().Count());
    [Fact] public void RoleSubstitutionRequiresCertifiedAuthority() => Assert.Contains("P13_GALLERY_INSUFFICIENT_CERTIFIED_ROLE_CONTENT", Assert.Throws<InvalidOperationException>(() =>
        Phase13GalleryAuthority.ResolveRolePlan(Roles, "CONSTELLATION_ORION", BaseClaims()[..5], EmptyPhase4(), EmptyPhase6(), ["Orion"], [])).Message);
    [Fact] public void SixResolvedRolesRemainUnique() => Assert.True(Phase13GalleryAuthority.EvaluateCopyDiversity(SubstitutionPlan().Selections, ["Orion"]).RoleDiversityPassed);
    [Fact] public void FailureOccursOnlyWhenSixCertifiedRolesCannotBeBuilt() => Assert.Equal(6, SubstitutionPlan().Selections.Length);

    private static readonly string[] Roles = ["cover-identity", "how-to-identify", "bright-stars-or-key-objects", "deep-sky-highlight", "science-or-story-highlight", "observation-checklist"];
    private static CertifiedKnowledgeClaim[] BaseClaims() =>
    [
        Claim("identity", "Identity", "Orion is a certified constellation."),
        Claim("stars", "BrightObjects", "Betelgeuse and Rigel are bright stars."),
        Claim("deep", "DeepSky", "M42 is a deep sky nebula."),
        Claim("history", "History", "Orion appears in historical sky traditions."),
        Claim("observe", "Observation", "Observe Orion under a clear dark sky."),
        Claim("science", "ScientificFact", "Orion contains stars at different distances.")
    ];
    private static (Phase13GalleryAuthority.GalleryRoleContentSelection[] Selections, Phase13GalleryAuthority.GalleryRoleResolutionDiagnostic[] Diagnostics) SubstitutionPlan() =>
        Phase13GalleryAuthority.ResolveRolePlan(Roles, "CONSTELLATION_ORION", BaseClaims(), EmptyPhase4(), EmptyPhase6(), ["Orion"], ["Betelgeuse", "Rigel", "M42"]);
    private static void AssertStrategy(IReadOnlyList<CertifiedKnowledgeClaim> claims, JsonElement p4, StoryFramesAuthority p6, string expected) => Assert.Equal(expected, Resolve(claims, p4, p6).ResolutionStrategy);
    private static Phase13GalleryAuthority.ResolvedGallerySemanticAuthority Resolve(IReadOnlyList<CertifiedKnowledgeClaim> claims, JsonElement p4, StoryFramesAuthority p6) =>
        Phase13GalleryAuthority.ResolveGallerySemanticAuthority("how-to-identify", "CONSTELLATION_ORION", claims, p4, p6, ["Orion", "Alnitak", "Alnilam", "Mintaka"]);
    private static CertifiedKnowledgeClaim Claim(string id, string category, string text, string status = "Accepted") => new(id, category, category, text, null, null, ["certified-source"], null, 1m, null, null, status == "Accepted" ? "Certified" : "Draft", status, "CONSTELLATION");
    private static JsonElement EmptyPhase4() => Json("{}");
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static StoryFramesAuthority EmptyPhase6() => Authority([]);
    private static StoryFramesAuthority Phase6(string intent) => Authority([new("f", "s", 1, 1, "Short", "Hook", "Recognition", "Hero", [], [], [], intent, "Show a distinctive recognition cue.", "Wide", "Static", "None", "Orion", "Sky", "Centered", "Natural", "Clear", "Still", "Cut", "Cut", [], [], [], [], false, "None", 0, 3, [], [], [])]);
    private static StoryFramesAuthority Authority(IReadOnlyList<StoryFrameAuthorityFrame> frames) => new("a", "e", "p", "orion", "en", "profile", "c", "cc", "ec", "ecc", "p4", "builder", "1", ["Short"], frames, DateTimeOffset.UnixEpoch, "sum");
}
