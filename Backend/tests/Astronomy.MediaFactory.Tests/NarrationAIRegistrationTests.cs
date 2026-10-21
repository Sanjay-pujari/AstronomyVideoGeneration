using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationAIRegistrationTests
{
    [Fact]
    public void FullMediaFactoryRegistration_DefaultAndLegacyTextServicesRemainAzure()
    {
        var configuration = Config(new()
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com",
            ["AzureOpenAI:ChatDeployment"] = "test-deployment",
            ["AzureOpenAI:ApiKey"] = "mock-only-key"
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediaFactory(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("AzureOpenAI", provider.GetRequiredService<IOptions<NarrationAIOptions>>().Value.Provider);
        Assert.IsType<AzureOpenAiContentGenerationService>(provider.GetRequiredService<INarrationPerformer>());
        Assert.IsType<AzureOpenAiContentGenerationService>(provider.GetRequiredService<IScriptGenerationService>());
        Assert.IsType<AzureOpenAiContentGenerationService>(provider.GetRequiredService<IShortsScriptGenerationService>());
        Assert.IsType<AzureOpenAiContentGenerationService>(provider.GetRequiredService<IMetadataOptimizationModelClient>());
    }

    [Fact]
    public void FullMediaFactoryRegistration_LocalNarrationDoesNotInstantiateAzure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediaFactory(Config(LocalSettings()));
        services.AddTransient<AzureOpenAiContentGenerationService>(_ => throw new InvalidOperationException("Azure initialized"));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<OllamaNarrationPerformer>(provider.GetRequiredService<INarrationPerformer>());
        Assert.Equal("", provider.GetRequiredService<IOptions<AzureOpenAiOptions>>().Value.ApiKey);
    }

    [Fact]
    public async Task SelectedAzureNarration_StillUsesOriginalTransportAndProviderIdentity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNarrationAI(Config(new()
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com",
            ["AzureOpenAI:ChatDeployment"] = "test-deployment",
            ["AzureOpenAI:ApiKey"] = "mock-only-key"
        }));
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        var handler = new AzureNarrationHandler();
        services.AddHttpClient<AzureOpenAiContentGenerationService>().ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddTransient<OllamaNarrationPerformer>(_ => throw new InvalidOperationException("Ollama initialized in Azure mode"));
        using var provider = services.BuildServiceProvider();
        var performer = provider.GetRequiredService<INarrationPerformer>();
        var result = await performer.InvokeAsync(new("azure-call", "attempt", "short", "system", "user", "checksum", 1), CancellationToken.None);
        Assert.IsType<AzureOpenAiContentGenerationService>(performer);
        Assert.Equal("Azure OpenAI", result.ProviderName);
        Assert.Equal("test-deployment", result.ModelOrDeployment);
        Assert.Contains("Moon", result.Response);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ProductionNarrationHost_StartsWithoutAzureCredentialsOrClients()
    {
        using var host = BuildNarrationHost(LocalSettings());
        await host.StartAsync();
        Assert.Equal(Environments.Production, host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var scope = host.Services.CreateScope();
        Assert.IsType<OllamaNarrationPerformer>(scope.ServiceProvider.GetRequiredService<INarrationPerformer>());
        Assert.True(host.Services.GetRequiredService<IOptions<StartupValidationOptions>>().Value.RequireAzureOpenAi);
        await host.StopAsync();
    }

    [Fact]
    public async Task AzureSelectedWithoutCredentials_RetainsProductionValidation()
    {
        using var host = BuildNarrationHost(new());
        var ex = await Record.ExceptionAsync(() => host.StartAsync());
        Assert.NotNull(ex);
        Assert.True(ex is OptionsValidationException or AggregateException, ex.ToString());
        Assert.Contains("AzureOpenAI", ex.ToString());
        Assert.Contains("ApiKey", ex.ToString());
    }

    [Fact]
    public async Task OllamaSelection_DoesNotDisableOtherProductionAzureGates()
    {
        using var host = BuildNarrationHost(LocalSettings(), requireOtherAzure: true);
        var ex = await Record.ExceptionAsync(() => host.StartAsync());
        Assert.NotNull(ex);
        Assert.True(ex is OptionsValidationException or AggregateException, ex.ToString());
        Assert.Contains("AzureSpeech", ex.ToString());
        Assert.Contains("AzureBlob", ex.ToString());
    }

    [Fact]
    public async Task LocalSelection_IgnoresUnselectedPartialAzureChatSettings()
    {
        var settings = LocalSettings();
        settings["AzureOpenAI:Endpoint"] = "stale-invalid-endpoint";
        settings["AzureOpenAI:ChatDeployment"] = "stale-deployment";
        using var host = BuildNarrationHost(settings);
        await host.StartAsync();
        Assert.Equal("stale-invalid-endpoint", host.Services.GetRequiredService<IOptions<AzureOpenAiOptions>>().Value.Endpoint);
        await host.StopAsync();
    }

    [Fact]
    public void LaterConfigurationProviderWins_WithoutChangingLegacyDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NarrationAI:Provider"] = "AzureOpenAI" })
            .AddInMemoryCollection(LocalSettings()).Build();
        var services = new ServiceCollection().AddNarrationAI(config);
        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IOptions<NarrationAIOptions>>().Value.IsOllama);
    }

    [Theory]
    [InlineData("NarrationAI:Provider", "OtherPaidProvider")]
    [InlineData("NarrationAI:Provider", "")]
    [InlineData("NarrationAI:Ollama:Model", "")]
    [InlineData("NarrationAI:Ollama:Model", "model:cloud")]
    [InlineData("NarrationAI:Ollama:BaseUrl", "file:///tmp/model")]
    [InlineData("NarrationAI:Ollama:BaseUrl", "http://user:password@localhost:11434")]
    [InlineData("NarrationAI:Ollama:ExpectedModelDigest", "not-a-digest")]
    [InlineData("NarrationAI:Ollama:TimeoutSeconds", "0")]
    [InlineData("NarrationAI:Ollama:MaxConcurrentRequests", "0")]
    [InlineData("NarrationAI:Ollama:ContextWindow", "64")]
    [InlineData("NarrationAI:Ollama:MaxOutputTokens", "8192")]
    [InlineData("NarrationAI:Ollama:MaxRetries", "100")]
    public void InvalidSelectionOrLocalSettings_FailExplicitly(string key, string value)
    {
        var settings = LocalSettings();
        settings[key] = value;
        using var provider = new ServiceCollection().AddNarrationAI(Config(settings)).BuildServiceProvider();
        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NarrationAIOptions>>().Value);
        Assert.Contains("NarrationAI", ex.Message);
    }

    private static Dictionary<string, string?> LocalSettings() => new()
    {
        ["NarrationAI:Provider"] = "Ollama", ["NarrationAI:Ollama:Model"] = "test-narrator:local"
    };

    private static IConfiguration Config(Dictionary<string, string?> values)
    {
        values["ConnectionStrings:Postgres"] = "Host=localhost;Database=mock_only;Username=test;Password=test";
        values["DatabaseSafety:AllowLocalhostPostgres"] = "true";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IHost BuildNarrationHost(Dictionary<string, string?> settings, bool requireOtherAzure = false)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddNarrationAI(builder.Configuration);
        builder.Services.AddTransient<AzureOpenAiContentGenerationService>(_ => throw new InvalidOperationException("Unselected Azure client initialized"));
        builder.Services.AddOptions<StartupValidationOptions>().Configure(o =>
        {
            o.RequireAzureSpeech = requireOtherAzure;
            o.RequireBlobStorage = requireOtherAzure;
            o.RequireYouTubeWhenPublishingEnabled = false;
            o.RequireSkyfieldWhenEnabled = false;
        }).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<StartupValidationOptions>, ProductionStartupValidator>();
        // This host deliberately starts only narration/config validation, not DB, media or RC2 services.
        return builder.Build();
    }

    private sealed class AzureNarrationHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("example.openai.azure.com", request.RequestUri!.Host);
            Assert.Contains("/openai/deployments/test-deployment/chat/completions", request.RequestUri.AbsolutePath);
            Assert.Equal("mock-only-key", Assert.Single(request.Headers.GetValues("api-key")));
            const string content = "{\"variant\":\"Short\",\"scenes\":[{\"sceneNumber\":1,\"narrationText\":\"The Moon reflects sunlight.\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } })
            });
        }
    }
}
