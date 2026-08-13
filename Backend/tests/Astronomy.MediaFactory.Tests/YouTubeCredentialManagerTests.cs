using System.Net;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class YouTubeCredentialManagerTests
{
    [Fact]
    public async Task ValidAccessToken_DoesNotRefresh()
    {
        using var workspace = new Workspace();
        await workspace.WriteAsync("refresh", "access", DateTimeOffset.UtcNow.AddHours(1));
        var handler = new RefreshHandler();
        var service = workspace.Create(handler);

        Assert.Equal("access", await service.GetAccessTokenAsync(default));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ExpiredAccessToken_RefreshesAndPreservesRefreshToken()
    {
        using var workspace = new Workspace();
        await workspace.WriteAsync("keep-me", "expired", DateTimeOffset.UtcNow.AddMinutes(-1));
        var handler = new RefreshHandler();

        Assert.Equal("new-access", await workspace.Create(handler).GetAccessTokenAsync(default));
        var stored = await workspace.ReadAsync();
        Assert.Equal("keep-me", stored.RefreshToken);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExpiredAccessToken_AtomicallyPersistsRotatedRefreshTokenAndExpiry()
    {
        using var workspace = new Workspace();
        await workspace.WriteAsync("old-refresh", "expired", DateTimeOffset.UtcNow.AddMinutes(-1));
        var handler = new RefreshHandler(body:
            "{\"access_token\":\"new-access\",\"refresh_token\":\"rotated-refresh\",\"expires_in\":3600}");

        Assert.Equal("new-access", await workspace.Create(handler).GetAccessTokenAsync(default));

        var stored = await workspace.ReadAsync();
        Assert.Equal("rotated-refresh", stored.RefreshToken);
        Assert.Equal("new-access", stored.AccessToken);
        Assert.True(stored.AccessTokenExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.False(stored.ReauthorizationRequired);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void OAuthWriterAndPublishingReaderResolveSameNormalizedAbsolutePath()
    {
        var relativePath = Path.Combine("credentials", "youtube-oauth-token.json");
        var options = new YouTubeOptions { TokenFilePath = relativePath };

        var writerPath = YouTubeTokenResolver.ResolveTokenFilePath(options);
        var readerPath = YouTubeTokenResolver.ResolveTokenFilePath(options);

        Assert.True(Path.IsPathFullyQualified(writerPath));
        Assert.Equal(writerPath, readerPath);
    }

    [Fact]
    public async Task InvalidGrant_RequiresReauthorization()
    {
        using var workspace = new Workspace();
        await workspace.WriteAsync("revoked", "expired", DateTimeOffset.UtcNow.AddMinutes(-1));
        var handler = new RefreshHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");

        await Assert.ThrowsAsync<YouTubeReauthorizationRequiredException>(() => workspace.Create(handler).GetAccessTokenAsync(default));
        Assert.True((await workspace.ReadAsync()).ReauthorizationRequired);
    }

    [Fact]
    public async Task TenConcurrentExpiredRequests_PerformOneRefresh()
    {
        using var workspace = new Workspace();
        await workspace.WriteAsync("refresh", "expired", DateTimeOffset.UtcNow.AddMinutes(-1));
        var handler = new RefreshHandler(delay: TimeSpan.FromMilliseconds(25));
        var service = workspace.Create(handler);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.GetAccessTokenAsync(default)));

        Assert.All(tokens, token => Assert.Equal("new-access", token));
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "youtube-credential-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(directory, "youtube-oauth-token.json");
        public Workspace() => Directory.CreateDirectory(directory);
        public Task WriteAsync(string refresh, string access, DateTimeOffset expiry) => File.WriteAllTextAsync(Path,
            JsonSerializer.Serialize(new YouTubeOAuthTokenFile("UC-expected", "AstroPulse", refresh, DateTimeOffset.UtcNow, access, expiry)));
        public async Task<YouTubeOAuthTokenFile> ReadAsync() => JsonSerializer.Deserialize<YouTubeOAuthTokenFile>(await File.ReadAllTextAsync(Path))!;
        public YouTubeAuthService Create(HttpMessageHandler handler) => new(new HttpClient(handler), Options.Create(new YouTubeOptions
        {
            ClientId = "client", ClientSecret = "secret", TokenFilePath = Path
        }), NullLogger<YouTubeAuthService>.Instance);
        public void Dispose() => Directory.Delete(directory, true);
    }

    private sealed class RefreshHandler(HttpStatusCode status = HttpStatusCode.OK,
        string body = "{\"access_token\":\"new-access\",\"expires_in\":3600}", TimeSpan? delay = null) : HttpMessageHandler
    {
        private int callCount;
        public int CallCount => callCount;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            if (delay.HasValue) await Task.Delay(delay.Value, cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
