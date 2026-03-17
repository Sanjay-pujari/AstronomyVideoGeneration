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
        var tempDir = CreateTempDirectory();

        try
        {
            var client = new StubAzureSpeechClient(_ => Task.FromResult(new byte[] { 1, 2, 3, 4 }));
            var sut = CreateService(client);

            var audioPath = await sut.SynthesizeAsync("Tonight we observe Mars.", tempDir, CancellationToken.None);

            var textPath = Path.Combine(tempDir, "narration.txt");
            Assert.Equal(Path.Combine(tempDir, "narration.mp3"), audioPath);
            Assert.True(File.Exists(textPath));
            Assert.True(File.Exists(audioPath));
            Assert.Equal("Tonight we observe Mars.", await File.ReadAllTextAsync(textPath));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(audioPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SynthesizeAsync_WritesPlaceholderAudio_WhenAzureSpeechFails()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var client = new StubAzureSpeechClient(_ => throw new InvalidOperationException("speech failed"));
            var sut = CreateService(client);

            var audioPath = await sut.SynthesizeAsync("Fallback script", tempDir, CancellationToken.None);

            var textPath = Path.Combine(tempDir, "narration.txt");
            Assert.Equal(Path.Combine(tempDir, "narration.mp3"), audioPath);
            Assert.True(File.Exists(textPath));
            Assert.True(File.Exists(audioPath));
            Assert.Equal("Fallback script", await File.ReadAllTextAsync(textPath));
            Assert.Empty(await File.ReadAllBytesAsync(audioPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static AzureSpeechSynthesisService CreateService(IAzureSpeechClient speechClient)
    {
        var options = Options.Create(new AzureSpeechOptions
        {
            Key = "fake-key",
            Region = "eastus",
            Voice = "en-US-FableMultilingualNeural"
        });

        return new AzureSpeechSynthesisService(options, speechClient, NullLogger<AzureSpeechSynthesisService>.Instance);
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"speech-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private sealed class StubAzureSpeechClient(Func<string, Task<byte[]>> synthesizer) : IAzureSpeechClient
    {
        private readonly Func<string, Task<byte[]>> _synthesizer = synthesizer;

        public Task<byte[]> SynthesizeMp3Async(string text, string key, string region, string voice, CancellationToken cancellationToken)
            => _synthesizer(text);
    }
}
