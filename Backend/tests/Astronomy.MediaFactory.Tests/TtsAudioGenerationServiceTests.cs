using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class TtsAudioGenerationServiceTests : IDisposable
{
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "phase9c1-tts-audio-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateTtsAudio_DryRun_ReturnsPlannedPathsWithoutCreatingAudioFiles()
    {
        var planId = Guid.Parse("36cb768a-4aa6-4189-ac48-f45ae5ee4f6b");
        WriteFinalPackage(planId);
        var service = CreateService(new StubAzureSpeechClient(_ => Task.FromResult(CreateWavBytes())));

        var result = await service.GenerateTtsAudioAsync(new TtsAudioGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            MaxPlans: 1,
            DryRun: true,
            OverwriteExisting: false,
            CombineSegments: true), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(0, result.SegmentAudioCount);
        Assert.Contains(result.GeneratedFiles, path => path.EndsWith(Path.Combine("tts", "audio", "scene-01.wav"), StringComparison.Ordinal));
        Assert.Contains(result.GeneratedFiles, path => path.EndsWith(Path.Combine("tts", "audio", "narration-combined.wav"), StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "tts", "audio")));
    }

    [Fact]
    public async Task GenerateTtsAudio_DryRunFalse_CreatesFourSegmentWavsCombinedWavAndManifest()
    {
        var planId = Guid.Parse("36cb768a-4aa6-4189-ac48-f45ae5ee4f6b");
        WriteFinalPackage(planId);
        var service = CreateService(new StubAzureSpeechClient(_ => Task.FromResult(CreateWavBytes())));

        var result = await service.GenerateTtsAudioAsync(new TtsAudioGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            MaxPlans: 1,
            DryRun: false,
            OverwriteExisting: false,
            CombineSegments: true), CancellationToken.None);

        var audioRoot = Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "tts", "audio");
        Assert.Equal(1, result.PlanCount);
        Assert.Equal(4, result.SegmentAudioCount);
        Assert.Equal(1, result.CombinedAudioCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        for (var scene = 1; scene <= 4; scene++)
            Assert.True(File.Exists(Path.Combine(audioRoot, $"scene-{scene:00}.wav")));
        Assert.True(File.Exists(Path.Combine(audioRoot, "narration-combined.wav")));
        Assert.True(File.Exists(Path.Combine(audioRoot, "tts-audio-manifest.json")));
    }


    [Fact]
    public async Task GenerateTtsAudio_RejectsAllPlanGenerationAndMaxPlansAbovePilotLimit()
    {
        var service = CreateService(new StubAzureSpeechClient(_ => Task.FromResult(CreateWavBytes())));

        await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateTtsAudioAsync(new TtsAudioGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [],
            MaxPlans: 1,
            DryRun: false), CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateTtsAudioAsync(new TtsAudioGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [Guid.Parse("36cb768a-4aa6-4189-ac48-f45ae5ee4f6b")],
            MaxPlans: 2,
            DryRun: false), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateTtsAudio_MissingAzureConfiguration_ReturnsWarningWithoutFailingPlan()
    {
        var planId = Guid.Parse("36cb768a-4aa6-4189-ac48-f45ae5ee4f6b");
        WriteFinalPackage(planId);
        var service = CreateService(new StubAzureSpeechClient(_ => throw new InvalidOperationException("should not call Azure")), key: "", region: "");

        var result = await service.GenerateTtsAudioAsync(new TtsAudioGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            MaxPlans: 1,
            DryRun: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("AzureSpeech:Key", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private AzureTtsAudioGenerationService CreateService(IAzureSpeechClient client, string key = "fake-key", string region = "eastus")
    {
        return new AzureTtsAudioGenerationService(
            Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }),
            Options.Create(new AzureSpeechOptions { Key = key, Region = region }),
            client,
            NullLogger<AzureTtsAudioGenerationService>.Instance);
    }

    private void WriteFinalPackage(Guid planId)
    {
        var ttsRoot = Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "tts");
        Directory.CreateDirectory(ttsRoot);
        var package = new FinalTtsPackageDocument(
            planId.ToString("D"),
            "IN-RJ-UDAIPUR",
            "en",
            "RareEventAlert",
            "ShortForm",
            "Rare Event Alert",
            "AzureSpeech",
            new TtsVoiceProfile("Narrator", "en-US-AriaNeural", "calm", "+0%", "medium", "medium"),
            new TtsMusicProfile("cinematic", "low", "ambient"),
            Enumerable.Range(1, 4).Select(scene => new TtsPackageSegment(
                scene,
                $"Scene {scene}",
                $"Scene {scene} narration text.",
                $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\"><voice name=\"en-US-AriaNeural\">Scene {scene} narration text.</voice></speak>",
                4,
                [],
                [],
                null,
                $"/tts/audio/scene-{scene:00}.wav")).ToArray(),
            16,
            true,
            "Phase9B.3",
            DateTimeOffset.UtcNow,
            "Valid",
            DateTimeOffset.UtcNow,
            true,
            "AlreadyValid",
            DateTimeOffset.UtcNow,
            []);

        File.WriteAllText(Path.Combine(ttsRoot, "tts-package-final.json"), JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private static byte[] CreateWavBytes()
    {
        const ushort channels = 1;
        const uint sampleRate = 24000;
        const ushort bitsPerSample = 16;
        const uint byteRate = sampleRate * channels * bitsPerSample / 8;
        const ushort blockAlign = (ushort)(channels * bitsPerSample / 8);
        const int dataSize = 48000;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write((uint)(36 + dataSize));
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write((uint)16);
        writer.Write((ushort)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write((uint)dataSize);
        writer.Write(new byte[dataSize]);
        return stream.ToArray();
    }

    private sealed class StubAzureSpeechClient(Func<string, Task<byte[]>> synthesize) : IAzureSpeechClient
    {
        public Task<byte[]> SynthesizeMp3Async(string text, AzureSpeechOptions options, CancellationToken cancellationToken)
            => synthesize(text);

        public Task<byte[]> SynthesizeWavSsmlAsync(string ssml, AzureSpeechOptions options, CancellationToken cancellationToken)
            => synthesize(ssml);
    }
}
