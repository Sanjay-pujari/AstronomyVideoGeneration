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
        var existingLongformPath = Path.Combine(runRoot, "audio", "longform", "weekly-skyforecast-longform.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(existingLongformPath)!);
        await File.WriteAllTextAsync(existingLongformPath, "previous real-run audio");

        var service = new WeeklySkyForecastAudioGenerationService(
            Options.Create(new RenderingOptions { WorkingDirectory = workingRoot }),
            Options.Create(new AzureSpeechOptions { DefaultVoiceName = "hi-IN-MadhurNeural" }),
            new StaticWeeklyPipelineRunDirectoryResolver(runRoot),
            new ThrowingTtsSynthesizer(),
            NullLogger<WeeklySkyForecastAudioGenerationService>.Instance);

        var response = await service.GenerateAsync(pipelineRunId, new WeeklySkyForecastAudioGenerationRequest(DryRun: true), CancellationToken.None);

        response.DryRun.Should().BeTrue();
        response.AudioGenerationReady.Should().BeTrue();
        response.LongformAudioGenerated.Should().BeFalse();
        response.ShortformAudioGenerated.Should().BeFalse();
        response.LongformSegmentAudioCount.Should().Be(0);
        response.ShortformSegmentAudioCount.Should().Be(0);
        response.PlannedLongformSegmentAudioCount.Should().Be(2);
        response.PlannedShortformSegmentAudioCount.Should().Be(1);
        response.LongformCombinedAudioPath.Should().BeNull();
        response.ShortformCombinedAudioPath.Should().BeNull();
        response.PlannedLongformCombinedAudioPath.Should().Be(existingLongformPath);
        response.PlannedShortformCombinedAudioPath.Should().Be(Path.Combine(runRoot, "audio", "shortform", "weekly-skyforecast-shortform.mp3"));
        response.ExistingLongformCombinedAudioPath.Should().Be(existingLongformPath);
        response.ExistingShortformCombinedAudioPath.Should().BeNull();
        response.Warnings.Should().Contain("Dry run completed. TTS was not called and MP3 files were not generated.");
        File.Exists(Path.Combine(runRoot, "audio", "temp", "longform", "hero.ssml")).Should().BeTrue();
        File.Exists(Path.Combine(runRoot, "audio", "segments", "longform", "hero.mp3")).Should().BeFalse();
        File.Exists(response.AudioGenerationReportPath).Should().BeTrue();
        File.Exists(response.AudioSegmentManifestPath).Should().BeTrue();
        File.Exists(response.AudioTimingValidationReportPath).Should().BeTrue();
        response.NarrationParsingReady.Should().BeTrue();
        response.LongformNormalizedSegmentCount.Should().Be(2);
        response.ShortformNormalizedSegmentCount.Should().Be(1);
        File.Exists(response.NormalizedLongformNarrationPath).Should().BeTrue();
        File.Exists(response.NormalizedShortformNarrationPath).Should().BeTrue();

        using var reportDocument = JsonDocument.Parse(await File.ReadAllTextAsync(response.AudioGenerationReportPath));
        reportDocument.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        reportDocument.RootElement.GetProperty("ttsCalled").GetBoolean().Should().BeFalse();
        reportDocument.RootElement.GetProperty("mp3Generated").GetBoolean().Should().BeFalse();
        reportDocument.RootElement.GetProperty("audioConcatExecuted").GetBoolean().Should().BeFalse();
        reportDocument.RootElement.GetProperty("longformAudioGenerated").GetBoolean().Should().BeFalse();
        reportDocument.RootElement.GetProperty("shortformAudioGenerated").GetBoolean().Should().BeFalse();

        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(response.AudioSegmentManifestPath));
        manifestDocument.RootElement.GetProperty("longform")[0].GetProperty("status").GetString().Should().Be("Planned");
    }

    [Fact]
    public async Task GenerateAudio_WhenNarrationSegmentsAreMissing_ThrowsValidationError()
    {
        var pipelineRunId = Guid.NewGuid();
        var workingRoot = Path.Combine(Path.GetTempPath(), "weekly-audio-tests", Guid.NewGuid().ToString("N"));
        var runRoot = Path.Combine(workingRoot, pipelineRunId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(runRoot, "episode"));
        Directory.CreateDirectory(Path.Combine(runRoot, "render"));
        await WriteInputsAsync(runRoot, pipelineRunId);

        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var longformWithMissingSegments = new
        {
            PipelineRunId = pipelineRunId,
            GeneratedAtUtc = DateTime.UtcNow,
            Language = "hi",
            Style = "documentary",
            TargetDurationSeconds = 380,
            TotalEstimatedDurationSeconds = 84,
            Segments = (object?)null
        };
        await File.WriteAllTextAsync(Path.Combine(runRoot, "episode", "longform-narration.json"), JsonSerializer.Serialize(longformWithMissingSegments, options));

        var service = new WeeklySkyForecastAudioGenerationService(
            Options.Create(new RenderingOptions { WorkingDirectory = workingRoot }),
            Options.Create(new AzureSpeechOptions { DefaultVoiceName = "hi-IN-MadhurNeural" }),
            new StaticWeeklyPipelineRunDirectoryResolver(runRoot),
            new ThrowingTtsSynthesizer(),
            NullLogger<WeeklySkyForecastAudioGenerationService>.Instance);

        var act = () => service.GenerateAsync(pipelineRunId, new WeeklySkyForecastAudioGenerationRequest(DryRun: true), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unable to read longform narration.*");
    }

    [Fact]
    public async Task GenerateAudio_WhenPascalCaseNarrationIsMissingMetadata_NormalizesWithWarnings()
    {
        var pipelineRunId = Guid.NewGuid();
        var workingRoot = Path.Combine(Path.GetTempPath(), "weekly-audio-tests", Guid.NewGuid().ToString("N"));
        var runRoot = Path.Combine(workingRoot, pipelineRunId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(runRoot, "episode"));
        Directory.CreateDirectory(Path.Combine(runRoot, "render"));
        await WriteInputsAsync(runRoot, pipelineRunId);

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(runRoot, "episode", "longform-narration.json"), JsonSerializer.Serialize(new
        {
            Segments = new[]
            {
                new { SegmentId = "hero", SegmentType = "HeroEvent", NarrationText = "आज रात आकाश देखें।", EstimatedDurationSeconds = 84 },
                new { SegmentId = "summary", SegmentType = "WeeklySummary", NarrationText = "यह सप्ताह शांत है।", EstimatedDurationSeconds = 20 }
            }
        }, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "episode", "shortform-narration.json"), JsonSerializer.Serialize(new[]
        {
            new { segmentId = "short-hook", segmentType = "ShortHook", narrationText = "आज आसमान देखें।", estimatedDurationSeconds = 15 }
        }, options));

        var service = new WeeklySkyForecastAudioGenerationService(
            Options.Create(new RenderingOptions { WorkingDirectory = workingRoot }),
            Options.Create(new AzureSpeechOptions { DefaultVoiceName = "hi-IN-MadhurNeural" }),
            new StaticWeeklyPipelineRunDirectoryResolver(runRoot),
            new ThrowingTtsSynthesizer(),
            NullLogger<WeeklySkyForecastAudioGenerationService>.Instance);

        var response = await service.GenerateAsync(pipelineRunId, new WeeklySkyForecastAudioGenerationRequest(DryRun: true, Language: "hi"), CancellationToken.None);

        response.NarrationParsingReady.Should().BeTrue();
        response.LongformNormalizedSegmentCount.Should().Be(2);
        response.ShortformNormalizedSegmentCount.Should().Be(1);
        response.Warnings.Should().Contain(w => w.Contains("missing pipelineRunId", StringComparison.OrdinalIgnoreCase));
        response.Warnings.Should().Contain(w => w.Contains("missing language", StringComparison.OrdinalIgnoreCase));
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

    private sealed class StaticWeeklyPipelineRunDirectoryResolver(string root) : IWeeklyPipelineRunDirectoryResolver
    {
        public Task<string> ResolveRunDirectoryAsync(Guid pipelineRunId) => Task.FromResult(root);
    }
}
