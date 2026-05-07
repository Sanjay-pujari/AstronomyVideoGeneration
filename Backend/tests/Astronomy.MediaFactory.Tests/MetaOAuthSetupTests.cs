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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class MetaOAuthSetupTests
{
    private const string ShortToken = "short-token-secret-value";
    private const string LongToken = "long-lived-token-secret-value";
    private const string PageToken = "page-token-secret-value";

    [Fact]
    public async Task Start_ReturnsMetaConsentUrl_WithRequiredScopes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(MetaOAuthController).Assembly);
        using var workspace = new TemporaryMetaOAuthWorkspace();
        builder.Services.AddSingleton<IMetaOAuthService>(CreateService(workspace.TokenFilePath, CreateSuccessHandler()));
        var app = builder.Build();
        app.MapControllers();

        await app.StartAsync();
        var response = await app.GetTestClient().GetAsync("/api/metaoauth/start");
        var payload = await response.Content.ReadFromJsonAsync<MetaOAuthStartResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(payload);
        Assert.True(payload.Success);
        Assert.Equal("Open authorizationUrl in a browser to grant Meta publishing access.", payload.Message);
        var authorizationUrl = Uri.UnescapeDataString(payload.AuthorizationUrl);
        Assert.StartsWith("https://www.facebook.com/v23.0/dialog/oauth", authorizationUrl, StringComparison.Ordinal);
        Assert.Contains("client_id=app-id", authorizationUrl);
        Assert.Contains("response_type=code", authorizationUrl);
        Assert.Contains("scope=pages_manage_posts,pages_read_engagement,pages_show_list,instagram_basic,instagram_content_publish,business_management", authorizationUrl);
        await app.StopAsync();
    }

    [Fact]
    public async Task Start_WithRedirectQuery_ReturnsMetaConsentRedirect()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(MetaOAuthController).Assembly);
        using var workspace = new TemporaryMetaOAuthWorkspace();
        builder.Services.AddSingleton<IMetaOAuthService>(CreateService(workspace.TokenFilePath, CreateSuccessHandler()));
        var app = builder.Build();
        app.MapControllers();

        await app.StartAsync();
        var response = await app.GetTestClient().GetAsync("/api/metaoauth/start?redirect=true");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Uri.UnescapeDataString(response.Headers.Location?.ToString() ?? string.Empty);
        Assert.StartsWith("https://www.facebook.com/v23.0/dialog/oauth", location, StringComparison.Ordinal);
        Assert.Contains("client_id=app-id", location);
        Assert.Contains("response_type=code", location);
        Assert.Contains("scope=pages_manage_posts,pages_read_engagement,pages_show_list,instagram_basic,instagram_content_publish,business_management", location);
        await app.StopAsync();
    }

    [Fact]
    public async Task Start_WithRedirectQueryFromCorsRequest_ReturnsAuthorizationUrlWithoutFollowingExternalRedirect()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(MetaOAuthController).Assembly);
        using var workspace = new TemporaryMetaOAuthWorkspace();
        builder.Services.AddSingleton<IMetaOAuthService>(CreateService(workspace.TokenFilePath, CreateSuccessHandler()));
        var app = builder.Build();
        app.MapControllers();

        await app.StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/metaoauth/start?redirect=true");
        request.Headers.Add("Origin", "http://localhost:5173");
        var response = await app.GetTestClient().SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<MetaOAuthStartResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.Success);
        Assert.StartsWith("https://www.facebook.com/v23.0/dialog/oauth", payload.AuthorizationUrl, StringComparison.Ordinal);
        Assert.Contains("top-level browser navigation", payload.Message);
        await app.StopAsync();
    }

    [Fact]
    public async Task Callback_WithoutCode_FailsClearly()
    {
        using var app = await CreateCallbackAppAsync(new StubMetaOAuthService("https://example.test"));

        var response = await app.GetTestClient().GetAsync("/api/metaoauth/callback");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("OAuth authorization code is required", body);
    }

    [Fact]
    public async Task CompleteSetup_ExchangesAuthorizationCodeForShortLivedToken()
    {
        using var workspace = new TemporaryMetaOAuthWorkspace();
        var handler = CreateSuccessHandler();
        var service = CreateService(workspace.TokenFilePath, handler);

        await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        var tokenExchange = handler.Requests.Single(request => request.AbsolutePath == "/v23.0/oauth/access_token" && request.Query.Contains("code=auth-code", StringComparison.Ordinal));
        Assert.Contains("client_id=app-id", tokenExchange.Query);
        Assert.Contains("redirect_uri=https%3A%2F%2Flocalhost%3A59235%2Fapi%2Fmetaoauth%2Fcallback", tokenExchange.Query);
    }

    [Fact]
    public async Task CompleteSetup_ExchangesShortTokenForLongLivedToken()
    {
        using var workspace = new TemporaryMetaOAuthWorkspace();
        var handler = CreateSuccessHandler();
        var service = CreateService(workspace.TokenFilePath, handler);

        await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        var longExchange = handler.Requests.Single(request => request.AbsolutePath == "/v23.0/oauth/access_token" && request.Query.Contains("grant_type=fb_exchange_token", StringComparison.Ordinal));
        Assert.Contains($"fb_exchange_token={ShortToken}", Uri.UnescapeDataString(longExchange.Query));
    }

    [Fact]
    public async Task CompleteSetup_DiscoversPagesAndSelectsConfiguredPage()
    {
        using var workspace = new TemporaryMetaOAuthWorkspace();
        var handler = CreateSuccessHandler();
        var service = CreateService(workspace.TokenFilePath, handler, expectedPageId: "page-2");

        var result = await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Expected Astro Page", result.FacebookPageName);
        Assert.Equal("page-2", result.FacebookPageId);
        Assert.True(handler.Requests.Any(request => request.AbsolutePath == "/v23.0/me/accounts" && Uri.UnescapeDataString(request.Query).Contains("fields=id,name,access_token", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CompleteSetup_DiscoversInstagramBusinessAccountAndUsername()
    {
        using var workspace = new TemporaryMetaOAuthWorkspace();
        var service = CreateService(workspace.TokenFilePath, CreateSuccessHandler(), expectedPageId: "page-2", expectedInstagramUsername: "astropulse");

        var result = await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        Assert.Equal("ig-123", result.InstagramBusinessAccountId);
        Assert.Equal("astropulse", result.InstagramUsername);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task CompleteSetup_WhenExpectedPageMismatch_FailsClearly()
    {
        using var workspace = new TemporaryMetaOAuthWorkspace();
        var service = CreateService(workspace.TokenFilePath, CreateSuccessHandler(), expectedPageId: "missing-page");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteSetupAsync("auth-code", CancellationToken.None));

        Assert.Equal(MetaOAuthService.PageMismatchMessage, ex.Message);
    }

    [Fact]
    public async Task CompleteSetup_CreatesTokenFileAndRedactedDiagnostics()
    {
        using var workspace = new TemporaryMetaOAuthWorkspace();
        var service = CreateService(workspace.TokenFilePath, CreateSuccessHandler(), expectedPageId: "page-2");

        await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        Assert.True(File.Exists(workspace.TokenFilePath));
        Assert.True(File.Exists(workspace.ResultFilePath));
        var tokenFile = await File.ReadAllTextAsync(workspace.TokenFilePath);
        var diagnostics = await File.ReadAllTextAsync(workspace.ResultFilePath);
        Assert.Contains(LongToken, tokenFile);
        Assert.Contains(PageToken, tokenFile);
        Assert.DoesNotContain(LongToken, diagnostics);
        Assert.DoesNotContain(PageToken, diagnostics);
        Assert.Contains("Expected Astro Page", diagnostics);
    }

    [Fact]
    public async Task CompleteSetup_DoesNotLogSecretsOrFullTokens()
    {
        using var workspace = new TemporaryMetaOAuthWorkspace();
        var logger = new CapturingLogger<MetaOAuthService>();
        var service = CreateService(workspace.TokenFilePath, CreateSuccessHandler(), logger: logger);

        await service.CompleteSetupAsync("auth-code", CancellationToken.None);

        var logs = string.Join("\n", logger.Messages);
        Assert.DoesNotContain("app-secret", logs);
        Assert.DoesNotContain(ShortToken, logs);
        Assert.DoesNotContain(LongToken, logs);
        Assert.DoesNotContain(PageToken, logs);
    }

    private static async Task<WebApplication> CreateCallbackAppAsync(IMetaOAuthService oauthService)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(MetaOAuthController).Assembly);
        builder.Services.AddSingleton(oauthService);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static MetaOAuthService CreateService(
        string tokenFilePath,
        DispatchingJsonHandler handler,
        string expectedPageId = "",
        string expectedInstagramUsername = "",
        CapturingLogger<MetaOAuthService>? logger = null)
    {
        var options = Options.Create(new MetaOptions
        {
            AppId = "app-id",
            AppSecret = "app-secret",
            RedirectUri = "https://localhost:59235/api/metaoauth/callback",
            ExpectedFacebookPageId = expectedPageId,
            ExpectedInstagramUsername = expectedInstagramUsername,
            TokenFilePath = tokenFilePath
        });

        return new MetaOAuthService(new HttpClient(handler), options, logger ?? new CapturingLogger<MetaOAuthService>());
    }

    private static DispatchingJsonHandler CreateSuccessHandler()
        => new(uri =>
        {
            var decodedQuery = Uri.UnescapeDataString(uri.Query);
            return uri.AbsolutePath switch
            {
                "/v23.0/oauth/access_token" when decodedQuery.Contains("code=auth-code", StringComparison.Ordinal) => $"{{\"access_token\":\"{ShortToken}\",\"expires_in\":3600,\"token_type\":\"bearer\"}}",
                "/v23.0/oauth/access_token" when decodedQuery.Contains("grant_type=fb_exchange_token", StringComparison.Ordinal) => $"{{\"access_token\":\"{LongToken}\",\"expires_in\":5184000,\"token_type\":\"bearer\"}}",
                "/v23.0/me/accounts" => $"{{\"data\":[{{\"id\":\"page-1\",\"name\":\"Other Page\",\"access_token\":\"other-page-token\"}},{{\"id\":\"page-2\",\"name\":\"Expected Astro Page\",\"access_token\":\"{PageToken}\"}}]}}",
                "/v23.0/page-2" => "{\"instagram_business_account\":{\"id\":\"ig-123\"}}",
                "/v23.0/page-1" => "{\"instagram_business_account\":{\"id\":\"ig-123\"}}",
                "/v23.0/ig-123" => "{\"username\":\"astropulse\"}",
                _ => throw new InvalidOperationException($"Unexpected request: {uri}")
            };
        });

    private sealed class StubMetaOAuthService : IMetaOAuthService
    {
        private readonly string _authUrl;

        public StubMetaOAuthService(string authUrl)
        {
            _authUrl = authUrl;
        }

        public string BuildAuthorizationUrl() => _authUrl;

        public Task<MetaOAuthSetupResult> CompleteSetupAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(new MetaOAuthSetupResult(true, "Expected Astro Page", "page-2", "astropulse", "ig-123", true));
    }

    private sealed class DispatchingJsonHandler : HttpMessageHandler
    {
        private readonly Func<Uri, string> _dispatch;

        public DispatchingJsonHandler(Func<Uri, string> dispatch)
        {
            _dispatch = dispatch;
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            Requests.Add(uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_dispatch(uri), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class TemporaryMetaOAuthWorkspace : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meta-oauth-{Guid.NewGuid():N}");

        public TemporaryMetaOAuthWorkspace()
        {
            Directory.CreateDirectory(_directory);
        }

        public string TokenFilePath => Path.Combine(_directory, "meta-oauth-token.json");
        public string ResultFilePath => Path.Combine(_directory, "meta-oauth-result.json");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
