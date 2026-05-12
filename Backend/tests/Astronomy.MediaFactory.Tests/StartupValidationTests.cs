using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class StartupValidationTests
{
    [Fact]
    public void ProductionValidation_Fails_WhenCriticalAzureSettingsMissing()
    {
        var validator = CreateValidator(
            environmentName: Environments.Production,
            openAi: new AzureOpenAiOptions(),
            speech: new AzureSpeechOptions { UseManagedIdentity = true, Region = "eastus" },
            blob: new AzureBlobOptions(),
            sidecar: new SkyfieldSidecarOptions { Enabled = true, BaseUrl = "not-a-uri" },
            youTube: new YouTubeOptions { PublishingEnabled = true });

        var result = validator.Validate(null, new StartupValidationOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, x => x.Contains("AzureOpenAI:Endpoint"));
        Assert.Contains(result.Failures!, x => x.Contains("AzureBlob"));
        Assert.Contains(result.Failures!, x => x.Contains("AzureSpeech:ResourceId"));
        Assert.Contains(result.Failures!, x => x.Contains("YouTube"));
    }

    [Fact]
    public void ProductionValidation_Passes_WithManagedIdentityCompatibleSettings()
    {
        var validator = CreateValidator(
            environmentName: Environments.Production,
            openAi: new AzureOpenAiOptions { Endpoint = "https://example.openai.azure.com", ChatDeployment = "gpt-4o", UseManagedIdentity = true },
            speech: new AzureSpeechOptions { Region = "eastus", ResourceId = "/subscriptions/123/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/speech", UseManagedIdentity = true },
            blob: new AzureBlobOptions { UseManagedIdentity = true, AccountName = "myaccount", ContainerName = "videos" },
            sidecar: new SkyfieldSidecarOptions { Enabled = true, BaseUrl = "https://sidecar.internal" },
            youTube: new YouTubeOptions { PublishingEnabled = false });

        var result = validator.Validate(null, new StartupValidationOptions());

        Assert.True(result.Succeeded);
    }


    [Fact]
    public async Task ObsoleteConfigurationWarningHostedService_Warns_WhenLegacyMetaSectionsExist()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InstagramPublishing:PublishingEnabled"] = "true",
                ["FacebookPublishing:PublishingEnabled"] = "true",
                ["MetaPublishing:Enabled"] = "true"
            })
            .Build();
        var logger = new RecordingLogger<ObsoleteConfigurationWarningHostedService>();
        var service = new ObsoleteConfigurationWarningHostedService(configuration, logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Contains(logger.Warnings, x => x.Contains("InstagramPublishing"));
        Assert.Contains(logger.Warnings, x => x.Contains("FacebookPublishing"));
    }

    [Fact]
    public async Task ObsoleteConfigurationWarningHostedService_DoesNotWarn_WhenLegacyMetaSectionsAreAbsent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MetaPublishing:Enabled"] = "true"
            })
            .Build();
        var logger = new RecordingLogger<ObsoleteConfigurationWarningHostedService>();
        var service = new ObsoleteConfigurationWarningHostedService(configuration, logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void DevelopmentValidation_Passes_WithLocalFallbackFriendlySettings()
    {
        var validator = CreateValidator(
            environmentName: Environments.Development,
            openAi: new AzureOpenAiOptions(),
            speech: new AzureSpeechOptions(),
            blob: new AzureBlobOptions(),
            sidecar: new SkyfieldSidecarOptions { Enabled = true, BaseUrl = "not-a-uri" },
            youTube: new YouTubeOptions { PublishingEnabled = true });

        var result = validator.Validate(null, new StartupValidationOptions());

        Assert.True(result.Succeeded);
    }

    private static ProductionStartupValidator CreateValidator(
        string environmentName,
        AzureOpenAiOptions openAi,
        AzureSpeechOptions speech,
        AzureBlobOptions blob,
        SkyfieldSidecarOptions sidecar,
        YouTubeOptions youTube,
        PublishingOptions? publishing = null)
        => new(
            new TestHostEnvironment { EnvironmentName = environmentName },
            Options.Create(openAi),
            Options.Create(speech),
            Options.Create(blob),
            Options.Create(sidecar),
            Options.Create(youTube),
            Options.Create(publishing ?? new PublishingOptions()));


    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(Directory.GetCurrentDirectory());
    }
}
