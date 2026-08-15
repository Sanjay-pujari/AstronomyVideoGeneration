using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2InstagramPostVerificationTests
{
    private const string MediaId = "18124439758691190";
    private const string AccountId = "17841433998640998";
    private const string Username = "sanjaypujari25";

    [Fact]
    public async Task Provider_permalink_is_persisted_exactly_and_wins_over_stale_guessed_url()
    {
        await using var f = await Fixture.CreateAsync();
        const string authoritative = "https://www.instagram.com/p/PROVIDER_VALUE/";
        f.Row.RemoteUrl = "https://www.instagram.com/p/guessed/";
        f.Api.Responses.Enqueue(Media(authoritative));

        await f.VerifyAsync();

        Assert.Equal(authoritative, f.Row.RemoteUrl);
        Assert.True(f.Row.RemoteVerificationCompleted);
        Assert.Equal(Rc2PublicationState.Published, f.Row.Status);
        Assert.Equal(MediaId, f.Row.RemotePublicationId);
        Assert.Equal(0, f.Api.CreateCalls);
        Assert.Equal(0, f.Api.PublishCalls);
    }

    [Fact]
    public async Task Temporarily_null_permalink_is_polled_without_guessing_or_republishing()
    {
        await using var f = await Fixture.CreateAsync(attempts: 2);
        f.Row.RemoteUrl = "https://www.instagram.com/p/guessed/";
        f.Api.Responses.Enqueue(Media(null));
        f.Api.Responses.Enqueue(Media("https://www.instagram.com/p/authoritative/"));

        await f.VerifyAsync();

        Assert.Equal("https://www.instagram.com/p/authoritative/", f.Row.RemoteUrl);
        Assert.Equal(2, f.Api.GetCalls);
        Assert.Equal(0, f.Api.CreateCalls + f.Api.PublishCalls);
    }

    [Fact]
    public async Task Missing_permalink_after_bound_preserves_media_id_and_requires_reconciliation()
    {
        await using var f = await Fixture.CreateAsync(attempts: 1);
        f.Row.RemoteUrl = "https://www.instagram.com/p/guessed/";
        f.Api.Responses.Enqueue(Media(null));

        var error = await Assert.ThrowsAsync<Rc2PublishingControlException>(() => f.VerifyAsync());

        Assert.Equal("RC2_PUBLISH_AWAITING_PERMALINK", error.Code);
        Assert.Null(f.Row.RemoteUrl);
        Assert.Equal(MediaId, f.Row.RemotePublicationId);
        Assert.False(f.Row.RemoteVerificationCompleted);
        Assert.Equal(Rc2PublicationStep.PublishedRemote, f.Row.LastCompletedStep);
        Assert.Equal(0, f.Api.CreateCalls + f.Api.PublishCalls);
    }

    [Fact]
    public async Task Remote_query_error_fails_reconciliation_without_duplicate_publication()
    {
        await using var f = await Fixture.CreateAsync();
        f.Api.Error = new InvalidOperationException("Meta Graph failed: http=404; code=100; message=Unsupported get request");

        var error = await Assert.ThrowsAsync<Rc2PublishingControlException>(() => f.VerifyAsync());

        Assert.Equal("RC2_PUBLISH_REMOTE_VERIFICATION_FAILED", error.Code);
        Assert.Contains("http=404", error.Message);
        Assert.Equal(MediaId, f.Row.RemotePublicationId);
        Assert.Equal(0, f.Api.CreateCalls + f.Api.PublishCalls);
    }

    [Fact]
    public async Task Already_verified_published_row_makes_no_provider_calls()
    {
        await using var f = await Fixture.CreateAsync();
        f.Row.Status = Rc2PublicationState.Published;
        f.Row.RemoteVerificationCompleted = true;
        f.Row.RemoteUrl = "https://www.instagram.com/p/provider/";

        await f.VerifyAsync();

        Assert.Equal(0, f.Api.GetCalls + f.Api.CreateCalls + f.Api.PublishCalls);
        Assert.Equal(MediaId, f.Row.RemotePublicationId);
    }

    private static Rc2InstagramMedia Media(string? permalink) => new(MediaId, "IMAGE", "FEED", AccountId,
        Username, DateTimeOffset.Parse("2026-08-14T08:30:00Z"), permalink);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public MediaFactoryDbContext Db { get; }
        public FakeInstagramApi Api { get; }
        public Rc2PublishingExecutionService Service { get; }
        public Rc2PublishingPublication Row { get; }
        public Rc2PublishingPlan Plan { get; }

        private Fixture(SqliteConnection connection, MediaFactoryDbContext db, FakeInstagramApi api,
            Rc2PublishingExecutionService service, Rc2PublishingPublication row, Rc2PublishingPlan plan)
        { this.connection = connection; Db = db; Api = api; Service = service; Row = row; Plan = plan; }

        public static async Task<Fixture> CreateAsync(int attempts = 1)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            var plan = new Rc2PublishingPlan(Guid.NewGuid(), "Astronomy", "English", "US", ".", ".", ".", "validation.json");
            var row = new Rc2PublishingPublication { Id = Guid.NewGuid(), PlanId = plan.PlanId, PublishingPackageId = "package",
                Phase20AuthorityChecksum = "authority", Target = Rc2PublishingTarget.InstagramPost, RoleOrMediaType = "HeroPortrait",
                IdempotencyKey = Guid.NewGuid().ToString("N"), Status = Rc2PublicationState.Publishing, MediaPrepared = true,
                PublicMediaStaged = true, RemoteContainerId = "container", ContainerReady = true, PublishRequested = true,
                RemotePublicationId = MediaId, LastCompletedStep = Rc2PublicationStep.PublishedRemote,
                CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow };
            db.Add(row); await db.SaveChangesAsync();
            var api = new FakeInstagramApi();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Meta:ExpectedInstagramAccountId"] = AccountId, ["Meta:ExpectedInstagramUsername"] = Username }).Build();
            var service = new Rc2PublishingExecutionService(null!, null!, db, null!, null!, null!, null!, null!, null!, null!,
                Options.Create(new PublishingTargetsOptions()), Options.Create(new PublishingOptions()), Options.Create(new YouTubeOptions()),
                Options.Create(new MetaPublishingOptions { InstagramPermalinkPollAttempts = attempts, InstagramPermalinkPollDelaySeconds = 0 }),
                Options.Create(new PlatformPublishingOptions()), Options.Create(new PublicMediaStorageOptions()), config,
                NullLogger<Rc2PublishingExecutionService>.Instance, new FakeStorage(), api);
            return new(connection, db, api, service, row, plan);
        }

        public Task VerifyAsync() => Service.PublishInstagramPostAsync(Row, Plan,
            new PreparedProviderMedia("unused", "unused", "id", "hash", 1, 1080, 1350, "image/jpeg", "v1", "source"), CancellationToken.None);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class FakeInstagramApi : IRc2InstagramApiClient
    {
        public int CreateCalls, PublishCalls, GetCalls;
        public Exception? Error;
        public Queue<Rc2InstagramMedia?> Responses { get; } = new();
        public Task<string> CreateImageContainerAsync(string imageUrl, string caption, CancellationToken ct) { CreateCalls++; return Task.FromResult("container"); }
        public Task<string> GetContainerStatusAsync(string containerId, CancellationToken ct) => Task.FromResult("FINISHED");
        public Task<string> PublishContainerAsync(string containerId, CancellationToken ct) { PublishCalls++; return Task.FromResult(MediaId); }
        public Task<Rc2InstagramMedia?> GetMediaAsync(string mediaId, CancellationToken ct)
        { GetCalls++; if (Error is not null) throw Error; return Task.FromResult(Responses.Dequeue()); }
    }

    private sealed class FakeStorage : IPublicMediaStorageService
    {
        public Task<PublicMediaUploadResult> UploadForInstagramAsync(string path, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PublicMediaUploadResult> UploadPublicAssetAsync(string path, Guid id, string name, string type, CancellationToken ct) => throw new NotSupportedException();
    }
}
