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

public sealed class Rc2FacebookPostVerificationTests
{
    private const string PhotoId = "TEST_FB_REMOTE_123";
    private const string PostId = "1135323479659435_456";
    private const string PageId = "1135323479659435";
    private const string PageName = "AstroPulse";
    private const string Permalink = "https://www.facebook.com/...provider-returned...";

    [Fact]
    public async Task Success_checkpoints_both_ids_and_exact_provider_permalink()
    {
        await using var f = await Fixture.CreateAsync();
        f.Api.QueryResponses.Enqueue(Photo());

        await f.PublishAsync();

        Assert.Equal(PhotoId, f.Row.RemotePublicationId);
        Assert.Equal(PostId, f.Row.RemotePostId);
        Assert.Equal(Permalink, f.Row.RemoteUrl);
        Assert.True(f.Row.RemoteVerificationCompleted);
        Assert.Equal(Rc2PublicationState.Published, f.Row.Status);
        Assert.Equal(1, f.Api.CreateCalls);
    }

    [Fact]
    public async Task Crash_after_create_resumes_verification_without_second_create()
    {
        await using var f = await Fixture.CreateAsync();
        f.Api.QueryError = new InvalidOperationException("simulated crash after durable create checkpoint");

        await Assert.ThrowsAsync<Rc2PublishingControlException>(() => f.PublishAsync());
        Assert.Equal(PhotoId, f.Row.RemotePublicationId);
        Assert.Equal(Rc2PublicationStep.PublishedRemote, f.Row.LastCompletedStep);
        Assert.Equal(1, f.Api.CreateCalls);

        f.Api.QueryError = null;
        f.Api.QueryResponses.Enqueue(Photo());
        await f.PublishAsync();

        Assert.Equal(1, f.Api.CreateCalls);
        Assert.True(f.Row.RemoteVerificationCompleted);
    }

    [Fact]
    public async Task Ambiguous_create_is_fail_closed_on_retry()
    {
        await using var f = await Fixture.CreateAsync();
        f.Api.CreateError = new FacebookPhotoCreateOutcomeUnknownException("outcome unknown");

        await Assert.ThrowsAsync<FacebookPhotoCreateOutcomeUnknownException>(() => f.PublishAsync());
        Assert.True(f.Row.PublishRequested);
        f.Api.CreateError = null;

        await Assert.ThrowsAsync<FacebookPhotoCreateOutcomeUnknownException>(() => f.PublishAsync());
        Assert.Equal(1, f.Api.CreateCalls);
    }

    [Fact]
    public async Task Existing_remote_id_only_queries_and_never_creates()
    {
        await using var f = await Fixture.CreateAsync();
        f.Row.PublishRequested = true;
        f.Row.RemotePublicationId = PhotoId;
        f.Row.RemotePostId = PostId;
        f.Row.LastCompletedStep = Rc2PublicationStep.PublishedRemote;
        await f.Db.SaveChangesAsync();
        f.Api.QueryResponses.Enqueue(Photo());

        await f.PublishAsync();

        Assert.Equal(0, f.Api.CreateCalls);
        Assert.Equal(1, f.Api.QueryCalls);
        Assert.Equal(Permalink, f.Row.RemoteUrl);
    }

    [Fact]
    public async Task Already_verified_row_performs_zero_provider_calls()
    {
        await using var f = await Fixture.CreateAsync();
        f.Row.RemotePublicationId = PhotoId;
        f.Row.RemotePostId = PostId;
        f.Row.RemoteUrl = Permalink;
        f.Row.RemoteVerificationCompleted = true;
        f.Row.Status = Rc2PublicationState.Published;

        await f.PublishAsync();

        Assert.Equal(0, f.Api.CreateCalls + f.Api.QueryCalls);
        Assert.Equal(Permalink, f.Row.RemoteUrl);
    }

    private static Rc2FacebookPhoto Photo() => new(PhotoId, PageId, PageName, Permalink, true);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public MediaFactoryDbContext Db { get; }
        public FakeFacebookApi Api { get; }
        public Rc2PublishingExecutionService Service { get; }
        public Rc2PublishingPublication Row { get; }
        public Rc2PublishingPlan Plan { get; }
        public Phase20PublishingArtifact Artifact { get; }

        private Fixture(SqliteConnection connection, MediaFactoryDbContext db, FakeFacebookApi api,
            Rc2PublishingExecutionService service, Rc2PublishingPublication row, Rc2PublishingPlan plan,
            Phase20PublishingArtifact artifact)
        { this.connection = connection; Db = db; Api = api; Service = service; Row = row; Plan = plan; Artifact = artifact; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync(); await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            var root = Path.Combine(Path.GetTempPath(), "rc2-facebook-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var image = Path.Combine(root, "hero.jpg"); await File.WriteAllBytesAsync(image, [1, 2, 3]);
            var plan = new Rc2PublishingPlan(Guid.NewGuid(), "Orion constellation guide", "English", "US", root, root, root, "validation.json");
            var artifact = new Phase20PublishingArtifact("HeroLandscape", image, 3, "unused", null);
            var row = new Rc2PublishingPublication { Id = Guid.NewGuid(), PlanId = plan.PlanId, PublishingPackageId = "package",
                Phase20AuthorityChecksum = "authority", Target = Rc2PublishingTarget.FacebookPost, RoleOrMediaType = "HeroLandscape",
                IdempotencyKey = Guid.NewGuid().ToString("N"), Status = Rc2PublicationState.Publishing,
                CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow };
            db.Add(row); await db.SaveChangesAsync();
            var api = new FakeFacebookApi();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Meta:ExpectedFacebookPageId"] = PageId, ["Meta:ExpectedFacebookPageName"] = PageName }).Build();
            var service = new Rc2PublishingExecutionService(null!, null!, db, null!, null!, null!, null!, null!, null!, null!,
                Options.Create(new PublishingTargetsOptions()), Options.Create(new PublishingOptions()), Options.Create(new YouTubeOptions()),
                Options.Create(new MetaPublishingOptions()), Options.Create(new PlatformPublishingOptions()),
                Options.Create(new PublicMediaStorageOptions()), config, NullLogger<Rc2PublishingExecutionService>.Instance,
                null, null, api);
            return new(connection, db, api, service, row, plan, artifact);
        }

        public Task PublishAsync() => Service.PublishFacebookPostAsync(Row, Plan, Artifact, CancellationToken.None);
        public async ValueTask DisposeAsync()
        { Directory.Delete(Plan.PlanOutputRoot, true); await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class FakeFacebookApi : IRc2FacebookPhotoApiClient
    {
        public int CreateCalls, QueryCalls;
        public Exception? CreateError, QueryError;
        public Queue<Rc2FacebookPhoto?> QueryResponses { get; } = new();
        public Task<Rc2FacebookPhotoCreateResult> CreatePagePhotoAsync(string path, string message, CancellationToken ct)
        { CreateCalls++; if (CreateError is not null) throw CreateError; Assert.Equal("Orion constellation guide", message); return Task.FromResult(new Rc2FacebookPhotoCreateResult(PhotoId, PostId)); }
        public Task<Rc2FacebookPhoto?> GetPhotoAsync(string id, CancellationToken ct)
        { QueryCalls++; if (QueryError is not null) throw QueryError; return Task.FromResult(QueryResponses.Dequeue()); }
    }
}
