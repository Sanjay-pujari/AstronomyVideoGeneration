using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using System.Text.Json;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase18VideoAssemblyAuthorityTests
{
    [Fact]
    public void Phase18ConfiguredToolchainResolvesFullExecutablePath()
    {
        var executable = Environment.ProcessPath!;
        var resolved = Phase18MediaToolchainResolver.Resolve(executable, "P18_UNUSED_TOOL", "ffmpeg");

        Assert.Equal(Path.GetFullPath(executable), resolved.Path);
        Assert.Equal("Configuration", resolved.Source);
    }

    [Fact]
    public void Phase18PathToolchainResolvesWithoutConfiguredPath()
    {
        var executable = OperatingSystem.IsWindows() ? "cmd.exe" : "sh";
        var resolved = Phase18MediaToolchainResolver.Resolve(null, "P18_UNUSED_TOOL", executable);

        Assert.True(Path.IsPathRooted(resolved.Path));
        Assert.Equal("PATH", resolved.Source);
    }

    [Fact]
    public void Phase18MissingToolchainDoesNotEscapeRawWin32Exception()
    {
        var exception = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18MediaToolchainResolver.Resolve(null, "P18_UNUSED_TOOL", $"missing-phase18-{Guid.NewGuid():N}"));

        Assert.Equal(Phase18ReasonCodes.MediaToolchainUnavailable, exception.ReasonCode);
        Assert.Contains("could not be resolved or started", exception.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase18NeverUsesShortestToTerminateVideo()
    {
        Assert.False(Phase18VideoAssemblyAuthorityPublisher.CanonicalArgumentsAreSafe(["-shortest"]));
        Assert.False(Phase18VideoAssemblyAuthorityPublisher.CanonicalArgumentsAreSafe(["-af", "atrim=end=1"]));
        Assert.True(Phase18VideoAssemblyAuthorityPublisher.CanonicalArgumentsAreSafe(["-af", "apad=whole_dur=30"]));
    }

    [Theory]
    [InlineData(@"D:\test\subs\final.srt", "subtitles=filename='D\\:/test/subs/final.srt'")]
    [InlineData(@"D:\Astronomy Workspace\Test Subs\final.srt", "subtitles=filename='D\\:/Astronomy Workspace/Test Subs/final.srt'")]
    [InlineData(@"D:\Astronomy Workspace\Observer's Guide\final.srt", "subtitles=filename='D\\:/Astronomy Workspace/Observer'\\\\''s Guide/final.srt'")]
    [InlineData(@"D:\Astronomy Workspace\天文\final.srt", "subtitles=filename='D\\:/Astronomy Workspace/天文/final.srt'")]
    public void Phase18SubtitleFilterEscapesWindowsAbsolutePaths(string path, string expected)
    {
        var runtimeFilter = Phase18VideoAssemblyAuthorityPublisher.BuildSubtitleFilter(path);

        Assert.Equal(expected, runtimeFilter);
        Assert.Equal(['D', '\\', ':', '/'], runtimeFilter.Skip("subtitles=filename='".Length).Take(4).ToArray());
        Assert.DoesNotContain("D\\\\:", runtimeFilter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Phase18ConfiguredWindowsFfmpegBurnsSubtitlesFromRepresentativeDrivePaths()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ffmpeg = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? @"D:\AstronomyWorkspace\ffmpeg\bin\ffmpeg.exe";
        if (!File.Exists(ffmpeg) || !Directory.Exists(@"D:\")) return;

        var root = Path.Combine(@"D:\", "phase18-subtitle-filter-tests", Guid.NewGuid().ToString("N"));
        var paths = new[]
        {
            Path.Combine(root, "test", "subs", "final.srt"),
            Path.Combine(root, "Astronomy Workspace", "Test Subs", "final.srt"),
            Path.Combine(root, "Observer's Guide", "final.srt"),
            Path.Combine(root, "AstronomyWorkspace", "Astronomy", "media-output", "plans", "GLOBAL", "2026",
                Guid.NewGuid().ToString("N"), "18-video-assembly", ".staging", Guid.NewGuid().ToString("N"), "en", "short", "final.srt")
        };

        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.mp4");
            await Phase18VideoAssemblyAuthorityPublisher.Run(ffmpeg,
                ["-y", "-f", "lavfi", "-i", "color=c=black:s=160x90:d=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", input],
                new MediaProcessContext("SubtitleFixture", "Short"), root, CancellationToken.None);
            foreach (var subtitle in paths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(subtitle)!);
                await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:00,800\nPhase 18\n");
                var output = Path.Combine(Path.GetDirectoryName(subtitle)!, "burned.mp4");
                var filter = Phase18VideoAssemblyAuthorityPublisher.BuildSubtitleFilter(subtitle);
                var result = await Phase18VideoAssemblyAuthorityPublisher.Run(ffmpeg,
                    ["-y", "-i", input, "-vf", filter, "-c:v", "libx264", output],
                    new MediaProcessContext("SubtitleBurn", "Short"), root, CancellationToken.None);

                Assert.Equal(filter, result.Arguments[4]);
                Assert.Equal(0, result.ExitCode);
                Assert.True(new FileInfo(output).Length > 0);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Phase18CodecPolicyIsFrozenForShortAndLong()
    {
        var policy = Phase18VideoAssemblyAuthorityPublisher.VideoPolicy;
        Assert.Equal((1080, 1920), (policy.ShortWidth, policy.ShortHeight));
        Assert.Equal((1280, 720), (policy.LongWidth, policy.LongHeight));
        Assert.Equal(30, policy.FramesPerSecond);
        Assert.Equal("libx264", policy.Encoder);
        Assert.Equal("yuv420p", policy.PixelFormat);
        Assert.Equal("veryfast", policy.Preset);
    }

    [Fact]
    public void Phase18AudioAndSubtitlePoliciesAreExplicit()
    {
        var audio = Phase18VideoAssemblyAuthorityPublisher.AudioPolicy;
        Assert.Equal(("aac", 48_000, 2, 192_000), (audio.Codec, audio.SampleRate, audio.Channels, audio.Bitrate));
        Assert.Equal(Phase18SubtitleMode.SidecarOnly,
            Phase18VideoAssemblyAuthorityPublisher.SubtitlePolicy.EnglishMode);
        Assert.Equal(Phase18SubtitleMode.SidecarOnly,
            Phase18VideoAssemblyAuthorityPublisher.SubtitlePolicy.HindiMode);
        Assert.Equal((34, 22), (Phase18VideoAssemblyAuthorityPublisher.SubtitlePolicy.ShortFontSize,
            Phase18VideoAssemblyAuthorityPublisher.SubtitlePolicy.LongFontSize));
    }

    [Fact]
    public void Phase18MotionFilterConsumesGovernedTransformsFocalPointEasingAndFades()
    {
        var entry = Motion(Phase17MotionType.SlowZoomIn,
            new(1, 0, 0), new(1.13, .2, -.1), Phase17Easing.EaseInOut,
            new(Phase17TransitionType.FadeThroughBlack, 400), new(Phase17TransitionType.FadeThroughBlack, 500));

        var filter = Phase18VideoAssemblyAuthorityPublisher.BuildMotionFilter(entry, 1080, 1920);

        Assert.Contains("zoompan=z='1+(0.13)*((1-cos(PI*on/239))/2)'", filter);
        Assert.Contains("fade=t=in:st=0:d=0.4", filter);
        Assert.Contains("fade=t=out:st=7.5:d=0.5", filter);
        Assert.Contains("s=1080x1920:fps=30", filter);
    }

    [Theory]
    [InlineData(Phase17MotionType.SlowZoomOut, 1.13, 1.0)]
    [InlineData(Phase17MotionType.PanLeft, 1.08, 1.08)]
    [InlineData(Phase17MotionType.ZoomInPanRight, 1.0, 1.18)]
    [InlineData(Phase17MotionType.Hold, 1.0, 1.0)]
    public void Phase18SupportsGovernedCinematicMotionVocabulary(Phase17MotionType type, double start, double end)
    {
        var filter = Phase18VideoAssemblyAuthorityPublisher.BuildMotionFilter(
            Motion(type, new(start, -.2, 0), new(end, .2, 0), Phase17Easing.Linear,
                new(Phase17TransitionType.Cut, 0), new(Phase17TransitionType.Cut, 0)), 1280, 720);
        Assert.Contains("zoompan", filter);
        Assert.Contains("s=1280x720", filter);
    }

    [Fact]
    public void Phase18BurnInSidecarPathCannotCollideWithFinalVideoBasename()
    {
        var relative = "short/captions/en.srt";
        Assert.NotEqual(Path.GetFileNameWithoutExtension("short/final.mp4"), Path.GetFileNameWithoutExtension(relative));
        Assert.Contains("FontSize=34", Phase18VideoAssemblyAuthorityPublisher.BuildSubtitleFilter("captions/en.srt", Phase18ProductionFormat.Short));
    }

    private static Phase17MotionEntry Motion(Phase17MotionType type, Phase17NormalizedTransform start,
        Phase17NormalizedTransform end, Phase17Easing easing, Phase17Transition transitionIn,
        Phase17Transition transitionOut) => new("scene", "unit", "Short", 1, "en", 8_000, 0, 8_000,
            [], "audio", "image.png", "visual", 1920, 1080, "Portrait", "p16", "visual-authority", "p10",
            "Hero", type, start, end, [], easing, null, new(.2, .2, .4, .4), [],
            Phase17SafetyDecision.CertifiedRegionSafe, false, transitionIn, transitionOut, "motion", "safety");

    [Fact]
    public void Phase18AcceptedAuthorityProjectsGovernedReasonCode()
    {
        Assert.Equal("P18_VIDEO_ASSEMBLY_AUTHORITY_ACCEPTED", Phase18ReasonCodes.Accepted);
        Assert.NotEqual(Phase18ReasonCodes.Accepted, Phase18ReasonCodes.UpstreamPhase17Invalid);
    }

    [Fact]
    public async Task Phase18AcceptsCurrentPhase15ArtifactFieldOwnership()
    {
        using var fixture = Phase15Fixture.Create();
        var snapshot = await Phase18VideoAssemblyAuthorityPublisher.LoadPhase15AuthorityAsync(
            fixture.Files, "en", CancellationToken.None);

        Assert.True(snapshot.SemanticValidationPassed);
        Assert.True(snapshot.PublicationCommitted);
        Assert.Equal(fixture.Checksum, snapshot.AuthorityChecksum);
        Assert.Equal(fixture.Files, snapshot.LoadedAuthorityArtifacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Phase18FailsClosedOnInvalidCanonicalSemanticOwner(bool? semanticValidationPassed)
    {
        using var fixture = Phase15Fixture.Create(semanticValidationPassed);
        var error = await Assert.ThrowsAsync<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.LoadPhase15AuthorityAsync(fixture.Files, "en", CancellationToken.None));

        Assert.Equal(Phase18ReasonCodes.UpstreamPhase15Invalid, error.ReasonCode);
        Assert.Equal(fixture.Files, error.LoadedAuthorityArtifacts);
        Assert.Contains("semanticValidationPassed", error.Message);
    }

    [Fact]
    public async Task Phase18Phase15ChecksumMismatchReportsLoadedAuthorityArtifacts()
    {
        using var fixture = Phase15Fixture.Create(reportChecksum: "different");
        var error = await Assert.ThrowsAsync<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.LoadPhase15AuthorityAsync(fixture.Files, "en", CancellationToken.None));

        Assert.Equal(Phase18ReasonCodes.UpstreamPhase15Invalid, error.ReasonCode);
        Assert.Equal(3, error.LoadedAuthorityArtifacts.Count);
    }

    [Fact]
    public void Phase18AcceptsDistinctAuthoritiesWithCorrectReferences()
    {
        var result = Phase18VideoAssemblyAuthorityPublisher.ValidateAuthorityLineage(
            "AAA", "P14", "BBB", "AAA", "CCC", "BBB", ["p15", "p16", "p17"]);

        Assert.True(result.Phase15To16LineagePassed);
        Assert.True(result.Phase16To17LineagePassed);
        Assert.True(result.OverallLineagePassed);
        Assert.NotEqual(result.Phase15AuthorityChecksum, result.Phase16AuthorityChecksum);
        Assert.NotEqual(result.Phase16AuthorityChecksum, result.Phase17AuthorityChecksum);
    }

    [Fact]
    public void Phase18Phase15To16MismatchIsPreciseAndReportsLoadedArtifacts()
    {
        var files = new[] { "phase15-manifest.json", "phase16-manifest.json", "phase17-manifest.json" };
        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateAuthorityLineage(
                "AAA", "P14", "BBB", "XXX", "CCC", "BBB", files));

        Assert.Equal(Phase18ReasonCodes.LineageMismatch, error.ReasonCode);
        Assert.Equal(files, error.LoadedAuthorityArtifacts);
        Assert.Contains("Expected 'AAA', actual 'XXX'", error.Reason);
    }

    [Fact]
    public void Phase18Phase16To17MismatchIsPreciseAndReportsReasonCode()
    {
        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateAuthorityLineage(
                "AAA", "P14", "BBB", "AAA", "CCC", "YYY", ["p15", "p16", "p17"]));

        Assert.Equal("P18_LINEAGE_MISMATCH", error.ReasonCode);
        Assert.Contains("Expected 'BBB', actual 'YYY'", error.Reason);
    }

    [Theory]
    [InlineData("different-scene", "hash", 1, 0, 100)]
    [InlineData("scene", "different-hash", 1, 0, 100)]
    [InlineData("scene", "hash", 101, 0, 101)]
    public void Phase18RejectsRowPhysicalLineageMismatch(string sceneId, string audioHash,
        long duration, long start, long end)
    {
        var p15 = new Phase18SceneLineageRow("unit", "scene", "Short", 1, "en", "hash", 0, 0, 0, 1);
        var p16 = p15 with { DurationMs = 100, SceneEndMs = 100 };
        var p17 = p16 with { SceneId = sceneId, AudioSha256 = audioHash, DurationMs = duration,
            SceneStartMs = start, SceneEndMs = end };

        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage([p15], [p16], [p17], ["p15", "p16", "p17"]));
        Assert.Equal(Phase18ReasonCodes.LineageMismatch, error.ReasonCode);
        Assert.Contains("Phase17.", error.Reason);
    }

    [Fact]
    public void Phase18AllowsPhysicalAudioShorterThanFinalScene()
    {
        var p15 = Row(audioHash: "AAA", actualAudioDurationMs: 24_000);
        var p16 = Row(audioHash: "AAA", durationMs: 30_000, endMs: 30_000);
        var p17 = Row(audioHash: "AAA", durationMs: 30_000, endMs: 30_000);

        Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage([p15], [p16], [p17], ["p15", "p16", "p17"]);
    }

    [Fact]
    public void Phase18RejectsPhysicalAudioLongerThanFinalSceneWithPreciseDiagnostic()
    {
        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage(
                [Row(audioHash: "AAA", actualAudioDurationMs: 31_000)],
                [Row(audioHash: "AAA", durationMs: 30_000, endMs: 30_000)],
                [Row(audioHash: "AAA", durationMs: 30_000, endMs: 30_000)], ["p15", "p16", "p17"]));

        Assert.Contains("Phase15.ActualAudioDurationMs='31000'", error.Reason);
        Assert.Contains("Phase16.FinalSceneDurationMs+codecToleranceMs(35)='30035'", error.Reason);
    }

    [Fact]
    public void Phase18ValidatesPhase17AudioHashAgainstPhase15()
    {
        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage(
                [Row(audioHash: "AAA", actualAudioDurationMs: 100)],
                [Row(audioHash: "AAA", durationMs: 100, endMs: 100)],
                [Row(audioHash: "BBB", durationMs: 100, endMs: 100)], ["p15", "p16", "p17"]));

        Assert.Contains("Phase17.AudioSha256='BBB' but Phase15.AudioSha256='AAA'", error.Reason);
    }

    [Fact]
    public void Phase18RequiresPhase16CopiedAudioHashLineage()
    {
        var p15 = Row(audioHash: "AAA", actualAudioDurationMs: 100);
        var p17 = Row(audioHash: "AAA", durationMs: 100, endMs: 100);
        var missing = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage(
                [p15], [Row(audioHash: null, durationMs: 100, endMs: 100)], [p17], ["p15", "p16", "p17"]));
        Assert.Contains("Phase16.AudioSha256=<absent>", missing.Reason);

        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage(
                [p15], [Row(audioHash: "BBB", durationMs: 100, endMs: 100)], [p17], ["p15", "p16", "p17"]));
        Assert.Contains("Phase16.AudioSha256='BBB' but Phase15.AudioSha256='AAA'", error.Reason);
    }

    [Fact]
    public void Phase18RequiresExactPhase16ToPhase17Timing()
    {
        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage(
                [Row(audioHash: "AAA", actualAudioDurationMs: 100)],
                [Row(audioHash: "AAA", durationMs: 30_000, endMs: 30_000)],
                [Row(audioHash: "AAA", durationMs: 29_999, endMs: 30_000)], ["p15", "p16", "p17"]));
        Assert.Contains("Phase17.DurationMs='29999' but Phase16.FinalSceneDurationMs='30000'", error.Reason);
    }

    [Theory]
    [InlineData("short", Phase18ProductionFormat.Short)]
    [InlineData("Short", Phase18ProductionFormat.Short)]
    [InlineData("SHORT", Phase18ProductionFormat.Short)]
    [InlineData("long", Phase18ProductionFormat.Long)]
    [InlineData("Long", Phase18ProductionFormat.Long)]
    [InlineData("LONG", Phase18ProductionFormat.Long)]
    [InlineData(" short ", Phase18ProductionFormat.Short)]
    public void Phase18ParsesOnlyDocumentedProductionFormatCasing(string raw, Phase18ProductionFormat expected) =>
        Assert.Equal(expected, Phase18VideoAssemblyAuthorityPublisher.ParseProductionFormat(raw));

    [Theory]
    [InlineData("short", "Short")]
    [InlineData("Short", "short")]
    [InlineData("SHORT", "Short")]
    [InlineData("long", "Long")]
    [InlineData("Long", "long")]
    [InlineData("LONG", "Long")]
    public void Phase18ComparesFormatByClosedSemanticType(string phase15Format, string phase17Format)
    {
        var p15 = Row("AAA", actualAudioDurationMs: 100) with { Format = phase15Format };
        var p16 = Row("AAA", durationMs: 100, endMs: 100) with { Format = phase15Format };
        var p17 = Row("AAA", durationMs: 100, endMs: 100) with { Format = phase17Format };

        Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage([p15], [p16], [p17], ["p15", "p16", "p17"]);
    }

    [Theory]
    [InlineData("portrait")]
    [InlineData("shortvideo")]
    [InlineData("")]
    [InlineData(null)]
    public void Phase18RejectsUndocumentedProductionFormats(string? raw)
    {
        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ParseProductionFormat(raw));
        Assert.Equal(Phase18ReasonCodes.FormatInvalid, error.ReasonCode);
    }

    [Fact]
    public void Phase18SceneAudioUnitIdsRemainOrdinal()
    {
        var p15 = Row("AAA", actualAudioDurationMs: 100) with { SceneAudioUnitId = "sau-ABC", Format = "short" };
        var p16 = Row("AAA", durationMs: 100, endMs: 100) with { SceneAudioUnitId = "sau-ABC", Format = "short" };
        var p17 = Row("AAA", durationMs: 100, endMs: 100) with { SceneAudioUnitId = "sau-abc", Format = "Short" };

        var error = Assert.Throws<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage([p15], [p16], [p17], []));
        Assert.Contains("Phase15.SceneAudioUnitId='<missing>'", error.Reason);
    }

    [Fact]
    public async Task Phase18ReadsCanonicalPhase15EntriesArray()
    {
        using var fixture = CanonicalTimelineFixture.Create();
        var entries = await Phase18VideoAssemblyAuthorityPublisher.ReadTimeline15(
            fixture.Path, fixture.Authority, CancellationToken.None);

        var entry = Assert.Single(entries).Value;
        Assert.Equal(1, entry.Sequence);
        Assert.Equal("15-tts/en/short/unit.mp3", entry.AudioRelativePath);
        Assert.Equal(12_345, entry.AudioByteLength);
        Assert.Equal("AAA", entry.AudioSha256);
        Assert.Equal(24_000, entry.ActualAudioDurationMs);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("long")]
    public async Task Phase18DoesNotDeserializeCompatibilityItemsAsPhase15Entry(string compatibilityFormat)
    {
        using var fixture = CanonicalTimelineFixture.Create(compatibilityFormat);
        var entries = await Phase18VideoAssemblyAuthorityPublisher.ReadTimeline15(
            fixture.Path, fixture.Authority, CancellationToken.None);

        Assert.Single(entries);
        Assert.DoesNotContain(entries.Keys, key => key.StartsWith("compat-", StringComparison.Ordinal));
    }

    [Fact]
    public void Phase18AcceptsAuthorityDrivenNonFourTwelveCounts()
    {
        var formats = Enumerable.Repeat("Short", 3).Concat(Enumerable.Repeat("Long", 8)).ToArray();
        Phase18VideoAssemblyAuthorityPublisher.ValidateAuthorityDrivenSceneCounts(
            formats, formats.Select(x => x.ToLowerInvariant()), formats, ["p15", "p16", "p17"]);
    }

    [Fact]
    public void Phase18CurrentStyleFourShortAndTwelveLongRowsPassLineage()
    {
        Phase18SceneLineageRow Make(int index, string format, bool audio) =>
            new($"sau-{index:00}", $"scene-{index:00}", format, index, "en", $"hash-{index:00}",
                audio ? 0 : 1_000, (index - 1) * 1_000, index * 1_000, audio ? 900 : 0);
        var p15 = Enumerable.Range(1, 16).Select(i => Make(i, i <= 4 ? "short" : "long", true)).ToArray();
        var p16 = Enumerable.Range(1, 16).Select(i => Make(i, i <= 4 ? "short" : "long", false)).ToArray();
        var p17 = Enumerable.Range(1, 16).Select(i => Make(i, i <= 4 ? "Short" : "Long", false)).ToArray();

        Phase18VideoAssemblyAuthorityPublisher.ValidateSceneLineage(p15, p16, p17, ["p15", "p16", "p17"]);
    }

    [Fact]
    public async Task Phase18ProcessFailurePrioritizesStderrTailAndPreservesContext()
    {
        if (OperatingSystem.IsWindows()) return;
        var banner = new string('B', 7_000);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.Run("/bin/sh", ["-c", $"printf '%s\\n' '{banner}' >&2; echo 'ACTUAL ERROR AT END' >&2; exit 1"],
                new MediaProcessContext("SceneRender", "Short", 3, "sau-003", "scene-003"), Path.GetTempPath(), CancellationToken.None));

        Assert.Contains("ACTUAL ERROR AT END", error.Message);
        Assert.DoesNotContain(banner[..2_000], error.Message);
        Assert.Equal("SceneRender", error.Data["renderOperation"]);
        Assert.Equal("Short", error.Data["renderFormat"]);
        Assert.Equal(3, error.Data["renderSequence"]);
        Assert.Equal("sau-003", error.Data["renderSceneAudioUnitId"]);
        Assert.Contains("ACTUAL ERROR AT END", Assert.IsType<string>(error.Data["ffmpegStderrTail"]));
    }

    [Fact]
    public async Task Phase18SuccessfulProcessCapturesSafeArgumentListAndOutput()
    {
        if (OperatingSystem.IsWindows()) return;
        var result = await Phase18VideoAssemblyAuthorityPublisher.Run("/bin/sh", ["-c", "printf success"],
            new MediaProcessContext("SceneRender", "Long", 1, "sau-001", "scene-001"), Path.GetTempPath(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("success", result.StdOut);
        Assert.Equal(["-c", "printf success"], result.Arguments);
        Assert.True(result.DurationMs >= 0);
    }

    private static Phase18SceneLineageRow Row(string? audioHash, long durationMs = 0, long endMs = 0,
        long actualAudioDurationMs = 0) =>
        new("unit", "scene", "Short", 1, "en", audioHash, durationMs, 0, endMs, actualAudioDurationMs);

    private sealed class CanonicalTimelineFixture : IDisposable
    {
        private CanonicalTimelineFixture(string root, string path, Phase18Phase15AuthoritySnapshot authority)
        { Root = root; Path = path; Authority = authority; }
        public string Root { get; }
        public string Path { get; }
        public Phase18Phase15AuthoritySnapshot Authority { get; }

        public static CanonicalTimelineFixture Create(string compatibilityFormat = "both")
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phase18-timeline-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = System.IO.Path.Combine(root, "tts-timeline.json");
            object Item(string format) => new { sceneAudioUnitId = $"compat-{format}", sceneId = "wrong",
                cueIndex = 1, format, audioPath = "wrong.mp3", durationSec = 1 };
            var shortItems = compatibilityFormat is "short" or "both" ? new[] { Item("short") } : [];
            var longItems = compatibilityFormat is "long" or "both" ? new[] { Item("long") } : [];
            File.WriteAllText(path, JsonSerializer.Serialize(new { authorityChecksum = "P15", sourcePhase14AuthorityChecksum = "P14",
                entries = new[] { new { sceneAudioUnitId = "unit", sceneId = "scene", sequence = 1, format = "Short", language = "en",
                    audioRelativePath = "15-tts/en/short/unit.mp3", audioByteLength = 12_345, audioSha256 = "AAA", textChecksum = "TEXT",
                    actualAudioDurationMs = 24_000, voiceProfileRef = "voice", speechStyleRef = "style", resolvedVoice = "resolved",
                    resolvedRate = "1", resolvedStyle = "neutral", providerRequestId = "request", subtitleSegmentIds = new[] { "sub-1" },
                    sourcePhase14AuthorityChecksum = "P14" } },
                @short = new { items = shortItems }, @long = new { items = longItems } }));
            var authority = new Phase18Phase15AuthoritySnapshot("en", "P15", "P14", true, true, true, true, true,
                true, true, true, "Valid", true, [path]);
            return new(root, path, authority);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class Phase15Fixture : IDisposable
    {
        private Phase15Fixture(string root, string[] files, string checksum)
        { Root = root; Files = files; Checksum = checksum; }
        public string Root { get; }
        public string[] Files { get; }
        public string Checksum { get; }

        public static Phase15Fixture Create(bool? semanticValidationPassed = true, string? reportChecksum = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "phase18-phase15-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            const string checksum = "phase15-checksum";
            const string source = "phase14-checksum";
            var files = new[] { "phase15-manifest.json", "phase15-publication-report.json", "phase-15-validation.json" }
                .Select(name => Path.Combine(root, name)).ToArray();
            // These shapes intentionally mirror the frozen publisher: semantic/checksum/manifest
            // gates are absent from both manifest and publication report.
            File.WriteAllText(files[0], JsonSerializer.Serialize(new { schemaVersion = "phase15.manifest/1.0",
                language = "en", sourcePhase14AuthorityChecksum = source, authorityChecksum = checksum,
                validationStatus = "Valid", publicationCommitted = true, downstreamReady = true }));
            File.WriteAllText(files[1], JsonSerializer.Serialize(new { schemaVersion = "phase15.publication/1.0",
                candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true,
                committedReadbackPassed = true, committedStateValidationPassed = true, downstreamReady = true,
                sourcePhase14AuthorityChecksum = source, authorityChecksum = reportChecksum ?? checksum }));
            var validation = new Dictionary<string, object?> { ["phaseNo"] = 15, ["status"] = "Succeeded",
                ["reasonCode"] = "P15_TTS_AUTHORITY_ACCEPTED", ["sourcePhase14AuthorityChecksum"] = source,
                ["authorityChecksum"] = checksum, ["validationStatus"] = "Valid",
                ["semanticValidationPassed"] = semanticValidationPassed, ["checksumValidationPassed"] = true,
                ["manifestValidationPassed"] = true, ["downstreamReady"] = true };
            if (semanticValidationPassed is null) validation.Remove("semanticValidationPassed");
            File.WriteAllText(files[2], JsonSerializer.Serialize(validation));
            return new(root, files, checksum);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
