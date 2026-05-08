using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class TokenHealthServiceTests
{
    [Fact]
    public async Task YouTubeValidToken_ReturnsChannelInfo()
    {
        using var handler = new TokenHealthHandler();
        var service = CreateService(handler, youtube: new YouTubeOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RefreshToken = "refresh-secret",
            ExpectedChannelId = "channel-1",
            ExpectedChannelTitle = "Astronomy Channel"
        });

        var result = await service.CheckYouTubeAsync(CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("channel-1", result.AccountId);
        Assert.Equal("Astronomy Channel", result.AccountName);
    }

    [Fact]
    public async Task YouTubeMissingRefreshToken_ReturnsInvalid()
    {
        using var handler = new TokenHealthHandler();
        var service = CreateService(handler, youtube: new YouTubeOptions { ClientId = "client-id", ClientSecret = "client-secret" });

        var result = await service.CheckYouTubeAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("refresh token", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MetaMissingTokenFile_ReturnsInvalid()
    {
        using var workspace = new TempTokenHealthWorkspace();
        var service = CreateService(new TokenHealthHandler(), meta: new MetaOptions { AppId = "app-id", AppSecret = "app-secret", TokenFilePath = workspace.TokenPath });

        var result = await service.CheckMetaAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("token file is missing", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MetaMissingScope_ReturnsInvalid()
    {
        using var workspace = new TempTokenHealthWorkspace();
        workspace.WriteMetaToken();
        using var handler = new TokenHealthHandler { MetaScopes = ["pages_manage_posts"] };
        var service = CreateService(handler, meta: new MetaOptions { AppId = "app-id", AppSecret = "app-secret", TokenFilePath = workspace.TokenPath });

        var result = await service.CheckMetaAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("missing required scopes", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MetaNearExpiry_ReturnsWarning()
    {
        using var workspace = new TempTokenHealthWorkspace();
        workspace.WriteMetaToken();
        using var handler = new TokenHealthHandler { MetaExpiresAt = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds() };
        var service = CreateService(handler, meta: new MetaOptions { AppId = "app-id", AppSecret = "app-secret", TokenFilePath = workspace.TokenPath });

        var result = await service.CheckMetaAsync(CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Contains("expire soon", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.CanRefresh);
    }

    [Fact]
    public async Task PrePublishBlocksInvalidYouTubeOnly()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var youtube = new TrackingYouTubePublishService();
        var content = new ContentPublishService(
            repository,
            youtube,
            new FixedTokenHealthService(youtube: new TokenHealthResult { Platform = "YouTube", IsValid = false, Error = "expired" }, meta: ValidMeta()),
            Options.Create(new PublishingOptions { Enabled = true, Mode = "DryRun", RequirePrePublishValidation = false, PublishShortVideo = true }),
            Options.Create(new YouTubeOptions()),
            Options.Create(new TokenHealthOptions { Enabled = true, CheckBeforePublish = true }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            NullLogger<ContentPublishService>.Instance);

        var youtubeResults = await content.PublishForPipelineRunAsync(run.Id, "short", CancellationToken.None);
        var meta = MetaPublishingTestFactory.CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, PublishInstagramReel = false });
        var metaResult = (await meta.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        var shortYouTubeResult = youtubeResults.Single(result => result.AssetType == "ShortVideo");

        Assert.False(youtube.Called);
        Assert.Contains("token health", shortYouTubeResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(metaResult.Success);
    }

    [Fact]
    public async Task PrePublishBlocksInvalidMetaOnly()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var youtube = new TrackingYouTubePublishService();
        var content = new ContentPublishService(
            repository,
            youtube,
            new FixedTokenHealthService(youtube: ValidYouTube(), meta: new TokenHealthResult { Platform = "Meta", IsValid = false, Error = "expired" }),
            Options.Create(new PublishingOptions { Enabled = true, Mode = "DryRun", RequirePrePublishValidation = false, PublishShortVideo = true }),
            Options.Create(new YouTubeOptions()),
            Options.Create(new TokenHealthOptions { Enabled = true, CheckBeforePublish = true }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            NullLogger<ContentPublishService>.Instance);
        var meta = new MetaPublishService(
            repository,
            new FacebookReelPublishService(new HttpClient(new TrackingMetaHandler()), Options.Create(new MetaOptions { TokenFilePath = workspace.TokenPath }), Options.Create(new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true }), Options.Create(new RenderingOptions()), NullLogger<FacebookReelPublishService>.Instance),
            new InstagramReelPublishService(new HttpClient(new TrackingMetaHandler()), Options.Create(new MetaOptions { TokenFilePath = workspace.TokenPath }), Options.Create(new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true }), Options.Create(new RenderingOptions()), NullLogger<InstagramReelPublishService>.Instance, null),
            new FixedTokenHealthService(youtube: ValidYouTube(), meta: new TokenHealthResult { Platform = "Meta", IsValid = false, Error = "expired" }),
            Options.Create(new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, PublishInstagramReel = false }),
            Options.Create(new TokenHealthOptions { Enabled = true, CheckBeforePublish = true }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            NullLogger<MetaPublishService>.Instance);

        var youtubeResult = (await content.PublishForPipelineRunAsync(run.Id, "short", CancellationToken.None)).Single(result => result.AssetType == "ShortVideo");
        var metaResult = (await meta.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(youtubeResult.Success);
        Assert.True(youtube.Called);
        Assert.False(metaResult.Success);
        Assert.Contains("token health", metaResult.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TokenHealthReport_DoesNotContainSecrets()
    {
        using var workspace = new TempTokenHealthWorkspace();
        var writer = new TokenHealthReportWriter(Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }));

        var path = await writer.WriteAsync([
            new TokenHealthResult { Platform = "YouTube", IsValid = true, AccountName = "Astronomy", AccountId = "channel-1" },
            new TokenHealthResult { Platform = "Meta", IsValid = false, AccountName = "AstroPulse", AccountId = "page-1", Error = "expired" }
        ], CancellationToken.None);

        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("client-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access-token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh-token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user-token-secret", json, StringComparison.OrdinalIgnoreCase);
    }

    private static TokenHealthService CreateService(TokenHealthHandler handler, YouTubeOptions? youtube = null, MetaOptions? meta = null)
        => new(
            new HttpClient(handler),
            Options.Create(youtube ?? new YouTubeOptions()),
            Options.Create(meta ?? new MetaOptions()),
            Options.Create(new TokenHealthOptions { RefreshBeforeExpiryDays = 7 }),
            NullLogger<TokenHealthService>.Instance);

    private static TokenHealthResult ValidYouTube() => new() { Platform = "YouTube", IsValid = true, IsConfigured = true, AccountId = "channel-1", AccountName = "Astronomy" };
    private static TokenHealthResult ValidMeta() => new() { Platform = "Meta", IsValid = true, IsConfigured = true, AccountId = "page-1", AccountName = "AstroPulse" };
}

public sealed class FixedTokenHealthService : ITokenHealthService
{
    private readonly TokenHealthResult _youtube;
    private readonly TokenHealthResult _meta;

    public FixedTokenHealthService(TokenHealthResult? youtube = null, TokenHealthResult? meta = null)
    {
        _youtube = youtube ?? new TokenHealthResult { Platform = "YouTube", IsValid = true, IsConfigured = true };
        _meta = meta ?? new TokenHealthResult { Platform = "Meta", IsValid = true, IsConfigured = true };
    }

    public Task<IReadOnlyList<TokenHealthResult>> CheckAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TokenHealthResult>>([_youtube, _meta]);
    public Task<TokenHealthResult> CheckYouTubeAsync(CancellationToken cancellationToken) => Task.FromResult(_youtube);
    public Task<TokenHealthResult> CheckMetaAsync(CancellationToken cancellationToken) => Task.FromResult(_meta);
}

public sealed class TokenHealthHandler : HttpMessageHandler, IDisposable
{
    public List<string> RequestBodies { get; } = [];
    public List<string> MetaScopes { get; set; } =
    [
        "pages_manage_posts",
        "pages_read_engagement",
        "pages_show_list",
        "instagram_basic",
        "instagram_content_publish",
        "business_management"
    ];
    public long MetaExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        RequestBodies.Add(body);

        if (request.RequestUri!.Host == "oauth2.googleapis.com")
        {
            return JsonResponse(new { access_token = "access-token", token_type = "Bearer" });
        }

        if (request.RequestUri.Host == "www.googleapis.com" && request.RequestUri.AbsolutePath.Contains("/youtube/v3/channels", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(new { items = new[] { new { id = "channel-1", snippet = new { title = "Astronomy Channel" } } } });
        }

        if (request.RequestUri.AbsolutePath.EndsWith("/debug_token", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(new { data = new { is_valid = true, expires_at = MetaExpiresAt, scopes = MetaScopes } });
        }

        if (request.RequestUri.AbsolutePath.EndsWith("/page-1", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(new { id = "page-1", name = "AstroPulse" });
        }

        if (request.RequestUri.AbsolutePath.EndsWith("/ig-1", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(new { id = "ig-1", username = "astro" });
        }

        return new HttpResponseMessage(HttpStatusCode.BadRequest);
    }

    private static HttpResponseMessage JsonResponse(object payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
}

public sealed class TempTokenHealthWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "token-health-tests", Guid.NewGuid().ToString("N"));
    public string TokenPath => Path.Combine(Root, "meta-oauth-token.json");

    public TempTokenHealthWorkspace() => Directory.CreateDirectory(Root);

    public void WriteMetaToken()
        => File.WriteAllText(TokenPath, JsonSerializer.Serialize(new MetaOAuthTokenFile("page-1", "AstroPulse", "page-token-secret", "ig-1", "astro", "user-token-secret", DateTimeOffset.UtcNow)));

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
