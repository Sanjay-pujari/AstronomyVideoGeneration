using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class Phase4ToPhase5LineageTests
{
    [Fact]
    public void Phase4ToPhase5Lineage_CommittedLongChecksumMatchesExpectedAuthority()
    {
        var authority = Phase5CertificationFixture.Create().PublishedPhase4;
        Assert.Equal(authority.LongProjectionChecksum, Phase5ExpectedPhase4Authority.From(authority).LongChecksum);
    }

    [Fact]
    public void Phase4ToPhase5Lineage_CommittedShortChecksumMatchesExpectedAuthority()
    {
        var authority = Phase5CertificationFixture.Create().PublishedPhase4;
        Assert.Equal(authority.ShortProjectionChecksum, Phase5ExpectedPhase4Authority.From(authority).ShortChecksum);
    }

    [Fact]
    public void Phase4ToPhase5Lineage_CompatibilityAdapterPreservesAuthoritativeLongChecksum()
    {
        var fixture = Phase5CertificationFixture.Create();
        Assert.Same(fixture.PublishedPhase4, fixture.Request.PublishedAggregate);
        Assert.Equal(fixture.PublishedPhase4.LongProjectionChecksum, fixture.Result.Certification.SourceLongBlueprintChecksum);
        Assert.NotEqual(fixture.Request.Long.Metadata.Checksum, fixture.Result.Certification.SourceLongBlueprintChecksum);
    }

    [Fact]
    public void Phase4ToPhase5Lineage_CompatibilityAdapterPreservesAuthoritativeShortChecksum()
    {
        var fixture = Phase5CertificationFixture.Create();
        Assert.Equal(fixture.PublishedPhase4.ShortProjectionChecksum, fixture.Result.Certification.SourceShortBlueprintChecksum);
    }

    [Fact]
    public void Phase4ToPhase5Lineage_CertificationPreservesSourceChecksums()
    {
        var fixture = Phase5CertificationFixture.Create();
        Assert.Equal((fixture.PublishedPhase4.DeterministicChecksum, fixture.PublishedPhase4.LongProjectionChecksum,
                fixture.PublishedPhase4.ShortProjectionChecksum),
            (fixture.Result.Certification.SourcePhase4Checksum, fixture.Result.Certification.SourceLongBlueprintChecksum,
                fixture.Result.Certification.SourceShortBlueprintChecksum));
    }

    [Fact]
    public void Phase4ToPhase5Lineage_AllPhase5ReportsPreserveSourceLongChecksum()
    {
        var fixture = Phase5CertificationFixture.Create();
        var reports = new[] { fixture.Result.Validation.SourceLongChecksum, fixture.Result.SceneIntents.SourceLongChecksum,
            fixture.Result.Coverage.SourceLongChecksum, fixture.Result.Transitions.SourceLongChecksum,
            fixture.Result.PauseTest.SourceLongChecksum };
        Assert.All(reports, checksum => Assert.Equal(fixture.PublishedPhase4.LongProjectionChecksum, checksum));
    }

    [Fact]
    public void Phase4ToPhase5Lineage_AllPhase5ReportsPreserveSourceShortChecksum()
    {
        var fixture = Phase5CertificationFixture.Create();
        var reports = new[] { fixture.Result.Validation.SourceShortChecksum, fixture.Result.SceneIntents.SourceShortChecksum,
            fixture.Result.Coverage.SourceShortChecksum, fixture.Result.Transitions.SourceShortChecksum,
            fixture.Result.PauseTest.SourceShortChecksum };
        Assert.All(reports, checksum => Assert.Equal(fixture.PublishedPhase4.ShortProjectionChecksum, checksum));
    }

    [Fact]
    public void Phase4ToPhase5Lineage_RejectsCompatibilityChecksumUsedAsSourceChecksum()
    {
        var fixture = Phase5CertificationFixture.Create();
        var incompatible = fixture.Result.Certification with {
            SourceLongBlueprintChecksum = fixture.Request.Long.Metadata.Checksum };
        incompatible = incompatible with { SemanticChecksum = DocumentaryBlueprintCertificationChecksum.Calculate(incompatible) };

        var errors = DocumentaryBlueprintCertificationArtifactValidator.Validate(
            fixture.Result with { Certification = incompatible }, fixture.Request);

        Assert.Contains("Certification source long blueprint checksum is stale.", errors);
    }
}
