using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AzureTtsAudioGenerationService(
    IOptions<RenderingOptions> renderingOptions,
    IOptions<AzureSpeechOptions> azureSpeechOptions,
    IAzureSpeechClient speechClient,
    ILogger<AzureTtsAudioGenerationService> logger) : ITtsAudioGenerationService
{
    private const string FinalPackageFileName = "tts-package-final.json";
    private const string ManifestFileName = "tts-audio-manifest.json";
    private const string ProviderName = "AzureSpeech";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<TtsAudioGenerationResult> GenerateTtsAudioAsync(TtsAudioGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");
        if ((request.MaxPlans ?? 1) > 1)
            throw new ArgumentException("Phase 9C.1 audio generation pilot supports maxPlans=1 only.");
        if (request.PlanIds is not { Count: > 0 })
            throw new ArgumentException("Phase 9C.1 audio generation pilot requires an explicit planIds list; all-plan generation is disabled.");

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var segmentAudioCount = 0;
        var combinedAudioCount = 0;
        var completedCount = 0;
        var failedCount = 0;
        var packagePaths = EnumeratePackageFiles(request).ToList();

        if (!request.DryRun && !IsAzureSpeechConfigured(azureSpeechOptions.Value, out var configurationWarning))
        {
            warnings.Add(configurationWarning);
            return new TtsAudioGenerationResult(packagePaths.Count, 0, 0, 0, 0, generatedFiles, warnings);
        }

        foreach (var packagePath in packagePaths)
        {
            try
            {
                var package = await ReadFinalPackageAsync(packagePath, cancellationToken);
                if (package is null)
                {
                    warnings.Add($"Skipped invalid final TTS package JSON at {packagePath}.");
                    continue;
                }

                var validationWarnings = ValidatePackage(package, request, packagePath).ToList();
                if (validationWarnings.Count > 0)
                {
                    warnings.AddRange(validationWarnings);
                    if (!request.DryRun)
                        failedCount++;
                    continue;
                }

                var ttsDirectory = Path.GetDirectoryName(packagePath) ?? ResolveTtsDirectory(package.RegionId, package.ContentGenerationPlanId);
                var audioDirectory = Path.Combine(ttsDirectory, "audio");
                var segmentPlans = package.Segments
                    .OrderBy(segment => segment.SceneNumber)
                    .Select(segment => new SegmentPlan(segment, Path.Combine(audioDirectory, $"scene-{segment.SceneNumber:00}.wav")))
                    .ToList();
                var combinedPath = Path.Combine(audioDirectory, "narration-combined.wav");
                var manifestPath = Path.Combine(audioDirectory, ManifestFileName);

                if (request.DryRun)
                {
                    generatedFiles.AddRange(segmentPlans.Select(plan => plan.AudioPath));
                    if (request.CombineSegments)
                        generatedFiles.Add(combinedPath);
                    continue;
                }

                Directory.CreateDirectory(audioDirectory);
                var manifestSegments = new List<TtsAudioManifestSegment>();
                foreach (var segmentPlan in segmentPlans)
                {
                    if (File.Exists(segmentPlan.AudioPath) && !request.OverwriteExisting)
                    {
                        var existingInfo = ValidateWavFile(segmentPlan.AudioPath);
                        EnsurePilotWavQuality(existingInfo, $"existing scene {segmentPlan.Segment.SceneNumber}");
                        manifestSegments.Add(new TtsAudioManifestSegment(segmentPlan.Segment.SceneNumber, segmentPlan.AudioPath, existingInfo.DurationSeconds, existingInfo.FileSizeBytes, "Completed"));
                        segmentAudioCount++;
                        generatedFiles.Add(segmentPlan.AudioPath);
                        continue;
                    }

                    var audioBytes = await speechClient.SynthesizeWavSsmlAsync(segmentPlan.Segment.Ssml, azureSpeechOptions.Value, cancellationToken);
                    await File.WriteAllBytesAsync(segmentPlan.AudioPath, audioBytes, cancellationToken);

                    var validation = ValidateWavFile(segmentPlan.AudioPath);
                    EnsurePilotWavQuality(validation, $"scene {segmentPlan.Segment.SceneNumber}");

                    manifestSegments.Add(new TtsAudioManifestSegment(segmentPlan.Segment.SceneNumber, segmentPlan.AudioPath, validation.DurationSeconds, validation.FileSizeBytes, "Completed"));
                    segmentAudioCount++;
                    generatedFiles.Add(segmentPlan.AudioPath);
                }

                var combinedAudioPath = string.Empty;
                var totalDurationSeconds = manifestSegments.Sum(segment => segment.DurationSeconds);
                if (request.CombineSegments)
                {
                    if (!File.Exists(combinedPath) || request.OverwriteExisting)
                        CombineWavFiles(segmentPlans.Select(plan => plan.AudioPath).ToList(), combinedPath);

                    var combinedValidation = ValidateWavFile(combinedPath);
                    EnsurePilotWavQuality(combinedValidation, "combined narration");

                    combinedAudioPath = combinedPath;
                    totalDurationSeconds = combinedValidation.DurationSeconds;
                    combinedAudioCount++;
                    generatedFiles.Add(combinedPath);
                }

                var manifest = new TtsAudioManifest(
                    package.ContentGenerationPlanId,
                    package.RegionId,
                    package.VoiceProfile.VoiceName,
                    ProviderName,
                    manifestSegments,
                    combinedAudioPath,
                    totalDurationSeconds,
                    DateTimeOffset.UtcNow);
                await using (var stream = File.Create(manifestPath))
                {
                    await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
                }

                generatedFiles.Add(manifestPath);
                completedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "TTS audio generation failed for package {PackagePath}", packagePath);
                warnings.Add($"TTS audio generation failed for package {packagePath}: {ex.Message}");
                failedCount++;
            }
        }

        return new TtsAudioGenerationResult(packagePaths.Count, segmentAudioCount, combinedAudioCount, completedCount, failedCount, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings);
    }


    public async Task<TtsAudioBulkGenerationResult> GenerateTtsAudioBulkAsync(TtsAudioBulkGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var completedCount = 0;
        var failedCount = 0;
        var skippedExistingCount = 0;
        var combinedAudioCount = 0;
        var packagePaths = await SelectBulkPackageFilesAsync(request, warnings, cancellationToken);
        var selectedPackagePaths = new List<string>();

        foreach (var candidate in packagePaths)
        {
            var ttsDirectory = Path.GetDirectoryName(candidate.Path) ?? ResolveTtsDirectory(candidate.Package.RegionId, candidate.Package.ContentGenerationPlanId);
            var combinedPath = Path.Combine(ttsDirectory, "audio", "narration-combined.wav");
            if (!request.OverwriteExisting && File.Exists(combinedPath))
            {
                skippedExistingCount++;
                continue;
            }

            selectedPackagePaths.Add(candidate.Path);
            if (selectedPackagePaths.Count >= (request.MaxPlans ?? int.MaxValue))
                break;
        }

        if (!request.DryRun && selectedPackagePaths.Count > 0 && !IsAzureSpeechConfigured(azureSpeechOptions.Value, out var configurationWarning))
        {
            warnings.Add(configurationWarning);
            return new TtsAudioBulkGenerationResult(selectedPackagePaths.Count, 0, 0, skippedExistingCount, 0, generatedFiles, warnings);
        }

        foreach (var packagePath in selectedPackagePaths)
        {
            try
            {
                var package = await ReadFinalPackageAsync(packagePath, cancellationToken);
                if (package is null)
                {
                    warnings.Add($"Skipped invalid final TTS package JSON at {packagePath}.");
                    continue;
                }

                var validationWarnings = ValidateBulkPackage(package, request, packagePath).ToList();
                if (validationWarnings.Count > 0)
                {
                    warnings.AddRange(validationWarnings);
                    if (!request.DryRun)
                        failedCount++;
                    continue;
                }

                var ttsDirectory = Path.GetDirectoryName(packagePath) ?? ResolveTtsDirectory(package.RegionId, package.ContentGenerationPlanId);
                var audioDirectory = Path.Combine(ttsDirectory, "audio");
                var segmentPlans = package.Segments
                    .OrderBy(segment => segment.SceneNumber)
                    .Select(segment => new SegmentPlan(segment, Path.Combine(audioDirectory, $"scene-{segment.SceneNumber:00}.wav")))
                    .ToList();
                var combinedPath = Path.Combine(audioDirectory, "narration-combined.wav");
                var manifestPath = Path.Combine(audioDirectory, ManifestFileName);

                if (request.DryRun)
                {
                    generatedFiles.AddRange(segmentPlans.Select(plan => plan.AudioPath));
                    if (request.CombineSegments)
                        generatedFiles.Add(combinedPath);
                    generatedFiles.Add(manifestPath);
                    continue;
                }

                Directory.CreateDirectory(audioDirectory);
                var manifestSegments = new List<TtsAudioManifestSegment>();
                foreach (var segmentPlan in segmentPlans)
                {
                    if (File.Exists(segmentPlan.AudioPath) && !request.OverwriteExisting)
                    {
                        var existingInfo = ValidateWavFile(segmentPlan.AudioPath);
                        EnsurePilotWavQuality(existingInfo, $"existing scene {segmentPlan.Segment.SceneNumber}");
                        manifestSegments.Add(new TtsAudioManifestSegment(segmentPlan.Segment.SceneNumber, segmentPlan.AudioPath, existingInfo.DurationSeconds, existingInfo.FileSizeBytes, "Completed"));
                        generatedFiles.Add(segmentPlan.AudioPath);
                        continue;
                    }

                    var audioBytes = await speechClient.SynthesizeWavSsmlAsync(segmentPlan.Segment.Ssml, azureSpeechOptions.Value, cancellationToken);
                    await File.WriteAllBytesAsync(segmentPlan.AudioPath, audioBytes, cancellationToken);

                    var validation = ValidateWavFile(segmentPlan.AudioPath);
                    EnsurePilotWavQuality(validation, $"scene {segmentPlan.Segment.SceneNumber}");

                    manifestSegments.Add(new TtsAudioManifestSegment(segmentPlan.Segment.SceneNumber, segmentPlan.AudioPath, validation.DurationSeconds, validation.FileSizeBytes, "Completed"));
                    generatedFiles.Add(segmentPlan.AudioPath);
                }

                var combinedAudioPath = string.Empty;
                var totalDurationSeconds = manifestSegments.Sum(segment => segment.DurationSeconds);
                if (request.CombineSegments)
                {
                    if (!File.Exists(combinedPath) || request.OverwriteExisting)
                        CombineWavFiles(segmentPlans.Select(plan => plan.AudioPath).ToList(), combinedPath);

                    var combinedValidation = ValidateWavFile(combinedPath);
                    EnsurePilotWavQuality(combinedValidation, "combined narration");

                    combinedAudioPath = combinedPath;
                    totalDurationSeconds = combinedValidation.DurationSeconds;
                    combinedAudioCount++;
                    generatedFiles.Add(combinedPath);
                }

                var manifest = new TtsAudioManifest(
                    package.ContentGenerationPlanId,
                    package.RegionId,
                    package.VoiceProfile.VoiceName,
                    ProviderName,
                    manifestSegments,
                    combinedAudioPath,
                    totalDurationSeconds,
                    DateTimeOffset.UtcNow);
                await using (var stream = File.Create(manifestPath))
                {
                    await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
                }

                generatedFiles.Add(manifestPath);
                completedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Bulk TTS audio generation failed for package {PackagePath}", packagePath);
                warnings.Add($"Bulk TTS audio generation failed for package {packagePath}: {ex.Message}");
                failedCount++;
            }
        }

        return new TtsAudioBulkGenerationResult(selectedPackagePaths.Count, completedCount, failedCount, skippedExistingCount, combinedAudioCount, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings);
    }

    private IEnumerable<string> EnumeratePackageFiles(TtsAudioGenerationRequest request)
    {
        var root = ResolveWorkingDirectoryRoot();
        var assetsRoot = Path.Combine(root, "assets");
        var region = SanitizePathSegment(request.RegionId);
        var requestedPlanIds = request.PlanIds is { Count: > 0 } ? request.PlanIds.ToArray() : [];

        IEnumerable<string> files;
        if (requestedPlanIds.Length > 0)
        {
            var regionRoots = !string.IsNullOrWhiteSpace(region)
                ? new[] { Path.Combine(assetsRoot, region) }
                : Directory.Exists(assetsRoot) ? Directory.EnumerateDirectories(assetsRoot).ToArray() : [];
            files = regionRoots.SelectMany(regionRoot => requestedPlanIds.Select(planId => Path.Combine(regionRoot, "plans", planId.ToString("D"), "tts", FinalPackageFileName)));
        }
        else
        {
            var searchRoot = !string.IsNullOrWhiteSpace(region) ? Path.Combine(assetsRoot, region, "plans") : assetsRoot;
            files = Directory.Exists(searchRoot)
                ? Directory.EnumerateFiles(searchRoot, FinalPackageFileName, SearchOption.AllDirectories)
                : [];
        }

        return files.Where(File.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(request.MaxPlans ?? int.MaxValue);
    }


    private async Task<IReadOnlyList<BulkPackageCandidate>> SelectBulkPackageFilesAsync(TtsAudioBulkGenerationRequest request, List<string> warnings, CancellationToken cancellationToken)
    {
        var root = ResolveWorkingDirectoryRoot();
        var assetsRoot = Path.Combine(root, "assets");
        var region = SanitizePathSegment(request.RegionId);
        var requestedPlanIds = request.PlanIds is { Count: > 0 } ? request.PlanIds.ToArray() : [];

        IEnumerable<string> files;
        if (requestedPlanIds.Length > 0)
        {
            var regionRoots = !string.IsNullOrWhiteSpace(region)
                ? new[] { Path.Combine(assetsRoot, region) }
                : Directory.Exists(assetsRoot) ? Directory.EnumerateDirectories(assetsRoot).ToArray() : [];
            files = regionRoots.SelectMany(regionRoot => requestedPlanIds.Select(planId => Path.Combine(regionRoot, "plans", planId.ToString("D"), "tts", FinalPackageFileName)));
        }
        else
        {
            var searchRoot = !string.IsNullOrWhiteSpace(region) ? Path.Combine(assetsRoot, region, "plans") : assetsRoot;
            files = Directory.Exists(searchRoot)
                ? Directory.EnumerateFiles(searchRoot, FinalPackageFileName, SearchOption.AllDirectories)
                : [];
        }

        var candidates = new List<BulkPackageCandidate>();
        foreach (var path in files.Where(File.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            FinalTtsPackageDocument? package;
            try
            {
                package = await ReadFinalPackageAsync(path, cancellationToken);
            }
            catch (JsonException ex)
            {
                warnings.Add($"Skipped invalid final TTS package JSON at {path}: {ex.Message}");
                continue;
            }

            if (package is null)
            {
                warnings.Add($"Skipped invalid final TTS package JSON at {path}.");
                continue;
            }

            var validationWarnings = ValidateBulkPackage(package, request, path).ToList();
            if (validationWarnings.Count > 0)
                continue;

            candidates.Add(new BulkPackageCandidate(path, package));
        }

        return candidates;
    }

    private static async Task<FinalTtsPackageDocument?> ReadFinalPackageAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<FinalTtsPackageDocument>(stream, JsonOptions, cancellationToken);
    }

    private static IEnumerable<string> ValidatePackage(FinalTtsPackageDocument package, TtsAudioGenerationRequest request, string path)
    {
        if (!string.Equals(package.ContentCategory, "RareEventAlert", StringComparison.OrdinalIgnoreCase))
            yield return $"Skipped plan {package.ContentGenerationPlanId}: Phase 9C.1 pilot only supports contentCategory RareEventAlert.";
        if (!string.Equals(package.TtsProvider, ProviderName, StringComparison.OrdinalIgnoreCase))
            yield return $"Skipped plan {package.ContentGenerationPlanId}: TTS provider must be {ProviderName}.";
        if (!package.ReadyForTts)
            yield return $"Skipped plan {package.ContentGenerationPlanId}: readyForTts must be true.";
        if (!package.ReadyForAudioGeneration)
            yield return $"Skipped plan {package.ContentGenerationPlanId}: readyForAudioGeneration must be true.";
        if (!string.Equals(package.SsmlValidationStatus, "Valid", StringComparison.OrdinalIgnoreCase))
            yield return $"Skipped plan {package.ContentGenerationPlanId}: ssmlValidationStatus must be Valid.";
        if (package.Segments.Count == 0)
            yield return $"Skipped plan {package.ContentGenerationPlanId}: at least one TTS segment is required.";
        if (!string.IsNullOrWhiteSpace(request.RegionId) && !string.Equals(package.RegionId, request.RegionId.Trim(), StringComparison.OrdinalIgnoreCase))
            yield return $"Skipped package {path}: regionId does not match the request.";
        if (request.PlanIds is { Count: > 0 } planIds && (!Guid.TryParse(package.ContentGenerationPlanId, out var planId) || !planIds.Contains(planId)))
            yield return $"Skipped package {path}: contentGenerationPlanId does not match the request.";
    }


    private static IEnumerable<string> ValidateBulkPackage(FinalTtsPackageDocument package, TtsAudioBulkGenerationRequest request, string path)
    {
        if (!string.Equals(package.TtsProvider, ProviderName, StringComparison.OrdinalIgnoreCase))
            yield return $"Skipped plan {package.ContentGenerationPlanId}: TTS provider must be {ProviderName}.";
        if (!package.ReadyForTts)
            yield return $"Skipped plan {package.ContentGenerationPlanId}: readyForTts must be true.";
        if (!package.ReadyForAudioGeneration)
            yield return $"Skipped plan {package.ContentGenerationPlanId}: readyForAudioGeneration must be true.";
        if (!string.Equals(package.SsmlValidationStatus, "Valid", StringComparison.OrdinalIgnoreCase))
            yield return $"Skipped plan {package.ContentGenerationPlanId}: ssmlValidationStatus must be Valid.";
        if (package.Segments.Count == 0)
            yield return $"Skipped plan {package.ContentGenerationPlanId}: at least one TTS segment is required.";
        if (!string.IsNullOrWhiteSpace(request.RegionId) && !string.Equals(package.RegionId, request.RegionId.Trim(), StringComparison.OrdinalIgnoreCase))
            yield return $"Skipped package {path}: regionId does not match the request.";
        if (request.PlanIds is { Count: > 0 } planIds && (!Guid.TryParse(package.ContentGenerationPlanId, out var planId) || !planIds.Contains(planId)))
            yield return $"Skipped package {path}: contentGenerationPlanId does not match the request.";
    }

    private static bool IsAzureSpeechConfigured(AzureSpeechOptions options, out string warning)
    {
        if (options.UseManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.ResourceId))
            {
                warning = "Azure Speech configuration is missing Region and/or ResourceId for managed identity. TTS audio generation was skipped.";
                return false;
            }

            warning = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.Key))
        {
            warning = "Azure Speech configuration is missing AzureSpeech:Key. TTS audio generation was skipped.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.Region) && string.IsNullOrWhiteSpace(options.Endpoint))
        {
            warning = "Azure Speech configuration is missing AzureSpeech:Region or AzureSpeech:Endpoint. TTS audio generation was skipped.";
            return false;
        }

        warning = string.Empty;
        return true;
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private string ResolveTtsDirectory(string regionId, string planId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId) ?? regionId, "plans", planId, "tts");

    private static string? SanitizePathSegment(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : string.Join("_", value.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    private static WavInfo ValidateWavFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Expected WAV file was not created.", path);

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || !IsAscii(bytes, 0, "RIFF") || !IsAscii(bytes, 8, "WAVE"))
            throw new InvalidOperationException($"WAV format is invalid for {path}.");

        var fmtOffset = FindChunk(bytes, "fmt ");
        var dataOffset = FindChunk(bytes, "data");
        if (fmtOffset < 0 || dataOffset < 0)
            throw new InvalidOperationException($"WAV format is missing fmt or data chunks for {path}.");

        var audioFormat = BitConverter.ToUInt16(bytes, fmtOffset + 8);
        var channels = BitConverter.ToUInt16(bytes, fmtOffset + 10);
        var sampleRate = BitConverter.ToUInt32(bytes, fmtOffset + 12);
        var byteRate = BitConverter.ToUInt32(bytes, fmtOffset + 16);
        var dataSize = BitConverter.ToUInt32(bytes, dataOffset + 4);
        if (audioFormat != 1 || channels == 0 || sampleRate == 0 || byteRate == 0 || dataSize == 0)
            throw new InvalidOperationException($"WAV PCM metadata is invalid for {path}.");

        return new WavInfo(bytes.Length, dataSize / (double)byteRate, dataOffset + 8, (int)dataSize, channels, sampleRate, byteRate);
    }

    private static void EnsurePilotWavQuality(WavInfo info, string label)
    {
        if (info.FileSizeBytes <= 10 * 1024 || info.DurationSeconds <= 1)
            throw new InvalidOperationException($"Generated audio failed validation for {label}: file size must exceed 10 KB and duration must exceed 1 second.");
    }

    private static void CombineWavFiles(IReadOnlyList<string> inputPaths, string outputPath)
    {
        if (inputPaths.Count == 0)
            throw new InvalidOperationException("At least one WAV file is required for combination.");

        var wavs = inputPaths.Select(path => (Path: path, Info: ValidateWavFile(path), Bytes: File.ReadAllBytes(path))).ToList();
        var first = wavs[0].Info;
        if (wavs.Any(wav => wav.Info.Channels != first.Channels || wav.Info.SampleRate != first.SampleRate || wav.Info.ByteRate != first.ByteRate))
            throw new InvalidOperationException("Cannot combine WAV files with different PCM formats.");

        var dataSize = wavs.Sum(wav => wav.Info.DataSize);
        using var output = new BinaryWriter(File.Create(outputPath));
        output.Write("RIFF"u8.ToArray());
        output.Write((uint)(36 + dataSize));
        output.Write("WAVE"u8.ToArray());
        output.Write("fmt "u8.ToArray());
        output.Write((uint)16);
        output.Write((ushort)1);
        output.Write(first.Channels);
        output.Write(first.SampleRate);
        output.Write(first.ByteRate);
        output.Write((ushort)(first.ByteRate / first.SampleRate));
        output.Write((ushort)16);
        output.Write("data"u8.ToArray());
        output.Write((uint)dataSize);
        foreach (var wav in wavs)
            output.Write(wav.Bytes, wav.Info.DataOffset, wav.Info.DataSize);
    }

    private static int FindChunk(byte[] bytes, string chunkId)
    {
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var size = (int)BitConverter.ToUInt32(bytes, offset + 4);
            if (IsAscii(bytes, offset, chunkId))
                return offset;
            offset += 8 + size + (size % 2);
        }

        return -1;
    }

    private static bool IsAscii(byte[] bytes, int offset, string value)
    {
        if (offset + value.Length > bytes.Length)
            return false;
        for (var i = 0; i < value.Length; i++)
        {
            if (bytes[offset + i] != value[i])
                return false;
        }

        return true;
    }

    private sealed record SegmentPlan(TtsPackageSegment Segment, string AudioPath);
    private sealed record BulkPackageCandidate(string Path, FinalTtsPackageDocument Package);
    private sealed record WavInfo(long FileSizeBytes, double DurationSeconds, int DataOffset, int DataSize, ushort Channels, uint SampleRate, uint ByteRate);
}
