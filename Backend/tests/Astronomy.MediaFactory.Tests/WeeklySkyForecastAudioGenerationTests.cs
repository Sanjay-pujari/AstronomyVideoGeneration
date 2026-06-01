using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastAudioGenerationTests
{
    [Fact]
    public async Task GenerateAudio_DryRun_WritesSsmlAndReportsWithoutTts()
    {
        var pipelineRunId = Guid.NewGuid();
        var workingRoot = Path.Combine(Path.GetTempPath(), "weekly-audio-tests", Guid.NewGuid().ToString("N"));
        var runRoot = Path.Combine(workingRoot, pipelineRunId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(runRoot, "episode"));
        Directory.CreateDirectory(Path.Combine(runRoot, "render"));
        await WriteInputsAsync(runRoot, pipelineRunId);

        var service = new WeeklySkyForecastAudioGenerationService(
            Options.Create(new RenderingOptions { WorkingDirectory = workingRoot }),
            Options.Create(new AzureSpeechOptions { DefaultVoiceName = "hi-IN-MadhurNeural" }),
            new ThrowingTtsSynthesizer(),
            NullLogger<WeeklySkyForecastAudioGenerationService>.Instance);

        var response = await service.GenerateAsync(pipelineRunId, new WeeklySkyForecastAudioGenerationRequest(DryRun: true), CancellationToken.None);

        response.AudioGenerationReady.Should().BeTrue();
        response.LongformSegmentAudioCount.Should().Be(2);
        response.ShortformSegmentAudioCount.Should().Be(1);
        File.Exists(Path.Combine(runRoot, "audio", "temp", "longform-hero.ssml")).Should().BeTrue();
        File.Exists(response.AudioGenerationReportPath).Should().BeTrue();
        File.Exists(response.AudioSegmentManifestPath).Should().BeTrue();
        File.Exists(response.AudioTimingValidationReportPath).Should().BeTrue();
    }

    private static async Task WriteInputsAsync(string runRoot, Guid pipelineRunId)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var longform = new WeeklyNarrationPackage(pipelineRunId, DateTime.UtcNow, "hi", "documentary", 380, 84, [new WeeklyNarrationSegment("hero", "HeroEvent", "आज रात आकाश देखें।", 84, 1, 1, true), new WeeklyNarrationSegment("summary", "WeeklySummary", "यह सप्ताह शांत है।", 20, 1, 1, false)]);
        var shortform = new WeeklyNarrationPackage(pipelineRunId, DateTime.UtcNow, "hi", "short", 50, 15, [new WeeklyNarrationSegment("short-hook", "ShortHook", "आज आसमान देखें।", 15, 1, 1, true)]);
        var contract = new WeeklyRenderContract(pipelineRunId, "WeeklySkyForecast", new DateOnly(2026, 6, 1), "in", "hi", new WeeklyEpisodeRenderContract(true, 1920, 1080, 30, 380, "timeline", 2, "long.mp4"), new WeeklyEpisodeRenderContract(true, 1080, 1920, 30, 50, "timeline", 1, "short.mp4"));
        var audioPlan = new WeeklyAudioAlignmentPlan(pipelineRunId, DateTime.UtcNow, "long.mp3", "short.mp3", [new WeeklyAudioSegmentAlignment("longform", "hero", "HeroEvent", "आज रात आकाश देखें।", "hero.mp3", 0, 84, 84), new WeeklyAudioSegmentAlignment("longform", "summary", "WeeklySummary", "यह सप्ताह शांत है।", "summary.mp3", 84, 104, 20), new WeeklyAudioSegmentAlignment("shortform", "short-hook", "ShortHook", "आज आसमान देखें।", "short-hook.mp3", 0, 15, 15)], true);

        await File.WriteAllTextAsync(Path.Combine(runRoot, "episode", "longform-narration.json"), JsonSerializer.Serialize(longform, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "episode", "shortform-narration.json"), JsonSerializer.Serialize(shortform, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "episode", "narration-timeline-map.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(runRoot, "render", "audio-alignment-plan.json"), JsonSerializer.Serialize(audioPlan, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "render", "weekly-render-contract.json"), JsonSerializer.Serialize(contract, options));
    }

    private sealed class ThrowingTtsSynthesizer : IWeeklySkyForecastTtsSynthesizer
    {
        public Task SynthesizeSsmlToFileAsync(string ssml, string outputPath, string voiceName, string audioFormat, CancellationToken cancellationToken)
            => throw new InvalidOperationException("TTS should not run during dryRun.");
    }
}
