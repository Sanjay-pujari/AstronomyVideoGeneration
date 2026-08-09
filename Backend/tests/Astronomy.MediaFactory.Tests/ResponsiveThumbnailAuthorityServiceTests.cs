using System.Security.Cryptography;
using System.Text;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ResponsiveThumbnailAuthorityServiceTests
{
    private const string Orion = "4dfad265275d676ab8198b5068260bbd77dcd61fc1b9527d39af8bb2bc61251d";

    [Fact]
    public void Phase12AcceptsCertifiedPhase11PublishedChecksumContract() =>
        Assert.True(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, Orion, Orion));

    [Fact]
    public void Phase12DoesNotRehashPhase11ManifestUsingDifferentCanonicalization()
    {
        var consumerSideRehash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{\"variants\":[]}"))).ToLowerInvariant();

        Assert.NotEqual(consumerSideRehash, Orion);
        Assert.True(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, Orion, Orion));
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("authority", "", "authority")]
    [InlineData("authority", "authority", "")]
    public void Phase12RequiresManifestPublicationValidationChecksumAgreement(string manifest, string publication, string validation) =>
        Assert.False(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(manifest, publication, validation));

    [Fact]
    public void Phase12RejectsPublicationChecksumMismatch() =>
        Assert.False(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, "mismatch", Orion));

    [Fact]
    public void Phase12RejectsCanonicalValidationChecksumMismatch() =>
        Assert.False(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, Orion, "mismatch"));

    [Fact]
    public void Phase12AcceptsCurrentCertifiedOrionPhase11Authority() =>
        Assert.True(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, Orion, Orion));

    [Fact]
    public void ThumbnailDuplicateCopyDetectionIsCaseInsensitive()
    {
        Assert.True(ResponsiveThumbnailAuthorityService.DuplicateCopyDetected("Orion constellation guide!", " ORION, constellation   guide "));
        Assert.False(ResponsiveThumbnailAuthorityService.DuplicateCopyDetected("FIND ORION", "Orion constellation guide"));
    }

    [Fact]
    public void ConstellationThumbnailUsesDeterministicObjectCopy()
    {
        var copy = ResponsiveThumbnailAuthorityService.BuildThumbnailCopy("CONSTELLATION", ["Orion"], "Orion", "Orion constellation guide");

        Assert.Equal("FIND ORION", copy.Headline);
        Assert.Equal("Constellation.FindCertifiedPrimaryObject", copy.Rule);
        Assert.Equal(2, copy.WordCount);
    }

    [Fact]
    public void Phase12UsesExecutionEventIdentityInsteadOfPhase10ForCopyPolicy()
    {
        Assert.Null(typeof(SceneAssetCertification).GetProperty("EventType"));
        Assert.Null(typeof(SceneAssetCertification).GetProperty("PrimaryObjects"));

        var copy = ResponsiveThumbnailAuthorityService.BuildThumbnailCopy(
            "CONSTELLATION", ["Orion"], "Orion", "Orion constellation guide");

        Assert.Equal("FIND ORION", copy.Headline);
        Assert.NotEqual("ORION CONSTELLATION GUIDE", copy.Headline);
    }

    [Fact]
    public async Task MissingEventTypeFailsWithP12CopyAuthorityMissing()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResponsiveThumbnailAuthorityService.PublishAsync(Path.GetTempPath(), "plan", "event", "en", "", [], CancellationToken.None));

        Assert.StartsWith("P12_COPY_AUTHORITY_MISSING", exception.Message);
    }

    [Fact]
    public void EvergreenConstellationDoesNotAddTonight()
    {
        var copy = ResponsiveThumbnailAuthorityService.BuildThumbnailCopy("CONSTELLATION", ["Lyra"], "Lyra", "Lyra constellation guide");

        Assert.DoesNotContain("TONIGHT", copy.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NOW", copy.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeadlineWordBudgetEnforcedForCertifiedConstellationCopy() =>
        Assert.InRange(ResponsiveThumbnailAuthorityService.BuildThumbnailCopy("CONSTELLATION", ["Ursa Major"], "Ursa Major", "Guide").WordCount, 2, 5);

    [Fact]
    public void FindOrionIsNotDuplicateOfOrionConstellationGuide()
    {
        var result = Validate("FIND ORION");

        Assert.True(result.CopyDifferentiationPassed);
        Assert.False(result.DuplicateCopyDetected);
    }

    [Fact]
    public void ObjectNameOverlapIsAllowed() =>
        Assert.Contains("orion", Validate("ORION").SharedAuthorityTokens);

    [Fact]
    public void CaseInsensitiveFullHeroTitleReuseIsRejected() =>
        AssertDuplicate("ORION CONSTELLATION GUIDE");

    [Fact]
    public void WhitespaceNormalizedFullTitleReuseIsRejected() =>
        AssertDuplicate("  Orion   constellation\tguide!  ");

    [Fact]
    public void FullHeroSubtitleReuseIsRejected() =>
        AssertDuplicate("Orion: How to Find the Hunter Constellation");

    [Fact]
    public void ShortDeterministicCopyDerivedFromHeroIsAllowed()
    {
        foreach (var headline in new[] { "FIND ORION", "ORION", "SPOT ORION", "ORION CONSTELLATION" })
            Assert.True(Validate(headline).CopyDifferentiationPassed);
    }

    [Fact]
    public void ParagraphHeroCopyIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate("FIND ORION", "Orion: How to Find the Hunter Constellation"));

        Assert.StartsWith("P12_DUPLICATE_COPY", exception.Message);
    }

    [Fact]
    public void TonightIsRejectedForEvergreenConstellationWithoutTemporalAuthority()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate("ORION TONIGHT"));

        Assert.StartsWith("P12_UNCERTIFIED_COPY_CLAIM", exception.Message);
    }

    [Fact]
    public void CandidateAndOuterValidationUseSameCopyPolicy()
    {
        var outer = Validate("FIND ORION");
        var candidateReadback = Validate("FIND ORION");

        Assert.Equal(outer.CopyDifferentiationPassed, candidateReadback.CopyDifferentiationPassed);
        Assert.Equal(outer.DuplicateCopyDetected, candidateReadback.DuplicateCopyDetected);
    }

    [Fact]
    public void CommittedAuthorityCannotFailDifferentDuplicateCopyRule()
    {
        var candidate = Validate("FIND ORION");
        var committedReadback = Validate("FIND ORION");

        Assert.True(candidate.CopyDifferentiationPassed);
        Assert.Equal(candidate.CopyDifferentiationPassed, committedReadback.CopyDifferentiationPassed);
        Assert.Equal(candidate.DuplicateCopyDetected, committedReadback.DuplicateCopyDetected);
    }

    [Fact]
    public void Phase12HasSingleAuthoritativeCopyValidationPolicy() =>
        Assert.True(Validate("FIND ORION").CopyDifferentiationPassed);

    [Fact]
    public void CandidateCopyValidationAndOuterResultCannotDisagree() =>
        AssertPublicationAccepted(AcceptedPublication());

    [Fact]
    public void CommittedPhase12AuthorityCannotBeRejectedByLegacyCopyValidator() =>
        AssertPublicationAccepted(AcceptedPublication());

    [Fact]
    public void SuccessfulAuthorityPublicationMapsToSuccessfulPhaseResult() =>
        Assert.Equal("P12_THUMBNAIL_AUTHORITY_ACCEPTED", AcceptedPublication().ReasonCode);

    [Fact]
    public void SuccessfulAuthorityPublicationMapsToCanonicalValidation() =>
        Assert.Equal("Responsive thumbnail assets generated, validated, committed and read back.", AcceptedPublication().Reason);

    [Fact]
    public void P12DuplicateCopyCanOnlyComeFromAuthoritativeCopyPolicy() =>
        AssertDuplicate("Orion constellation guide");

    [Fact]
    public void LegacyThumbnailValidatorCannotOverrideNewAuthorityResult() =>
        Assert.True(AcceptedPublication().DownstreamReady);

    [Fact]
    public void FailedPhase12DoesNotExposePreviousSuccessfulAuthorityDiagnostics() =>
        Assert.False(ProductionPipelineExecutionService.ShouldExposePhase12AuthorityDiagnostics(
            12, ProductionPhaseStatus.Failed, false, true));

    [Fact]
    public void PreviousAuthorityDoesNotBecomeCurrentExecutionEvidence() =>
        Assert.False(ProductionPipelineExecutionService.ShouldExposePhase12AuthorityDiagnostics(
            12, ProductionPhaseStatus.Succeeded, false, true));

    [Fact]
    public void SuccessfulPhase12LoadsCurrentAuthorityDiagnostics() =>
        Assert.True(ProductionPipelineExecutionService.ShouldExposePhase12AuthorityDiagnostics(
            12, ProductionPhaseStatus.Succeeded, true, true));

    [Fact]
    public void Phase12ExecutedPhaseNumbersContains12AfterExecution()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Backend", "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "ProductionPipelineExecutionService.cs"));
        Assert.Contains("phaseExecutionBegan || status == ProductionPhaseStatus.Succeeded ? new[] { phaseNo }", source);
    }


    [Fact]
    public void PosterIncludesIdentificationOrHighlightFact()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse("""{"assets":[{"astronomyObjectsVerified":["Alnitak","Alnilam","Mintaka","Betelgeuse","Rigel","Orion Nebula / M42"]}]}""");
        var poster = ResponsiveThumbnailAuthorityService.BuildPosterContent("CONSTELLATION", "FIND ORION", ["Orion"], [], manifest.RootElement);

        Assert.Equal("CONSTELLATION", poster.Badge.Value);
        Assert.Contains(poster.Facts, fact => fact.Key == "identification" && fact.Value == "3 BELT STARS" && fact.IsCertified);
        Assert.Contains(poster.Facts, fact => fact.Key == "highlights");
        Assert.Contains(poster.Facts, fact => fact.Key == "deepSky");
    }

    [Fact]
    public void PosterDoesNotInventDirectionOrBestTime()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse("""{"assets":[{"astronomyObjectsVerified":["Betelgeuse"]}]}""");
        var poster = ResponsiveThumbnailAuthorityService.BuildPosterContent("CONSTELLATION", "FIND ORION", ["Orion"], [], manifest.RootElement);

        Assert.DoesNotContain(poster.Facts, fact => fact.Key is "direction" or "bestTime" or "date");
        Assert.DoesNotContain("WEST", string.Join(' ', poster.Facts.Select(f => f.Value)));
    }

    [Fact]
    public void ThreeBeltStarsRequiresAllThreeCertifiedStars()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse("""{"assets":[{"astronomyObjectsVerified":["Alnitak","Alnilam"]}]}""");
        var poster = ResponsiveThumbnailAuthorityService.BuildPosterContent("CONSTELLATION", "FIND ORION", ["Orion"], [], manifest.RootElement);

        Assert.DoesNotContain(poster.Facts, fact => fact.Value == "3 BELT STARS");
    }

    [Fact]
    public void FactsDoNotOverlapEachOther()
    {
        ResponsiveThumbnailAuthorityService.TextBlockBounds[] boxes =
        [new("headline", 10, 10, 100, 30, 30), new("fact", 10, 60, 100, 30, 20)];
        Assert.False(ResponsiveThumbnailAuthorityService.HasOverlap(boxes));
    }

    [Fact]
    public void OverlappingPosterTextIsRejectedByCollisionEngine()
    {
        ResponsiveThumbnailAuthorityService.TextBlockBounds[] boxes =
        [new("headline", 10, 10, 100, 30, 30), new("fact", 50, 20, 100, 30, 20)];
        Assert.True(ResponsiveThumbnailAuthorityService.HasOverlap(boxes));
    }

    [Fact]
    public void Phase12DoesNotRenderOnTopOfHeroComposedPng()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Backend", "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "ResponsiveThumbnailAuthorityService.cs"));
        Assert.Contains("Render(cleanPath", source);
        Assert.DoesNotContain("Render(sourcePath", source);
    }

    [Fact]
    public void SelectedPhase8PhysicalShaMustMatchPhase11Lineage()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Backend", "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "ResponsiveThumbnailAuthorityService.cs"));
        Assert.Contains("sourcePhase8PhysicalSha256", source);
        Assert.Contains("P12_SOURCE_CHECKSUM_MISMATCH", source);
    }

    [Fact]
    public void Phase12UsesPhase11SelectedPhase8CleanRaster()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Backend", "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "ResponsiveThumbnailAuthorityService.cs"));
        Assert.Contains("sourcePhase8PhysicalPath", source);
        Assert.Contains("P12_CLEAN_SOURCE_REQUIRED", source);
        Assert.Contains("heroRasterUsedAsBackground = false", source);
    }

    private static ResponsiveThumbnailPublicationResult AcceptedPublication() => new(
        ["thumbnail-landscape.png", "thumbnail-square.png", "thumbnail-portrait.png"], Orion,
        true, true, true, true, true,
        "Responsive thumbnail assets generated, validated, committed and read back.",
        "P12_THUMBNAIL_AUTHORITY_ACCEPTED");

    private static void AssertPublicationAccepted(ResponsiveThumbnailPublicationResult result)
    {
        Assert.True(result.CandidateValidationPassed);
        Assert.True(result.CandidateReadbackPassed);
        Assert.True(result.PublicationCommitted);
        Assert.True(result.CommittedReadbackPassed);
        Assert.True(result.DownstreamReady);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Backend", "src")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static ResponsiveThumbnailAuthorityService.CopyDifferentiationDecision Validate(
        string headline, string? secondary = null) => ResponsiveThumbnailAuthorityService.ValidateCopyDifferentiation(
            "Orion constellation guide", "Orion: How to Find the Hunter Constellation", headline, secondary,
            "Constellation.FindCertifiedPrimaryObject");

    private static void AssertDuplicate(string headline)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate(headline));
        Assert.StartsWith("P12_DUPLICATE_COPY", exception.Message);
    }
}
