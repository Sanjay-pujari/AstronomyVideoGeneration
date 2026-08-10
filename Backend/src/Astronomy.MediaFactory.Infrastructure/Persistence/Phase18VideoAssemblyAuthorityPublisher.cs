using System.Diagnostics;
using System.ComponentModel;
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
internal sealed record Phase15TimelineEntry(string SceneAudioUnitId, string SceneId, int Sequence, string Format,
    string Language, string AudioRelativePath, long AudioByteLength, string AudioSha256, string TextChecksum,
    long ActualAudioDurationMs, string VoiceProfileRef, string SpeechStyleRef, string ResolvedVoice,
    string ResolvedRate, string ResolvedStyle, string ProviderRequestId, IReadOnlyList<string> SubtitleSegmentIds,
    string SourcePhase14AuthorityChecksum);

internal sealed record Phase18MediaTools(string FFmpegExecutable, string FFprobeExecutable,
    string FFmpegVersion, string FFprobeVersion, string FFmpegResolutionSource, string FFprobeResolutionSource);

/// <summary>One portable resolution boundary for both native executables used by Phase 18.</summary>
internal static class Phase18MediaToolchainResolver
{
    internal static async Task<Phase18MediaTools> ResolveAsync(string? configuredFfmpeg,
        string? configuredFfprobe, CancellationToken ct)
    {
        try
        {
            var ffmpeg = Resolve(configuredFfmpeg, "FFMPEG_PATH", "ffmpeg");
            var ffmpegVersion = await Version(ffmpeg.Path, "FFmpeg", ct);
            try
            {
                var ffprobe = Resolve(configuredFfprobe, "FFPROBE_PATH", "ffprobe");
                var ffprobeVersion = await Version(ffprobe.Path, "FFprobe", ct);
                return new(ffmpeg.Path, ffprobe.Path, ffmpegVersion, ffprobeVersion, ffmpeg.Source, ffprobe.Source);
            }
            catch (Phase18AuthorityValidationException ex)
            {
                ex.Data["ffmpegResolved"] = true;
                ex.Data["ffmpegVersion"] = ffmpegVersion;
                ex.Data["ffmpegResolutionSource"] = ffmpeg.Source;
                throw;
            }
        }
        catch (Phase18AuthorityValidationException) { throw; }
    }

    internal static (string Path, string Source) Resolve(string? configured, string environmentName, string executable)
    {
        // RenderingOptions defaults to the conventional executable name; regard that as PATH fallback,
        // rather than claiming it was an explicit application configuration.
        if (!string.IsNullOrWhiteSpace(configured) &&
            !string.Equals(configured.Trim(), executable, StringComparison.OrdinalIgnoreCase))
            return ResolveCandidate(configured.Trim(), "Configuration", executable);
        var environment = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(environment)) return ResolveCandidate(environment.Trim(), "Environment", executable);
        return ResolveCandidate(executable, "PATH", executable);
    }

    private static (string Path, string Source) ResolveCandidate(string candidate, string source, string executable)
    {
        if (Path.IsPathRooted(candidate) || candidate.Contains(Path.DirectorySeparatorChar) ||
            candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            if (File.Exists(candidate)) return (Path.GetFullPath(candidate), source);
            throw Unavailable(executable, $"configured candidate '{candidate}' does not exist");
        }
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var names = OperatingSystem.IsWindows() && Path.GetExtension(candidate).Length == 0
            ? new[] { candidate, candidate + ".exe" } : new[] { candidate };
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var name in names)
            {
                var full = Path.Combine(directory, name);
                if (File.Exists(full)) return (Path.GetFullPath(full), source);
            }
        throw Unavailable(executable, $"'{candidate}' was not found on PATH");
    }

    private static async Task<string> Version(string executable, string displayName, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-version");
            using var process = Process.Start(psi) ?? throw new Win32Exception("Process.Start returned null.");
            var output = await process.StandardOutput.ReadLineAsync(ct) ?? await process.StandardError.ReadLineAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException($"version command exited with code {process.ExitCode}");
            return output.Trim();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        { throw Unavailable(displayName, ex.Message); }
    }

    private static Phase18AuthorityValidationException Unavailable(string tool, string detail) =>
        new(Phase18ReasonCodes.MediaToolchainUnavailable,
            $"{tool} executable could not be resolved or started: {detail}.", []);
}

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
        bool overwrite, string? configuredFfmpeg, string? configuredFfprobe, CancellationToken ct)
    {
        try { return await ExecuteCoreAsync(root, language, overwrite, configuredFfmpeg, configuredFfprobe, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is Phase18AuthorityValidationException or Win32Exception or IOException or InvalidOperationException)
        {
            var authority = ex as Phase18AuthorityValidationException;
            var inputs = authority?.LoadedAuthorityArtifacts.Count > 0 ? authority.LoadedAuthorityArtifacts : ExistingInputs(root, language);
            var code = authority?.ReasonCode ?? Phase18ReasonCodes.RenderFailed;
            var reason = authority?.Reason ?? ex.Message;
            var result = FailedResult(inputs, code, reason);
            await Write(Path.Combine(root, "validation", "phase-18-validation.json"), FailureValidation(result,
                ffmpegResolved: ex.Data["ffmpegResolved"] as bool? ?? false, ffprobeResolved: false,
                ffmpegVersion: ex.Data["ffmpegVersion"] as string,
                resolutionSource: ex.Data["ffmpegResolutionSource"] as string,
                renderCalls: 0, probeCalls: 0), ct);
            return result;
        }
    }

    private static async Task<Phase18PublicationResult> ExecuteCoreAsync(string root, string language,
        bool overwrite, string? configuredFfmpeg, string? configuredFfprobe, CancellationToken ct)
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
        using var p16Timeline = await ReadDocument(timeline16, ct);
        using var p17Manifest = await ReadDocument(p17[0], ct);
        var loadedAuthorities = p15.Concat(p16).Concat(p17).Append(timeline16).Append(p17[0]).Distinct().ToArray();
        _ = ValidateAuthorityLineage(p15Checksum, p15Snapshot.SourcePhase14AuthorityChecksum,
            p16Checksum, String(p16Timeline.RootElement, "sourcePhase15AuthorityChecksum"),
            p17Checksum, String(p17Manifest.RootElement, "sourcePhase16AuthorityChecksum"), loadedAuthorities);

        var audio = await ReadTimeline15(timeline15, p15Snapshot, ct);
        var calibrated = await ReadTimeline16(timeline16, ct);
        var motion = new List<Phase17MotionPlan>();
        foreach (var path in plans) motion.Add(await Read<Phase17MotionPlan>(path, ct));
        ValidateSceneLineage(audio.Values.Select(x => new Phase18SceneLineageRow(x.SceneAudioUnitId, x.SceneId,
                x.Format, x.Sequence, x.Language, x.AudioSha256, 0, 0, 0, x.ActualAudioDurationMs)),
            calibrated.Values.Select(x => new Phase18SceneLineageRow(x.SceneAudioUnitId, x.SceneId,
                x.Format, x.Sequence, x.Language, x.AudioSha256, x.FinalSceneDurationMs, x.SceneStartMs, x.SceneEndMs)),
            motion.SelectMany(x => x.Entries).Select(x => new Phase18SceneLineageRow(x.SceneAudioUnitId, x.SceneId,
                x.Format, x.Sequence, x.Language, x.AudioSha256, x.DurationMs, x.SceneStartMs, x.SceneEndMs)),
            inputs);
        ValidateAuthorityDrivenSceneCounts(audio.Values, calibrated.Values, motion.SelectMany(x => x.Entries), inputs);
        await PreflightPhysicalEvidence(root, language, audio, calibrated, motion.SelectMany(x => x.Entries), srts, ct);
        var tools = await Phase18MediaToolchainResolver.ResolveAsync(configuredFfmpeg, configuredFfprobe, ct);
        var toolchain = $"{tools.FFmpegVersion} | {tools.FFprobeVersion}";
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
                evidence.Add(await RenderFormat(root, stage, language, requested[f], motion[f], calibrated, audio, srts[f], tools, ct));
            var authorityChecksum = Hash(JsonSerializer.Serialize(new { identity, outputs = evidence }, Json));
            var manifest = new Phase18Manifest(Schema, language, requested, p15Checksum, p16Checksum, p17Checksum,
                RenderPolicy, VideoPolicy.Version, AudioPolicy.Version, SubtitlePolicy.Version, toolchain, evidence,
                authorityChecksum, true, "Valid", true);
            await Write(Path.Combine(stage, "phase18-manifest.json"), manifest, ct);
            await Write(Path.Combine(stage, "phase18-authority-diagnostics.json"), new { schemaVersion = "phase18.diagnostics/1.0",
                language, requestedFormats = requested, renderPolicy = VideoPolicy, audioPolicy = AudioPolicy,
                subtitlePolicy = SubtitlePolicy, toolchainIdentity = toolchain, ffmpegResolved = true,
                ffprobeResolved = true, ffmpegVersion = tools.FFmpegVersion, ffprobeVersion = tools.FFprobeVersion,
                toolchainResolutionSource = new { ffmpeg = tools.FFmpegResolutionSource, ffprobe = tools.FFprobeResolutionSource },
                renderCallsThisPhase = evidence.Sum(x => x.SourceAudioSha256.Count) + 3, probeCallsThisPhase = 2,
                candidateValidationPassed = true,
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
        IReadOnlyDictionary<string, Phase15TimelineEntry> audio, string srt, Phase18MediaTools tools, CancellationToken ct)
    {
        var requestedFormat = ParseProductionFormat(format);
        var sequences = plan.Entries.Select(x => x.Sequence).ToArray();
        if (ParseProductionFormat(plan.Format) != requestedFormat || plan.Entries.Count != plan.SceneCount ||
            sequences.Distinct().Count() != sequences.Length || sequences.Zip(sequences.Skip(1)).Any(x => x.First >= x.Second))
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
                entry.Sequence != speech.Sequence || ParseProductionFormat(entry.Format) != ParseProductionFormat(timing.Format) ||
                ParseProductionFormat(entry.Format) != ParseProductionFormat(speech.Format) || entry.Language != language ||
                speech.Language != language || entry.DurationMs != timing.FinalSceneDurationMs)
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
            await Run(tools.FFmpegExecutable, ["-y", "-loop", "1", "-framerate", "30", "-i", image, "-i", speechPath,
                "-filter_complex", $"[0:v]scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h},fps=30,format=yuv420p[v];[1:a]apad=whole_dur={seconds},aresample=48000,aformat=channel_layouts=stereo[a]",
                "-map", "[v]", "-map", "[a]", "-t", seconds, "-c:v", "libx264", "-preset", VideoPolicy.Preset,
                "-crf", VideoPolicy.Crf.ToString(), "-pix_fmt", VideoPolicy.PixelFormat, "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", clip], ct);
        }
        ValidateSrt(srt);
        var sidecar = Path.Combine(dir, "final.srt"); File.Copy(srt, sidecar, true);
        var concat = Path.Combine(clips, "concat.txt");
        await File.WriteAllLinesAsync(concat, plan.Entries.Select(x => $"file '{x.Sequence:000}.mp4'"), new UTF8Encoding(false), ct);
        var unburned = Path.Combine(clips, "unsubtitled.mp4");
        await Run(tools.FFmpegExecutable, ["-y", "-f", "concat", "-safe", "0", "-i", concat, "-c", "copy", unburned], ct);
        var final = Path.Combine(dir, "final.mp4");
        var burn = language == "en";
        if (burn) await Run(tools.FFmpegExecutable, ["-y", "-i", unburned, "-vf", $"subtitles={EscapeFilter(sidecar)}", "-c:v", "libx264", "-preset", VideoPolicy.Preset, "-crf", VideoPolicy.Crf.ToString(), "-pix_fmt", "yuv420p", "-c:a", "copy", final], ct);
        else File.Copy(unburned, final, true);
        var physical = await ProbeDuration(tools.FFprobeExecutable, final, ct);
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

    internal static Phase18AuthorityLineageValidation ValidateAuthorityLineage(string phase15AuthorityChecksum,
        string phase15SourcePhase14AuthorityChecksum, string phase16AuthorityChecksum,
        string phase16SourcePhase15AuthorityChecksum, string phase17AuthorityChecksum,
        string phase17SourcePhase16AuthorityChecksum, IReadOnlyList<string> loadedAuthorityArtifacts)
    {
        var p15To16 = string.Equals(phase16SourcePhase15AuthorityChecksum, phase15AuthorityChecksum,
            StringComparison.Ordinal);
        var p16To17 = string.Equals(phase17SourcePhase16AuthorityChecksum, phase16AuthorityChecksum,
            StringComparison.Ordinal);
        var result = new Phase18AuthorityLineageValidation(phase15AuthorityChecksum,
            phase15SourcePhase14AuthorityChecksum, phase16AuthorityChecksum,
            phase16SourcePhase15AuthorityChecksum, phase17AuthorityChecksum,
            phase17SourcePhase16AuthorityChecksum, p15To16, p16To17, p15To16 && p16To17);
        if (!p15To16)
            throw new Phase18AuthorityValidationException(Phase18ReasonCodes.LineageMismatch,
                $"Phase16 source Phase15 checksum does not match committed Phase15 authority. Expected '{phase15AuthorityChecksum}', actual '{phase16SourcePhase15AuthorityChecksum}'.",
                loadedAuthorityArtifacts);
        if (!p16To17)
            throw new Phase18AuthorityValidationException(Phase18ReasonCodes.LineageMismatch,
                $"Phase17 source Phase16 checksum does not match committed Phase16 authority. Expected '{phase16AuthorityChecksum}', actual '{phase17SourcePhase16AuthorityChecksum}'.",
                loadedAuthorityArtifacts);
        return result;
    }

    internal static void ValidateSceneLineage(IEnumerable<Phase18SceneLineageRow> phase15,
        IEnumerable<Phase18SceneLineageRow> phase16, IEnumerable<Phase18SceneLineageRow> phase17,
        IReadOnlyList<string> loadedAuthorityArtifacts)
    {
        // Normalize the three frozen serialization contracts once, at the adapter boundary.
        // Raw values remain on Phase18SceneLineageRow and are therefore available to diagnostics.
        var p15 = phase15.Select(x => NormalizeFormat(x, "Phase15", loadedAuthorityArtifacts))
            .ToDictionary(x => x.Row.SceneAudioUnitId, StringComparer.Ordinal);
        var p16 = phase16.Select(x => NormalizeFormat(x, "Phase16", loadedAuthorityArtifacts))
            .ToDictionary(x => x.Row.SceneAudioUnitId, StringComparer.Ordinal);
        var p17 = phase17.Select(x => NormalizeFormat(x, "Phase17", loadedAuthorityArtifacts)).ToArray();
        foreach (var motion in p17)
        {
            if (!p15.TryGetValue(motion.Row.SceneAudioUnitId, out var audio))
                Mismatch(motion.Row.SceneAudioUnitId, "Phase15.SceneAudioUnitId", "<missing>", "Phase17.SceneAudioUnitId", motion.Row.SceneAudioUnitId, loadedAuthorityArtifacts);
            if (!p16.TryGetValue(motion.Row.SceneAudioUnitId, out var timing))
                Mismatch(motion.Row.SceneAudioUnitId, "Phase16.SceneAudioUnitId", "<missing>", "Phase17.SceneAudioUnitId", motion.Row.SceneAudioUnitId, loadedAuthorityArtifacts);

            var unit = motion.Row.SceneAudioUnitId;
            var audioRow = audio.Row;
            var timingRow = timing.Row;
            var motionRow = motion.Row;

            Compare(unit, "Phase15.SceneId", audioRow.SceneId, "Phase17.SceneId", motionRow.SceneId, loadedAuthorityArtifacts);
            Compare(unit, "Phase16.SceneId", timingRow.SceneId, "Phase17.SceneId", motionRow.SceneId, loadedAuthorityArtifacts);
            Compare(unit, "Phase15.ParsedFormat", audio.ParsedFormat, "Phase17.ParsedFormat", motion.ParsedFormat, loadedAuthorityArtifacts);
            Compare(unit, "Phase16.ParsedFormat", timing.ParsedFormat, "Phase17.ParsedFormat", motion.ParsedFormat, loadedAuthorityArtifacts);
            Compare(unit, "Phase15.Sequence", audioRow.Sequence, "Phase17.Sequence", motionRow.Sequence, loadedAuthorityArtifacts);
            Compare(unit, "Phase16.Sequence", timingRow.Sequence, "Phase17.Sequence", motionRow.Sequence, loadedAuthorityArtifacts);
            Compare(unit, "Phase15.Language", audioRow.Language, "Phase17.Language", motionRow.Language, loadedAuthorityArtifacts);
            Compare(unit, "Phase16.Language", timingRow.Language, "Phase17.Language", motionRow.Language, loadedAuthorityArtifacts);
            Compare(unit, "Phase15.AudioSha256", audioRow.AudioSha256, "Phase17.AudioSha256", motionRow.AudioSha256, loadedAuthorityArtifacts);
            if (string.IsNullOrWhiteSpace(timingRow.AudioSha256))
                Mismatch(unit, "Phase15.AudioSha256", audioRow.AudioSha256, "Phase16.AudioSha256", timingRow.AudioSha256, loadedAuthorityArtifacts);
            Compare(unit, "Phase15.AudioSha256", audioRow.AudioSha256, "Phase16.AudioSha256", timingRow.AudioSha256, loadedAuthorityArtifacts);
            if (audioRow.ActualAudioDurationMs <= 0)
                Mismatch(unit, "Phase15.ActualAudioDurationMs", audioRow.ActualAudioDurationMs, "required minimum", 1, loadedAuthorityArtifacts);
            if (audioRow.ActualAudioDurationMs > timingRow.DurationMs + ProbeToleranceMs)
                Mismatch(unit, "Phase15.ActualAudioDurationMs", audioRow.ActualAudioDurationMs,
                    $"Phase16.FinalSceneDurationMs+codecToleranceMs({ProbeToleranceMs})", timingRow.DurationMs + ProbeToleranceMs, loadedAuthorityArtifacts);
            Compare(unit, "Phase16.FinalSceneDurationMs", timingRow.DurationMs, "Phase17.DurationMs", motionRow.DurationMs, loadedAuthorityArtifacts);
            Compare(unit, "Phase16.SceneStartMs", timingRow.SceneStartMs, "Phase17.SceneStartMs", motionRow.SceneStartMs, loadedAuthorityArtifacts);
            Compare(unit, "Phase16.SceneEndMs", timingRow.SceneEndMs, "Phase17.SceneEndMs", motionRow.SceneEndMs, loadedAuthorityArtifacts);
        }
        if (p17.Length != p15.Count || p17.Length != p16.Count)
            throw new Phase18AuthorityValidationException(Phase18ReasonCodes.LineageMismatch,
                $"Scene lineage counts differ. Phase15={p15.Count}, Phase16={p16.Count}, Phase17={p17.Length}.",
                loadedAuthorityArtifacts);
    }

    internal static Phase18ProductionFormat ParseProductionFormat(string? value)
    {
        var token = value?.Trim();
        return token switch
        {
            "short" or "Short" or "SHORT" => Phase18ProductionFormat.Short,
            "long" or "Long" or "LONG" => Phase18ProductionFormat.Long,
            _ => throw new Phase18AuthorityValidationException(Phase18ReasonCodes.FormatInvalid,
                $"Production format token {Value(value)} is not one of short/Short/SHORT/long/Long/LONG.", [])
        };
    }

    private static NormalizedLineageRow NormalizeFormat(Phase18SceneLineageRow row, string phase,
        IReadOnlyList<string> loadedAuthorityArtifacts)
    {
        try { return new(row, ParseProductionFormat(row.Format)); }
        catch (Phase18AuthorityValidationException error)
        {
            throw new Phase18AuthorityValidationException(error.ReasonCode,
                $"SceneAudioUnitId {row.SceneAudioUnitId}: {phase}.Format={Value(row.Format)} is invalid.",
                loadedAuthorityArtifacts);
        }
    }

    private sealed record NormalizedLineageRow(Phase18SceneLineageRow Row, Phase18ProductionFormat ParsedFormat);

    internal static void ValidateAuthorityDrivenSceneCounts(IEnumerable<string> phase15Formats,
        IEnumerable<string> phase16Formats, IEnumerable<string> phase17Formats,
        IReadOnlyList<string> loadedAuthorityArtifacts)
    {
        Dictionary<Phase18ProductionFormat, int> Counts(IEnumerable<string> formats) => formats
            .GroupBy(ParseProductionFormat).ToDictionary(x => x.Key, x => x.Count());
        var p15 = Counts(phase15Formats); var p16 = Counts(phase16Formats); var p17 = Counts(phase17Formats);
        foreach (var format in Enum.GetValues<Phase18ProductionFormat>())
        {
            var c15 = p15.GetValueOrDefault(format); var c16 = p16.GetValueOrDefault(format); var c17 = p17.GetValueOrDefault(format);
            if (c15 != c16 || c15 != c17)
                throw new Phase18AuthorityValidationException(Phase18ReasonCodes.LineageMismatch,
                    $"Canonical {format} scene counts differ: Phase15={c15}, Phase16={c16}, Phase17={c17}.",
                    loadedAuthorityArtifacts);
        }
    }

    private static void ValidateAuthorityDrivenSceneCounts(IEnumerable<Phase15TimelineEntry> phase15,
        IEnumerable<Phase16CalibratedScene> phase16, IEnumerable<Phase17MotionEntry> phase17,
        IReadOnlyList<string> loadedAuthorityArtifacts) => ValidateAuthorityDrivenSceneCounts(
            phase15.Select(x => x.Format), phase16.Select(x => x.Format), phase17.Select(x => x.Format), loadedAuthorityArtifacts);

    private static async Task PreflightPhysicalEvidence(string root, string language,
        IReadOnlyDictionary<string, Phase15TimelineEntry> audio,
        IReadOnlyDictionary<string, Phase16CalibratedScene> calibrated,
        IEnumerable<Phase17MotionEntry> motion, IEnumerable<string> srts, CancellationToken ct)
    {
        foreach (var srt in srts) ValidateSrt(srt);
        foreach (var entry in motion)
        {
            var speech = audio[entry.SceneAudioUnitId];
            var timing = calibrated[entry.SceneAudioUnitId];
            if (entry.Language != language || speech.Language != language || timing.Language != language)
                throw new Phase18AuthorityValidationException(Phase18ReasonCodes.LineageMismatch,
                    $"SceneAudioUnitId {entry.SceneAudioUnitId}: requested language='{language}', Phase15='{speech.Language}', Phase16='{timing.Language}', Phase17='{entry.Language}'.", []);
            if (speech.AudioByteLength <= 0 || string.IsNullOrWhiteSpace(speech.AudioSha256) ||
                speech.ActualAudioDurationMs <= 0 || string.IsNullOrWhiteSpace(speech.AudioRelativePath))
                Fail(Phase18ReasonCodes.AudioPhysicalEvidenceInvalid, $"Canonical Phase15 audio fields are invalid for {entry.SceneAudioUnitId}.");
            var speechPath = ResolveOwned(root, speech.AudioRelativePath);
            if (!File.Exists(speechPath) || new FileInfo(speechPath).Length != speech.AudioByteLength ||
                await HashFile(speechPath, ct) != speech.AudioSha256)
                Fail(Phase18ReasonCodes.AudioPhysicalEvidenceInvalid, entry.SceneId);
            var image = ResolveOwned(root, entry.VisualAssetPath);
            if (!File.Exists(image) || await HashFile(image, ct) != entry.VisualAssetSha256)
                Fail(Phase18ReasonCodes.VisualPhysicalEvidenceInvalid, entry.SceneId);
            var dimensions = await Image.IdentifyAsync(image, ct);
            if (dimensions?.Width != entry.Width || dimensions.Height != entry.Height)
                Fail(Phase18ReasonCodes.VisualPhysicalEvidenceInvalid, entry.SceneId);
        }
    }

    private static void Compare<T>(string unit, string expectedField, T expected, string actualField, T actual,
        IReadOnlyList<string> loadedAuthorityArtifacts)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Mismatch(unit, expectedField, expected, actualField, actual, loadedAuthorityArtifacts);
    }

    [DoesNotReturn]
    private static void Mismatch<TExpected, TActual>(string unit, string expectedField, TExpected expected,
        string actualField, TActual actual, IReadOnlyList<string> loadedAuthorityArtifacts) =>
        throw new Phase18AuthorityValidationException(Phase18ReasonCodes.LineageMismatch,
            $"SceneAudioUnitId {unit}: {actualField}={Value(actual)} but {expectedField}={Value(expected)}.",
            loadedAuthorityArtifacts);

    private static string Value<T>(T value) => value is null ? "<absent>" : $"'{value}'";
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

    private static async Task<long> ProbeDuration(string ffprobe, string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffprobe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
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
    internal static async Task<Dictionary<string, Phase15TimelineEntry>> ReadTimeline15(string path, Phase18Phase15AuthoritySnapshot authority, CancellationToken ct)
    {
        using var d = await ReadDocument(path, ct);
        if (!String(d.RootElement, "authorityChecksum").Equals(authority.AuthorityChecksum, StringComparison.Ordinal)
            || !String(d.RootElement, "sourcePhase14AuthorityChecksum").Equals(authority.SourcePhase14AuthorityChecksum, StringComparison.Ordinal))
            throw new Phase18AuthorityValidationException(Phase18ReasonCodes.UpstreamPhase15Invalid,
                "Timeline authority checksum lineage does not agree.", authority.LoadedAuthorityArtifacts.Append(path).ToArray());
        var entries = d.RootElement.GetProperty("entries").Deserialize<List<Phase15TimelineEntry>>(Json)
            ?? throw new JsonException("Phase15 canonical entries are absent.");
        foreach (var entry in entries)
        {
            if (!entry.SourcePhase14AuthorityChecksum.Equals(authority.SourcePhase14AuthorityChecksum, StringComparison.Ordinal))
                throw new Phase18AuthorityValidationException(Phase18ReasonCodes.LineageMismatch,
                    $"SceneAudioUnitId {entry.SceneAudioUnitId}: Phase15 entry source Phase14 checksum differs from its timeline root.",
                    authority.LoadedAuthorityArtifacts.Append(path).ToArray());
            if (string.IsNullOrWhiteSpace(entry.SceneAudioUnitId) || string.IsNullOrWhiteSpace(entry.SceneId) ||
                string.IsNullOrWhiteSpace(entry.Language) || string.IsNullOrWhiteSpace(entry.AudioRelativePath) ||
                entry.AudioByteLength <= 0 || string.IsNullOrWhiteSpace(entry.AudioSha256) || entry.ActualAudioDurationMs <= 0)
                throw new Phase18AuthorityValidationException(Phase18ReasonCodes.UpstreamPhase15Invalid,
                    $"Canonical Phase15 entry {entry.SceneAudioUnitId} has missing physical authority fields.",
                    authority.LoadedAuthorityArtifacts.Append(path).ToArray());
        }
        return entries.ToDictionary(x => x.SceneAudioUnitId, StringComparer.Ordinal);
    }
    private static async Task<Dictionary<string, Phase16CalibratedScene>> ReadTimeline16(string path, CancellationToken ct) { using var d = await ReadDocument(path, ct); return new[] { "short", "long" }.SelectMany(n => d.RootElement.GetProperty(n).Deserialize<List<Phase16CalibratedScene>>(Json) ?? []).ToDictionary(x => x.SceneAudioUnitId, StringComparer.Ordinal); }
    private static void ValidateSrt(string path) { var text = File.ReadAllText(path, new UTF8Encoding(false, true)); if (string.IsNullOrWhiteSpace(text) || !text.Contains(" --> ", StringComparison.Ordinal)) Fail(Phase18ReasonCodes.SubtitlePhysicalEvidenceInvalid, "SRT is not valid UTF-8/timed text."); }
    private static string ResolveOwned(string root, string path) { var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path)); var owned = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; if (!full.StartsWith(owned, StringComparison.Ordinal)) Fail(Phase18ReasonCodes.CandidateValidationFailed, "Authority path escapes the execution root."); return full; }
    private static async Task ProjectCompatibility(string root, string language, IEnumerable<Phase18MediaEvidence> outputs, string canonical, CancellationToken ct) { foreach (var x in outputs) { var format = x.Format.ToLowerInvariant(); var destinations = new[] { Path.Combine(root, "video-assembly", language, format, "final.mp4"), Path.Combine(root, "video", format, format == "short" ? "final-short.mp4" : "final-long.mp4") }; foreach (var destination in destinations) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(Path.Combine(canonical, x.VideoRelativePath), destination, true); } } await Task.CompletedTask; }
    private static object Publication(string checksum) => new { schemaVersion = "phase18.publication/1.0", reasonCode = Phase18ReasonCodes.Accepted, candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, committedStateValidationPassed = true, semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true, validationStatus = "Valid", downstreamReady = true, authorityChecksum = checksum };
    private static object Validation(Phase18PublicationResult x) => new { phaseNo = 18, phaseName = "Video Assembly Authority", status = "Succeeded", x.ReasonCode, x.Reason, inputFiles = x.InputFiles, outputFiles = x.OutputFiles, x.Generated, x.Reused, x.Regenerated, x.CandidateValidationPassed, x.CandidateReadbackPassed, x.PublicationCommitted, x.CommittedReadbackPassed, x.CommittedStateValidationPassed, x.SourcePhase15AuthorityChecksum, x.SourcePhase16AuthorityChecksum, x.SourcePhase17AuthorityChecksum, x.AuthorityChecksum, x.ManifestValidationStatus, x.ValidationStatus, x.SemanticValidationPassed, x.ChecksumValidationPassed, x.ManifestValidationPassed, x.DownstreamReady };
    private static Phase18PublicationResult Result(IReadOnlyList<string> i, IReadOnlyList<string> o, bool g, bool u, bool r, string p15, string p16, string p17, string checksum) => new(i, o, Phase18ReasonCodes.Accepted, "Phase 18 video assembly authority accepted.", g, u, r, true, true, true, true, true, p15, p16, p17, checksum, "Valid", "Valid", true, true, true, true);
    private static string[] ExistingInputs(string root, string language) =>
        new[] { "15-tts", "16-duration-calibration", "17-motion" }
            .SelectMany(phase => Directory.Exists(Path.Combine(root, phase, language))
                ? Directory.EnumerateFiles(Path.Combine(root, phase, language), "*", SearchOption.AllDirectories)
                : []).Concat(new[] { 15, 16, 17 }.Select(number => Path.Combine(root, "validation", $"phase-{number}-validation.json"))
                    .Where(File.Exists)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static Phase18PublicationResult FailedResult(IReadOnlyList<string> inputs, string code, string reason) =>
        new(inputs, [], code, reason, false, false, false, false, false, false, false, false,
            "", "", "", "", "Invalid", "Invalid", false, false, false, false);
    private static object FailureValidation(Phase18PublicationResult x, bool ffmpegResolved, bool ffprobeResolved,
        string? ffmpegVersion, string? resolutionSource, int renderCalls, int probeCalls) => new { phaseNo = 18, phaseName = "Cinematic Video Assembly V2",
            status = "Failed", x.ReasonCode, x.Reason, inputFiles = x.InputFiles, outputFiles = Array.Empty<string>(),
            x.PublicationCommitted, x.DownstreamReady, ffmpegResolved, ffprobeResolved,
            ffmpegVersion, ffprobeVersion = (string?)null, toolchainResolutionSource = resolutionSource,
            renderCallsThisPhase = renderCalls, probeCallsThisPhase = probeCalls };
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
