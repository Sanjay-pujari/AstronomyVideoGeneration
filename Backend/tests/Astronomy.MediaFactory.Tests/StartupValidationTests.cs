using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Microsoft.Extensions.FileProviders;
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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(Directory.GetCurrentDirectory());
    }
}
