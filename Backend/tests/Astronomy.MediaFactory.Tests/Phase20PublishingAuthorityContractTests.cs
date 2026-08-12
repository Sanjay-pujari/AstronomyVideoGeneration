namespace Astronomy.MediaFactory.Tests;

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
}
