using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Tests;

public sealed class ConstellationThumbnailContentTests
{
    private static CertifiedKnowledgeClaim Claim(string text, string category = "Recognition", bool certified = true) =>
        new(Guid.NewGuid().ToString(), category, category, text, null, null, ["canonical"], null, 1, null, null,
            certified ? "Certified" : "Draft", certified ? "Accepted" : "Pending", "CONSTELLATION");

    private static readonly CertifiedKnowledgeClaim[] OrionClaims =
    [
        Claim("Three stars form Orion's Belt."),
        Claim("Betelgeuse and Rigel are bright key stars in Orion.", "KeyObjects"),
        Claim("The Orion Nebula / M42 is a deep sky object in Orion.", "DeepSkyObjects")
    ];

    [Fact] public void ConstellationThumbnailHeadlineOnlyFailsWhenFactsAvailable()
    {
        var content = MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", OrionClaims);
        var error = Assert.Throws<InvalidOperationException>(() => MatureThumbnailCandidatePublisher.ValidateConstellationInformation("CONSTELLATION", content.CertifiedFacts, []));
        Assert.Contains("P12_THUMBNAIL_CONTENT_PLAN_INSUFFICIENT", error.Message);
    }

    [Fact] public void ConstellationThumbnailCanPassHeadlineOnlyWhenNoCertifiedFactsExist() =>
        MatureThumbnailCandidatePublisher.ValidateConstellationInformation("CONSTELLATION", [], []);

    [Fact] public void ConstellationLandscapePrefersThreeDiverseFacts() => Assert.Equal(
        ["Hook", "DeepSky", "BrightObjects"], Select("Landscape", 1280, 288).Select(x => x.Category));

    [Fact] public void ConstellationSquarePrefersTwoFacts() => Assert.Equal(2, Select("Square", 1080, 432).Count);

    [Fact] public void ConstellationPortraitPrefersTwoOrThreeFacts() => Assert.InRange(Select("Portrait", 1080, 768).Count, 2, 3);

    [Fact] public void BeltCueRequiresCertifiedRelationship()
    {
        var content = MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", [Claim("Alnitak, Alnilam, and Mintaka are stars in Orion.")]);
        Assert.Null(content.IdentificationCue);
    }

    [Fact] public void BrightStarsLabelRequiresCertifiedClassification()
    {
        var content = MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", [Claim("Betelgeuse and Rigel are in Orion.")]);
        Assert.Empty(content.BrightObjects);
    }

    [Fact] public void M42DeepSkyFactRequiresCertifiedAuthority()
    {
        var content = MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", [Claim("Orion Nebula / M42 is a deep sky object.", certified: false)]);
        Assert.Empty(content.DeepSkyHighlights);
    }

    [Fact] public void MissingTimingDoesNotGenerateFakeTime() => Assert.DoesNotContain(
        MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", OrionClaims).CertifiedFacts,
        x => x.DisplayValue.Contains("PM", StringComparison.OrdinalIgnoreCase) || x.DisplayValue.Contains("TONIGHT", StringComparison.OrdinalIgnoreCase));

    [Fact] public void MissingDirectionDoesNotGenerateFakeDirection() => Assert.DoesNotContain(
        MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", OrionClaims).CertifiedFacts,
        x => x.DisplayValue.Contains("EAST", StringComparison.OrdinalIgnoreCase) || x.DisplayValue.Contains("WEST", StringComparison.OrdinalIgnoreCase));

    [Fact] public void AiPromptContainsNoFactualText()
    {
        var prompt = MatureThumbnailCandidatePublisher.BuildPrompt("CONSTELLATION", ["Orion"], "Square");
        Assert.Contains("NO embedded text. NO labels. NO numbers. NO watermark", prompt);
        Assert.DoesNotContain("Belt", prompt); Assert.DoesNotContain("M42", prompt);
    }

    [Fact] public void OverlayFactsAreDeterministic()
    {
        var first = MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", OrionClaims);
        var second = MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", OrionClaims);
        Assert.Equal(first.CertifiedFacts.Select(x => (x.Category, x.DisplayValue, x.AuthorityPath)), second.CertifiedFacts.Select(x => (x.Category, x.DisplayValue, x.AuthorityPath)));
    }

    [Fact] public void ConstellationPlannerConsumesVerifiedShortTitle()
    {
        var plan = OrionShortTitlePlan([]);
        Assert.Equal("SPOT THE FAMOUS BELT", plan.Hook);
        Assert.Equal("M42", plan.SupportingHighlight);
    }

    [Fact] public void ConstellationPlannerConsumesCertifiedKnowledgeContext()
        => Assert.Contains(MatureThumbnailCandidatePublisher.BuildThumbnailContentPlan("CONSTELLATION", "Orion", [], "", false, OrionClaims).Facts,
            x => x.AuthorityPath.StartsWith("02-intelligence/certified-knowledge-context.json", StringComparison.Ordinal));

    [Fact] public void ConstellationPlannerDoesNotReturnHeadlineOnlyWhenHookAuthorityExists()
        => Assert.True(OrionShortTitlePlan([]).ContentQualityPassed);

    [Fact] public void ConstellationPlannerRecordsAuthorityReferences()
        => Assert.NotEmpty(OrionShortTitlePlan([]).AuthorityReferences);

    [Fact] public void ConstellationPlannerRecordsTransformationRules()
        => Assert.Contains("verified-short-title-belt-hook", OrionShortTitlePlan([]).TransformationRules);

    [Fact] public void BeltCopyRequiresCertifiedBeltAuthority()
        => Assert.DoesNotContain("BELT", MatureThumbnailCandidatePublisher.BuildThumbnailContentPlan(
            "CONSTELLATION", "Orion", ["Mintaka", "Alnilam", "Alnitak"], "", true, []).Hook);

    [Fact] public void MissingTimingDoesNotProduceTonight()
        => Assert.DoesNotContain("TONIGHT", OrionShortTitlePlan([]).Facts.Select(x => x.DisplayValue));

    [Fact] public void MissingDirectionDoesNotProduceDirection()
        => Assert.DoesNotContain(OrionShortTitlePlan([]).Facts, fact => new[] { "EAST", "WEST", "NORTH", "SOUTH" }
            .Any(direction => fact.DisplayValue.Contains(direction, StringComparison.OrdinalIgnoreCase)));

    private static MatureThumbnailCandidatePublisher.ThumbnailContentPlan OrionShortTitlePlan(IReadOnlyList<CertifiedKnowledgeClaim> claims) =>
        MatureThumbnailCandidatePublisher.BuildThumbnailContentPlan("CONSTELLATION", "Orion", ["Betelgeuse", "Rigel", "Orion Nebula / M42"],
            "Orion is a bright, easy-to-find constellation with famous Belt stars and the Orion Nebula.", true, claims);

    private static IReadOnlyList<MatureThumbnailCandidatePublisher.ConstellationThumbnailFact> Select(string aspect, int width, int height)
    {
        var facts = MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", OrionClaims).CertifiedFacts;
        return MatureThumbnailCandidatePublisher.SelectThumbnailFactsForAspect("CONSTELLATION", aspect, facts, new Rectangle(0, 0, width, height));
    }
}
