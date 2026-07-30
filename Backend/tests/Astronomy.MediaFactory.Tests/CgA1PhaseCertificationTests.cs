using Astronomy.MediaFactory.Core.Certification;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests;

public sealed class CgA1PhaseCertificationTests
{
    [Fact]
    public void PhaseRegistry_ReturnsActualPhaseSixArtifacts()
    {
        var registry = new PhaseArtifactRegistry();
        var definitions = registry.GetDefinitions(6, Context(NewRoot()));
        definitions.Select(d => d.RelativePath).Should().Contain(new[]
        {
            "validation/phase-06-validation.json",
            "creative/creative-storyboard.json",
            "creative/documentary-contract.long.json",
            "creative/documentary-contract.short.json",
            "creative/documentary-architecture-diagnostics.json",
            "creative/documentary-decision-log.json"
        });
    }

    [Fact]
    public async Task ArtifactVerifier_FailsMissingRequiredArtifact_AndWarnsForMissingOptionalArtifact()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "validation"));
        await File.WriteAllTextAsync(Path.Combine(root, "validation", "phase-03-validation.json"), "{}");
        var definitions = new[]
        {
            new PhaseArtifactDefinition { ArtifactId = "required", PhaseNumber = 3, RelativePath = "validation/phase-03-validation.json", Required = true, ValidateJson = true, RequireNonEmpty = true },
            new PhaseArtifactDefinition { ArtifactId = "optional", PhaseNumber = 3, RelativePath = "missing.json", Required = false, ValidateJson = true, RequireNonEmpty = true },
            new PhaseArtifactDefinition { ArtifactId = "missing-required", PhaseNumber = 3, RelativePath = "required-missing.json", Required = true, ValidateJson = true, RequireNonEmpty = true }
        };
        var results = await new CertificationArtifactVerifier().VerifyAsync(Context(root), definitions, CancellationToken.None);
        results.Single(r => r.ArtifactId == "required").IsValid.Should().BeTrue();
        results.Single(r => r.ArtifactId == "optional").IsValid.Should().BeTrue();
        results.Single(r => r.ArtifactId == "missing-required").IsValid.Should().BeFalse();
        CertificationStatusAggregator.FromArtifacts(results).Should().Be(CertificationStatus.Failed);
    }

    [Fact]
    public async Task PhaseOneCertifier_ReturnsPassedWithWarnings_WhenRequiredArtifactsExistAndOptionalArtifactsAreAbsent()
    {
        var root = NewRoot();
        foreach (var relativePath in Rc2CanonicalArtifactCatalog.Phase1ManifestArtifacts)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{}");
        }
        var provider = new ServiceCollection().AddCgA1CertificationFoundation().BuildServiceProvider();
        var certifier = provider.GetServices<IPhaseCertifier>().Single(c => c.PhaseNumber == 1);
        var result = await certifier.CertifyAsync(Context(root), CancellationToken.None);
        result.StructuralStatus.Should().Be(CertificationStatus.Passed);
        result.SemanticStatus.Should().Be(CertificationStatus.NotEvaluated);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void PhaseOneAndTwoRegistries_UseCanonicalAuthority_NotDeprecatedPlanInputArtifacts()
    {
        var registry = new PhaseArtifactRegistry();
        var phase1 = registry.GetDefinitions(1, Context(NewRoot())).Select(item => item.RelativePath).ToArray();
        var phase2 = registry.GetDefinitions(2, Context(NewRoot())).Select(item => item.RelativePath).ToArray();

        phase1.Should().BeEquivalentTo(Rc2CanonicalArtifactCatalog.Phase1ManifestArtifacts);
        phase2.Should().Contain(Rc2CanonicalArtifactCatalog.Phase2AuthorityArtifacts);
        phase2.Should().Contain("validation/phase-02-validation.json");
        phase1.Should().NotContain("plan-input/production-pipeline-request.json");
        phase2.Should().NotContain("plan-input/production-event-intelligence-diagnostics.json");
    }

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cg-a1-" + Guid.NewGuid())).FullName;
    private static FamilyCertificationContext Context(string root) => new()
    {
        OutputRoot = root,
        ValidationRoot = Path.Combine(root, "validation"),
        PlanId = "plan-1",
        EventTitle = "Event",
        EventType = "GenericAstronomy",
        Language = "en",
        RegionId = "US",
        RequestedStartPhase = 1,
        RequestedEndPhase = 7
    };
}

public sealed class CgA1PhaseCertificationCompletionTests
{
    [Fact]
    public async Task JsonReader_ReturnsStructuredInvalidJsonResult()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cg-a1-" + Guid.NewGuid())).FullName;
        var path = Path.Combine(root, "bad.json");
        await File.WriteAllTextAsync(path, "{ bad");
        var result = await new CertificationJsonReader().ReadAsync(path, CancellationToken.None);
        result.Exists.Should().BeTrue();
        result.Length.Should().BeGreaterThan(0);
        result.ValidJson.Should().BeFalse();
        result.Document.Should().BeNull();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PathHelpers_RejectDirectoryTraversal()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cg-a1-" + Guid.NewGuid())).FullName;
        var context = new FamilyCertificationContext { OutputRoot = root, ValidationRoot = Path.Combine(root, "validation"), PlanId = "p", EventTitle = "e", EventType = "t", Language = "en", RegionId = "US", RequestedStartPhase = 1, RequestedEndPhase = 7 };
        Action act = () => CertificationPathHelpers.ResolveArtifactPath(context, "../../secret.json");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task PhaseSixDiscoversScenes_AndFlagsManifestCountMismatchAndDuplicateIds()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cg-a1-" + Guid.NewGuid())).FullName;
        Directory.CreateDirectory(Path.Combine(root, "validation")); Directory.CreateDirectory(Path.Combine(root, "creative")); Directory.CreateDirectory(Path.Combine(root, "story-frames", "short"));
        foreach (var p in new[] { "validation/phase-06-validation.json", "creative/creative-storyboard.json", "creative/documentary-contract.long.json", "creative/documentary-contract.short.json", "creative/documentary-architecture-diagnostics.json", "creative/documentary-decision-log.json" })
        { var full = Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(full)!); await File.WriteAllTextAsync(full, "{}"); }
        await File.WriteAllTextAsync(Path.Combine(root, "story-frames", "short", "manifest.json"), "{\"scenes\":[{\"sceneId\":\"a\"},{\"sceneId\":\"b\"},{\"sceneId\":\"missing\"}]}");
        await File.WriteAllTextAsync(Path.Combine(root, "story-frames", "short", "scene-001.json"), "{\"sceneId\":\"a\",\"narrationIntents\":[]}");
        await File.WriteAllTextAsync(Path.Combine(root, "story-frames", "short", "scene-002.json"), "{\"sceneId\":\"a\",\"narrationIntents\":[]}");
        var result = await new Phase6Certifier(new PhaseArtifactRegistry(), new CertificationArtifactVerifier()).CertifyAsync(new FamilyCertificationContext { OutputRoot = root, ValidationRoot = Path.Combine(root, "validation"), PlanId = "p", EventTitle = "e", EventType = "t", Language = "en", RegionId = "US", RequestedStartPhase = 1, RequestedEndPhase = 7 }, CancellationToken.None);
        result.Issues.Select(i => i.Code).Should().Contain(new[] { "P6.ManifestCountMismatch", "P6.DuplicateSceneId", "P6.InvalidManifestReference" });
    }

    [Fact]
    public void PhaseSevenRegistry_UsesNarrationV5PathsOnly()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cg-a1-" + Guid.NewGuid())).FullName;
        var context = new FamilyCertificationContext { OutputRoot = root, ValidationRoot = Path.Combine(root, "validation"), PlanId = "p", EventTitle = "e", EventType = "t", Language = "en", RegionId = "US", RequestedStartPhase = 1, RequestedEndPhase = 7 };
        var paths = new PhaseArtifactRegistry().GetDefinitions(7, context).Select(d => d.RelativePath).ToArray();
        paths.Should().OnlyContain(p => p.StartsWith("narration-v5/", StringComparison.Ordinal));
        paths.Should().NotContain(p => p.StartsWith("narration/", StringComparison.Ordinal));
    }

    [Fact]
    public void StatusAggregation_ReturnsSemanticNotEvaluatedAndQualityNotApplicable()
    {
        CertificationStatusAggregator.SemanticUnavailable().Should().Be(CertificationStatus.NotEvaluated);
        CertificationStatusAggregator.QualityFromDiagnostics().Should().Be(CertificationStatus.NotApplicable);
    }
}
