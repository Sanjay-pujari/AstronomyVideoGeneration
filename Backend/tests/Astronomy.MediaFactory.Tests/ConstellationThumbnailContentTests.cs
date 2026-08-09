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
        Assert.Contains("P12_CONSTELLATION_INFORMATION_INSUFFICIENT", error.Message);
    }

    [Fact] public void ConstellationThumbnailCanPassHeadlineOnlyWhenNoCertifiedFactsExist() =>
        MatureThumbnailCandidatePublisher.ValidateConstellationInformation("CONSTELLATION", [], []);

    [Fact] public void ConstellationLandscapePrefersThreeDiverseFacts() => Assert.Equal(
        ["Identification", "DeepSky", "BrightObjects"], Select("Landscape", 1280, 288).Select(x => x.Category));

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

    private static IReadOnlyList<MatureThumbnailCandidatePublisher.ConstellationThumbnailFact> Select(string aspect, int width, int height)
    {
        var facts = MatureThumbnailCandidatePublisher.BuildConstellationContent("CONSTELLATION", "Orion", OrionClaims).CertifiedFacts;
        return MatureThumbnailCandidatePublisher.SelectThumbnailFactsForAspect("CONSTELLATION", aspect, facts, new Rectangle(0, 0, width, height));
    }
}
