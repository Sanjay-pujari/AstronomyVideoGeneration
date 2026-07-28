using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.ProductionAdapters;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostDiTests
{
    [Fact]
    public void Coordinator_and_compatibility_host_resolve()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        Assert.IsType<DocumentaryProductionExecutionCoordinator>(services.GetRequiredService<IDocumentaryProductionExecutionCoordinator>());
        Assert.IsType<DocumentaryProductionExecutionHost>(services.GetRequiredService<IDocumentaryProductionExecutionHost>());
        Assert.NotNull(services.GetRequiredService<IDocumentaryProductionOperationRunner>());
        Assert.NotNull(services.GetRequiredService<IDocumentaryProductionExecutionRequestBuilder>());
        Assert.NotNull(services.GetRequiredService<IDocumentaryProductionExecutionDependencyResolver>());
        Assert.NotNull(services.GetRequiredService<IDocumentaryProductionExecutionRecordMapper>());
    }

    [Fact]
    public void Execution_host_is_disabled_by_default()
    {
        using var provider = Provider();
        Assert.False(provider.GetRequiredService<IOptions<DocumentaryProductionExecutionHostOptions>>().Value.Enabled);
    }

    [Fact]
    public void Execution_host_options_validation_rejects_invalid_values()
    {
        var validator = new DocumentaryProductionExecutionHostOptionsValidator();
        var result = validator.Validate(null, new DocumentaryProductionExecutionHostOptions
        {
            MaximumAttemptsPerOperation = 0,
            VisualGenerationTimeoutSeconds = 0
        });
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, x => x.Contains("MaximumAttemptsPerOperation", StringComparison.Ordinal));
        Assert.Contains(result.Failures, x => x.Contains("timeouts", StringComparison.Ordinal));
    }

    private static ServiceProvider Provider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rendering:FfmpegPath"] = "never-executed",
            ["Rendering:FfprobePath"] = "never-executed",
            ["DocumentaryProductionAdapters:ExecutionHost:MaximumAttemptsPerOperation"] = "2",
            ["DocumentaryProductionAdapters:Narration:EnglishVoiceId"] = "en-US-AvaMultilingualNeural",
            ["DocumentaryProductionAdapters:Narration:HindiVoiceId"] = "hi-IN-SwaraNeural"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAzureSpeechClient, FakeAzureSpeechClient>();
        services.AddSingleton<ISsmlBuilder, FakeSsmlBuilder>();
        services.AddSingleton<IProcessRunner, A39RecordingProcessRunner>();
        services.AddDocumentaryProductionBridge(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class FakeAzureSpeechClient : IAzureSpeechClient
    {
        public Task<byte[]> SynthesizeMp3Async(string text, AzureSpeechOptions options, CancellationToken token) => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]> SynthesizeWavSsmlAsync(string ssml, AzureSpeechOptions options, CancellationToken token) => Task.FromResult(Array.Empty<byte>());
    }

    private sealed class FakeSsmlBuilder : ISsmlBuilder
    {
        public string BuildSsml(string text, string voiceName, SsmlNarrationProfile? profile = null, string? rateOverride = null, string? pitchOverride = null) => text;
    }
}
