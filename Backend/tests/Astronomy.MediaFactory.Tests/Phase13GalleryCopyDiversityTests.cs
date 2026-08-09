using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

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

    [Theory]
    [InlineData("Outcome03")]
    [InlineData("OpeningHook")]
    [InlineData("The final narration remains under review")]
    [InlineData("Advance the certified workflow")]
    public void GalleryRejectsInternalWorkflowCopy(string leakedCopy)
    {
        var plans = Plans();
        plans[0] = plans[0] with { PrimaryContent = leakedCopy };
        var error = Assert.Throws<InvalidOperationException>(() => Phase13GalleryAuthority.ValidatePublicCopy(plans));
        Assert.StartsWith("P13_GALLERY_INTERNAL_COPY_LEAK", error.Message);
    }

    [Fact] public void GalleryRejectsOutcomeTokenInHeadline() => RejectField(p => p with { Headline = "Outcome01" });
    [Fact] public void GalleryRejectsOutcomeTokenInDetail() => RejectField(p => p with { PrimaryContent = "Outcome01" });
    [Fact] public void GalleryRejectsOutcomeTokenInFacts() => RejectField(p => p with { SupportingContent = ["Outcome01"] });

    [Fact]
    public void GalleryRejectsOutcomeTokenInPrompt()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Phase13GalleryAuthority.ValidateAiPrompt("Create a view illustrating Outcome01."));
        Assert.StartsWith("P13_GALLERY_INTERNAL_COPY_LEAK", error.Message);
    }

    [Fact]
    public void GalleryAllowsOutcomeTokenOnlyInAuthorityMetadata()
    {
        var plans = Plans();
        plans[0] = plans[0] with { PrimaryContentAuthority = "authority.json#/outcomes/Outcome01" };
        Phase13GalleryAuthority.ValidatePublicCopy(plans);
    }

    [Theory]
    [InlineData("Outcome01")]
    [InlineData("Objective01")]
    [InlineData("Scene01")]
    [InlineData("Beat01")]
    [InlineData("Knowledge01")]
    [InlineData("Frame01")]
    public void CanonicalEditorialIdsAreReferencesNotPublicationText(string value) =>
        Assert.True(Phase13GallerySemanticHydrator.IsInternalReference(value));

    [Fact]
    public void GalleryRejectsUnresolvedOutcomeReferenceWithoutPublishingIt()
    {
        Assert.True(Phase13GallerySemanticHydrator.IsInternalReference("Outcome99"));
        var plans = Plans();
        plans[0] = plans[0] with { PrimaryContent = "Outcome99" };
        Assert.StartsWith("P13_GALLERY_INTERNAL_COPY_LEAK",
            Assert.Throws<InvalidOperationException>(() => Phase13GalleryAuthority.ValidatePublicCopy(plans)).Message);
    }

    [Fact]
    public void OutcomeReferenceResolvesToViewerFacingTakeawayBeforeCopyOrPromptPlanning()
    {
        var source = OrionDocumentaryBlueprintFixture.Scene();
        var scene = new DocumentarySceneBlueprint(source.SceneId, source.SceneNumber, source.Title,
            source.NarrativeStage, source.SceneRole, source.ViewerQuestion, source.SceneObjective,
            new EditorialOutcome("Identify Orion by its three Belt stars.", "Outcome01", true, true, true, false, false),
            source.EditorialPriority, source.KnowledgeReferences, source.VisualOpportunities,
            source.Transition, source.EstimatedDurationSeconds);

        var resolved = Phase13GallerySemanticHydrator.ResolveEditorialOutcomeReference("Outcome01", [scene]);

        Assert.Equal("Resolved", resolved.ResolutionStatus);
        Assert.Equal("editorialOutcomeId", resolved.ReferenceType);
        Assert.Equal("Identify Orion by its three Belt stars.", resolved.ResolvedText);
        Assert.DoesNotContain("Outcome01", resolved.ResolvedText);
        Phase13GalleryAuthority.ValidateAiPrompt($"Visual purpose: {resolved.ResolvedText}");
        Assert.Equal("Outcome01", resolved.ReferenceId); // lineage metadata deliberately retains the pointer id
    }

    [Fact]
    public void Outcome99FailsClosedInTypedResolver()
    {
        var resolved = Phase13GallerySemanticHydrator.ResolveEditorialOutcomeReference("Outcome99", []);
        Assert.Equal("Unresolved", resolved.ResolutionStatus);
        Assert.False(resolved.Certified);
        Assert.Null(resolved.ResolvedText);
        var error = Assert.Throws<InvalidOperationException>(() =>
            Phase13GallerySemanticHydrator.RequireResolvedEditorialReference(resolved,
                Phase13GallerySemanticHydrator.Phase4Blueprint, "/longVariant/blueprint/scenes/0/editorialOutcome/narrativeContribution"));
        Assert.StartsWith("P13_GALLERY_EDITORIAL_REFERENCE_UNRESOLVED", error.Message);
    }

    [Fact]
    public void GalleryPublicCopyRequiresAuthorityReferences()
    {
        var plans = Plans();
        plans[0] = plans[0] with { PrimaryContentAuthority = "" };
        var error = Assert.Throws<InvalidOperationException>(() => Phase13GalleryAuthority.ValidatePublicCopy(plans));
        Assert.StartsWith("P13_GALLERY_COPY_AUTHORITY_MISSING", error.Message);
    }

    [Fact]
    public void EditorialIntentIsVisualPlanningEligibleButNeverPublicationEligible()
    {
        var source = "Advance the certified HistoricalContext intent; final narration remains owned by Phase 7.";
        var item = new Phase13GallerySemanticHydrator.GallerySemanticItem("Scene05", "HistoricalContext",
            Phase13GallerySemanticHydrator.GallerySemanticUsage.EditorialIntent, Phase13GallerySemanticHydrator.Phase4Blueprint,
            "/longVariant/blueprint/scenes/4/sceneObjective/learningGoal", source, null, "checksum", true,
            "planning-only", "historical astronomy context");
        Assert.False(item.IsPublicationEligible);
        Assert.True(item.IsVisualPlanningEligible);
        Assert.Null(item.ResolvedPublicValue);
    }

    [Fact]
    public void SanitizedVisualPromptContainsConceptNotWorkflowSentence()
    {
        var context = new Phase13GalleryAuthority.GalleryVisualPromptContext("Orion constellation guide", ["Orion", "M42"],
            ["Orion appears in historical sky traditions."], "historical astronomy context", "science-or-story-highlight",
            "CONSTELLATION", "Full-frame astronomy composition");
        var prompt = Phase13GalleryAuthority.BuildMatureGalleryPrompt(context);
        Assert.Contains("historical astronomy context", prompt);
        Assert.DoesNotContain("Advance the certified", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("final narration remains", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Outcome01", prompt, StringComparison.OrdinalIgnoreCase);
        Phase13GalleryAuthority.ValidateAiPrompt(prompt);
    }

    private static Phase13GalleryAuthority.GalleryRoleContentSelection Select(string role) =>
        Phase13GalleryAuthority.SelectCertifiedContentForGalleryRole("CONSTELLATION", role, Claims, ["Orion"], Objects, [], []);
    private static Phase13GalleryAuthority.GalleryRoleContentSelection[] Plans() => Roles.Select(Select).ToArray();
    private static Phase13GalleryAuthority.GalleryCopyDiversityResult Result() =>
        Phase13GalleryAuthority.EvaluateCopyDiversity(Plans(), ["Orion"]);
    private static CertifiedKnowledgeClaim Claim(string id, string category, string text) =>
        new(id, category, category, text, null, null, ["certified-source"], null, 1m, null, null, "Certified", "Accepted", "CONSTELLATION");

    private static void RejectField(Func<Phase13GalleryAuthority.GalleryRoleContentSelection,
        Phase13GalleryAuthority.GalleryRoleContentSelection> mutate)
    {
        var plans = Plans();
        plans[0] = mutate(plans[0]);
        Assert.StartsWith("P13_GALLERY_INTERNAL_COPY_LEAK",
            Assert.Throws<InvalidOperationException>(() => Phase13GalleryAuthority.ValidatePublicCopy(plans)).Message);
    }
}
