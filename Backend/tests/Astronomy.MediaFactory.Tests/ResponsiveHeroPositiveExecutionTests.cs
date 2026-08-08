using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Astronomy.MediaFactory.Tests;

public sealed class ResponsiveHeroPositiveExecutionTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string root = ResolveOutputRoot();

    [Fact]
    public async Task OrionHeroCertificationPlanPublishesResponsiveAuthorityWithoutMutatingUpstream()
    {
        var request = ReadCertificationRequest();
        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"], request.RequestedOutputsOverride);
        Assert.Equal(11, request.StartPhaseNo);
        Assert.Equal(11, request.EndPhaseNo);
        Assert.Equal("ReadOnly", request.DependencyExpansionMode);
        Assert.True(request.OverwriteExisting);
        Assert.True(ProductionPipelineExecutionService.IsPhaseRequiredForRequestedOutputs(request.RequestedOutputsOverride, 11));

        WriteCommittedAuthorities(request);
        var upstreamBefore = SnapshotUpstream();
        Assert.False(Directory.Exists(Path.Combine(root, "11-hero")));

        var result = await new ResponsiveHeroAuthorityService().PublishAsync(new(root, request.PlanId,
            request.EventId, request.Language, request.Title, request.Subtitle,
            "EvergreenConstellationEducation", request.OverwriteExisting), CancellationToken.None);

        Assert.Equal(Phase11ReasonCodes.Accepted, result.ReasonCode);
        Assert.Equal("Responsive Hero assets generated, validated, committed and read back.", result.Reason);
        Assert.False(string.IsNullOrWhiteSpace(result.ManifestChecksum));
        Assert.Equal("Valid", result.ManifestValidationStatus);
        Assert.Equal("Valid", result.ValidationStatus);
        Assert.True(result.PublicationCommitted);
        Assert.True(result.SemanticValidationPassed);
        Assert.True(result.ChecksumValidationPassed);
        Assert.True(result.ManifestValidationPassed);
        Assert.True(result.CommittedStateValidationPassed);
        Assert.True(result.DownstreamReady);
        Assert.NotNull(result.HeroAuthorityDiagnostics);
        Assert.Equal(upstreamBefore, SnapshotUpstream());

        var authorityRoot = Path.Combine(root, "11-hero");
        await AssertImage(Path.Combine(authorityRoot, "hero-landscape.png"), 1920, 1080);
        await AssertImage(Path.Combine(authorityRoot, "hero-square.png"), 1080, 1080);
        await AssertImage(Path.Combine(authorityRoot, "hero-portrait.png"), 1080, 1920);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(authorityRoot, "hero-asset-manifest.json")));
        var document = manifest.RootElement;
        Assert.Equal(request.PlanId, document.GetProperty("planId").GetString());
        Assert.Equal(request.EventId, document.GetProperty("eventId").GetString());
        Assert.Equal(request.Title, document.GetProperty("title").GetString());
        Assert.Equal("ProductionEventIntelligence.Title", document.GetProperty("titleAuthoritySource").GetString());
        Assert.Equal("ProductionEventIntelligence.ShortTitle", document.GetProperty("subtitleAuthoritySource").GetString());
        Assert.Equal("Committed", document.GetProperty("publicationState").GetString());
        Assert.Equal("Valid", document.GetProperty("validationStatus").GetString());
        Assert.True(document.GetProperty("downstreamReady").GetBoolean());
        Assert.Equal(result.ManifestChecksum, document.GetProperty("deterministicChecksum").GetString());

        var variants = document.GetProperty("variants").EnumerateArray().ToArray();
        Assert.Equal(3, variants.Length);
        Assert.All(variants, variant =>
        {
            Assert.Contains(variant.GetProperty("sourceVisualStyle").GetString(), new[] { "Cinematic", "HybridCinematic" });
            Assert.True(variant.GetProperty("scientificGeometryPreserved").GetBoolean());
            Assert.Equal("Valid", variant.GetProperty("validationStatus").GetString());
            Assert.Equal(64, variant.GetProperty("physicalSha256").GetString()!.Length);
        });
        Assert.Equal("long-cinematic", Variant(variants, "Landscape").GetProperty("sourcePhase8AssetId").GetString());
        Assert.Equal("short-cinematic", Variant(variants, "Portrait").GetProperty("sourcePhase8AssetId").GetString());

        using var diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(authorityRoot, "phase11-authority-diagnostics.json")));
        var diag = diagnostics.RootElement;
        Assert.Equal(0, diag.GetProperty("infographicSourceCount").GetInt32());
        Assert.Equal(0, diag.GetProperty("azureImageCallsThisPhase").GetInt32());
        Assert.Equal(0, diag.GetProperty("proceduralAstronomyGenerationCallsThisPhase").GetInt32());
        Assert.False(diag.GetProperty("legacyQuestionEngineAuthorityUsed").GetBoolean());
        Assert.True(diag.GetProperty("candidateValidationPassed").GetBoolean());
        Assert.True(diag.GetProperty("candidateReadbackPassed").GetBoolean());
        Assert.True(diag.GetProperty("publicationCommitted").GetBoolean());
        Assert.True(diag.GetProperty("committedReadbackPassed").GetBoolean());
        Assert.True(diag.GetProperty("downstreamReady").GetBoolean());
        Assert.False(diag.GetProperty("upstreamArtifactsModified").GetBoolean());
        Assert.False(Directory.Exists(authorityRoot + ".staging"));
    }

    private void WriteCommittedAuthorities(CertificationRequest request)
    {
        var p8Root = Path.Combine(root, "08-scene-assets");
        var p10Root = Path.Combine(root, "10-scene-validation");
        Directory.CreateDirectory(p8Root);
        Directory.CreateDirectory(p10Root);
        WriteRaster(Path.Combine(p8Root, "long.png"), 1920, 1080, new Rgba32(15, 31, 62));
        WriteRaster(Path.Combine(p8Root, "short.png"), 1080, 1920, new Rgba32(30, 20, 55));
        var assets = new[]
        {
            Asset("long-cinematic", "Long", "long-scene-01", "08-scene-assets/long.png", 1920, 1080, "Cinematic", false),
            Asset("short-cinematic", "Short", "short-scene-01", "08-scene-assets/short.png", 1080, 1920, "HybridCinematic", true)
        };
        const string p8Checksum = "phase8-orion-authority-checksum";
        var manifest = new SceneAssetManifest("1.0", request.PlanId, "phase11-positive-execution", request.EventId,
            request.Language, DateTimeOffset.UtcNow, "Committed", "blueprint", "frames", "long-narration",
            "short-narration", ["Short", "Long"], assets, "Valid", p8Checksum);
        File.WriteAllText(Path.Combine(p8Root, "scene-asset-manifest.json"), JsonSerializer.Serialize(manifest, Json));

        var shortCertification = Certification("short-scene-01");
        var longCertification = Certification("long-scene-01");
        var certification = new SceneAssetCertification("1.0", request.PlanId, "phase11-positive-execution",
            request.EventId, request.Language, DateTimeOffset.UtcNow, ["Short", "Long"], "phase6-checksum",
            p8Checksum, "phase9-checksum", shortCertification, longCertification, 2, 2, true, "Valid", "Committed",
            "phase10-orion-certification-checksum", true);
        File.WriteAllText(Path.Combine(p10Root, "scene-asset-certification.json"), JsonSerializer.Serialize(certification, Json));
        File.WriteAllText(Path.Combine(p10Root, "phase10-publication-report.json"), JsonSerializer.Serialize(new
        {
            publicationCommitted = true, candidateReadbackPassed = true, committedReadbackPassed = true
        }, Json));
    }

    private SceneAssetManifestItem Asset(string id, string variant, string sceneId, string path, int width, int height,
        string style, bool scientific) => new(id, variant, sceneId, sceneId, $"frame-{sceneId}", 1, "Primary",
        "Cinematic", style, null, "Succeeded", $"instruction-{sceneId}", ["constellation.orion"], path,
        width, height, $"{width}:{height}", Sha(Path.Combine(root, path)), $"Orion/{variant}", false, null, [], false,
        false, "Valid", [], ScientificGeometryCertified: scientific, RequiresScientificGeometry: scientific);

    private static SceneVariantCertification Certification(string sceneId) => new(true, 1, 1, 1, [sceneId], [], [],
        true, true, true, true, "Valid");

    private Dictionary<string, string> SnapshotUpstream() => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => path.Contains("08-scene-assets") || path.Contains("10-scene-validation"))
        .ToDictionary(path => Path.GetRelativePath(root, path), Sha, StringComparer.Ordinal);

    private static JsonElement Variant(IEnumerable<JsonElement> variants, string name) =>
        variants.Single(item => item.GetProperty("variant").GetString() == name);

    private static async Task AssertImage(string path, int width, int height)
    {
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        using var image = await Image.LoadAsync(path);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(64, Sha(path).Length);
    }

    private static void WriteRaster(string path, int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        image.SaveAsPng(path);
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static CertificationRequest ReadCertificationRequest()
    {
        var path = Path.Combine(RepositoryTestPaths.Root(), "Backend/tests/Astronomy.MediaFactory.Tests/Fixtures/Phase11/orion-hero-positive-production-plan.json");
        return JsonSerializer.Deserialize<CertificationRequest>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException("The Phase 11 positive certification request could not be read.");
    }

    private sealed record CertificationRequest(string PlanId, string EventId, string Language, string Title,
        string Subtitle, IReadOnlyList<string> RequestedOutputsOverride, int StartPhaseNo, int EndPhaseNo,
        string DependencyExpansionMode, bool OverwriteExisting);

    private static string ResolveOutputRoot() =>
        Environment.GetEnvironmentVariable("PHASE11_CERTIFICATION_OUTPUT_ROOT") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(Path.GetTempPath(), $"phase11-orion-positive-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PHASE11_CERTIFICATION_OUTPUT_ROOT"))
            && Directory.Exists(root)) Directory.Delete(root, true);
    }
}
