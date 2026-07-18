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
        Directory.CreateDirectory(Path.Combine(root, "validation"));
        Directory.CreateDirectory(Path.Combine(root, "plan-input"));
        await File.WriteAllTextAsync(Path.Combine(root, "validation", "phase-01-validation.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(root, "plan-input", "content-plan-production-request.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(root, "plan-input", "production-pipeline-request.json"), "{}");
        var provider = new ServiceCollection().AddCgA1CertificationFoundation().BuildServiceProvider();
        var certifier = provider.GetServices<IPhaseCertifier>().Single(c => c.PhaseNumber == 1);
        var result = await certifier.CertifyAsync(Context(root), CancellationToken.None);
        result.StructuralStatus.Should().Be(CertificationStatus.Passed);
        result.SemanticStatus.Should().Be(CertificationStatus.NotEvaluated);
        result.Issues.Should().BeEmpty();
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
