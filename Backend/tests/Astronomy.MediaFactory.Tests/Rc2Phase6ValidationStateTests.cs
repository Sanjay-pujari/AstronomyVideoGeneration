using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2Phase6ValidationStateTests
{
    [Fact]
    public void CompleteLongAndShortRun_SucceedsAndIsCertificationCandidate()
    {
        using var doc = BuildPayload(CreateCompleteRun());
        Assert.Equal("Succeeded", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("auroraCertificationCandidate").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("longStoryFramesRequested").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("shortStoryFramesRequested").GetBoolean());
        Assert.Empty(doc.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("Validation passed.", doc.RootElement.GetProperty("reason").GetString());
        Assert.True(doc.RootElement.GetProperty("longQualityScore").GetInt32() > 0);
        Assert.True(doc.RootElement.GetProperty("shortQualityScore").GetInt32() > 0);
    }

    [Fact]
    public void MissingLongDocumentaryContract_FailsWithBlockingError()
    {
        var root = CreateCompleteRun();
        File.Delete(Path.Combine(root, "creative", "documentary-contract.long.json"));
        using var doc = BuildPayload(root);
        Assert.Equal("Failed", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains(doc.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("documentary-contract.long.json"));
        Assert.False(doc.RootElement.GetProperty("auroraCertificationCandidate").GetBoolean());
    }

    [Fact]
    public void SharedMutableBeatCollectionDetected_Fails()
    {
        var root = CreateCompleteRun(sharedMutableBeatCollectionUsed: true);
        using var doc = BuildPayload(root);
        Assert.Equal("Failed", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains(doc.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("shared a mutable beat collection"));
    }

    [Fact]
    public void NarrationLeakageDetected_Fails()
    {
        var root = CreateCompleteRun(narrationLeakage: true);
        using var doc = BuildPayload(root);
        Assert.Equal("Failed", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains(doc.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("Narration leaked"));
    }

    [Fact]
    public void OptionalFactMissingOnly_SucceedsWithWarningAndCertificationAllowed()
    {
        using var doc = BuildPayload(CreateCompleteRun(), warnings: ["Optional fact 'moonInterference' was unavailable."]);
        Assert.Equal("Succeeded", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("auroraCertificationCandidate").GetBoolean());
        Assert.Contains(doc.RootElement.GetProperty("warnings").EnumerateArray(), e => e.GetString()!.Contains("Optional fact"));
    }

    [Fact]
    public void RuntimeException_FailsRecordsExceptionAndRetryClassification()
    {
        using var doc = BuildPayload(CreateCompleteRun(), status: ProductionPhaseStatus.Failed, errors: ["Azure timeout"], exception: new TimeoutException("Azure timeout"), canRetry: true, reason: "Azure timeout");
        Assert.Equal("Failed", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("TimeoutException", doc.RootElement.GetProperty("exceptionType").GetString());
        Assert.True(doc.RootElement.GetProperty("canRetry").GetBoolean());
    }

    private static string CreateCompleteRun(bool sharedMutableBeatCollectionUsed = false, bool narrationLeakage = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "rc2-phase6-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "creative"));
        Directory.CreateDirectory(Path.Combine(root, "story-frames", "long"));
        Directory.CreateDirectory(Path.Combine(root, "story-frames", "short"));
        File.WriteAllText(Path.Combine(root, "creative", "documentary-contract.long.json"), "{}");
        File.WriteAllText(Path.Combine(root, "creative", "documentary-contract.short.json"), "{}");
        File.WriteAllText(Path.Combine(root, "creative", "documentary-architecture-diagnostics.json"), $$"""{ "sharedMutableBeatCollectionUsed": {{sharedMutableBeatCollectionUsed.ToString().ToLowerInvariant()}}, "fixedSceneCountUsed": false, "oneSemanticBeatToOneFrameForced": false, "legacyFallbackUsed": false }""");
        WriteManifest(root, "long", "landscape", "16:9", 1920, 1080, 9);
        WriteManifest(root, "short", "portrait", "9:16", 2160, 3840, 5);
        WriteDiagnostics(root, "long", narrationLeakage);
        WriteDiagnostics(root, "short", false);
        return root;
    }

    private static void WriteManifest(string root, string format, string orientation, string aspectRatio, int width, int height, int count)
        => File.WriteAllText(Path.Combine(root, "story-frames", format, "story-frame-manifest.json"), $$"""{ "requested": true, "format": "{{format}}", "orientation": "{{orientation}}", "aspectRatio": "{{aspectRatio}}", "targetWidth": {{width}}, "targetHeight": {{height}}, "generatedSceneCount": {{count}} }""");

    private static void WriteDiagnostics(string root, string format, bool narrationLeakage)
        => File.WriteAllText(Path.Combine(root, "story-frames", format, "story-frame-diagnostics.json"), $$"""{ "overallStoryFrameQualityScore": 100, "generatedFromDocumentaryContract": true, "narrationLeakageWarnings": [{{(narrationLeakage ? "\"Narration leaked into visual planning.\"" : "")}}], "errors": [] }""");

    private static JsonDocument BuildPayload(string root, IReadOnlyList<string>? warnings = null, IReadOnlyList<string>? errors = null, ProductionPhaseStatus status = ProductionPhaseStatus.Succeeded, Exception? exception = null, bool canRetry = false, string reason = "Validation passed.")
    {
        var now = DateTimeOffset.UtcNow;
        var result = new ProductionPhaseResult(6, "Creative Intelligence / Story Frames", status, now, now, 0, [], [], null, warnings ?? [], errors ?? [], canRetry, reason);
        var method = typeof(Rc2ContentPlanningBatchOrchestrator).GetMethod("BuildPhase6ValidationPayload", BindingFlags.NonPublic | BindingFlags.Static)!;
        var payload = method.Invoke(null, [root, 6, "Creative Intelligence / Story Frames", status, now, now, result, warnings ?? [], errors ?? [], exception, canRetry, reason])!;
        return JsonDocument.Parse(JsonSerializer.Serialize(payload));
    }
}
