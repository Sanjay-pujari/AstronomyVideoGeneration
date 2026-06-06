using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class StellariumCaptureExecutionServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "stellarium-capture-execution-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteCaptureAsync_DryRunMaxJobsOne_ReturnsOnePreviewAndLaunchesNothing()
    {
        await using var db = CreateDb();
        var job1 = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 1, priority: 1);
        await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 2, priority: 2);
        var executable = Path.Combine(_workingDirectory, "fake-stellarium.sh");
        await File.WriteAllTextAsync(executable, "#!/usr/bin/env bash\necho launched > \"$1\"\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var service = CreateService(db, executable);

        var result = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [], 1, DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        var previewPath = Assert.Single(result.CapturedFiles);
        Assert.EndsWith(Path.Combine("assets", "IN-RJ-UDAIPUR", "events", job1.AstronomyEventIntelligenceId!.Value.ToString("D"), "stellarium-captures", $"capture-scene-{job1.SceneNumber}-{job1.Id:D}.png"), previewPath);
        Assert.False(File.Exists(previewPath));
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, job1.Status);
        Assert.EndsWith(".ssc", job1.OutputPath);
        Assert.Empty(Directory.GetFiles(_workingDirectory, "execute-capture-*.ssc", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExecuteCaptureAsync_MaxJobsOneLimitsRealExecution()
    {
        await using var db = CreateDb();
        var job1 = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 1, priority: 1);
        var job2 = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 2, priority: 2);
        var executable = await CreateFakeStellariumAsync(_workingDirectory);
        var service = CreateService(db, executable);

        var result = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [], 1, DryRun: false), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        var capturePath = Assert.Single(result.CapturedFiles);
        Assert.True(File.Exists(capturePath));
        Assert.True(new FileInfo(capturePath).Length > 100 * 1024);
        Assert.Equal("Passed", result.ValidationStatus);
        Assert.True(result.FileSizeBytes > 100 * 1024);
        Assert.True(result.ImageWidth >= 1280);
        Assert.True(result.ImageHeight >= 720);
        Assert.Equal(0, result.RetryCount);
        var saved1 = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job1.Id);
        var saved2 = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job2.Id);
        Assert.Equal(capturePath, saved1.OutputPath);
        Assert.EndsWith(".ssc", saved2.OutputPath);
    }

    [Fact]
    public async Task ExecuteCaptureAsync_MetadataKeepsSscPathAndPngPath()
    {
        await using var db = CreateDb();
        var job = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 3, priority: 1);
        var sscPath = job.OutputPath!;
        var executable = await CreateFakeStellariumAsync(_workingDirectory);
        var service = CreateService(db, executable);

        var result = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [job.Id], 1, DryRun: false), CancellationToken.None);

        var capturePath = Assert.Single(result.CapturedFiles);
        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.Equal(capturePath, saved.OutputPath);
        using var document = JsonDocument.Parse(saved.MetadataJson!);
        Assert.Equal(sscPath, document.RootElement.GetProperty("SscPath").GetString());
        Assert.Equal(capturePath, document.RootElement.GetProperty("CapturePath").GetString());
        Assert.Equal(sscPath, document.RootElement.GetProperty("sscFile").GetString());
        Assert.True(document.RootElement.GetProperty("captureExecuted").GetBoolean());
        Assert.Equal("Phase8D.3", document.RootElement.GetProperty("captureSource").GetString());
        Assert.Equal("Completed", document.RootElement.GetProperty("sscGenerationStatus").GetString());
        Assert.Equal("Completed", document.RootElement.GetProperty("captureStatus").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("captureAttemptCount").GetInt32());
        Assert.Equal("Passed", document.RootElement.GetProperty("validationResult").GetString());
        Assert.True(document.RootElement.GetProperty("fileSizeBytes").GetInt64() > 100 * 1024);
        Assert.True(document.RootElement.GetProperty("imageWidth").GetInt32() >= 1280);
        Assert.True(document.RootElement.GetProperty("imageHeight").GetInt32() >= 720);
    }

    [Fact]
    public async Task ExecuteCaptureAsync_CapturePathGeneratedAndExistingPngSkippedUnlessOverwrite()
    {
        await using var db = CreateDb();
        var job = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 4, priority: 1);
        var service = CreateService(db, await CreateFakeStellariumAsync(_workingDirectory));
        var dryRun = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [job.Id], 1, DryRun: true), CancellationToken.None);
        var expectedCapture = Assert.Single(dryRun.CapturedFiles);
        Directory.CreateDirectory(Path.GetDirectoryName(expectedCapture)!);
        await WriteValidPngAsync(expectedCapture);

        var result = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [job.Id], 1, DryRun: false, OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(0, result.JobCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(result.CapturedFiles);
        Assert.Contains(result.Warnings, warning => warning.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteCaptureAsync_RetriesOnceWhenFirstPngValidationFails()
    {
        await using var db = CreateDb();
        var job = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 5, priority: 1);
        var executable = await CreateRetryFakeStellariumAsync(_workingDirectory);
        var service = CreateService(db, executable);

        var result = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [job.Id], 1, DryRun: false, OverwriteExisting: true), CancellationToken.None);

        Assert.Equal(1, result.CompletedCount);
        Assert.Equal("Passed", result.ValidationStatus);
        Assert.Equal(1, result.RetryCount);
        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        using var document = JsonDocument.Parse(saved.MetadataJson!);
        Assert.Equal(2, document.RootElement.GetProperty("captureAttemptCount").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("retryCount").GetInt32());
        Assert.Equal("Passed", document.RootElement.GetProperty("validationResult").GetString());
    }


    [Fact]
    public async Task ExecuteCaptureAsync_UsesJobIdFilterAndDoesNotMatchContentPlanId()
    {
        await using var db = CreateDb();
        var job = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 6, priority: 1);
        var service = CreateService(db, await CreateFakeStellariumAsync(_workingDirectory));

        var result = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [job.ContentGenerationPlanId], 1, DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.JobCount);
        Assert.Empty(result.CapturedFiles);
    }

    [Fact]
    public async Task ExecuteCaptureAsync_MissingExecutableKeepsCompletedSscJobAndRecordsFailedCaptureMetadata()
    {
        await using var db = CreateDb();
        var job = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 7, priority: 1);
        var sscPath = job.OutputPath!;
        var service = CreateService(db, Path.Combine(_workingDirectory, "missing-stellarium"));

        var result = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [job.Id], 1, DryRun: false, OverwriteExisting: true), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("failed non-blockingly", StringComparison.OrdinalIgnoreCase));
        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.Equal(sscPath, saved.OutputPath);
        Assert.Null(saved.FailureReason);
        using var document = JsonDocument.Parse(saved.MetadataJson!);
        Assert.Equal(sscPath, document.RootElement.GetProperty("sscPath").GetString());
        Assert.Equal("Completed", document.RootElement.GetProperty("sscGenerationStatus").GetString());
        Assert.Equal("Failed", document.RootElement.GetProperty("captureStatus").GetString());
        Assert.True(document.RootElement.TryGetProperty("failureReason", out var failureReason));
        Assert.Contains("Stellarium executable", failureReason.GetString(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task ExecuteCaptureAsync_SelectsUsableSscWithoutRequiringPendingStatus()
    {
        await using var db = CreateDb();
        var job = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, sceneNumber: 8, priority: 1);
        job.Status = AstronomyAssetProductionJobStatuses.Completed;
        await db.SaveChangesAsync();
        var service = CreateService(db, await CreateFakeStellariumAsync(_workingDirectory));

        var result = await service.ExecuteCaptureAsync(new StellariumAssetCaptureExecutionRequest("IN-RJ-UDAIPUR", [job.Id], 1, DryRun: false), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.EndsWith(".png", saved.OutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }

    private StellariumCaptureExecutionService CreateService(MediaFactoryDbContext db, string executablePath)
        => new(
            db,
            Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }),
            Options.Create(new StellariumOptions { ExecutablePath = executablePath, CaptureTimeoutSeconds = 5 }),
            NullLogger<StellariumCaptureExecutionService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<string> CreateFakeStellariumAsync(string workingDirectory)
    {
        var fixturePng = Path.Combine(workingDirectory, "valid-stellarium-capture.png");
        await WriteValidPngAsync(fixturePng);
        var executable = Path.Combine(workingDirectory, "fake-stellarium.sh");
        await File.WriteAllTextAsync(executable, $"""
#!/usr/bin/env bash
script=""
while [[ $# -gt 0 ]]; do
  if [[ "$1" == "--startup-script" ]]; then
    script="$2"
    shift 2
  else
    shift
  fi
done
line=$(grep 'core.screenshot' "$script")
prefix=$(echo "$line" | sed -E 's/.*core\.screenshot\("([^"]+)".*/\1/')
dir=$(echo "$line" | sed -E 's/.*core\.screenshot\("[^"]+", false, "([^"]+)".*/\1/')
mkdir -p "$dir"
cp "{fixturePng}" "$dir/$prefix.png"
exit 0
""");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return executable;
    }

    private static async Task<string> CreateRetryFakeStellariumAsync(string workingDirectory)
    {
        var fixturePng = Path.Combine(workingDirectory, "valid-stellarium-capture-retry.png");
        await WriteValidPngAsync(fixturePng);
        var attemptFile = Path.Combine(workingDirectory, "capture-attempt-count.txt");
        var executable = Path.Combine(workingDirectory, "fake-stellarium-retry.sh");
        await File.WriteAllTextAsync(executable, $"""
#!/usr/bin/env bash
script=""
while [[ $# -gt 0 ]]; do
  if [[ "$1" == "--startup-script" ]]; then
    script="$2"
    shift 2
  else
    shift
  fi
done
line=$(grep 'core.screenshot' "$script")
prefix=$(echo "$line" | sed -E 's/.*core\.screenshot\("([^"]+)".*/\1/')
dir=$(echo "$line" | sed -E 's/.*core\.screenshot\("[^"]+", false, "([^"]+)".*/\1/')
mkdir -p "$dir"
count=0
if [[ -f "{attemptFile}" ]]; then count=$(cat "{attemptFile}"); fi
count=$((count + 1))
echo "$count" > "{attemptFile}"
if [[ "$count" == "1" ]]; then
  printf '\x89PNG\r\n\x1a\ncorrupt' > "$dir/$prefix.png"
else
  cp "{fixturePng}" "$dir/$prefix.png"
fi
exit 0
""");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return executable;
    }

    private static async Task WriteValidPngAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(1280, 720);
        var random = new Random(12345);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            }
        });
        await image.SaveAsPngAsync(path);
    }

    private static async Task<AstronomyAssetProductionJob> SeedCompletedStellariumScreenshotJobAsync(MediaFactoryDbContext db, string workingDirectory, int sceneNumber, int priority)
    {
        var eventIntelligenceId = Guid.NewGuid();
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "Phase8D",
            Title = "Phase 8D capture execution plan",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.Parse("2026-06-07T11:00:00Z"),
            Status = "Planned",
            PlanStatus = "Planned",
            PlannedFormat = "Short",
            PrimaryAstronomyEventTypeCode = "PlanetConjunction",
            AstronomyEventIntelligenceId = eventIntelligenceId,
            PriorityScore = 9m
        };

        var job = new AstronomyAssetProductionJob
        {
            ContentGenerationPlan = plan,
            ContentGenerationPlanId = plan.Id,
            AstronomyEventIntelligenceId = eventIntelligenceId,
            SceneNumber = sceneNumber,
            SceneName = "Venus near Jupiter Stellarium frame",
            AssetType = "StellariumScreenshot",
            AssetPurpose = "Capture reusable Stellarium SSC script",
            PlannedProvider = "StellariumScreenshotProducer",
            ObjectNamesJson = JsonSerializer.Serialize(new[] { "Venus", "Jupiter" }),
            PromptOrInstruction = "Capture reusable SSC only.",
            ExpectedOutputType = "PngImage",
            Priority = priority,
            AssetPriority = AstronomyAssetClassificationRules.Preferred,
            AssetExecutionGroup = AstronomyAssetClassificationRules.ResolveExecutionGroup("StellariumScreenshot"),
            Status = AstronomyAssetProductionJobStatuses.Completed,
            MetadataJson = JsonSerializer.Serialize(new
            {
                regionId = "IN-RJ-UDAIPUR",
                objectNames = new[] { "Venus", "Jupiter" },
                scheduledUtc = "2026-06-07T11:00:00Z",
                peakUtc = "2026-06-07T11:30:00Z",
                orientation = "Western horizon landscape after sunset",
                requiresConstellationLines = true,
                requiresLabels = true,
                requiresLandscape = true
            })
        };

        var sscDirectory = Path.Combine(workingDirectory, "assets", "IN-RJ-UDAIPUR", "events", eventIntelligenceId.ToString("D"), "stellarium-scripts");
        Directory.CreateDirectory(sscDirectory);
        var sscPath = Path.Combine(sscDirectory, $"scene-{job.SceneNumber}-stellarium-{job.Id:D}.ssc");
        await File.WriteAllTextAsync(sscPath, "// reusable SSC preview\ncore.clear(\"natural\");\ncore.wait(1.0);\n");
        job.OutputPath = sscPath;
        db.ContentGenerationPlans.Add(plan);
        db.AstronomyAssetProductionJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }
}
