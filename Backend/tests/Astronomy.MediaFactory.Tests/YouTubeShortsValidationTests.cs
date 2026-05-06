using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class YouTubeShortsValidationTests
{
    [Fact]
    public void Evaluate_MarksPortraitMp4H264AacUnderSixtySecondsValid()
    {
        var diagnostics = YouTubeShortsValidation.Evaluate(
            width: 1080,
            height: 1920,
            durationSeconds: 59.9,
            videoCodec: "h264",
            audioCodec: "aac",
            fps: 30,
            bitrate: 8_000_000,
            container: "mov,mp4,m4a,3gp,3g2,mj2");

        Assert.Equal(1080, diagnostics.Width);
        Assert.Equal(1920, diagnostics.Height);
        Assert.Equal("9:16", diagnostics.AspectRatio);
        Assert.True(diagnostics.IsValidYouTubeShort);
    }

    [Fact]
    public void Evaluate_MarksLandscapeVideoInvalid()
    {
        var diagnostics = YouTubeShortsValidation.Evaluate(
            width: 1920,
            height: 1080,
            durationSeconds: 45,
            videoCodec: "h264",
            audioCodec: "aac",
            fps: 30,
            bitrate: 8_000_000,
            container: "mp4");

        Assert.False(diagnostics.IsValidYouTubeShort);
        var report = YouTubeShortsValidation.BuildValidationReport(diagnostics);
        Assert.False(report.YouTubeShortEligible);
        Assert.Contains(report.Warnings, warning => warning.Contains("not portrait", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnsureShortsMarkerInDescription_AppendsShortsOnce()
    {
        var description = YouTubeShortsValidation.EnsureShortsMarkerInDescription("Meteor shower", "Fast sky guide.");
        var secondPass = YouTubeShortsValidation.EnsureShortsMarkerInDescription("Meteor shower", description);

        Assert.EndsWith("#Shorts", description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(description, secondPass);
        Assert.Equal(1, CountShortsMarkers(secondPass));
    }

    [Fact]
    public async Task ProbeAndWriteDiagnosticsAsync_WritesShortVideoDiagnosticsJson()
    {
        var tempDir = Directory.CreateTempSubdirectory("short-diagnostics");
        var videoPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        await File.WriteAllTextAsync(videoPath, "video");
        var ffprobePath = await WriteFfprobeStubAsync(tempDir.FullName, width: 1080, height: 1920, duration: 42, videoCodec: "h264", audioCodec: "aac");

        var diagnostics = await YouTubeShortsValidation.ProbeAndWriteDiagnosticsAsync(videoPath, tempDir.FullName, ffprobePath, CancellationToken.None);

        var diagnosticsPath = Path.Combine(tempDir.FullName, YouTubeShortsValidation.DiagnosticsFileName);
        Assert.True(File.Exists(diagnosticsPath));
        Assert.Equal(1080, diagnostics.Width);
        Assert.Equal(1920, diagnostics.Height);
        Assert.True(diagnostics.IsValidYouTubeShort);
        var json = await File.ReadAllTextAsync(diagnosticsPath);
        Assert.Contains("isValidYouTubeShort", json);
    }

    [Fact]
    public async Task ValidateBeforeUploadAsync_WritesValidationJsonWithEligibilityFalseForLandscapeVideo()
    {
        var tempDir = Directory.CreateTempSubdirectory("short-upload-validation");
        var videoPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        await File.WriteAllTextAsync(videoPath, "video");
        var ffprobePath = await WriteFfprobeStubAsync(tempDir.FullName, width: 1920, height: 1080, duration: 42, videoCodec: "h264", audioCodec: "aac");

        var report = await YouTubeShortsValidation.ValidateBeforeUploadAsync(videoPath, ffprobePath, CancellationToken.None);

        var validationPath = Path.Combine(tempDir.FullName, YouTubeShortsValidation.UploadValidationFileName);
        Assert.True(File.Exists(validationPath));
        Assert.False(report.YouTubeShortEligible);
        var json = await File.ReadAllTextAsync(validationPath);
        Assert.Contains("youtubeShortEligible", json);
        Assert.Contains("false", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task YouTubeShortsPlatformPublisher_AddsShortsMarkerToUploadMetadata()
    {
        var tempDir = Directory.CreateTempSubdirectory("short-upload-metadata");
        var videoPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        await File.WriteAllTextAsync(videoPath, "video");
        var diagnostics = YouTubeShortsValidation.Evaluate(1080, 1920, 42, "h264", "aac", 30, 8_000_000, "mp4");
        await File.WriteAllTextAsync(
            Path.Combine(tempDir.FullName, YouTubeShortsValidation.DiagnosticsFileName),
            JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var youtube = new CapturingYouTubePublishingService();
        var publisher = new YouTubeShortsPlatformPublisher(
            youtube,
            Options.Create(new YouTubeOptions { PublishingEnabled = true, PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()));

        var result = await publisher.PublishAsync(new PlatformPublicationTarget
        {
            Platform = ShortFormPlatform.YouTubeShorts,
            Enabled = true,
            Title = "Meteor shower",
            Caption = "Fast sky guide.",
            Hashtags = ["#astronomy"],
            VideoPath = videoPath
        }, CancellationToken.None);

        Assert.Equal(PlatformPublicationStatus.Published, result.Status);
        Assert.True(result.YouTubeShortEligible);
        Assert.Contains("#Shorts", youtube.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountShortsMarkers(youtube.Description));
    }

    private static int CountShortsMarkers(string value)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = value.IndexOf("#Shorts", start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return count;
            count += 1;
            start = index + "#Shorts".Length;
        }
    }

    private static async Task<string> WriteFfprobeStubAsync(string directory, int width, int height, double duration, string videoCodec, string audioCodec)
    {
        var path = Path.Combine(directory, "ffprobe-stub.sh");
        var json = $$"""
{
  "streams": [
    { "codec_type": "video", "codec_name": "{{videoCodec}}", "width": {{width}}, "height": {{height}}, "avg_frame_rate": "30/1" },
    { "codec_type": "audio", "codec_name": "{{audioCodec}}" }
  ],
  "format": { "duration": "{{duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}}", "bit_rate": "8000000", "format_name": "mov,mp4,m4a,3gp,3g2,mj2" }
}
""";
        await File.WriteAllTextAsync(path, $"#!/usr/bin/env bash\ncat <<'JSON'\n{json}\nJSON\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private sealed class CapturingYouTubePublishingService : IYouTubePublishingService
    {
        public string Description { get; private set; } = string.Empty;

        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
        {
            Description = description;
            return Task.FromResult<string?>("yt-short-123");
        }
    }
}
