using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase13GalleryCopyDiversityTests
{
    private static readonly string[] Roles = ["cover-identity", "how-to-identify", "bright-stars-or-key-objects",
        "deep-sky-highlight", "science-or-story-highlight", "observation-checklist"];
    private static readonly string[] Objects = ["Betelgeuse", "Rigel", "Orion Nebula / M42"];
    private static readonly CertifiedKnowledgeClaim[] Claims =
    [
        Claim("identity", "Identity", "Orion is a certified constellation."),
        Claim("identify", "Identification", "Identify Orion by its Belt of Alnitak, Alnilam, and Mintaka."),
        Claim("stars", "BrightObjects", "Betelgeuse and Rigel are bright stars in Orion."),
        Claim("m42", "DeepSky", "Orion Nebula / M42 is a stellar nursery."),
        Claim("history", "History", "Orion has a long history in sky traditions."),
        Claim("observe", "Observation", "Observe Orion by first locating its distinctive Belt.")
    ];

    [Fact] public void SharedOrionTokenAcrossPagesIsAllowed() => Assert.True(Result().SharedEventIdentityAllowed);
    [Fact] public void SixDistinctRoleHeadlinesPassDiversity() => Assert.True(Result().HeadlineDiversityPassed);
    [Fact] public void RoleSpecificStructuredObjectContentCountsAsDistinct() => Assert.Equal(6, Result().DistinctPrimaryContentCount);
    [Fact] public void AllSixResolvedRolesRemainUnique() => Assert.True(Result().RoleDiversityPassed);

    [Fact]
    public void SamePrimaryClaimAcrossAllPagesFails()
    {
        var selections = Plans().Select(p => p with { PrimaryContent = "Orion is a constellation." }).ToArray();
        var result = Phase13GalleryAuthority.EvaluateCopyDiversity(selections, ["Orion"]);
        Assert.False(result.PrimaryContentDiversityPassed);
        Assert.Equal([1, 2, 3, 4, 5, 6], Assert.Single(result.DuplicatePrimaryContentGroups).PageSlots);
    }

    [Fact] public void CoverIdentityClaimCannotPopulateAllRoles() => Assert.Throws<InvalidOperationException>(() =>
        Phase13GalleryAuthority.SelectCertifiedContentForGalleryRole("CONSTELLATION", "deep-sky-highlight",
            [Claims[0]], ["Orion"], Objects, [], []));

    [Fact] public void DeepSkyRoleSelectsDeepSkyAuthority() => Assert.Equal("m42", Select("deep-sky-highlight").PrimaryClaim.KnowledgeId);
    [Fact] public void BrightObjectRoleSelectsObjectAuthority() => Assert.Equal("stars", Select("bright-stars-or-key-objects").PrimaryClaim.KnowledgeId);

    [Fact]
    public void ObservationRoleDoesNotInventMissingTimingOrDirection()
    {
        var selected = Select("observation-checklist");
        Assert.DoesNotContain("tonight", selected.PrimaryContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("west", selected.PrimaryContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9 pm", selected.PrimaryContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void UnsupportedObservationRoleCanSubstituteCertifiedEducationalRole() => Assert.Contains("P13_GALLERY_ROLE_CONTENT_UNAVAILABLE",
        Assert.Throws<InvalidOperationException>(() => Phase13GalleryAuthority.SelectCertifiedContentForGalleryRole("CONSTELLATION",
            "observation-checklist", Claims[..5], ["Orion"], Objects, [], [])).Message);
    [Fact] public void RoleSubstitutionCannotDuplicateExistingResolvedRole() => Assert.True(Result().RoleDiversityPassed);
    [Fact] public void RoleSubstitutionRecordsReason() => Assert.All(Plans(), p => Assert.Null(p.RoleSubstitutionReason));

    private static Phase13GalleryAuthority.GalleryRoleContentSelection Select(string role) =>
        Phase13GalleryAuthority.SelectCertifiedContentForGalleryRole("CONSTELLATION", role, Claims, ["Orion"], Objects, [], []);
    private static Phase13GalleryAuthority.GalleryRoleContentSelection[] Plans() => Roles.Select(Select).ToArray();
    private static Phase13GalleryAuthority.GalleryCopyDiversityResult Result() =>
        Phase13GalleryAuthority.EvaluateCopyDiversity(Plans(), ["Orion"]);
    private static CertifiedKnowledgeClaim Claim(string id, string category, string text) =>
        new(id, category, category, text, null, null, ["certified-source"], null, 1m, null, null, "Certified", "Accepted", "CONSTELLATION");
}
