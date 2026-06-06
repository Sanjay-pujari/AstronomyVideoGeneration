using System.Text.Json;
using System.Xml.Linq;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class TtsPackageValidationServiceTests : IDisposable
{
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "phase9b1-tts-validation-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ValidateTtsPackages_DryRun_FixesMergedSentencesAndParsesXmlWithoutWriting()
    {
        var planId = Guid.NewGuid();
        var inputPath = WritePackage(planId, ssml: BuildSsml("not a physical one.The closeness belongs to line of sight."));
        var service = CreateService();

        var result = await service.ValidateTtsPackagesAsync(new TtsPackageValidationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        var cleanPackage = Assert.Single(result.CleanPackages);
        var segment = Assert.Single(cleanPackage.Segments);
        Assert.Contains("one.<break time=\"300ms\" />The closeness", segment.Ssml, StringComparison.Ordinal);
        XDocument.Parse(segment.Ssml);
        Assert.True(cleanPackage.ReadyForTts);
        Assert.Equal("Valid", cleanPackage.SsmlValidationStatus);
        Assert.Equal(1, result.ValidCount);
        Assert.Equal(1, result.FixedCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(inputPath)!, "tts-package-clean.json")));
    }

    [Fact]
    public async Task ValidateTtsPackages_DryRunFalse_WritesCleanJsonOnlyAndDoesNotGenerateAudio()
    {
        var planId = Guid.NewGuid();
        WritePackage(planId, ssml: BuildSsml("background sky.That perspective turns distance into a viewing angle."), text: "background sky. That perspective turns distance into a viewing angle.");
        var service = CreateService();

        var result = await service.ValidateTtsPackagesAsync(new TtsPackageValidationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: false), CancellationToken.None);

        var generatedFile = Assert.Single(result.GeneratedFiles);
        Assert.EndsWith("tts-package-clean.json", generatedFile, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(generatedFile));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedFile));
        Assert.True(document.RootElement.GetProperty("readyForTts").GetBoolean());
        Assert.Equal("Valid", document.RootElement.GetProperty("ssmlValidationStatus").GetString());
        Assert.True(document.RootElement.GetProperty("segmentValidationResults")[0].GetProperty("isValid").GetBoolean());

        var files = Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories);
        Assert.All(files, path => Assert.EndsWith(".json", path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateTtsPackages_CriticalSsmlIssue_RemainsNotReadyForTts()
    {
        var planId = Guid.NewGuid();
        WritePackage(planId, ssml: "<speak><voice name=\"en-US-GuyNeural\"><prosody rate=\"-3%\" pitch=\"+0%\" volume=\"medium\">The Moon rises tonight.");
        var service = CreateService();

        var result = await service.ValidateTtsPackagesAsync(new TtsPackageValidationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        var cleanPackage = Assert.Single(result.CleanPackages);
        Assert.False(cleanPackage.ReadyForTts);
        Assert.Equal("Invalid", cleanPackage.SsmlValidationStatus);
        Assert.Equal(1, result.InvalidCount);
        var validation = Assert.Single(cleanPackage.SegmentValidationResults);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Contains("SSML XML parse failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateTtsPackages_InvalidOutputAudioPath_RemainsNotReadyForTts()
    {
        var planId = Guid.NewGuid();
        WritePackage(planId, ssml: BuildSsml("The Moon rises tonight."), text: "The Moon rises tonight.", outputAudioPath: Path.Combine(outputRoot, "scene-one.mp3"));
        var service = CreateService();

        var result = await service.ValidateTtsPackagesAsync(new TtsPackageValidationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        var cleanPackage = Assert.Single(result.CleanPackages);
        Assert.False(cleanPackage.ReadyForTts);
        var validation = Assert.Single(cleanPackage.SegmentValidationResults);
        Assert.Contains(validation.Issues, issue => issue.Contains(".wav", StringComparison.OrdinalIgnoreCase));
    }

    private string WritePackage(Guid planId, string ssml, string text = "not a physical one. The closeness belongs to line of sight.", string? outputAudioPath = null)
    {
        var ttsRoot = Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "tts");
        Directory.CreateDirectory(ttsRoot);
        var package = new TtsPackageDocument(
            planId.ToString("D"),
            "IN-RJ-UDAIPUR",
            "en",
            "PlanetConjunction",
            "Short",
            "Tonight's line-of-sight conjunction",
            "AzureSpeech",
            new TtsVoiceProfile("calm documentary guide", "en-US-DavisNeural", "documentary", "neutral", "-3%", "medium"),
            new TtsMusicProfile("wonder", "low", "ambient wonder"),
            [new TtsPackageSegment(
                1,
                "Opening",
                text,
                ssml,
                8,
                [],
                [],
                null,
                outputAudioPath ?? Path.Combine(ttsRoot, "audio", "scene-01.wav"))],
            8,
            true,
            "Phase9B",
            DateTimeOffset.UtcNow);
        var path = Path.Combine(ttsRoot, "tts-package.json");
        File.WriteAllText(path, JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return path;
    }

    private static string BuildSsml(string text)
        => $"<speak version=\"1.0\" xml:lang=\"en-US\"><voice name=\"en-US-DavisNeural\"><prosody rate=\"-3%\" pitch=\"+0%\" volume=\"medium\">{text}</prosody></voice></speak>";

    private TtsPackageValidationService CreateService()
        => new(Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }), NullLogger<TtsPackageValidationService>.Instance);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(outputRoot))
                Directory.Delete(outputRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test files.
        }
    }
}
