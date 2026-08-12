namespace Astronomy.MediaFactory.Tests;

using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class Phase20PublishingAuthorityContractTests
{
    private static string Publisher => File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "Phase20PublishingAuthorityPublisher.cs"));

    [Fact]
    public void Closed_roles_cover_requested_media_and_promotional_assets()
    {
        var roles = File.ReadAllText(RepositoryTestPaths.CoreSource("Phase20PublishingAuthority.cs"));
        foreach (var role in new[] { "ShortVideo", "LongVideo", "ShortCaptionSrt", "ShortCaptionAss", "LongCaptionSrt", "LongCaptionAss",
                     "ThumbnailLandscape", "ThumbnailPortrait", "ThumbnailSquare", "HeroLandscape", "HeroPortrait", "HeroSquare", "GalleryImage" })
            Assert.Contains(role, roles);
    }

    [Fact]
    public void Supporting_authorities_are_loaded_only_for_requested_outputs()
    {
        Assert.Contains("if (requested.Contains(\"Thumbnail\"))", Publisher);
        Assert.Contains("if (requested.Contains(\"HeroAsset\"))", Publisher);
        Assert.Contains("if (requested.Contains(\"Gallery\"))", Publisher);
    }

    [Fact]
    public void Platform_map_keeps_video_hero_and_gallery_roles_distinct()
    {
        foreach (var target in new[] { "YouTubeLong", "YouTubeShort", "FacebookLong", "FacebookReel", "InstagramReel",
                     "InstagramPost", "InstagramCarousel", "FacebookPost", "FacebookCarousel" })
            Assert.Contains($"[\"{target}\"]", Publisher);
        Assert.Contains("HeroPortrait", Publisher);
        Assert.Contains("HeroLandscape", Publisher);
        Assert.Contains("OrderBy(x => x.Sequence)", Publisher);
    }

    [Fact]
    public void Phase20_is_reference_first_and_has_no_external_publisher_calls()
    {
        Assert.Contains("policy.PortableExportEnabled", Publisher);
        Assert.DoesNotContain("YouTubePublisher", Publisher);
        Assert.DoesNotContain("FacebookPublisher", Publisher);
        Assert.DoesNotContain("InstagramPublisher", Publisher);
        Assert.DoesNotContain("MetaPublisher", Publisher);
    }

    [Fact]
    public void Publication_is_staged_committed_and_read_back()
    {
        Assert.Contains("20-publishing\", \".staging", Publisher);
        Assert.Contains("Commit(stage, finalRoot", Publisher);
        Assert.Contains("CommittedReadbackFailed", Publisher);
        Assert.Contains("canonicalOwnedRoots", Publisher);
    }

    [Fact]
    public void Successful_publication_cleans_only_its_transaction_staging_and_backup()
    {
        Assert.Contains("DeleteCurrentTransaction(transactionRoot)", Publisher);
        Assert.Contains("DeleteCurrentTransaction(backupTransactionRoot)", Publisher);
        Assert.Contains("DeleteContainerIfEmpty(Path.Combine(outputRoot, \"20-publishing\", \".staging\"))", Publisher);
        Assert.Contains("DeleteContainerIfEmpty(Path.Combine(outputRoot, \"20-publishing\", \".backup\"))", Publisher);
        Assert.Contains("Directory.Move(stage, final)", Publisher);
    }

    [Fact]
    public async Task Empty_requested_outputs_fail_closed_without_committing_an_authority()
    {
        using var fixture = await Phase19Fixture.Create();
        var error = await Assert.ThrowsAsync<Phase20AuthorityException>(() => fixture.Publish());
        Assert.Equal(Phase20ReasonCodes.RequestedOutputsUnresolved, error.ReasonCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "20-publishing", "en")));
    }

    [Fact]
    public void Nonempty_candidate_with_no_entries_fails_as_empty()
    {
        var error = Assert.Throws<Phase20AuthorityException>(() =>
            Phase20PublishingAuthorityPublisher.ValidateCandidate(["HeroAsset"], []));
        Assert.Equal(Phase20ReasonCodes.PackageEmpty, error.ReasonCode);
    }

    [Fact]
    public void Partial_hero_candidate_fails_role_completeness()
    {
        var entry = new PublishingManifestEntry(PublishingPackageRole.HeroLandscape, "png", "en", 11,
            "authority", "11-hero/hero.png", null, "sha", 1, "image/png");
        var error = Assert.Throws<Phase20AuthorityException>(() =>
            Phase20PublishingAuthorityPublisher.ValidateCandidate(["HeroAsset"], [entry]));
        Assert.Equal(Phase20ReasonCodes.CandidateValidationFailed, error.ReasonCode);
        Assert.Contains("HeroPortrait", error.Message);
        Assert.Contains("HeroSquare", error.Message);
    }

    [Fact]
    public async Task Phase19_checksum_disagreement_has_precise_failure_and_loaded_inputs()
    {
        using var fixture = await Phase19Fixture.Create(validationChecksum: "different");
        var error = await Assert.ThrowsAsync<Phase20AuthorityException>(() => fixture.Publish(["ShortVideo"]));
        Assert.Equal(Phase20ReasonCodes.UpstreamPhase19Invalid, error.ReasonCode);
        Assert.Contains("validation authority checksum mismatch", error.Message);
        Assert.Equal(4, error.LoadedAuthorityArtifacts.Count);
    }

    [Fact]
    public async Task Missing_phase19_validation_does_not_fall_back_to_legacy_evidence()
    {
        using var fixture = await Phase19Fixture.Create(includeValidation: false);
        var error = await Assert.ThrowsAsync<Phase20AuthorityException>(() => fixture.Publish(["ShortVideo"]));
        Assert.Contains("evidence file missing: phase-19-validation.json", error.Message);
        Assert.Equal(3, error.LoadedAuthorityArtifacts.Count);
    }

    [Fact]
    public async Task Rejected_empty_candidate_leaves_all_phase19_evidence_byte_identical()
    {
        using var fixture = await Phase19Fixture.Create();
        var before = fixture.Evidence.ToDictionary(path => path, Sha256);
        await fixture.Publish();
        var after = fixture.Evidence.ToDictionary(path => path, Sha256);
        Assert.All(before, item => Assert.Equal(item.Value, after[item.Key]));
    }

    private sealed class Phase19Fixture : IDisposable
    {
        private const string Checksum = "certified-phase19-authority";
        private readonly string root;
        public string Root => root;
        public IReadOnlyList<string> Evidence { get; }
        private Phase19Fixture(string root, IReadOnlyList<string> evidence) { this.root = root; Evidence = evidence; }

        public static async Task<Phase19Fixture> Create(string? validationChecksum = null, bool includeValidation = true)
        {
            var root = Path.Combine(Path.GetTempPath(), "phase20-contract-" + Guid.NewGuid().ToString("N"));
            var authority = Path.Combine(root, "19-video-qa", "en");
            var validationRoot = Path.Combine(root, "validation");
            Directory.CreateDirectory(authority); Directory.CreateDirectory(validationRoot);
            var manifest = Path.Combine(authority, "phase19-manifest.json");
            await Write(manifest, new { schemaVersion = "phase19.video-qa/1.0", language = "en", sourcePhase18AuthorityChecksum = "p18",
                requestedFormats = Array.Empty<string>(), qaPolicyVersion = "test", durationValidationMode = "test", outputs = Array.Empty<object>(),
                authorityChecksum = Checksum, technicalQaApproved = true, publicationCommitted = true, validationStatus = "Valid", downstreamReady = true,
                publishApproved = false, phase19ReviewApproved = false, publishGateChecked = false });
            var common = new { authorityChecksum = Checksum, publicationCommitted = true, committedReadbackPassed = true,
                committedStateValidationPassed = true, semanticValidationPassed = true, checksumValidationPassed = true,
                manifestValidationPassed = true, validationStatus = "Valid", downstreamReady = true };
            var diagnostics = Path.Combine(authority, "phase19-authority-diagnostics.json");
            var report = Path.Combine(authority, "phase19-publication-report.json");
            await Write(diagnostics, common); await Write(report, common);
            var validation = Path.Combine(validationRoot, "phase-19-validation.json");
            if (includeValidation) await Write(validation, new { authorityChecksum = validationChecksum ?? Checksum, publicationCommitted = true,
                committedReadbackPassed = true, committedStateValidationPassed = true, semanticValidationPassed = true,
                checksumValidationPassed = true, manifestValidationPassed = true, manifestValidationStatus = "Valid",
                validationStatus = "Valid", technicalQaApproved = true, downstreamReady = true });
            return new(root, new[] { manifest, diagnostics, report, validation }.Where(File.Exists).ToArray());
        }

        public Task<IReadOnlyList<string>> Publish(IReadOnlyList<string>? requested = null) => Phase20PublishingAuthorityPublisher.ExecuteAsync(root, Guid.NewGuid(), "en", requested ?? [], true,
            false, new PublishingOptions { ManualReviewRequired = true }, NullLogger.Instance, CancellationToken.None);
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private static Task Write(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value));
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
