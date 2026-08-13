using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2YouTubeLongCheckpointTests
{
    private const string VideoId = "TEST_VIDEO_123";

    [Theory]
    [InlineData("English", "en", "English")]
    [InlineData("en", "en", "English")]
    [InlineData("Spanish", "es", "Spanish")]
    public void Caption_language_is_deterministically_mapped_from_plan(string input, string code, string name) =>
        Assert.Equal((code, name), Rc2PublishingExecutionService.ResolveCaptionLanguage(input));

    [Fact]
    public void Unknown_caption_language_fails_closed() =>
        Assert.Throws<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ResolveCaptionLanguage("unknown"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_title_fails_closed(string title) =>
        Assert.Throws<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ValidateYouTubeMetadata(title, "private", "28"));

    [Fact]
    public void Provider_title_limit_is_enforced() =>
        Assert.Throws<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ValidateYouTubeMetadata(new string('a', 101), "private", "28"));

    [Theory]
    [InlineData("friends", "28")]
    [InlineData("private", "science")]
    public void Invalid_provider_metadata_fails_closed(string privacy, string category) =>
        Assert.Throws<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ValidateYouTubeMetadata("title", privacy, category));

    [Fact]
    public void Valid_provider_metadata_passes() =>
        Rc2PublishingExecutionService.ValidateYouTubeMetadata("title", "private", "28");

    [Fact]
    public async Task Video_create_is_durably_checkpointed_before_thumbnail_and_retry_does_not_recreate_video()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.BeforeThumbnail = async () =>
        {
            fixture.Db.ChangeTracker.Clear();
            var saved = await fixture.Db.Rc2PublishingPublications.SingleAsync();
            Assert.Equal(VideoId, saved.RemotePublicationId);
            Assert.Equal($"https://www.youtube.com/watch?v={VideoId}", saved.RemoteUrl);
            Assert.True(saved.VideoUploadCompleted);
            Assert.NotNull(saved.VideoCreatedUtc);
            Assert.Equal(Rc2PublicationStep.VideoCreated, saved.LastCompletedStep);
            throw new SimulatedCrashException();
        };

        await Assert.ThrowsAsync<SimulatedCrashException>(() => fixture.PublishAsync());
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(VideoId, (await fixture.Db.Rc2PublishingPublications.SingleAsync()).RemotePublicationId);

        fixture.Api.BeforeThumbnail = null;
        await fixture.PublishAsync();

        var completed = await fixture.Db.Rc2PublishingPublications.SingleAsync();
        Assert.Equal(1, fixture.Api.VideoInsertCalls);
        Assert.Equal(2, fixture.Api.ThumbnailCalls);
        Assert.Equal(1, fixture.Api.CaptionCalls);
        Assert.Equal(1, fixture.Api.VerificationCalls);
        Assert.True(completed.ThumbnailCompleted);
        Assert.True(completed.CaptionCompleted);
        Assert.True(completed.RemoteVerificationCompleted);
        Assert.Equal(Rc2PublicationStep.RemoteVerified, completed.LastCompletedStep);
        Assert.Equal(Rc2PublicationState.Published, completed.Status);
    }

    [Fact]
    public async Task Caption_receives_governed_long_srt_language_and_resumes_without_duplicate_video_or_thumbnail()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.CaptionFailuresRemaining = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PublishAsync());
        fixture.Db.ChangeTracker.Clear();
        var checkpoint = await fixture.Db.Rc2PublishingPublications.SingleAsync();
        Assert.True(checkpoint.VideoUploadCompleted);
        Assert.True(checkpoint.ThumbnailCompleted);
        Assert.False(checkpoint.CaptionCompleted);
        Assert.Equal(Rc2PublicationStep.ThumbnailCompleted, checkpoint.LastCompletedStep);

        await fixture.PublishAsync();

        Assert.Equal(1, fixture.Api.VideoInsertCalls);
        Assert.Equal(1, fixture.Api.ThumbnailCalls);
        Assert.Equal(2, fixture.Api.CaptionCalls);
        Assert.Equal(VideoId, fixture.Api.CaptionVideoId);
        Assert.Equal(fixture.CaptionPath, fixture.Api.CaptionPath);
        Assert.Equal("en", fixture.Api.CaptionLanguage);
        Assert.Equal("English", fixture.Api.CaptionName);
        Assert.Contains("Astronomy caption", await File.ReadAllTextAsync(fixture.Api.CaptionPath!));
    }

    [Fact]
    public async Task Failed_checkpoint_with_confirmed_video_skips_video_and_thumbnail_and_resumes_caption_only()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Row.Status = Rc2PublicationState.Failed;
        fixture.Row.RemotePublicationId = "B7gsJ2toKps";
        fixture.Row.RemoteUrl = "https://www.youtube.com/watch?v=B7gsJ2toKps";
        fixture.Row.VideoUploadCompleted = true;
        fixture.Row.ThumbnailCompleted = true;
        fixture.Row.LastCompletedStep = Rc2PublicationStep.ThumbnailCompleted;
        await fixture.Db.SaveChangesAsync();

        await fixture.PublishAsync();

        Assert.Equal(0, fixture.Api.VideoInsertCalls);
        Assert.Equal(0, fixture.Api.ThumbnailCalls);
        Assert.Equal(1, fixture.Api.CaptionCalls);
        Assert.Equal("B7gsJ2toKps", fixture.Api.CaptionVideoId);
        Assert.Equal(1, fixture.Api.VerificationCalls);
        Assert.Equal(Rc2PublicationState.Published, (await fixture.Db.Rc2PublishingPublications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Failure_message_longer_than_legacy_limit_persists_without_changing_checkpoints()
    {
        await using var fixture = await Fixture.CreateAsync();
        var diagnostic = "caption processing failed: " + new string('x', 2_000);
        fixture.Row.Status = Rc2PublicationState.Failed;
        fixture.Row.RemotePublicationId = VideoId;
        fixture.Row.VideoUploadCompleted = true;
        fixture.Row.ThumbnailCompleted = true;
        fixture.Row.FailureCode = "RC2_PUBLISH_PROVIDER_FAILED";
        fixture.Row.FailureMessage = diagnostic;

        await fixture.Service.PersistFailureAsync(fixture.Row, CancellationToken.None);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Rc2PublishingPublications.SingleAsync();
        Assert.Equal(diagnostic, saved.FailureMessage);
        Assert.Equal(Rc2PublicationState.Failed, saved.Status);
        Assert.Equal(VideoId, saved.RemotePublicationId);
        Assert.True(saved.VideoUploadCompleted && saved.ThumbnailCompleted);
    }

    [Fact]
    public void Pathological_provider_diagnostic_is_redacted_bounded_and_api_response_is_concise()
    {
        var diagnostic = "status=400 reason=captionFailed Authorization: Bearer top-secret " +
            "access_token=also-secret client_secret=never-log " + new string('z', 100_000);

        var stored = Rc2PublishingExecutionService.NormalizeProviderFailure(diagnostic);
        var api = Rc2PublishingExecutionService.ConciseApiFailureMessage(stored)!;

        Assert.Contains("status=400 reason=captionFailed", stored);
        Assert.DoesNotContain("top-secret", stored);
        Assert.DoesNotContain("also-secret", stored);
        Assert.DoesNotContain("never-log", stored);
        Assert.True(stored.Length <= Rc2PublishingExecutionService.MaximumStoredProviderDiagnosticLength + 23);
        Assert.True(api.Length <= Rc2PublishingExecutionService.MaximumApiFailureMessageLength + 20);
    }

    [Fact]
    public async Task Detailed_failure_save_falls_back_without_losing_remote_checkpoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_large_failure BEFORE UPDATE ON rc2_publishing_publications
            WHEN length(NEW.FailureMessage) > 1024
            BEGIN SELECT RAISE(ABORT, 'simulated diagnostic serialization failure'); END;
            """);
        fixture.Row.Status = Rc2PublicationState.Failed;
        fixture.Row.RemotePublicationId = VideoId;
        fixture.Row.RemoteUrl = $"https://www.youtube.com/watch?v={VideoId}";
        fixture.Row.VideoUploadCompleted = true;
        fixture.Row.ThumbnailCompleted = true;
        fixture.Row.LastCompletedStep = Rc2PublicationStep.ThumbnailCompleted;
        fixture.Row.FailureCode = "RC2_PUBLISH_PROVIDER_FAILED";
        fixture.Row.FailureMessage = new string('d', 2_000);

        await fixture.Service.PersistFailureAsync(fixture.Row, CancellationToken.None);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Rc2PublishingPublications.SingleAsync();
        Assert.Equal(Rc2PublishingExecutionService.FailureDiagnosticFallback, saved.FailureMessage);
        Assert.Equal(Rc2PublicationState.Failed, saved.Status);
        Assert.Equal(VideoId, saved.RemotePublicationId);
        Assert.True(saved.VideoUploadCompleted && saved.ThumbnailCompleted);
        Assert.False(saved.CaptionCompleted || saved.RemoteVerificationCompleted);
        Assert.Equal(Rc2PublicationStep.ThumbnailCompleted, saved.LastCompletedStep);
    }

    [Fact]
    public async Task Caption_validation_rejects_invalid_utf8_and_invalid_srt_structure()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [0xff]);
            await Assert.ThrowsAsync<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ValidateSrtAsync(path, CancellationToken.None));
            await File.WriteAllTextAsync(path, "caption-body");
            await Assert.ThrowsAsync<Rc2PublishingControlException>(() => Rc2PublishingExecutionService.ValidateSrtAsync(path, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Remote_verification_retries_alone_and_published_gate_requires_every_checkpoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.VerificationFailuresRemaining = 1;

        await Assert.ThrowsAsync<Rc2PublishingControlException>(() => fixture.PublishAsync());
        fixture.Db.ChangeTracker.Clear();
        var checkpoint = await fixture.Db.Rc2PublishingPublications.SingleAsync();
        Assert.True(checkpoint.VideoUploadCompleted && checkpoint.ThumbnailCompleted && checkpoint.CaptionCompleted);
        Assert.False(checkpoint.RemoteVerificationCompleted);
        Assert.NotEqual(Rc2PublicationState.Published, checkpoint.Status);

        await fixture.PublishAsync();

        Assert.Equal(1, fixture.Api.VideoInsertCalls);
        Assert.Equal(1, fixture.Api.ThumbnailCalls);
        Assert.Equal(1, fixture.Api.CaptionCalls);
        Assert.Equal(2, fixture.Api.VerificationCalls);
        Assert.Equal(VideoId, fixture.Api.VerifiedVideoId);
        Assert.Equal(Rc2PublicationState.Published, (await fixture.Db.Rc2PublishingPublications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Unconfirmed_video_create_fails_closed_without_supplementary_calls()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.ReturnUnconfirmedCreate = true;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => fixture.PublishAsync());

        Assert.Contains("confirmed video ID", error.Message);
        Assert.Equal(1, fixture.Api.VideoInsertCalls);
        Assert.Equal(0, fixture.Api.ThumbnailCalls);
        Assert.Equal(0, fixture.Api.CaptionCalls);
        Assert.Equal(0, fixture.Api.VerificationCalls);
        Assert.False((await fixture.Db.Rc2PublishingPublications.SingleAsync()).VideoUploadCompleted);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string root;
        public MediaFactoryDbContext Db { get; }
        public FakeYouTubeApi Api { get; }
        public Rc2PublishingExecutionService Service { get; }
        public Rc2PublishingPlan Plan { get; }
        public Rc2PublishingPublication Row { get; }
        public IReadOnlyList<Phase20PublishingArtifact> Artifacts { get; }
        public string CaptionPath => Path.Combine(root, "long.srt");

        private Fixture(SqliteConnection connection, string root, MediaFactoryDbContext db, FakeYouTubeApi api,
            Rc2PublishingExecutionService service, Rc2PublishingPlan plan, Rc2PublishingPublication row,
            IReadOnlyList<Phase20PublishingArtifact> artifacts)
        { this.connection = connection; this.root = root; Db = db; Api = api; Service = service; Plan = plan; Row = row; Artifacts = artifacts; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            var root = Path.Combine(Path.GetTempPath(), "rc2-youtube-cert-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "long.mp4"), "video");
            await File.WriteAllTextAsync(Path.Combine(root, "landscape.jpg"), "thumbnail");
            await File.WriteAllTextAsync(Path.Combine(root, "long.srt"), "1\n00:00:00,000 --> 00:00:02,000\nAstronomy caption\n");
            var plan = new Rc2PublishingPlan(Guid.NewGuid(), "Certified astronomy title", "English", "US", root, root, root, "validation.json");
            var row = new Rc2PublishingPublication { Id = Guid.NewGuid(), PlanId = plan.PlanId, PublishingPackageId = "package",
                Phase20AuthorityChecksum = "authority", Target = Rc2PublishingTarget.YouTubeLong, RoleOrMediaType = "roles",
                IdempotencyKey = Guid.NewGuid().ToString("N"), Status = Rc2PublicationState.Publishing,
                CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow };
            db.Rc2PublishingPublications.Add(row);
            await db.SaveChangesAsync();
            var artifacts = new[] { Artifact("LongVideo", "long.mp4"), Artifact("ThumbnailLandscape", "landscape.jpg"), Artifact("LongCaptionSrt", "long.srt") };
            var api = new FakeYouTubeApi();
            var youtubeOptions = Options.Create(new YouTubeOptions { ExpectedChannelId = "ASTROPULSE_CHANNEL", CategoryId = "28" });
            var service = new Rc2PublishingExecutionService(null!, null!, db, null!, null!, new FakeAuth(), api,
                null!, null!, null!, Options.Create(new PublishingTargetsOptions()), Options.Create(new PublishingOptions { Mode = "Private" }),
                youtubeOptions, Options.Create(new MetaPublishingOptions()), Options.Create(new PlatformPublishingOptions()),
                Options.Create(new PublicMediaStorageOptions()), new ConfigurationBuilder().Build(), NullLogger<Rc2PublishingExecutionService>.Instance);
            return new(connection, root, db, api, service, plan, row, artifacts);
        }

        public async Task PublishAsync()
        {
            Db.ChangeTracker.Clear();
            var row = await Db.Rc2PublishingPublications.SingleAsync();
            await Service.PublishYouTubeLongAsync(row, Plan, Artifacts, CancellationToken.None);
            await Db.SaveChangesAsync();
        }

        private static Phase20PublishingArtifact Artifact(string role, string path) => new(role, path, 1, "unused", null);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); Directory.Delete(root, true); }
    }

    private sealed class FakeAuth : IYouTubeAuthService
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("mock-token");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        { Assert.True(forceRefresh); return Task.FromResult("mock-refreshed-token"); }
    }

    private sealed class FakeYouTubeApi : IYouTubeApiClient
    {
        public int VideoInsertCalls, ThumbnailCalls, CaptionCalls, VerificationCalls, CaptionFailuresRemaining, VerificationFailuresRemaining;
        public bool ReturnUnconfirmedCreate;
        public Func<Task>? BeforeThumbnail;
        public string? CaptionVideoId, CaptionPath, CaptionLanguage, CaptionName, VerifiedVideoId;
        public Task<YouTubeChannelInfo> GetAuthenticatedChannelAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(new YouTubeChannelInfo { ChannelId = "ASTROPULSE_CHANNEL", ChannelTitle = "AstroPulse" });
        public Task<string> UploadVideoAsync(PublishRequest request, string accessToken, CancellationToken cancellationToken)
        { VideoInsertCalls++; return Task.FromResult(ReturnUnconfirmedCreate ? "" : VideoId); }
        public async Task UploadThumbnailAsync(string videoId, string thumbnailPath, string accessToken, CancellationToken cancellationToken)
        { ThumbnailCalls++; if (BeforeThumbnail is not null) await BeforeThumbnail(); }
        public Task UploadCaptionAsync(string videoId, string captionPath, string language, string name, string accessToken, CancellationToken cancellationToken)
        { CaptionCalls++; CaptionVideoId = videoId; CaptionPath = captionPath; CaptionLanguage = language; CaptionName = name;
          if (CaptionFailuresRemaining-- > 0) throw new InvalidOperationException("simulated caption failure"); return Task.CompletedTask; }
        public Task<YouTubeVideoPostUploadStatus?> GetVideoPostUploadStatusAsync(string videoId, string accessToken, CancellationToken cancellationToken)
        { VerificationCalls++; VerifiedVideoId = videoId; if (VerificationFailuresRemaining-- > 0) return Task.FromResult<YouTubeVideoPostUploadStatus?>(null);
          return Task.FromResult<YouTubeVideoPostUploadStatus?>(new() { VideoId = videoId, ChannelId = "ASTROPULSE_CHANNEL", PrivacyStatus = "private", UploadStatus = "uploaded" }); }
    }

    private sealed class SimulatedCrashException : Exception { }
}
