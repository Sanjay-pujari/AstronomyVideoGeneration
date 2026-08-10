using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>
/// Strict adapter from the three frozen upstream authorities to FFmpeg.  This class deliberately
/// contains no asset discovery, editorial fallback, timing calculation, or motion invention.
/// </summary>
internal static class Phase18VideoAssemblyAuthorityPublisher
{
    internal static readonly Phase18VideoPolicy VideoPolicy = new("phase18-video/1.0", "h264", "libx264",
        "yuv420p", 30, "veryfast", 20, 1080, 1920, 1280, 720);
    internal static readonly Phase18AudioPolicy AudioPolicy = new("phase18-audio/1.0", "aac", 48_000, 2, 192_000);
    internal static readonly Phase18SubtitlePolicy SubtitlePolicy = new("phase18-subtitle/1.0",
        Phase18SubtitleMode.BurnInAndSidecar, Phase18SubtitleMode.SidecarOnly, "Noto Sans Devanagari");
    internal const string RenderPolicy = "phase18-governed-scene-render/1.0";
    private const string Schema = "phase18.video-assembly/1.0";
    private const long ProbeToleranceMs = 35; // one 30 fps frame plus container rounding
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<Phase18PublicationResult> ExecuteAsync(string root, string language,
        bool overwrite, CancellationToken ct)
    {
        language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
        var p15 = AuthorityFiles(root, "15-tts", language, "phase15");
        var p16 = AuthorityFiles(root, "16-duration-calibration", language, "phase16");
        var p17 = AuthorityFiles(root, "17-motion", language, "phase17");
        var timeline15 = Path.Combine(root, "15-tts", language, "tts-timeline.json");
        var timeline16 = Path.Combine(root, "16-duration-calibration", language, "calibrated-scene-timeline.json");
        var subtitleTimeline = Path.Combine(root, "16-duration-calibration", language, "subtitle-timeline.json");
        var plans = new[] { "short", "long" }.Select(x => Path.Combine(root, "17-motion", language, x, "motion-plan.json")).ToArray();
        var srts = new[] { "short", "long" }.Select(x => Path.Combine(root, "16-duration-calibration", language, x, "final.srt")).ToArray();
        var inputs = p15.Concat(p16).Concat(p17).Append(timeline15).Append(timeline16).Append(subtitleTimeline).Concat(plans).Concat(srts).Distinct().ToArray();
        RequireFiles(p15.Append(timeline15), Phase18ReasonCodes.UpstreamPhase15Invalid);
        RequireFiles(p16.Append(timeline16).Append(subtitleTimeline).Concat(srts), Phase18ReasonCodes.UpstreamPhase16Invalid);
        RequireFiles(p17.Concat(plans), Phase18ReasonCodes.UpstreamPhase17Invalid);

        var p15Snapshot = await LoadPhase15AuthorityAsync(p15, language, ct);
        var p15Checksum = p15Snapshot.AuthorityChecksum;
        var p16Checksum = await ValidateAuthority(p16, "P16_DURATION_AUTHORITY_ACCEPTED", Phase18ReasonCodes.UpstreamPhase16Invalid, ct);
        var p17Checksum = await ValidateAuthority(p17, "P17_MOTION_AUTHORITY_ACCEPTED", Phase18ReasonCodes.UpstreamPhase17Invalid, ct);
        using var p16Manifest = await ReadDocument(p16[0], ct);
        using var p17Manifest = await ReadDocument(p17[0], ct);
        if (String(p16Manifest.RootElement, "sourcePhase15AuthorityChecksum") != p15Checksum ||
            String(p17Manifest.RootElement, "sourcePhase16AuthorityChecksum") != p16Checksum)
            Fail(Phase18ReasonCodes.LineageMismatch, "Phase 15 -> 16 -> 17 authority checksum lineage differs.");

        var audio = await ReadTimeline15(timeline15, p15Snapshot, ct);
        var calibrated = await ReadTimeline16(timeline16, ct);
        var motion = new List<Phase17MotionPlan>();
        foreach (var path in plans) motion.Add(await Read<Phase17MotionPlan>(path, ct));
        var toolchain = await ToolchainIdentity(ct);
        var requested = new[] { "Short", "Long" };
        var identity = Hash(JsonSerializer.Serialize(new { Schema, language, requested, p15Checksum, p16Checksum,
            p17Checksum, RenderPolicy, VideoPolicy, AudioPolicy, SubtitlePolicy, toolchain }, Json));
        var finalRoot = Path.Combine(root, "18-video-assembly", language);
        var existingManifestPath = Path.Combine(finalRoot, "phase18-manifest.json");
        if (!overwrite && File.Exists(existingManifestPath))
        {
            var existing = await Read<Phase18Manifest>(existingManifestPath, ct);
            if (existing.Outputs.Count == 2 && existing.SourcePhase15AuthorityChecksum == p15Checksum &&
                existing.SourcePhase16AuthorityChecksum == p16Checksum && existing.SourcePhase17AuthorityChecksum == p17Checksum &&
                existing.RenderPolicyVersion == RenderPolicy && existing.ToolchainIdentity == toolchain &&
                await OutputsValid(finalRoot, existing.Outputs, ct))
                return Result(inputs, Directory.EnumerateFiles(finalRoot, "*", SearchOption.AllDirectories).ToArray(),
                    false, true, false, p15Checksum, p16Checksum, p17Checksum, existing.AuthorityChecksum);
        }

        var transaction = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(root, "18-video-assembly", ".staging", transaction, language);
        var backup = Path.Combine(root, "18-video-assembly", ".backup", transaction, language);
        var evidence = new List<Phase18MediaEvidence>();
        var replaced = Directory.Exists(finalRoot);
        try
        {
            Directory.CreateDirectory(stage);
            for (var f = 0; f < requested.Length; f++)
                evidence.Add(await RenderFormat(root, stage, language, requested[f], motion[f], calibrated, audio, srts[f], ct));
            var authorityChecksum = Hash(JsonSerializer.Serialize(new { identity, outputs = evidence }, Json));
            var manifest = new Phase18Manifest(Schema, language, requested, p15Checksum, p16Checksum, p17Checksum,
                RenderPolicy, VideoPolicy.Version, AudioPolicy.Version, SubtitlePolicy.Version, toolchain, evidence,
                authorityChecksum, true, "Valid", true);
            await Write(Path.Combine(stage, "phase18-manifest.json"), manifest, ct);
            await Write(Path.Combine(stage, "phase18-authority-diagnostics.json"), new { schemaVersion = "phase18.diagnostics/1.0",
                language, requestedFormats = requested, renderPolicy = VideoPolicy, audioPolicy = AudioPolicy,
                subtitlePolicy = SubtitlePolicy, toolchainIdentity = toolchain, candidateValidationPassed = true,
                candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true,
                stagingCleanupRequired = true, backgroundMusicUsed = false, outroDurationMs = 0,
                sourcePhase15AuthorityChecksum = p15Checksum, sourcePhase16AuthorityChecksum = p16Checksum,
                sourcePhase17AuthorityChecksum = p17Checksum, authorityChecksum }, ct);
            await Write(Path.Combine(stage, "phase18-publication-report.json"), Publication(authorityChecksum), ct);
            if (!await OutputsValid(stage, evidence, ct)) Fail(Phase18ReasonCodes.CandidateValidationFailed, "Candidate readback failed.");
            await Phase16DurationCalibrationPublisher.ReplaceCommittedDirectoryAsync(stage, finalRoot, backup, async () =>
            {
                var committed = await Read<Phase18Manifest>(existingManifestPath, ct);
                if (committed.AuthorityChecksum != authorityChecksum || !await OutputsValid(finalRoot, committed.Outputs, ct))
                    Fail(Phase18ReasonCodes.CommittedReadbackFailed, "Committed media differs from the candidate.");
            });
            var validation = Path.Combine(root, "validation", "phase-18-validation.json");
            var result = Result(inputs, Directory.EnumerateFiles(finalRoot, "*", SearchOption.AllDirectories).Append(validation).ToArray(),
                true, false, replaced, p15Checksum, p16Checksum, p17Checksum, authorityChecksum);
            await Write(validation, Validation(result), ct);
            await ProjectCompatibility(root, language, evidence, finalRoot, ct);
            return result;
        }
        finally
        {
            Cleanup(Path.GetDirectoryName(stage)!); Cleanup(Path.GetDirectoryName(backup)!);
        }
    }

    private static async Task<Phase18MediaEvidence> RenderFormat(string root, string stage, string language,
        string format, Phase17MotionPlan plan, IReadOnlyDictionary<string, Phase16CalibratedScene> calibrated,
        IReadOnlyDictionary<string, Phase15Entry> audio, string srt, CancellationToken ct)
    {
        if (!plan.Format.Equals(format, StringComparison.OrdinalIgnoreCase) || plan.Entries.Count != plan.SceneCount ||
            plan.Entries.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(1, plan.Entries.Count)) is false)
            Fail(Phase18ReasonCodes.CandidateValidationFailed, $"{format} plan order/count is invalid.");
        var dir = Path.Combine(stage, format.ToLowerInvariant()); Directory.CreateDirectory(dir);
        var clips = Path.Combine(stage, ".intermediates", format.ToLowerInvariant()); Directory.CreateDirectory(clips);
        var sourceHashes = new List<string>(); long governed = 0;
        foreach (var entry in plan.Entries)
        {
            if (entry.MotionType != Phase17MotionType.Static || entry.TransitionIn.Type != Phase17TransitionType.Cut ||
                entry.TransitionOut.Type != Phase17TransitionType.Cut || entry.TransitionIn.DurationMs != 0 || entry.TransitionOut.DurationMs != 0)
                Fail(Phase18ReasonCodes.CandidateValidationFailed, "Only explicitly implemented Static and Cut/0 semantics are accepted.");
            if (!Enum.IsDefined(entry.Easing)) Fail(Phase18ReasonCodes.CandidateValidationFailed, "Unknown easing.");
            if (!calibrated.TryGetValue(entry.SceneAudioUnitId, out var timing))
                Fail(Phase18ReasonCodes.LineageMismatch, $"Calibrated timing is missing for {entry.SceneAudioUnitId}.");
            if (!audio.TryGetValue(entry.SceneAudioUnitId, out var speech))
                Fail(Phase18ReasonCodes.LineageMismatch, $"Speech audio is missing for {entry.SceneAudioUnitId}.");
            if (entry.SceneId != timing.SceneId || entry.SceneId != speech.SceneId || entry.Sequence != timing.Sequence ||
                entry.Sequence != speech.Sequence || !entry.Format.Equals(timing.Format, StringComparison.OrdinalIgnoreCase) ||
                !entry.Format.Equals(speech.Format, StringComparison.OrdinalIgnoreCase) || !entry.Language.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                !speech.Language.Equals(language, StringComparison.OrdinalIgnoreCase) || entry.DurationMs != timing.FinalSceneDurationMs)
                Fail(Phase18ReasonCodes.LineageMismatch, $"Strict row binding failed for {entry.SceneAudioUnitId}.");
            if (speech.ActualAudioDurationMs > entry.DurationMs + ProbeToleranceMs)
                Fail(Phase18ReasonCodes.AudioPhysicalEvidenceInvalid, $"Speech is longer than governed scene {entry.SceneId}.");
            var image = ResolveOwned(root, entry.VisualAssetPath);
            if (!File.Exists(image) || await HashFile(image, ct) != entry.VisualAssetSha256) Fail(Phase18ReasonCodes.VisualPhysicalEvidenceInvalid, entry.SceneId);
            var dimensions = await Image.IdentifyAsync(image, ct);
            if (dimensions?.Width != entry.Width || dimensions.Height != entry.Height) Fail(Phase18ReasonCodes.VisualPhysicalEvidenceInvalid, entry.SceneId);
            var speechPath = ResolveOwned(root, speech.AudioRelativePath);
            if (!File.Exists(speechPath) || new FileInfo(speechPath).Length != speech.AudioByteLength || await HashFile(speechPath, ct) != speech.AudioSha256)
                Fail(Phase18ReasonCodes.AudioPhysicalEvidenceInvalid, entry.SceneId);
            sourceHashes.Add(speech.AudioSha256); governed += entry.DurationMs;
            var clip = Path.Combine(clips, $"{entry.Sequence:000}.mp4");
            var seconds = (entry.DurationMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
            var (w, h) = format == "Short" ? (VideoPolicy.ShortWidth, VideoPolicy.ShortHeight) : (VideoPolicy.LongWidth, VideoPolicy.LongHeight);
            await Run("ffmpeg", ["-y", "-loop", "1", "-framerate", "30", "-i", image, "-i", speechPath,
                "-filter_complex", $"[0:v]scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h},fps=30,format=yuv420p[v];[1:a]apad=whole_dur={seconds},aresample=48000,aformat=channel_layouts=stereo[a]",
                "-map", "[v]", "-map", "[a]", "-t", seconds, "-c:v", "libx264", "-preset", VideoPolicy.Preset,
                "-crf", VideoPolicy.Crf.ToString(), "-pix_fmt", VideoPolicy.PixelFormat, "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", clip], ct);
        }
        ValidateSrt(srt);
        var sidecar = Path.Combine(dir, "final.srt"); File.Copy(srt, sidecar, true);
        var concat = Path.Combine(clips, "concat.txt");
        await File.WriteAllLinesAsync(concat, plan.Entries.Select(x => $"file '{x.Sequence:000}.mp4'"), new UTF8Encoding(false), ct);
        var unburned = Path.Combine(clips, "unsubtitled.mp4");
        await Run("ffmpeg", ["-y", "-f", "concat", "-safe", "0", "-i", concat, "-c", "copy", unburned], ct);
        var final = Path.Combine(dir, "final.mp4");
        var burn = language == "en";
        if (burn) await Run("ffmpeg", ["-y", "-i", unburned, "-vf", $"subtitles={EscapeFilter(sidecar)}", "-c:v", "libx264", "-preset", VideoPolicy.Preset, "-crf", VideoPolicy.Crf.ToString(), "-pix_fmt", "yuv420p", "-c:a", "copy", final], ct);
        else File.Copy(unburned, final, true);
        var physical = await ProbeDuration(final, ct);
        if (Math.Abs(physical - governed) > ProbeToleranceMs) Fail(Phase18ReasonCodes.VideoValidationFailed, $"{format} duration differs by {Math.Abs(physical-governed)}ms.");
        var relativeVideo = $"{format.ToLowerInvariant()}/final.mp4"; var relativeSrt = $"{format.ToLowerInvariant()}/final.srt";
        var result = new Phase18MediaEvidence(format, relativeVideo, relativeSrt, governed, physical,
            format == "Short" ? VideoPolicy.ShortWidth : VideoPolicy.LongWidth, format == "Short" ? VideoPolicy.ShortHeight : VideoPolicy.LongHeight,
            "h264", "yuv420p", "aac", 48000, 2, await HashFile(final, ct), new FileInfo(final).Length,
            await HashFile(sidecar, ct), new FileInfo(sidecar).Length, sourceHashes);
        Directory.Delete(clips, true);
        var intermediateRoot = Path.GetDirectoryName(clips)!;
        if (!Directory.EnumerateFileSystemEntries(intermediateRoot).Any()) Directory.Delete(intermediateRoot);
        return result;
    }

    internal static bool CanonicalArgumentsAreSafe(IEnumerable<string> args) =>
        !args.Any(x => x.Equals("-shortest", StringComparison.OrdinalIgnoreCase) || x.Contains("atrim", StringComparison.OrdinalIgnoreCase));
    private static async Task Run(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (!CanonicalArgumentsAreSafe(args)) Fail(Phase18ReasonCodes.RenderFailed, "Speech-trimming FFmpeg arguments are prohibited.");
        var psi = new ProcessStartInfo(file) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start FFmpeg.");
        var errorTask = process.StandardError.ReadToEndAsync(ct); await process.WaitForExitAsync(ct);
        var error = await errorTask;
        if (process.ExitCode != 0) Fail(Phase18ReasonCodes.RenderFailed, error[..Math.Min(2000, error.Length)]);
    }

    private static async Task<long> ProbeDuration(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var x in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path }) psi.ArgumentList.Add(x);
        using var p = Process.Start(psi)!; var output = await p.StandardOutput.ReadToEndAsync(ct); await p.WaitForExitAsync(ct);
        if (!double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sec) || p.ExitCode != 0)
            Fail(Phase18ReasonCodes.VideoValidationFailed, "ffprobe failed.");
        return (long)Math.Round(sec * 1000, MidpointRounding.AwayFromZero);
    }

    private static async Task<bool> OutputsValid(string root, IEnumerable<Phase18MediaEvidence> outputs, CancellationToken ct)
    { foreach (var x in outputs) { var v = Path.Combine(root, x.VideoRelativePath); var s = Path.Combine(root, x.SubtitleRelativePath); if (!File.Exists(v) || !File.Exists(s) || await HashFile(v, ct) != x.VideoSha256 || await HashFile(s, ct) != x.SubtitleSha256) return false; } return true; }
    private static async Task<string> ValidateAuthority(string[] files, string accepted, string code, CancellationToken ct)
    {
        using var manifest = await ReadDocument(files[0], ct); using var report = await ReadDocument(files[1], ct); using var validation = await ReadDocument(files[2], ct);
        var checksum = String(manifest.RootElement, "authorityChecksum"); if (string.IsNullOrWhiteSpace(checksum) || String(report.RootElement, "authorityChecksum") != checksum || String(validation.RootElement, "authorityChecksum") != checksum || String(validation.RootElement, "status") != "Succeeded") Fail(code, "Authority checksum/status is invalid.");
        if (validation.RootElement.TryGetProperty("reasonCode", out var reason) && reason.GetString() != accepted) Fail(code, "Authority reason is invalid.");
        foreach (var name in new[] { "publicationCommitted", "committedReadbackPassed", "committedStateValidationPassed", "semanticValidationPassed", "checksumValidationPassed", "manifestValidationPassed", "downstreamReady" })
            if (!Bool(report.RootElement, name) || !Bool(validation.RootElement, name)) Fail(code, $"{name} must be true.");
        if (!Bool(manifest.RootElement, "publicationCommitted") || !Bool(manifest.RootElement, "downstreamReady") || String(manifest.RootElement, "validationStatus") != "Valid") Fail(code, "Manifest is not downstream ready.");
        return checksum;
    }

    internal static async Task<Phase18Phase15AuthoritySnapshot> LoadPhase15AuthorityAsync(
        string[] files, string language, CancellationToken ct)
    {
        var loaded = new List<string>();
        try
        {
            var manifest = await Read<Phase15ManifestContract>(files[0], ct); loaded.Add(files[0]);
            var report = await Read<Phase15PublicationContract>(files[1], ct); loaded.Add(files[1]);
            var validation = await Read<Phase15ValidationContract>(files[2], ct); loaded.Add(files[2]);
            void Require(bool condition, string reason)
            { if (!condition) throw new Phase18AuthorityValidationException(Phase18ReasonCodes.UpstreamPhase15Invalid, reason, loaded.ToArray()); }

            Require(!string.IsNullOrWhiteSpace(manifest.AuthorityChecksum), "authorityChecksum must be present.");
            Require(string.Equals(report.AuthorityChecksum, manifest.AuthorityChecksum, StringComparison.OrdinalIgnoreCase)
                && string.Equals(validation.AuthorityChecksum, manifest.AuthorityChecksum, StringComparison.OrdinalIgnoreCase),
                "Authority checksums do not agree.");
            Require(string.Equals(report.SourcePhase14AuthorityChecksum, manifest.SourcePhase14AuthorityChecksum, StringComparison.OrdinalIgnoreCase)
                && string.Equals(validation.SourcePhase14AuthorityChecksum, manifest.SourcePhase14AuthorityChecksum, StringComparison.OrdinalIgnoreCase),
                "sourcePhase14AuthorityChecksum does not agree.");
            Require(report.PublicationCommitted, "publicationCommitted must be true.");
            Require(report.CandidateValidationPassed, "candidateValidationPassed must be true.");
            Require(report.CandidateReadbackPassed, "candidateReadbackPassed must be true.");
            Require(report.CommittedReadbackPassed, "committedReadbackPassed must be true.");
            Require(report.CommittedStateValidationPassed, "committedStateValidationPassed must be true.");
            Require(validation.SemanticValidationPassed == true, "semanticValidationPassed must be true.");
            Require(validation.ChecksumValidationPassed == true, "checksumValidationPassed must be true.");
            Require(validation.ManifestValidationPassed == true, "manifestValidationPassed must be true.");
            Require(validation.Status == "Succeeded" && validation.ReasonCode == "P15_TTS_AUTHORITY_ACCEPTED"
                && validation.ValidationStatus == "Valid", "Validation authority is not accepted and Valid.");
            Require(manifest.PublicationCommitted && manifest.DownstreamReady && manifest.ValidationStatus == "Valid",
                "Manifest is not downstream ready.");
            Require(report.DownstreamReady && validation.DownstreamReady == true, "downstreamReady must be true.");
            return new(language, manifest.AuthorityChecksum, manifest.SourcePhase14AuthorityChecksum,
                report.PublicationCommitted, report.CandidateValidationPassed, report.CandidateReadbackPassed,
                report.CommittedReadbackPassed, report.CommittedStateValidationPassed,
                validation.SemanticValidationPassed!.Value, validation.ChecksumValidationPassed!.Value,
                validation.ManifestValidationPassed!.Value, validation.ValidationStatus,
                manifest.DownstreamReady && report.DownstreamReady && validation.DownstreamReady == true, loaded.ToArray());
        }
        catch (Phase18AuthorityValidationException) { throw; }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            throw new Phase18AuthorityValidationException(Phase18ReasonCodes.UpstreamPhase15Invalid,
                $"Phase 15 authority could not be read: {ex.Message}", loaded.ToArray());
        }
    }
    private static string[] AuthorityFiles(string root, string phaseRoot, string language, string stem) => [Path.Combine(root, phaseRoot, language, $"{stem}-manifest.json"), Path.Combine(root, phaseRoot, language, $"{stem}-publication-report.json"), Path.Combine(root, "validation", stem.Replace("phase", "phase-") + "-validation.json")];
    private static async Task<Dictionary<string, Phase15Entry>> ReadTimeline15(string path, Phase18Phase15AuthoritySnapshot authority, CancellationToken ct)
    {
        using var d = await ReadDocument(path, ct);
        if (!String(d.RootElement, "authorityChecksum").Equals(authority.AuthorityChecksum, StringComparison.OrdinalIgnoreCase)
            || !String(d.RootElement, "sourcePhase14AuthorityChecksum").Equals(authority.SourcePhase14AuthorityChecksum, StringComparison.OrdinalIgnoreCase))
            throw new Phase18AuthorityValidationException(Phase18ReasonCodes.UpstreamPhase15Invalid,
                "Timeline authority checksum lineage does not agree.", authority.LoadedAuthorityArtifacts.Append(path).ToArray());
        return new[] { "short", "long" }.SelectMany(n => d.RootElement.GetProperty(n).GetProperty("items").Deserialize<List<Phase15Entry>>(Json) ?? [])
            .ToDictionary(x => x.SceneAudioUnitId, StringComparer.Ordinal);
    }
    private static async Task<Dictionary<string, Phase16CalibratedScene>> ReadTimeline16(string path, CancellationToken ct) { using var d = await ReadDocument(path, ct); return new[] { "short", "long" }.SelectMany(n => d.RootElement.GetProperty(n).Deserialize<List<Phase16CalibratedScene>>(Json) ?? []).ToDictionary(x => x.SceneAudioUnitId, StringComparer.Ordinal); }
    private static void ValidateSrt(string path) { var text = File.ReadAllText(path, new UTF8Encoding(false, true)); if (string.IsNullOrWhiteSpace(text) || !text.Contains(" --> ", StringComparison.Ordinal)) Fail(Phase18ReasonCodes.SubtitlePhysicalEvidenceInvalid, "SRT is not valid UTF-8/timed text."); }
    private static string ResolveOwned(string root, string path) { var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path)); var owned = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; if (!full.StartsWith(owned, StringComparison.Ordinal)) Fail(Phase18ReasonCodes.CandidateValidationFailed, "Authority path escapes the execution root."); return full; }
    private static async Task ProjectCompatibility(string root, string language, IEnumerable<Phase18MediaEvidence> outputs, string canonical, CancellationToken ct) { foreach (var x in outputs) { var format = x.Format.ToLowerInvariant(); var destinations = new[] { Path.Combine(root, "video-assembly", language, format, "final.mp4"), Path.Combine(root, "video", format, format == "short" ? "final-short.mp4" : "final-long.mp4") }; foreach (var destination in destinations) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(Path.Combine(canonical, x.VideoRelativePath), destination, true); } } await Task.CompletedTask; }
    private static object Publication(string checksum) => new { schemaVersion = "phase18.publication/1.0", reasonCode = Phase18ReasonCodes.Accepted, candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, committedStateValidationPassed = true, semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true, validationStatus = "Valid", downstreamReady = true, authorityChecksum = checksum };
    private static object Validation(Phase18PublicationResult x) => new { phaseNo = 18, phaseName = "Video Assembly Authority", status = "Succeeded", x.ReasonCode, x.Reason, inputFiles = x.InputFiles, outputFiles = x.OutputFiles, x.Generated, x.Reused, x.Regenerated, x.CandidateValidationPassed, x.CandidateReadbackPassed, x.PublicationCommitted, x.CommittedReadbackPassed, x.CommittedStateValidationPassed, x.SourcePhase15AuthorityChecksum, x.SourcePhase16AuthorityChecksum, x.SourcePhase17AuthorityChecksum, x.AuthorityChecksum, x.ManifestValidationStatus, x.ValidationStatus, x.SemanticValidationPassed, x.ChecksumValidationPassed, x.ManifestValidationPassed, x.DownstreamReady };
    private static Phase18PublicationResult Result(IReadOnlyList<string> i, IReadOnlyList<string> o, bool g, bool u, bool r, string p15, string p16, string p17, string checksum) => new(i, o, Phase18ReasonCodes.Accepted, "Phase 18 video assembly authority accepted.", g, u, r, true, true, true, true, true, p15, p16, p17, checksum, "Valid", "Valid", true, true, true, true);
    private static async Task<string> ToolchainIdentity(CancellationToken ct) { var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false }; psi.ArgumentList.Add("-version"); using var p = Process.Start(psi)!; var line = await p.StandardOutput.ReadLineAsync(ct) ?? await p.StandardError.ReadLineAsync(ct) ?? "ffmpeg-unknown"; await p.WaitForExitAsync(ct); return line.Trim(); }
    private static async Task<T> Read<T>(string path, CancellationToken ct) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidDataException(path);
    private static Task<JsonDocument> ReadDocument(string path, CancellationToken ct) => Task.Run(() => JsonDocument.Parse(File.ReadAllText(path)), ct);
    private static Task Write(string path, object value, CancellationToken ct) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false), ct); }
    private static string String(JsonElement e, string n) => e.TryGetProperty(n, out var v) ? v.GetString() ?? "" : ""; private static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static void RequireFiles(IEnumerable<string> files, string code) { if (files.Any(x => !File.Exists(x))) Fail(code, "A required committed artifact is missing."); }
    private static string Hash(string x) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x))).ToLowerInvariant(); private static async Task<string> HashFile(string p, CancellationToken ct) { await using var s = File.OpenRead(p); return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant(); }
    private static string EscapeFilter(string p) => p.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
    private static void Cleanup(string p) { if (Directory.Exists(p)) Directory.Delete(p, true); var parent = Path.GetDirectoryName(p); if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent); }
    [DoesNotReturn]
    private static void Fail(string code, string reason) => throw new InvalidOperationException($"{code}: {reason}");
    private sealed record Phase15Entry(string SceneAudioUnitId, string SceneId, int Sequence, string Format, string Language, string AudioRelativePath, long AudioByteLength, string AudioSha256, string TextChecksum, long ActualAudioDurationMs, IReadOnlyList<string> SubtitleSegmentIds, string SourcePhase14AuthorityChecksum);
    private sealed record Phase15ManifestContract(string SchemaVersion, string Language,
        string SourcePhase14AuthorityChecksum, string AuthorityChecksum, string ValidationStatus,
        bool PublicationCommitted, bool DownstreamReady);
    private sealed record Phase15PublicationContract(string SchemaVersion, bool CandidateValidationPassed,
        bool CandidateReadbackPassed, bool PublicationCommitted, bool CommittedReadbackPassed,
        bool CommittedStateValidationPassed, bool DownstreamReady, string SourcePhase14AuthorityChecksum,
        string AuthorityChecksum);
    private sealed record Phase15ValidationContract(int PhaseNo, string Status, string ReasonCode,
        string SourcePhase14AuthorityChecksum, string AuthorityChecksum, string ValidationStatus,
        bool? SemanticValidationPassed, bool? ChecksumValidationPassed, bool? ManifestValidationPassed,
        bool? DownstreamReady);
}
