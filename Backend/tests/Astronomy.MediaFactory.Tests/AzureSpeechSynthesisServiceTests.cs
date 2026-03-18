using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AzureSpeechSynthesisServiceTests
{
    [Fact]
    public async Task SynthesizeAsync_WritesScriptAndAudio_WhenAzureSpeechSucceeds()
    {
        var fileSystem = new InMemoryFileSystem();
        var client = new StubAzureSpeechClient(_ => Task.FromResult(new byte[] { 1, 2, 3, 4 }));
        var sut = CreateService(client, fileSystem);

        var audioPath = await sut.SynthesizeAsync("Tonight we observe Mars.", "/tmp/output", CancellationToken.None);

        Assert.Equal(Path.Combine("/tmp/output", "narration.mp3"), audioPath);
        Assert.Equal(new[] { "/tmp/output" }, fileSystem.CreatedDirectories);
        Assert.Equal("Tonight we observe Mars.", fileSystem.TextWrites[Path.Combine("/tmp/output", "narration.txt")]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, fileSystem.ByteWrites[Path.Combine("/tmp/output", "narration.mp3")]);
    }

    [Fact]
    public async Task SynthesizeAsync_WritesPlaceholderAudio_WhenAzureSpeechFails()
    {
        var fileSystem = new InMemoryFileSystem();
        var client = new StubAzureSpeechClient(_ => throw new InvalidOperationException("speech failed"));
        var sut = CreateService(client, fileSystem);

        var audioPath = await sut.SynthesizeAsync("Fallback script", "/tmp/fallback", CancellationToken.None);

        Assert.Equal(Path.Combine("/tmp/fallback", "narration.mp3"), audioPath);
        Assert.Equal("Fallback script", fileSystem.TextWrites[Path.Combine("/tmp/fallback", "narration.txt")]);
        Assert.Empty(fileSystem.ByteWrites[Path.Combine("/tmp/fallback", "narration.mp3")]);
    }

    [Theory]
    [InlineData(null, "eastus", null, false)]
    [InlineData("", "eastus", null, false)]
    [InlineData("fake-key", null, null, false)]
    [InlineData("fake-key", "", null, false)]
    [InlineData(null, "eastus", null, true)]
    [InlineData(null, "eastus", "", true)]
    public async Task SynthesizeAsync_WritesPlaceholderAudio_WhenConfigurationIsMissing(string? key, string? region, string? resourceId, bool useManagedIdentity)
    {
        var fileSystem = new InMemoryFileSystem();
        var client = new StubAzureSpeechClient(_ => Task.FromResult(new byte[] { 9 }));
        var sut = CreateService(client, fileSystem, key, region, resourceId, useManagedIdentity);

        var audioPath = await sut.SynthesizeAsync("Config fallback", "/tmp/missing-config", CancellationToken.None);

        Assert.Equal(Path.Combine("/tmp/missing-config", "narration.mp3"), audioPath);
        Assert.Equal("Config fallback", fileSystem.TextWrites[Path.Combine("/tmp/missing-config", "narration.txt")]);
        Assert.Empty(fileSystem.ByteWrites[Path.Combine("/tmp/missing-config", "narration.mp3")]);
        Assert.False(client.WasCalled);
    }

    [Fact]
    public async Task SynthesizeAsync_UsesManagedIdentityConfiguration_WhenConfigured()
    {
        var fileSystem = new InMemoryFileSystem();
        AzureSpeechOptions? capturedOptions = null;
        var client = new StubAzureSpeechClient(options =>
        {
            capturedOptions = options;
            return Task.FromResult(new byte[] { 7, 8 });
        });
        var sut = CreateService(
            client,
            fileSystem,
            key: null,
            region: "eastus",
            resourceId: "/subscriptions/123/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/speech",
            useManagedIdentity: true);

        var audioPath = await sut.SynthesizeAsync("MI script", "/tmp/managed-identity", CancellationToken.None);

        Assert.Equal(Path.Combine("/tmp/managed-identity", "narration.mp3"), audioPath);
        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions!.UseManagedIdentity);
        Assert.Equal("eastus", capturedOptions.Region);
        Assert.Contains("Microsoft.CognitiveServices", capturedOptions.ResourceId);
    }

    [Fact]
    public async Task SynthesizeAsync_WritesPlaceholderAudio_WhenAudioWriteFailsAfterSuccessfulSynthesis()
    {
        var fileSystem = new InMemoryFileSystem { ThrowOnAudioWrite = true };
        var client = new StubAzureSpeechClient(_ => Task.FromResult(new byte[] { 5, 6 }));
        var sut = CreateService(client, fileSystem);

        var audioPath = await sut.SynthesizeAsync("Write fallback", "/tmp/write-failure", CancellationToken.None);

        Assert.Equal(Path.Combine("/tmp/write-failure", "narration.mp3"), audioPath);
        Assert.Equal("Write fallback", fileSystem.TextWrites[Path.Combine("/tmp/write-failure", "narration.txt")]);
        Assert.Empty(fileSystem.ByteWrites[Path.Combine("/tmp/write-failure", "narration.mp3")]);
        Assert.Equal(2, fileSystem.AudioWriteAttempts);
    }

    private static AzureSpeechSynthesisService CreateService(
        IAzureSpeechClient speechClient,
        IFileSystem fileSystem,
        string? key = "fake-key",
        string? region = "eastus",
        string? resourceId = null,
        bool useManagedIdentity = false)
    {
        var options = Options.Create(new AzureSpeechOptions
        {
            Key = key,
            Region = region,
            ResourceId = resourceId,
            UseManagedIdentity = useManagedIdentity,
            Voice = "en-US-FableMultilingualNeural"
        });

        return new AzureSpeechSynthesisService(options, speechClient, fileSystem, NullLogger<AzureSpeechSynthesisService>.Instance);
    }

    private sealed class StubAzureSpeechClient(Func<AzureSpeechOptions, Task<byte[]>> synthesizer) : IAzureSpeechClient
    {
        private readonly Func<AzureSpeechOptions, Task<byte[]>> _synthesizer = synthesizer;

        public bool WasCalled { get; private set; }

        public Task<byte[]> SynthesizeMp3Async(string text, AzureSpeechOptions options, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return _synthesizer(options);
        }
    }

    private sealed class InMemoryFileSystem : IFileSystem
    {
        public List<string> CreatedDirectories { get; } = [];
        public Dictionary<string, string> TextWrites { get; } = [];
        public Dictionary<string, byte[]> ByteWrites { get; } = [];
        public bool ThrowOnAudioWrite { get; set; }
        public int AudioWriteAttempts { get; private set; }

        public void CreateDirectory(string path) => CreatedDirectories.Add(path);

        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            TextWrites[path] = contents;
            return Task.CompletedTask;
        }

        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            if (path.EndsWith("narration.mp3", StringComparison.Ordinal))
            {
                AudioWriteAttempts++;
            }

            if (ThrowOnAudioWrite && AudioWriteAttempts == 1)
            {
                throw new IOException("simulated file write failure");
            }

            ByteWrites[path] = bytes;
            return Task.CompletedTask;
        }
    }
}
