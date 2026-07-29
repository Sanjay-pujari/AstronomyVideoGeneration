using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class StoryFramePhase6ManifestSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"story-frame-security-{Guid.NewGuid():N}");
    private const string DirectoryName = "06-story-frames";
    private const string FileName = "story-frames.json";

    [Fact]
    public void Valid_canonical_manifest_path_is_accepted()
    {
        var candidate = Path.Combine(_root, DirectoryName, FileName);
        Assert.True(StoryFramePathSecurity.IsCanonicalContainedPath(_root, candidate, DirectoryName, FileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("story-frames.json:evil")]
    [InlineData("../06-story-frames/story-frames.json")]
    public void Invalid_or_malformed_relative_paths_are_rejected(string candidate)
        => Assert.False(StoryFramePathSecurity.IsCanonicalContainedPath(_root, candidate, DirectoryName, FileName));

    [Fact]
    public void Root_prefix_collision_is_rejected()
    {
        var candidate = Path.Combine(_root + "-malicious", DirectoryName, FileName);
        Assert.False(StoryFramePathSecurity.IsCanonicalContainedPath(_root, candidate, DirectoryName, FileName));
    }

    [Fact]
    public void Another_plan_root_is_rejected()
    {
        var candidate = Path.Combine(Path.GetDirectoryName(_root)!, "another-plan", DirectoryName, FileName);
        Assert.False(StoryFramePathSecurity.IsCanonicalContainedPath(_root, candidate, DirectoryName, FileName));
    }

    [Theory]
    [InlineData(".06-story-frames-staging-123")]
    [InlineData(".06-story-frames-backup-123")]
    [InlineData("wrong-parent")]
    public void Noncanonical_parent_is_rejected(string parent)
    {
        var candidate = Path.Combine(_root, parent, FileName);
        Assert.False(StoryFramePathSecurity.IsCanonicalContainedPath(_root, candidate, DirectoryName, FileName));
    }

    [Fact]
    public void Wrong_filename_and_ads_are_rejected()
    {
        Assert.False(StoryFramePathSecurity.IsCanonicalContainedPath(_root,
            Path.Combine(_root, DirectoryName, "wrong.json"), DirectoryName, FileName));
        Assert.False(StoryFramePathSecurity.IsCanonicalContainedPath(_root,
            Path.Combine(_root, DirectoryName, FileName + ":evil"), DirectoryName, FileName));
    }

    [Fact]
    public void Foreign_unc_path_is_rejected()
        => Assert.False(StoryFramePathSecurity.IsCanonicalContainedPath(_root,
            @"\\foreign\share\06-story-frames\story-frames.json", DirectoryName, FileName));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
