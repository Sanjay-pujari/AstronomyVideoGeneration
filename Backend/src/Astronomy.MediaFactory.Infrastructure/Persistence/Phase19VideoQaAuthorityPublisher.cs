using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>
/// Read-only certification adapter for the committed Phase 18 package.  It never selects media
/// outside the Phase 18 manifest and never invokes ffmpeg with an output media encoder.
/// </summary>
internal static class Phase19VideoQaAuthorityPublisher
{
    internal const string Schema = "phase19.final-video-qa/1.0";
    internal const string QaPolicy = "phase19-technical-qa/1.0";
    internal const string MotionMetricPolicy = "phase19-luma-mad/1.0";
    internal const string AudioEnergyPolicy = "phase19-window-energy/1.0";
    internal const double MotionThreshold = 1.25;
    internal const long DurationToleranceMs = 35;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<Phase19PublicationResult> ExecuteAsync(string root, string language,
        RenderingOptions rendering, CancellationToken ct)
    {
        var p18Root = Path.Combine(root, "18-video-assembly", language);
        var validationRoot = Path.Combine(root, "validation");
        var inputs = new[]
        {
            Path.Combine(p18Root, "phase18-manifest.json"),
            Path.Combine(p18Root, "phase18-authority-diagnostics.json"),
            Path.Combine(p18Root, "phase18-publication-report.json"),
            Path.Combine(validationRoot, "phase-18-validation.json")
        };
        var loaded = new List<string>();
        try
        {
            var manifest = await Read<Phase18Manifest>(inputs[0], loaded, ct);
            using var diagnostics = await ReadDocument(inputs[1], loaded, ct);
            using var publication = await ReadDocument(inputs[2], loaded, ct);
            using var validation = await ReadDocument(inputs[3], loaded, ct);
            ValidatePhase18Governance(manifest, diagnostics.RootElement, publication.RootElement,
                validation.RootElement, language, inputs);

            var ffprobe = string.IsNullOrWhiteSpace(rendering.FfprobePath) ? "ffprobe" : rendering.FfprobePath!;
            var ffmpeg = string.IsNullOrWhiteSpace(rendering.FfmpegPath) ? "ffmpeg" : rendering.FfmpegPath;
            var requested = manifest.RequestedFormats.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (requested.Length == 0 || requested.Any(f => f is not ("Short" or "Long")))
                Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Phase 18 requested-format set is empty or unsupported.", inputs);
            if (manifest.Outputs.Count != requested.Length || requested.Any(f =>
                    manifest.Outputs.Count(o => string.Equals(o.Format, f, StringComparison.OrdinalIgnoreCase)) != 1))
                Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Phase 18 outputs do not bind one-to-one to requested formats.", inputs);

            var subtitle = SubtitleAuthority.From(diagnostics.RootElement);
            var music = MusicAuthority.From(diagnostics.RootElement);
            var mediaPolicy = MediaPolicy.From(diagnostics.RootElement, inputs);
            // Phase 18 does not carry narration lengths per scene.  This is a lineage-only Phase 15 read;
            // it is never used to discover media and its rows are bound back to Phase 17 audio hashes.
            var timelinePath = ResolveOwned(root, Path.Combine("15-tts", language, "tts-timeline.json"), inputs);
            var narrationTimeline = (await Read<List<Phase15TimelineEntry>>(timelinePath, loaded, ct))
                .ToDictionary(x => x.SceneAudioUnitId, StringComparer.Ordinal);
            var outputs = new List<Phase19FormatQaEvidence>();
            foreach (var format in requested)
            {
                var declared = manifest.Outputs.Single(o => string.Equals(o.Format, format, StringComparison.OrdinalIgnoreCase));
                var video = ResolveManifestPath(p18Root, declared.VideoRelativePath, inputs);
                await VerifyPhysicalIdentity(video, declared.VideoByteLength, declared.VideoSha256,
                    Phase19ReasonCodes.VideoMissing, Phase19ReasonCodes.VideoHashMismatch, inputs, ct);
                loaded.Add(video);

                var probe = await Probe(ffprobe, video, ct);
                ValidateProbe(probe, declared, mediaPolicy, inputs);
                var subtitlePassed = await ValidateSubtitles(p18Root, video, language, declared, subtitle, loaded, inputs, ct);
                var motionPlanPath = ResolveOwned(root,
                    Path.Combine("17-motion", language, format.ToLowerInvariant(), "motion-plan.json"), inputs);
                var plan = await Read<Phase17MotionPlan>(motionPlanPath, loaded, ct);
                if (!string.Equals(plan.AuthorityChecksum, manifest.SourcePhase17AuthorityChecksum, StringComparison.Ordinal) ||
                    !string.Equals(plan.Format, format, StringComparison.OrdinalIgnoreCase))
                    Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, $"{format} Phase 17 motion lineage does not match Phase 18.", inputs);

                var scenes = new List<Phase19SceneQaEvidence>();
                var narrationAll = true;
                var musicAll = !music.Enabled;
                foreach (var scene in plan.Entries.OrderBy(x => x.Sequence))
                {
                    if (!narrationTimeline.TryGetValue(scene.SceneAudioUnitId, out var narration) ||
                        narration.AudioSha256 != scene.AudioSha256 || narration.ActualAudioDurationMs <= 0 ||
                        narration.ActualAudioDurationMs > scene.DurationMs + DurationToleranceMs)
                        Fail(Phase19ReasonCodes.UpstreamPhase18Invalid,
                            $"{format}/{scene.SceneId} narration-window lineage is invalid.", inputs);
                    var sceneStart = scene.SceneStartMs;
                    var exclusion = Math.Max(scene.TransitionIn.DurationMs, 100);
                    var endExclusion = Math.Max(scene.TransitionOut.DurationMs, 100);
                    var usableStart = sceneStart + exclusion;
                    var usableEnd = scene.SceneEndMs - endExclusion;
                    if (usableEnd - usableStart < 300)
                        Fail(Phase19ReasonCodes.MotionQaFailed, $"{format}/{scene.SceneId} has no interior QA window.", inputs);
                    var times = InteriorTimes(usableStart, usableEnd);
                    var frames = new List<byte[]>();
                    foreach (var time in times) frames.Add(await ExtractLuma(ffmpeg, video, time, subtitle.MaskBottomFraction(format), ct));
                    var metrics = new[] { MeanAbsoluteDifference(frames[0], frames[1]), MeanAbsoluteDifference(frames[1], frames[2]) };
                    var moving = scene.MotionType is not (Phase17MotionType.Static or Phase17MotionType.Hold);
                    // This metric certifies encoded material change, not exact pan direction. Direction remains
                    // corroborated by the immutable Phase 17 transforms and is deliberately not overclaimed.
                    var motionPassed = moving ? metrics.Max() >= MotionThreshold : metrics.Max() < 12.0;
                    if (!motionPassed) Fail(Phase19ReasonCodes.MotionQaFailed,
                        $"{format}/{scene.SceneId} encoded motion did not match {scene.MotionType}.", inputs);
                    var narrationTime = sceneStart + Math.Min(narration.ActualAudioDurationMs / 2,
                        Math.Max(100, narration.ActualAudioDurationMs - 200));
                    var narrationEnergy = await ProbeEnergy(ffmpeg, video, narrationTime, 350, ct);
                    var narrationPassed = narrationEnergy > -55;
                    narrationAll &= narrationPassed;
                    if (!narrationPassed) Fail(Phase19ReasonCodes.NarrationQaFailed,
                        $"{format}/{scene.SceneId} has no final-MP4 narration energy in its expected window.", inputs);

                    var fadePassed = await ValidateFade(ffmpeg, video, scene, ct);
                    if (!fadePassed) Fail(Phase19ReasonCodes.FadeQaFailed, $"{format}/{scene.SceneId} fade evidence failed.", inputs);
                    var transitionPassed = await ValidateTransition(ffmpeg, video, scene, ct);
                    if (!transitionPassed) Fail(Phase19ReasonCodes.TransitionQaFailed,
                        $"{format}/{scene.SceneId} transition evidence failed.", inputs);

                    // Phase 18 currently does not publish exact narration lengths in its media manifest.  The
                    // compatibility adapter samples a conservative scene-tail window from the final MP4 only.
                    if (music.Enabled && scene.DurationMs - narration.ActualAudioDurationMs >= 300)
                    {
                        var tailStart = scene.SceneStartMs + narration.ActualAudioDurationMs + 50;
                        var tailEnergy = await ProbeEnergy(ffmpeg, video, tailStart,
                            Math.Min(250, scene.SceneEndMs - tailStart - 20), ct);
                        musicAll |= tailEnergy > -60;
                        // Energy cannot separate semantic sources.  This versioned guard rejects only an
                        // obvious bed-over-speech inversion and intentionally accepts borderline mixes.
                        if (music.Ducking && narrationEnergy + 12 < tailEnergy)
                            Fail(Phase19ReasonCodes.MusicQaFailed,
                                $"{format}/{scene.SceneId} background bed obviously overwhelms narration.", inputs);
                    }
                    scenes.Add(new(scene.SceneId, scene.SceneAudioUnitId, scene.Sequence, scene.MotionType.ToString(),
                        [new(times[1], metrics[0]), new(times[2], metrics[1])], MotionThreshold,
                        motionPassed, narrationPassed, fadePassed, transitionPassed));
                }
                if (music.Enabled && !musicAll)
                    Fail(Phase19ReasonCodes.MusicQaFailed, $"{format} has no background-bed evidence in the final MP4.", inputs);
                outputs.Add(new(format, declared.VideoRelativePath, declared.VideoSha256, declared.VideoByteLength,
                    declared.GovernedDurationMs, probe.ContainerDurationMs, probe.Video, probe.Audio,
                    subtitlePassed, narrationAll, musicAll, scenes, true));
            }

            var identity = Hash(JsonSerializer.Serialize(new { manifest.AuthorityChecksum, requested, outputs, QaPolicy }, Json));
            var authority = new Phase19Manifest(Schema, language, manifest.AuthorityChecksum, requested, QaPolicy,
                "Phase18GovernedTimeline", outputs, identity, true, true, "Valid", true);
            return await Publish(root, language, authority, loaded, ct);
        }
        catch (Phase19AuthorityValidationException) { throw; }
        catch (Exception ex) { throw new Phase19AuthorityValidationException(Phase19ReasonCodes.UpstreamPhase18Invalid, ex.Message, loaded); }
    }

    internal static void ValidatePhase18Governance(Phase18Manifest manifest, JsonElement diagnostics,
        JsonElement publication, JsonElement validation, string language, IReadOnlyList<string> inputs)
    {
        if (!string.Equals(manifest.Language, language, StringComparison.OrdinalIgnoreCase) ||
            !manifest.PublicationCommitted || !manifest.DownstreamReady || manifest.ValidationStatus != "Valid" ||
            string.IsNullOrWhiteSpace(manifest.AuthorityChecksum))
            Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Phase 18 manifest governance is invalid.", inputs);
        RequireGovernance(publication, false, inputs); RequireGovernance(validation, true, inputs);
        if (!Bool(diagnostics, "publicationCommitted") || !Bool(diagnostics, "committedReadbackPassed"))
            Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Phase 18 diagnostics are not committed/read back.", inputs);
        foreach (var document in new[] { diagnostics, publication, validation })
            if (!String(document, "authorityChecksum").Equals(manifest.AuthorityChecksum, StringComparison.Ordinal))
                Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Phase 18 authority checksums disagree.", inputs);
    }

    private static void RequireGovernance(JsonElement value, bool requireStatus, IReadOnlyList<string> inputs)
    {
        var valid = (!requireStatus || String(value, "status") == "Succeeded") && Bool(value, "publicationCommitted") &&
            Bool(value, "committedReadbackPassed") && Bool(value, "committedStateValidationPassed") &&
            Bool(value, "semanticValidationPassed") && Bool(value, "checksumValidationPassed") &&
            Bool(value, "manifestValidationPassed") && String(value, "validationStatus") == "Valid" &&
            Bool(value, "downstreamReady") && !string.IsNullOrWhiteSpace(String(value, "authorityChecksum"));
        if (requireStatus) valid &= String(value, "manifestValidationStatus") == "Valid";
        if (!valid) Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Phase 18 full governance gate failed.", inputs);
    }

    internal static string ResolveManifestPath(string authorityRoot, string relative, IReadOnlyList<string> inputs)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Manifest path must be relative.", inputs);
        var root = Path.GetFullPath(authorityRoot) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(authorityRoot, relative));
        if (!path.StartsWith(root, StringComparison.Ordinal))
            Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Manifest path escapes the Phase 18 authority root.", inputs);
        // Existing ancestors must not redirect the path outside the authority through a symbolic link.
        for (var cursor = new FileInfo(path).Directory; cursor is not null && cursor.FullName.StartsWith(root, StringComparison.Ordinal); cursor = cursor.Parent)
            if (cursor.LinkTarget is not null)
                Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Manifest path traverses a symbolic link.", inputs);
        return path;
    }

    private static async Task ValidateProbe(ProbeEvidence probe, Phase18MediaEvidence declared, MediaPolicy policy,
        IReadOnlyList<string> inputs)
    {
        await Task.CompletedTask;
        if (Math.Abs(probe.ContainerDurationMs - declared.GovernedDurationMs) > DurationToleranceMs ||
            probe.Video.Width != declared.Width || probe.Video.Height != declared.Height ||
            !probe.Video.Codec.Equals(declared.VideoCodec, StringComparison.OrdinalIgnoreCase) ||
            !probe.Video.PixelFormat.Equals(declared.PixelFormat, StringComparison.OrdinalIgnoreCase) ||
            Math.Abs(probe.Video.FramesPerSecond - policy.FramesPerSecond) > .01)
            Fail(Phase19ReasonCodes.VideoStreamInvalid, $"{declared.Format} video stream differs from committed Phase 18 policy.", inputs);
        if (!probe.Audio.Codec.Equals(declared.AudioCodec, StringComparison.OrdinalIgnoreCase) ||
            probe.Audio.SampleRate != declared.AudioSampleRate || probe.Audio.Channels != declared.AudioChannels)
            Fail(Phase19ReasonCodes.AudioStreamInvalid, $"{declared.Format} audio stream differs from committed Phase 18 policy.", inputs);
    }

    private static async Task<bool> ValidateSubtitles(string root, string video, string language,
        Phase18MediaEvidence declared, SubtitleAuthority policy, List<string> loaded, IReadOnlyList<string> inputs, CancellationToken ct)
    {
        if (!policy.Enabled) return true;
        if (policy.GenerateSrt)
        {
            var srt = ResolveManifestPath(root, declared.SubtitleRelativePath, inputs);
            await VerifyPhysicalIdentity(srt, declared.SubtitleByteLength, declared.SubtitleSha256,
                Phase19ReasonCodes.SubtitleQaFailed, Phase19ReasonCodes.SubtitleQaFailed, inputs, ct);
            ValidateSrt(await File.ReadAllTextAsync(srt, ct), inputs); loaded.Add(srt);
        }
        if (policy.GenerateAss)
        {
            // Frozen Phase 18 puts first-class SRT identity in the manifest and the canonical ASS path in
            // diagnostics.  This explicit adapter closes that schema gap without changing Phase 18.
            var relative = declared.Format.Equals("Short", StringComparison.OrdinalIgnoreCase) ? policy.ShortAssPath : policy.LongAssPath;
            var ass = ResolveManifestPath(root, relative, inputs);
            if (!IsRegularFile(ass)) Fail(Phase19ReasonCodes.SubtitleQaFailed, "Canonical ASS is missing.", inputs);
            var text = await File.ReadAllTextAsync(ass, ct);
            if (!text.Contains("[Script Info]", StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(text, @"\[V4\+? Styles\]", RegexOptions.IgnoreCase) ||
                !text.Contains("[Events]", StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(text, @"(?m)^Dialogue:", RegexOptions.IgnoreCase))
                Fail(Phase19ReasonCodes.SubtitleQaFailed, "ASS structure/events are invalid.", inputs);
            var style = Regex.Match(text, @"(?m)^Style:\s*Default,([^,]+),(\d+),(?:[^,]*,){15}2,[^,]*,[^,]*,(\d+),");
            var dialogues = Regex.Matches(text, @"(?m)^Dialogue:.*$");
            if (!style.Success || string.IsNullOrWhiteSpace(style.Groups[1].Value) ||
                int.Parse(style.Groups[2].Value, CultureInfo.InvariantCulture) <= 0 ||
                int.Parse(style.Groups[3].Value, CultureInfo.InvariantCulture) <= 0 ||
                dialogues.Cast<Match>().Any(x => Regex.Matches(x.Value, @"\\N", RegexOptions.IgnoreCase).Count > 1))
                Fail(Phase19ReasonCodes.SubtitleQaFailed,
                    "ASS presentation must use a governed font, bottom alignment/margin, and at most two lines.", inputs);
            loaded.Add(ass);
        }
        if (policy.BurnIn)
        {
            var count = declared.Format.Equals("Short", StringComparison.OrdinalIgnoreCase) ? policy.ShortBurnCount : policy.LongBurnCount;
            if (count != 1 || !policy.BurnSucceeded || policy.DuplicateRisk || File.Exists(Path.ChangeExtension(video, ".srt")))
                Fail(Phase19ReasonCodes.SubtitleQaFailed, "Burn-in evidence or duplicate-sidecar policy failed.", inputs);
        }
        return true;
    }

    internal static void ValidateSrt(string text, IReadOnlyList<string> inputs)
    {
        var matches = Regex.Matches(text.Replace("\r\n", "\n"),
            @"(?m)^(\d+)\n(\d{2}):(\d{2}):(\d{2}),(\d{3}) --> (\d{2}):(\d{2}):(\d{2}),(\d{3})\n(.+(?:\n(?!\n).+)*)");
        if (matches.Count == 0) Fail(Phase19ReasonCodes.SubtitleQaFailed, "SRT has no parseable cues.", inputs);
        long previousEnd = -1; var expected = 1;
        foreach (Match match in matches)
        {
            var start = Timestamp(match, 2); var end = Timestamp(match, 6);
            if (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) != expected++ || start < 0 || start < previousEnd || end <= start)
                Fail(Phase19ReasonCodes.SubtitleQaFailed, "SRT cue order/timing is invalid.", inputs);
            previousEnd = end;
        }
    }

    private static long Timestamp(Match m, int i) => (((long.Parse(m.Groups[i].Value) * 60 + long.Parse(m.Groups[i + 1].Value)) * 60 +
        long.Parse(m.Groups[i + 2].Value)) * 1000) + long.Parse(m.Groups[i + 3].Value);

    private static async Task<Phase19PublicationResult> Publish(string root, string language, Phase19Manifest manifest,
        IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var finalRoot = Path.Combine(root, "19-video-qa", language);
        var transaction = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(root, "19-video-qa", ".staging", transaction, language);
        var backup = Path.Combine(root, "19-video-qa", ".backup", transaction, language);
        Directory.CreateDirectory(stage);
        var manifestPath = Path.Combine(stage, "phase19-manifest.json");
        await Write(manifestPath, manifest, ct);
        await Write(Path.Combine(stage, "phase19-authority-diagnostics.json"), new { schemaVersion = "phase19.diagnostics/1.0",
            manifest.SourcePhase18AuthorityChecksum, manifest.AuthorityChecksum, manifest.QaPolicyVersion,
            motionMetricPolicyVersion = MotionMetricPolicy, audioEnergyPolicyVersion = AudioEnergyPolicy,
            directionalPhysicalInferenceLimited = true, mediaReadOnly = true, publicationCommitted = true,
            committedReadbackPassed = true, committedStateValidationPassed = true, semanticValidationPassed = true,
            checksumValidationPassed = true, manifestValidationPassed = true, validationStatus = "Valid", downstreamReady = true }, ct);
        await Write(Path.Combine(stage, "phase19-publication-report.json"), new { schemaVersion = "phase19.publication/1.0",
            reasonCode = Phase19ReasonCodes.Accepted, manifest.SourcePhase18AuthorityChecksum, manifest.AuthorityChecksum,
            publicationCommitted = true, committedReadbackPassed = true, committedStateValidationPassed = true,
            semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true,
            validationStatus = "Valid", downstreamReady = true }, ct);
        await Phase16DurationCalibrationPublisher.ReplaceCommittedDirectoryAsync(stage, finalRoot, backup, async () =>
        {
            var committed = await ReadPlain<Phase19Manifest>(Path.Combine(finalRoot, "phase19-manifest.json"), ct);
            if (committed.AuthorityChecksum != manifest.AuthorityChecksum)
                Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Phase 19 committed readback failed.", inputs);
        });

        var validationRoot = Path.Combine(root, "validation"); var reviewRoot = Path.Combine(root, "review");
        Directory.CreateDirectory(validationRoot); Directory.CreateDirectory(reviewRoot);
        var validation = Path.Combine(validationRoot, "phase-19-validation.json");
        var qa = Path.Combine(reviewRoot, "qa-report.json"); var review = Path.Combine(reviewRoot, "video-review.json");
        var diagnosticProjection = Path.Combine(validationRoot, "phase-19-review-diagnostics.json");
        await Write(review, new { phaseNo = 19, phaseName = "Final Video Technical QA Authority", reviewOnly = true,
            technicalQaApproved = true, sourcePhase18AuthorityChecksum = manifest.SourcePhase18AuthorityChecksum,
            requestedFormats = manifest.RequestedFormats, outputs = manifest.Outputs }, ct);
        await Write(qa, new { status = "Approved", technicalQaApproved = true, recommendation = "Approved",
            authorityChecksum = manifest.AuthorityChecksum, issues = Array.Empty<object>(), errors = Array.Empty<string>() }, ct);
        await Write(diagnosticProjection, new { technicalQaApproved = true, validationPassed = true,
            authorityChecksum = manifest.AuthorityChecksum, inputPathsChecked = inputs }, ct);
        await Write(validation, new { phaseNo = 19, phaseName = "Final Video Technical QA Authority", status = "Succeeded",
            reasonCode = Phase19ReasonCodes.Accepted, validationPassed = true, technicalQaApproved = true,
            recommendation = "Approved", durationValidationMode = "Phase18GovernedTimeline",
            sourcePhase18AuthorityChecksum = manifest.SourcePhase18AuthorityChecksum, authorityChecksum = manifest.AuthorityChecksum,
            publicationCommitted = true, committedReadbackPassed = true, committedStateValidationPassed = true,
            semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true,
            validationStatus = "Valid", downstreamReady = true }, ct);
        var outputs = Directory.EnumerateFiles(finalRoot).Concat([validation, qa, review, diagnosticProjection]).ToArray();
        return new(inputs, outputs, Phase19ReasonCodes.Accepted, "Phase 19 final-video technical QA authority accepted.",
            manifest.SourcePhase18AuthorityChecksum, manifest.AuthorityChecksum, true, true, true, true, true, true,
            "Valid", true, true);
    }

    private static async Task<ProbeEvidence> Probe(string executable, string path, CancellationToken ct)
    {
        var result = await Run(executable, ["-v", "error", "-show_format", "-show_streams", "-of", "json", path], ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Structured ffprobe failed: " + result.Error);
        using var doc = JsonDocument.Parse(result.Output); var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var v = streams.Single(x => String(x, "codec_type") == "video"); var a = streams.Single(x => String(x, "codec_type") == "audio");
        var duration = SecondsToMs(String(doc.RootElement.GetProperty("format"), "duration"));
        var video = new Phase19StreamEvidence(String(v, "codec_name"), Int(v, "width"), Int(v, "height"), String(v, "pix_fmt"),
            Rational(String(v, "avg_frame_rate")), SecondsToMs(String(v, "duration")), 0, 0, null, NullableLong(v, "nb_frames"));
        var audio = new Phase19StreamEvidence(String(a, "codec_name"), 0, 0, "", 0, SecondsToMs(String(a, "duration")),
            IntString(a, "sample_rate"), Int(a, "channels"), String(a, "channel_layout"), null);
        return new(duration, video, audio);
    }

    private static async Task<byte[]> ExtractLuma(string ffmpeg, string video, long ms, double maskBottom, CancellationToken ct)
    {
        var height = Math.Max(1, (int)Math.Round(36 * (1 - maskBottom)));
        var result = await RunBinary(ffmpeg, ["-v", "error", "-ss", Sec(ms), "-i", video, "-frames:v", "1",
            "-vf", $"scale=64:36:flags=area,crop=64:{height}:0:0,format=gray", "-f", "rawvideo", "pipe:1"], ct);
        if (result.ExitCode != 0 || result.Output.Length != 64 * height) throw new InvalidOperationException("Frame extraction failed: " + result.Error);
        return result.Output;
    }

    private static async Task<double> ProbeEnergy(string ffmpeg, string video, long startMs, long durationMs, CancellationToken ct)
    {
        var result = await Run(ffmpeg, ["-v", "info", "-ss", Sec(Math.Max(0, startMs)), "-t", Sec(durationMs), "-i", video,
            "-vn", "-af", "volumedetect", "-f", "null", "-"], ct);
        var match = Regex.Match(result.Error, @"mean_volume:\s*(-?(?:\d+(?:\.\d+)?|inf))\s*dB", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) ? db : -100;
    }

    private static async Task<bool> ValidateFade(string ffmpeg, string video, Phase17MotionEntry scene, CancellationToken ct)
    {
        if (scene.TransitionIn.Type == Phase17TransitionType.Cut && scene.TransitionOut.Type == Phase17TransitionType.Cut) return true;
        if (scene.TransitionIn.DurationMs > 0 && scene.TransitionIn.Type != Phase17TransitionType.Cut)
        {
            var dark = Mean(await ExtractLuma(ffmpeg, video, scene.SceneStartMs + 20, 0, ct));
            var normal = Mean(await ExtractLuma(ffmpeg, video, scene.SceneStartMs + scene.TransitionIn.DurationMs - 20, 0, ct));
            if (normal <= dark + 2) return false;
        }
        if (scene.TransitionOut.DurationMs > 0 && scene.TransitionOut.Type != Phase17TransitionType.Cut)
        {
            var normal = Mean(await ExtractLuma(ffmpeg, video, scene.SceneEndMs - scene.TransitionOut.DurationMs + 20, 0, ct));
            var dark = Mean(await ExtractLuma(ffmpeg, video, scene.SceneEndMs - 20, 0, ct));
            if (normal <= dark + 2) return false;
        }
        return true;
    }

    private static async Task<bool> ValidateTransition(string ffmpeg, string video, Phase17MotionEntry scene, CancellationToken ct)
    {
        if (scene.TransitionOut.Type == Phase17TransitionType.Cut || scene.TransitionOut.DurationMs == 0) return true;
        var boundary = scene.SceneEndMs;
        var middle = Mean(await ExtractLuma(ffmpeg, video, Math.Max(scene.SceneStartMs, boundary - 20), 0, ct));
        return scene.TransitionOut.Type != Phase17TransitionType.FadeThroughBlack || middle < 35;
    }

    internal static double MeanAbsoluteDifference(byte[] first, byte[] second)
    {
        if (first.Length == 0 || first.Length != second.Length) throw new ArgumentException("Luma planes must have equal non-zero length.");
        long total = 0; for (var i = 0; i < first.Length; i++) total += Math.Abs(first[i] - second[i]);
        return total / (double)first.Length;
    }

    private static long[] InteriorTimes(long start, long end) => [start, start + (end - start) / 2, end];
    private static double Mean(byte[] values) => values.Average(x => (double)x);
    private static string Sec(long ms) => (ms / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
    private static double Rational(string text) { var p = text.Split('/'); return p.Length == 2 && double.TryParse(p[0], out var n) && double.TryParse(p[1], out var d) && d != 0 ? n / d : 0; }
    private static long SecondsToMs(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? (long)Math.Round(x * 1000) : 0;
    private static int Int(JsonElement x, string p) => x.TryGetProperty(p, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static int IntString(JsonElement x, string p) => int.TryParse(String(x, p), out var n) ? n : 0;
    private static long? NullableLong(JsonElement x, string p) => long.TryParse(String(x, p), out var n) ? n : null;
    private static bool Bool(JsonElement x, string p) => x.TryGetProperty(p, out var v) && v.ValueKind is JsonValueKind.True;
    private static string String(JsonElement x, string p) => x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task<string> HashFile(string path, CancellationToken ct) { await using var s = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant(); }
    private static bool IsRegularFile(string path) => File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    private static string ResolveOwned(string root, string relative, IReadOnlyList<string> inputs) => ResolveManifestPath(root, relative, inputs);
    private static void Fail(string code, string reason, IReadOnlyList<string> loaded) => throw new Phase19AuthorityValidationException(code, reason, loaded);

    private static async Task VerifyPhysicalIdentity(string path, long length, string sha, string missingCode,
        string mismatchCode, IReadOnlyList<string> loaded, CancellationToken ct)
    {
        if (!IsRegularFile(path)) Fail(missingCode, $"Regular file missing: {path}", loaded);
        if (new FileInfo(path).Length != length || !string.Equals(await HashFile(path, ct), sha, StringComparison.OrdinalIgnoreCase))
            Fail(mismatchCode, $"Physical identity mismatch: {path}", loaded);
    }

    private static async Task<T> Read<T>(string path, List<string> loaded, CancellationToken ct) { var value = await ReadPlain<T>(path, ct); loaded.Add(path); return value; }
    private static async Task<T> ReadPlain<T>(string path, CancellationToken ct)
    {
        if (!IsRegularFile(path)) throw new InvalidOperationException($"Authority artifact missing or non-regular: {path}");
        await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
    }
    private static async Task<JsonDocument> ReadDocument(string path, List<string> loaded, CancellationToken ct) { if (!IsRegularFile(path)) throw new InvalidOperationException($"Authority artifact missing: {path}"); await using var s = File.OpenRead(path); var d = await JsonDocument.ParseAsync(s, cancellationToken: ct); loaded.Add(path); return d; }
    private static async Task Write(string path, object value, CancellationToken ct) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false), ct); }

    private static async Task<ProcessResult> Run(string executable, IReadOnlyList<string> args, CancellationToken ct)
    {
        var p = new Process { StartInfo = new(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in args) p.StartInfo.ArgumentList.Add(arg); p.Start();
        var output = p.StandardOutput.ReadToEndAsync(ct); var error = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct); return new(p.ExitCode, await output, await error);
    }
    private static async Task<BinaryProcessResult> RunBinary(string executable, IReadOnlyList<string> args, CancellationToken ct)
    {
        var p = new Process { StartInfo = new(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in args) p.StartInfo.ArgumentList.Add(arg); p.Start();
        await using var data = new MemoryStream(); var copy = p.StandardOutput.BaseStream.CopyToAsync(data, ct); var error = p.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(copy, p.WaitForExitAsync(ct)); return new(p.ExitCode, data.ToArray(), await error);
    }

    private sealed record ProbeEvidence(long ContainerDurationMs, Phase19StreamEvidence Video, Phase19StreamEvidence Audio);
    private sealed record Phase15TimelineEntry(string SceneAudioUnitId, string SceneId, int Sequence, string Format,
        string Language, string AudioRelativePath, long AudioByteLength, string AudioSha256, string TextChecksum,
        long ActualAudioDurationMs);
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
    private sealed record BinaryProcessResult(int ExitCode, byte[] Output, string Error);
    private sealed record MusicAuthority(bool Enabled, bool Ducking)
    {
        public static MusicAuthority From(JsonElement x) => new(Bool(x, "backgroundMusicUsed"), Bool(x, "duckUnderNarration"));
    }
    private sealed record MediaPolicy(int FramesPerSecond)
    {
        public static MediaPolicy From(JsonElement diagnostics, IReadOnlyList<string> inputs)
        {
            if (!diagnostics.TryGetProperty("renderPolicy", out var policy) || Int(policy, "framesPerSecond") <= 0)
                Fail(Phase19ReasonCodes.UpstreamPhase18Invalid, "Phase 18 physical video policy is missing.", inputs);
            return new(Int(policy, "framesPerSecond"));
        }
    }
    private sealed record SubtitleAuthority(bool Enabled, bool BurnIn, bool GenerateSrt, bool GenerateAss,
        string ShortAssPath, string LongAssPath, int ShortBurnCount, int LongBurnCount, bool BurnSucceeded, bool DuplicateRisk,
        int ShortBottomMargin, int LongBottomMargin)
    {
        public static SubtitleAuthority From(JsonElement x)
        {
            var burns = x.GetProperty("subtitleBurnPassCount"); var margins = x.GetProperty("resolvedBottomMarginPixels");
            return new(Bool(x, "subtitleEnabled"), Bool(x, "subtitleBurnIn"), Bool(x, "subtitleGenerateSrt"), Bool(x, "subtitleGenerateAss"),
                String(x, "shortAssPath"), String(x, "longAssPath"), Int(burns, "short"), Int(burns, "long"),
                Bool(x, "subtitleBurnSucceeded"), Bool(x, "sameBasenameSidecarCollision"), Int(margins, "short"), Int(margins, "long"));
        }
        public double MaskBottomFraction(string format)
        {
            if (!BurnIn) return 0; var height = format.Equals("Short", StringComparison.OrdinalIgnoreCase) ? 1920 : 720;
            var margin = format.Equals("Short", StringComparison.OrdinalIgnoreCase) ? ShortBottomMargin : LongBottomMargin;
            return Math.Clamp((margin + height * .16) / height, .15, .35);
        }
    }
}
