using System.Net;
using System.Net.Http.Json;
using System.Text;
using Astronomy.MediaFactory.Api.Controllers;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class YouTubeOAuthSetupTests
{
    [Fact]
    public async Task Start_ReturnsGoogleConsentUrl_WithOfflineConsentParameters()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(YouTubeOAuthController).Assembly);
        builder.Services.AddSingleton<IYouTubeOAuthService>(new StubOAuthService("https://accounts.google.com/o/oauth2/v2/auth?client_id=test-client&redirect_uri=http%3A%2F%2Flocalhost%3A5005%2Fapi%2Fyoutubeoauth%2Fcallback&response_type=code&scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fyoutube.upload%20https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fyoutube.readonly&access_type=offline&prompt=consent"));
        var app = builder.Build();
        app.MapControllers();

        await app.StartAsync();
        var response = await app.GetTestClient().GetAsync("/api/youtubeoauth/start");
        var payload = await response.Content.ReadFromJsonAsync<YouTubeOAuthStartResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.Success);
        Assert.Equal("Open authorizationUrl in a browser to grant YouTube upload access.", payload.Message);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", payload.AuthorizationUrl, StringComparison.Ordinal);
        Assert.Contains("access_type=offline", payload.AuthorizationUrl);
        Assert.Contains("prompt=consent", payload.AuthorizationUrl);
        Assert.Contains("youtube.upload", Uri.UnescapeDataString(payload.AuthorizationUrl));
        Assert.Contains("youtube.readonly", Uri.UnescapeDataString(payload.AuthorizationUrl));
        await app.StopAsync();
    }

    [Fact]
    public async Task Start_WithRedirectQuery_ReturnsGoogleConsentRedirect()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(YouTubeOAuthController).Assembly);
        builder.Services.AddSingleton<IYouTubeOAuthService>(new StubOAuthService("https://accounts.google.com/o/oauth2/v2/auth?client_id=test-client&access_type=offline&prompt=consent"));
        var app = builder.Build();
        app.MapControllers();

        await app.StartAsync();
        var response = await app.GetTestClient().GetAsync("/api/youtubeoauth/start?redirect=true");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", location, StringComparison.Ordinal);
        Assert.Contains("access_type=offline", location);
        Assert.Contains("prompt=consent", location);
        await app.StopAsync();
    }

    [Fact]
    public async Task Callback_WithoutCode_FailsClearly()
    {
        using var app = await CreateCallbackAppAsync(new StubOAuthService("https://example.test"));

        var response = await app.GetTestClient().GetAsync("/api/youtubeoauth/callback");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("OAuth authorization code is required", body);
    }

    [Fact]
    public async Task Callback_WithGoogleError_FailsClearly()
    {
        using var app = await CreateCallbackAppAsync(new StubOAuthService("https://example.test"));

        var response = await app.GetTestClient().GetAsync("/api/youtubeoauth/callback?error=access_denied");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Google OAuth returned error: access_denied", body);
    }


    [Fact]
    public void BuildAuthorizationUrl_IncludesUploadAndReadonlyScopes()
    {
        using var workspace = new TemporaryOAuthWorkspace();
        var service = CreateService(workspace.TokenFilePath, new TrackingYouTubeApiClient(), TokenJson("1//0gSCOPES"));

        var authorizationUrl = service.BuildAuthorizationUrl();

        var decodedUrl = Uri.UnescapeDataString(authorizationUrl);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", authorizationUrl, StringComparison.Ordinal);
        Assert.Contains(YouTubeOAuthService.YouTubeUploadScope, decodedUrl);
        Assert.Contains(YouTubeOAuthService.YouTubeReadonlyScope, decodedUrl);
        Assert.Contains("access_type=offline", decodedUrl);
        Assert.Contains("prompt=consent", decodedUrl);
    }

    [Fact]
    public async Task CompleteSetup_WithRefreshToken_ReturnsCleanSuccessResponse_AndPersistsTokenFile()
    {
        using var workspace = new TemporaryOAuthWorkspace();
        var refreshToken = "1//0gABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var service = CreateService(workspace.TokenFilePath, new TrackingYouTubeApiClient(), TokenJson(refreshToken));

        var result = await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Astronomy Channel", result.ChannelTitle);
        Assert.Equal("UC123", result.ChannelId);
        Assert.True(result.RefreshTokenGenerated);
        Assert.Equal("YouTube OAuth completed successfully. Full refresh token was saved to tokenFilePath.", result.Message);
        Assert.NotEqual(refreshToken, result.RefreshTokenPreview);
        Assert.Equal(workspace.TokenFilePath, result.TokenFilePath);
        Assert.True(File.Exists(workspace.TokenFilePath));
        var tokenFile = await File.ReadAllTextAsync(workspace.TokenFilePath);
        Assert.Contains(refreshToken, tokenFile);
    }

    [Fact]
    public async Task CompleteSetup_UsesAccessToken_ToVerifyAuthenticatedChannel()
    {
        using var workspace = new TemporaryOAuthWorkspace();
        var api = new TrackingYouTubeApiClient();
        var service = CreateService(workspace.TokenFilePath, api, TokenJson("1//0gVERIFY"));

        _ = await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        Assert.Equal("access-token-from-google", api.AccessTokenUsedForChannelVerification);
    }

    [Fact]
    public async Task CompleteSetup_ExpectedChannelMismatch_BlocksSave()
    {
        using var workspace = new TemporaryOAuthWorkspace();
        var api = new TrackingYouTubeApiClient();
        var service = CreateService(workspace.TokenFilePath, api, TokenJson("1//0gBLOCKED"), expectedChannelId: "UC-expected");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteSetupAsync("auth-code", CancellationToken.None));

        Assert.Equal(YouTubeOAuthService.ChannelMismatchMessage, ex.Message);
        Assert.False(File.Exists(workspace.TokenFilePath));
    }

    [Fact]
    public async Task CompleteSetup_WhenGoogleOmitsRefreshToken_FailsWithConsentGuidance()
    {
        using var workspace = new TemporaryOAuthWorkspace();
        var service = CreateService(workspace.TokenFilePath, new TrackingYouTubeApiClient(), "{\"access_token\":\"access-token-from-google\"}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteSetupAsync("auth-code", CancellationToken.None));

        Assert.Equal(YouTubeOAuthService.MissingRefreshTokenGuidance, ex.Message);
    }


    [Fact]
    public async Task CompleteSetup_WhenGoogleOmitsReadonlyScope_FailsWithScopeGuidance()
    {
        using var workspace = new TemporaryOAuthWorkspace();
        var service = CreateService(
            workspace.TokenFilePath,
            new TrackingYouTubeApiClient(),
            TokenJson("1//0gMISSINGREAD", scope: YouTubeOAuthService.YouTubeUploadScope));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteSetupAsync("auth-code", CancellationToken.None));

        Assert.Equal(YouTubeOAuthService.InsufficientOAuthScopesGuidance, ex.Message);
        Assert.False(File.Exists(workspace.TokenFilePath));
    }

    [Fact]
    public async Task CompleteSetup_WritesDiagnostics_WithoutSecretsOrTokens()
    {
        using var workspace = new TemporaryOAuthWorkspace();
        const string refreshToken = "1//0gSECRETREFRESH";
        const string clientSecret = "client-secret-value";
        var service = CreateService(workspace.TokenFilePath, new TrackingYouTubeApiClient(), TokenJson(refreshToken, accessToken: "access-token-secret"), clientSecret: clientSecret);

        _ = await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        var diagnostics = await File.ReadAllTextAsync(workspace.ResultFilePath);
        Assert.Contains("Astronomy Channel", diagnostics);
        Assert.Contains("UC123", diagnostics);
        Assert.Contains("refreshTokenGenerated", diagnostics);
        Assert.DoesNotContain(clientSecret, diagnostics);
        Assert.DoesNotContain("access-token-secret", diagnostics);
        Assert.DoesNotContain(refreshToken, diagnostics);
    }

    private static async Task<WebApplication> CreateCallbackAppAsync(IYouTubeOAuthService oauthService)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(YouTubeOAuthController).Assembly);
        builder.Services.AddSingleton(oauthService);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static YouTubeOAuthService CreateService(
        string tokenFilePath,
        TrackingYouTubeApiClient apiClient,
        string tokenJson,
        string expectedChannelId = "",
        string clientSecret = "client-secret")
    {
        var httpClient = new HttpClient(new StaticJsonHandler(tokenJson));
        var options = Options.Create(new YouTubeOptions
        {
            ClientId = "client-id",
            ClientSecret = clientSecret,
            RedirectUri = "http://localhost:5005/api/youtubeoauth/callback",
            TokenFilePath = tokenFilePath,
            ExpectedChannelId = expectedChannelId
        });

        return new YouTubeOAuthService(httpClient, apiClient, options);
    }

    private static string TokenJson(
        string refreshToken,
        string accessToken = "access-token-from-google",
        string scope = YouTubeOAuthService.YouTubeUploadScope + " " + YouTubeOAuthService.YouTubeReadonlyScope)
        => $"{{\"access_token\":\"{accessToken}\",\"refresh_token\":\"{refreshToken}\",\"expires_in\":3600,\"scope\":\"{scope}\",\"token_type\":\"Bearer\"}}";

    private sealed class StubOAuthService : IYouTubeOAuthService
    {
        private readonly string _authUrl;

        public StubOAuthService(string authUrl)
        {
            _authUrl = authUrl;
        }

        public string BuildAuthorizationUrl() => _authUrl;

        public Task<YouTubeOAuthSetupResult> CompleteSetupAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(new YouTubeOAuthSetupResult(true, "Astronomy Channel", "UC123", true, "YouTube OAuth completed successfully."));
    }

    private sealed class TrackingYouTubeApiClient : IYouTubeApiClient
    {
        public string? AccessTokenUsedForChannelVerification { get; private set; }

        public Task<YouTubeChannelInfo> GetAuthenticatedChannelAsync(string accessToken, CancellationToken cancellationToken)
        {
            AccessTokenUsedForChannelVerification = accessToken;
            return Task.FromResult(new YouTubeChannelInfo { ChannelId = "UC123", ChannelTitle = "Astronomy Channel" });
        }

        public Task<string> UploadVideoAsync(PublishRequest request, string accessToken, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UploadThumbnailAsync(string videoId, string thumbnailPath, string accessToken, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<YouTubeVideoPostUploadStatus?> GetVideoPostUploadStatusAsync(string videoId, string accessToken, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StaticJsonHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StaticJsonHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class TemporaryOAuthWorkspace : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"youtube-oauth-{Guid.NewGuid():N}");

        public TemporaryOAuthWorkspace()
        {
            Directory.CreateDirectory(_directory);
        }

        public string TokenFilePath => Path.Combine(_directory, "youtube-oauth-token.json");
        public string ResultFilePath => Path.Combine(_directory, "youtube-oauth-result.json");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
