using System.Text.Json;
using System.Xml.Linq;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class TtsAlignmentRepairServiceTests : IDisposable
{
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "phase9b2-tts-alignment-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RepairTtsAlignment_InvalidPackage_RebuildsValidAlignedSsmlWithoutWritingInDryRun()
    {
        var planId = Guid.NewGuid();
        var inputPath = WritePackage(planId,
            ssml: "<speak><voice><prosody>The Moon rises tonight.",
            text: "The Moon rises tonight. Watch Mars nearby.",
            emphasisWords: ["Mars"]);
        var service = CreateService();

        var result = await service.RepairTtsAlignmentAsync(new TtsAlignmentRepairRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(1, result.NormalizedValidCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(1, result.ReadyForAudioCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(inputPath)!, "tts-package-final.json")));

        var finalPackage = Assert.Single(result.FinalPackages);
        Assert.True(finalPackage.ReadyForTts);
        Assert.True(finalPackage.ReadyForAudioGeneration);
        Assert.Equal("Valid", finalPackage.SsmlValidationStatus);
        Assert.Equal("NormalizedValid", finalPackage.AlignmentRepairStatus);
        var segment = Assert.Single(finalPackage.Segments);
        XDocument.Parse(segment.Ssml);
        Assert.Contains("Watch", ExtractSpokenText(segment.Ssml), StringComparison.Ordinal);
        Assert.Equal(NormalizeForAlignment(segment.Text), NormalizeForAlignment(ExtractSpokenText(segment.Ssml)));
        Assert.Contains("<emphasis", segment.Ssml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<break", segment.Ssml, StringComparison.OrdinalIgnoreCase);
        Assert.True(Assert.Single(finalPackage.SegmentValidationResults).IsValid);
    }

    [Fact]
    public async Task RepairTtsAlignment_DryRunFalse_WritesFinalJsonOnlyAndDoesNotGenerateAudio()
    {
        var planId = Guid.NewGuid();
        WritePackage(planId,
            ssml: BuildSsml("One sky, several <emphasis level=\"moderate\">night</emphasis>s."),
            text: "One sky, several nights.");
        var service = CreateService();

        var result = await service.RepairTtsAlignmentAsync(new TtsAlignmentRepairRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: false), CancellationToken.None);

        var generatedFile = Assert.Single(result.GeneratedFiles);
        Assert.EndsWith("tts-package-final.json", generatedFile, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(generatedFile));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedFile));
        Assert.True(document.RootElement.GetProperty("readyForTts").GetBoolean());
        Assert.True(document.RootElement.GetProperty("readyForAudioGeneration").GetBoolean());
        Assert.Equal("Valid", document.RootElement.GetProperty("ssmlValidationStatus").GetString());
        Assert.Equal("NormalizedValid", document.RootElement.GetProperty("alignmentRepairStatus").GetString());
        XDocument.Parse(document.RootElement.GetProperty("segments")[0].GetProperty("ssml").GetString()!);

        var files = Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories);
        Assert.All(files, path => Assert.EndsWith(".json", path, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("One sky, several <emphasis level=\"moderate\">night</emphasis>s", "One sky, several nights")]
    [InlineData("our <emphasis level=\"moderate\">view</emphasis>point", "our viewpoint")]
    public async Task RepairTtsAlignment_InlineEmphasisSplittingWords_NormalizesAsValidWithoutRewriting(string ssmlBody, string text)
    {
        var planId = Guid.NewGuid();
        var ssml = BuildSsml(ssmlBody);
        WritePackage(planId, ssml: ssml, text: text);
        var service = CreateService();

        var result = await service.RepairTtsAlignmentAsync(new TtsAlignmentRepairRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.NormalizedValidCount);
        Assert.Equal(0, result.FailedCount);
        var finalPackage = Assert.Single(result.FinalPackages);
        Assert.Equal("NormalizedValid", finalPackage.AlignmentRepairStatus);
        Assert.True(finalPackage.ReadyForTts);
        Assert.True(finalPackage.ReadyForAudioGeneration);
        Assert.Equal(ssml, Assert.Single(finalPackage.Segments).Ssml);
        var validation = Assert.Single(finalPackage.SegmentValidationResults);
        Assert.True(validation.IsValid);
        Assert.Contains(validation.FixesApplied, fix => fix.Contains("inline-tag-aware normalization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RepairTtsAlignment_BreakTagsAndPunctuationDifferences_DoNotCreateFalseMismatch()
    {
        var planId = Guid.NewGuid();
        WritePackage(planId,
            ssml: BuildSsml("Tonight’s Moon<break time=\"300ms\"/>watch Mars nearby!"),
            text: "tonight's moon watch mars nearby");
        var service = CreateService();

        var result = await service.RepairTtsAlignmentAsync(new TtsAlignmentRepairRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        var finalPackage = Assert.Single(result.FinalPackages);
        Assert.Equal("AlreadyValid", finalPackage.AlignmentRepairStatus);
        Assert.True(finalPackage.ReadyForAudioGeneration);
        Assert.True(Assert.Single(finalPackage.SegmentValidationResults).IsValid);
    }

    [Fact]
    public async Task RepairTtsAlignment_RebuiltSsmlStillMissingWords_IncludesNormalizedMismatchDetails()
    {
        var planId = Guid.NewGuid();
        WritePackage(planId,
            ssml: BuildSsml("The Moon rises tonight."),
            text: "The Moon rises tonight. Watch Mars nearby.");
        var service = CreateService();

        var result = await service.RepairTtsAlignmentAsync(new TtsAlignmentRepairRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        var finalPackage = Assert.Single(result.FinalPackages);
        Assert.Equal("Failed", finalPackage.AlignmentRepairStatus);
        var validation = Assert.Single(finalPackage.SegmentValidationResults);
        Assert.NotNull(validation.AlignmentMismatch);
        Assert.Contains(validation.Issues, issue => issue.StartsWith("sourceNormalized=", StringComparison.Ordinal));
        Assert.Contains(validation.Issues, issue => issue.StartsWith("spokenNormalized=", StringComparison.Ordinal));
        Assert.Contains(validation.Issues, issue => issue.StartsWith("missingWords=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepairTtsAlignment_UsesCleanPackageBeforeRawPackage()
    {
        var planId = Guid.NewGuid();
        var rawPath = WritePackage(planId,
            ssml: BuildSsml("Raw package text."),
            text: "Raw package text.");
        var ttsRoot = Path.GetDirectoryName(rawPath)!;
        var cleanPackage = CreatePackage(planId,
            ssml: BuildSsml("Clean package text. Clean package wins."),
            text: "Clean package text. Clean package wins.",
            emphasisWords: []);
        await File.WriteAllTextAsync(Path.Combine(ttsRoot, "tts-package-clean.json"), JsonSerializer.Serialize(new CleanTtsPackageDocument(
            cleanPackage.ContentGenerationPlanId,
            cleanPackage.RegionId,
            cleanPackage.Language,
            cleanPackage.ContentCategory,
            cleanPackage.PlannedFormat,
            cleanPackage.Title,
            cleanPackage.TtsProvider,
            cleanPackage.VoiceProfile,
            cleanPackage.MusicProfile,
            cleanPackage.Segments,
            cleanPackage.TotalEstimatedDurationSeconds,
            false,
            cleanPackage.GenerationSource,
            cleanPackage.GeneratedUtc,
            "Invalid",
            DateTimeOffset.UtcNow,
            false,
            []), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        var service = CreateService();

        var result = await service.RepairTtsAlignmentAsync(new TtsAlignmentRepairRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        var segment = Assert.Single(Assert.Single(result.FinalPackages).Segments);
        Assert.Equal("Clean package text. Clean package wins.", segment.Text);
        Assert.Equal(NormalizeForAlignment(segment.Text), NormalizeForAlignment(ExtractSpokenText(segment.Ssml)));
    }


    [Fact]
    public async Task RepairTtsAlignment_ExistingAlignedPackage_RemainsReadyAndAlreadyValid()
    {
        var planId = Guid.NewGuid();
        WritePackage(planId,
            ssml: BuildSsml("The Moon rises tonight. Watch Mars nearby."),
            text: "The Moon rises tonight. Watch Mars nearby.");
        var service = CreateService();

        var result = await service.RepairTtsAlignmentAsync(new TtsAlignmentRepairRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.AlreadyValidCount);
        Assert.Equal(0, result.FailedCount);
        var finalPackage = Assert.Single(result.FinalPackages);
        Assert.Equal("AlreadyValid", finalPackage.AlignmentRepairStatus);
        var segment = Assert.Single(finalPackage.Segments);
        XDocument.Parse(segment.Ssml);
        Assert.Equal(NormalizeForAlignment(segment.Text), NormalizeForAlignment(ExtractSpokenText(segment.Ssml)));
        Assert.True(finalPackage.ReadyForAudioGeneration);
    }

    [Fact]
    public async Task RepairTtsAlignment_InvalidOutputPath_RemainsFailed()
    {
        var planId = Guid.NewGuid();
        WritePackage(planId,
            ssml: BuildSsml("The Moon rises tonight."),
            text: "The Moon rises tonight.",
            outputAudioPath: Path.Combine(outputRoot, "scene-01.mp3"));
        var service = CreateService();

        var result = await service.RepairTtsAlignmentAsync(new TtsAlignmentRepairRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [planId],
            DryRun: true), CancellationToken.None);

        var finalPackage = Assert.Single(result.FinalPackages);
        Assert.False(finalPackage.ReadyForTts);
        Assert.False(finalPackage.ReadyForAudioGeneration);
        Assert.Equal("Failed", finalPackage.AlignmentRepairStatus);
        Assert.Contains(Assert.Single(finalPackage.SegmentValidationResults).Issues, issue => issue.Contains(".wav", StringComparison.OrdinalIgnoreCase));
    }

    private string WritePackage(Guid planId, string ssml, string text, IReadOnlyList<string>? emphasisWords = null, string? outputAudioPath = null)
    {
        var ttsRoot = Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "tts");
        Directory.CreateDirectory(ttsRoot);
        var package = CreatePackage(planId, ssml, text, emphasisWords ?? [], outputAudioPath ?? Path.Combine(ttsRoot, "audio", "scene-01.wav"));
        var path = Path.Combine(ttsRoot, "tts-package.json");
        File.WriteAllText(path, JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return path;
    }

    private TtsPackageDocument CreatePackage(Guid planId, string ssml, string text, IReadOnlyList<string> emphasisWords, string? outputAudioPath = null)
    {
        var ttsRoot = Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "tts");
        return new TtsPackageDocument(
            planId.ToString("D"),
            "IN-RJ-UDAIPUR",
            "en",
            "PlanetConjunction",
            "Short",
            "Tonight's line-of-sight conjunction",
            "AzureSpeech",
            new TtsVoiceProfile("calm documentary guide", "en-US-DavisNeural", "documentary", "+0%", "-3%", "medium"),
            new TtsMusicProfile("wonder", "low", "ambient wonder"),
            [new TtsPackageSegment(
                1,
                "Opening",
                text,
                ssml,
                8,
                [],
                emphasisWords,
                new VoicePerformanceMetadata("calm", "low", "warm", [], "low"),
                outputAudioPath ?? Path.Combine(ttsRoot, "audio", "scene-01.wav"))],
            8,
            true,
            "Phase9B",
            DateTimeOffset.UtcNow);
    }

    private static string BuildSsml(string text)
        => $"<speak version=\"1.0\" xml:lang=\"en-US\"><voice name=\"en-US-DavisNeural\"><prosody rate=\"-3%\" pitch=\"+0%\" volume=\"medium\">{text}</prosody></voice></speak>";

    private static string ExtractSpokenText(string ssml)
    {
        var document = XDocument.Parse(ssml);
        return string.Join(' ', document.Root!.DescendantNodes().OfType<XText>().Select(t => t.Value));
    }

    private static string NormalizeForAlignment(string text)
    {
        var document = XDocument.Parse($"<root>{text}</root>");
        var extracted = ExtractInlineAwareText(document.Root!);
        var normalized = extracted.Replace('’', '\'').ToLowerInvariant();
        var withoutPunctuation = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\p{P}\p{S}]+", " ");
        return string.Join(' ', withoutPunctuation.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string ExtractInlineAwareText(XElement element)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
                builder.Append(text.Value);
            else if (node is XElement child && string.Equals(child.Name.LocalName, "break", StringComparison.OrdinalIgnoreCase))
                builder.Append(' ');
            else if (node is XElement childElement)
                builder.Append(ExtractInlineAwareText(childElement));
        }

        return builder.ToString();
    }

    private TtsAlignmentRepairService CreateService()
        => new(Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }), NullLogger<TtsAlignmentRepairService>.Instance);

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
