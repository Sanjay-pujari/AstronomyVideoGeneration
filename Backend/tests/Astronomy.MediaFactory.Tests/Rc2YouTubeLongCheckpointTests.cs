using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2YouTubeLongCheckpointTests
{
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
    public async Task Live_execution_checkpoints_each_remote_operation_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();

        var response = await fixture.PublishAsync();
        var row = await fixture.ReloadAsync();

        Assert.Equal("Succeeded", response.OverallStatus);
        Assert.Equal(Rc2PublicationState.Published, row.Status);
        Assert.Equal("TEST_VIDEO_123", row.RemotePublicationId);
        Assert.Equal("https://www.youtube.com/watch?v=TEST_VIDEO_123", row.RemoteUrl);
        Assert.NotNull(row.VideoCreatedUtc);
        Assert.True(row.VideoUploadCompleted);
        Assert.True(row.ThumbnailCompleted);
        Assert.True(row.CaptionCompleted);
        Assert.True(row.RemoteVerificationCompleted);
        Assert.Equal(Rc2PublicationStep.RemoteVerified, row.LastCompletedStep);
        Assert.Equal(["video", "thumbnail", "caption", "verify"], fixture.Api.Calls);
        Assert.Equal("TEST_VIDEO_123", fixture.Api.CaptionVideoId);
        Assert.Equal(fixture.CaptionPath, fixture.Api.CaptionPath);
        Assert.Equal("en", fixture.Api.CaptionLanguage);
        Assert.Equal("English", fixture.Api.CaptionName);
        Assert.True(fixture.Auth.ForceRefresh);

        var repeated = await fixture.PublishAsync();
        Assert.Equal(Rc2PublicationState.AlreadyPublished, repeated.Results.Single().PublicationState);
        Assert.True(repeated.Results.Single().Reused);
        Assert.Equal(1, fixture.Api.VideoCalls);
    }

    [Fact]
    public async Task Crash_after_video_checkpoint_resumes_without_duplicate_create()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.ThumbnailFailures = 1;

        var failed = await fixture.PublishAsync();
        var checkpoint = await fixture.ReloadAsync();
        Assert.Equal(Rc2PublicationState.Failed, failed.Results.Single().PublicationState);
        Assert.Equal("TEST_VIDEO_123", checkpoint.RemotePublicationId);
        Assert.True(checkpoint.VideoUploadCompleted);
        Assert.False(checkpoint.ThumbnailCompleted);
        Assert.Equal(Rc2PublicationStep.VideoCreated, checkpoint.LastCompletedStep);

        var resumed = await fixture.PublishAsync();
        var completed = await fixture.ReloadAsync();
        Assert.Equal(Rc2PublicationState.Published, resumed.Results.Single().PublicationState);
        Assert.Equal(1, fixture.Api.VideoCalls);
        Assert.Equal(2, fixture.Api.ThumbnailCalls);
        Assert.Equal(1, fixture.Api.CaptionCalls);
        Assert.Equal(Rc2PublicationStep.RemoteVerified, completed.LastCompletedStep);
    }

    [Fact]
    public async Task Caption_failure_resumes_caption_only_after_durable_thumbnail_checkpoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.CaptionFailures = 1;

        await fixture.PublishAsync();
        var checkpoint = await fixture.ReloadAsync();
        Assert.True(checkpoint.ThumbnailCompleted);
        Assert.False(checkpoint.CaptionCompleted);
        Assert.Equal(Rc2PublicationStep.ThumbnailCompleted, checkpoint.LastCompletedStep);

        await fixture.PublishAsync();
        Assert.Equal(1, fixture.Api.VideoCalls);
        Assert.Equal(1, fixture.Api.ThumbnailCalls);
        Assert.Equal(2, fixture.Api.CaptionCalls);
        Assert.Equal(1, fixture.Api.VerificationCalls);
    }

    [Fact]
    public async Task Verification_failure_resumes_verification_only_and_published_gate_stays_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.VerificationFailures = 1;

        await fixture.PublishAsync();
        var checkpoint = await fixture.ReloadAsync();
        Assert.Equal(Rc2PublicationState.Failed, checkpoint.Status);
        Assert.True(checkpoint.CaptionCompleted);
        Assert.False(checkpoint.RemoteVerificationCompleted);
        Assert.Equal(Rc2PublicationStep.CaptionCompleted, checkpoint.LastCompletedStep);

        await fixture.PublishAsync();
        Assert.Equal(1, fixture.Api.VideoCalls);
        Assert.Equal(1, fixture.Api.ThumbnailCalls);
        Assert.Equal(1, fixture.Api.CaptionCalls);
        Assert.Equal(2, fixture.Api.VerificationCalls);
        Assert.Equal(Rc2PublicationState.Published, (await fixture.ReloadAsync()).Status);
    }

    [Fact]
    public async Task Ambiguous_create_is_fail_closed_and_never_automatically_replayed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Api.ReturnUnconfirmedVideoId = true;

        var first = await fixture.PublishAsync();
        var second = await fixture.PublishAsync();

        Assert.Equal("RC2_PUBLISH_REMOTE_OUTCOME_UNKNOWN", first.Results.Single().FailureCode);
        Assert.Equal(Rc2PublicationState.Blocked, second.Results.Single().PublicationState);
        Assert.Equal("RC2_PUBLISH_REMOTE_OUTCOME_UNKNOWN", second.Results.Single().FailureCode);
        Assert.Equal(1, fixture.Api.VideoCalls);
        Assert.Equal(0, fixture.Api.ThumbnailCalls);
    }

    [Fact]
    public async Task Dry_run_does_not_claim_a_publication_or_invoke_remote_provider()
    {
        await using var fixture = await Fixture.CreateAsync();

        var response = await fixture.PublishAsync(dryRun: true);

        Assert.Equal("Succeeded", response.OverallStatus);
        Assert.Equal(Rc2PublicationState.NotPublished, response.Results.Single().PublicationState);
        Assert.True(response.Results.Single().DryRunPassed);
        Assert.Equal(0, response.Results.Single().AttemptCount);
        Assert.Empty(await fixture.Db.Rc2PublishingPublications.ToListAsync());
        Assert.Empty(fixture.Api.Calls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly Rc2PublishingExecutionService _service;
        public MediaFactoryDbContext Db { get; }
        public FakeYouTubeApi Api { get; } = new();
        public FakeAuth Auth { get; } = new();
        public string CaptionPath { get; }

        private Fixture(string root, MediaFactoryDbContext db, Rc2PublishingPlan plan,
            Phase20PublishingAuthoritySnapshot authority)
        {
            _root = root;
            Db = db;
            CaptionPath = Path.Combine(root, "caption.srt");
            var youtube = new YouTubeOptions { PublishingEnabled = true, ExpectedChannelId = FakeYouTubeApi.ChannelId, CategoryId = "28" };
            _service = new Rc2PublishingExecutionService(new Resolver(plan), new AuthorityReader(authority), db,
                new HealthyTokens(), new NullPublishers(), Auth, Api, new NullPublishers(), new NullPublishers(),
                new NullPublishers(), Options.Create(new PublishingTargetsOptions { YouTubeLong = true }),
                Options.Create(new PublishingOptions { Enabled = true, Mode = "Private", DefaultPrivacyStatus = "private" }),
                Options.Create(youtube), Options.Create(new MetaPublishingOptions()), Options.Create(new PlatformPublishingOptions()),
                Options.Create(new PublicMediaStorageOptions()), new ConfigurationBuilder().Build(),
                NullLogger<Rc2PublishingExecutionService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"rc2-youtube-cert-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var artifacts = new List<Phase20PublishingArtifact>();
            foreach (var item in new[] { ("LongVideo", "video.mp4"), ("ThumbnailLandscape", "thumbnail.jpg"), ("LongCaptionSrt", "caption.srt") })
            {
                var path = Path.Combine(root, item.Item2);
                await File.WriteAllTextAsync(path, item.Item1);
                var bytes = await File.ReadAllBytesAsync(path);
                artifacts.Add(new(item.Item1, item.Item2, bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), null));
            }
            var plan = new Rc2PublishingPlan(Guid.NewGuid(), "Certified astronomy title", "English", "global", root, root, root, "validation.json");
            var authority = new Phase20PublishingAuthoritySnapshot("package-1", "checksum-1", "Committed", true, true, 3,
                artifacts.GroupBy(x => x.Role).ToDictionary(x => x.Key, x => x.Count()), [Rc2PublishingTarget.YouTubeLong], artifacts);
            var options = new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var db = new MediaFactoryDbContext(options);
            db.Rc2PublishingApprovals.Add(new Rc2PublishingApproval { Id = Guid.NewGuid(), PlanId = plan.PlanId,
                PublishingPackageId = authority.PublishingPackageId, Phase20AuthorityChecksum = authority.AuthorityChecksum,
                Decision = Rc2PublishingApprovalStatus.Approved, DecisionSource = "certification", CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow, DecisionUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return new Fixture(root, db, plan, authority);
        }

        public Task<Rc2PublishingExecutionResponse> PublishAsync(bool dryRun = false) => _service.PublishVideoAsync(
            new Rc2PublishVideoRequest(Db.Rc2PublishingApprovals.Single().PlanId, [Rc2PublishingTarget.YouTubeLong], Rc2PublishMode.Now, dryRun), default);
        public async Task<Rc2PublishingPublication> ReloadAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Rc2PublishingPublications.SingleAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); Directory.Delete(_root, true); }
    }

    private sealed class Resolver(Rc2PublishingPlan plan) : IRc2PublishingPlanResolver
    { public Task<Rc2PublishingPlan> ResolveAsync(Guid planId, CancellationToken ct) => Task.FromResult(plan); }
    private sealed class AuthorityReader(Phase20PublishingAuthoritySnapshot authority) : IPhase20PublishingAuthorityReader
    { public Task<Phase20PublishingAuthoritySnapshot?> ReadAsync(Rc2PublishingPlan plan, CancellationToken ct) => Task.FromResult<Phase20PublishingAuthoritySnapshot?>(authority); }
    private sealed class HealthyTokens : ITokenHealthService
    {
        public Task<TokenHealthResult> CheckYouTubeAsync(CancellationToken ct) => Task.FromResult(new TokenHealthResult { Platform = "YouTube", IsConfigured = true, IsValid = true, Status = "Healthy" });
        public Task<TokenHealthResult> CheckMetaAsync(CancellationToken ct) => CheckYouTubeAsync(ct);
        public async Task<IReadOnlyList<TokenHealthResult>> CheckAllAsync(CancellationToken ct) => [await CheckYouTubeAsync(ct)];
    }
    private sealed class FakeAuth : IYouTubeAuthService
    {
        public bool ForceRefresh { get; private set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult("token");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) { ForceRefresh = forceRefresh; return Task.FromResult("refreshed-token"); }
    }
    private sealed class FakeYouTubeApi : IYouTubeApiClient
    {
        public const string ChannelId = "ASTROPULSE_TEST_CHANNEL";
        public List<string> Calls { get; } = [];
        public int VideoCalls { get; private set; }
        public int ThumbnailCalls { get; private set; }
        public int CaptionCalls { get; private set; }
        public int VerificationCalls { get; private set; }
        public int ThumbnailFailures { get; set; }
        public int CaptionFailures { get; set; }
        public int VerificationFailures { get; set; }
        public bool ReturnUnconfirmedVideoId { get; set; }
        public string? CaptionVideoId { get; private set; }
        public string? CaptionPath { get; private set; }
        public string? CaptionLanguage { get; private set; }
        public string? CaptionName { get; private set; }
        public Task<YouTubeChannelInfo> GetAuthenticatedChannelAsync(string token, CancellationToken ct) =>
            Task.FromResult(new YouTubeChannelInfo { ChannelId = ChannelId, ChannelTitle = "AstroPulse" });
        public Task<string> UploadVideoAsync(PublishRequest request, string token, CancellationToken ct)
        { VideoCalls++; Calls.Add("video"); return Task.FromResult(ReturnUnconfirmedVideoId ? "" : "TEST_VIDEO_123"); }
        public Task UploadThumbnailAsync(string id, string path, string token, CancellationToken ct)
        { ThumbnailCalls++; Calls.Add("thumbnail"); if (ThumbnailFailures-- > 0) throw new InvalidOperationException("simulated crash after video checkpoint"); return Task.CompletedTask; }
        public Task UploadCaptionAsync(string id, string path, string language, string name, string token, CancellationToken ct)
        { CaptionCalls++; Calls.Add("caption"); CaptionVideoId = id; CaptionPath = path; CaptionLanguage = language; CaptionName = name; if (CaptionFailures-- > 0) throw new InvalidOperationException("caption failed"); return Task.CompletedTask; }
        public Task<YouTubeVideoPostUploadStatus?> GetVideoPostUploadStatusAsync(string id, string token, CancellationToken ct)
        { VerificationCalls++; Calls.Add("verify"); if (VerificationFailures-- > 0) return Task.FromResult<YouTubeVideoPostUploadStatus?>(null); return Task.FromResult<YouTubeVideoPostUploadStatus?>(new() { VideoId = id, ChannelId = ChannelId, PrivacyStatus = "private", UploadStatus = "processed" }); }
    }
    private sealed class NullPublishers : IYouTubePublishService, IFacebookVideoPublishService, IFacebookReelPublishService, IInstagramReelPublishService
    {
        public string PlatformName => "unused";
        public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<MetaPublishResult> PublishVideoAsync(MetaPublishRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<MetaPublishResult> PublishReelAsync(MetaPublishRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}
