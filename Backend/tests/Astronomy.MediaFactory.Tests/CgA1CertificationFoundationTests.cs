using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.Certification;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CgA1CertificationFoundationModelTests
{
    [Fact]
    public void PhaseResult_DefaultCollectionsAreNonNull_AndStatusesAreIndependent()
    {
        var generated = new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);
        var result = new PhaseCertificationResult
        {
            PhaseNumber = 3,
            PhaseName = "Semantic projection",
            StructuralStatus = CertificationStatus.Passed,
            SemanticStatus = CertificationStatus.Failed,
            QualityStatus = CertificationStatus.NotEvaluated,
            GeneratedUtc = generated
        };

        result.Artifacts.Should().NotBeNull().And.BeEmpty();
        result.SemanticFacts.Should().NotBeNull().And.BeEmpty();
        result.Issues.Should().NotBeNull().And.BeEmpty();
        result.Warnings.Should().NotBeNull().And.BeEmpty();
        result.Recommendations.Should().NotBeNull().And.BeEmpty();
        result.StructuralStatus.Should().Be(CertificationStatus.Passed);
        result.SemanticStatus.Should().Be(CertificationStatus.Failed);
        result.QualityStatus.Should().Be(CertificationStatus.NotEvaluated);
        result.GeneratedUtc.Should().Be(generated);
    }

    [Fact]
    public void Summary_KeepsExecutionSemanticAndPublicationCertificationSeparate()
    {
        var generated = new DateTimeOffset(2026, 7, 18, 1, 0, 0, TimeSpan.Zero);
        var summary = new FamilyCertificationSummary
        {
            PlanId = "plan-1",
            EventTitle = "Perseids",
            EventType = "MeteorShower",
            FamilyId = "meteor-shower",
            Language = "en",
            RegionId = "US",
            ExecutionStatus = CertificationStatus.Passed,
            SemanticStatus = CertificationStatus.PassedWithWarnings,
            QualityStatus = CertificationStatus.NotApplicable,
            ExecutionCertified = true,
            SemanticCertified = false,
            PublicationCertified = false,
            GeneratedUtc = generated
        };

        summary.Phases.Should().NotBeNull().And.BeEmpty();
        summary.BlockingIssues.Should().NotBeNull().And.BeEmpty();
        summary.ExecutionCertified.Should().BeTrue();
        summary.SemanticCertified.Should().BeFalse();
        summary.PublicationCertified.Should().BeFalse();
        summary.GeneratedUtc.Should().Be(generated);
    }

    [Fact]
    public void Records_UseStableValueEquality_ForFoundationModels()
    {
        var first = new CertificationIssue { Category = CertificationIssueCategory.MissingArtifact, Code = "artifact.missing", Message = "Missing", IsBlocking = true };
        var second = new CertificationIssue { Category = CertificationIssueCategory.MissingArtifact, Code = "artifact.missing", Message = "Missing", IsBlocking = true };
        first.Should().Be(second);
    }
}

public sealed class CgA1CertificationProfileRegistryTests
{
    [Fact]
    public void ResolvesByFamilyIdAliasAndCaseInsensitiveEventType()
    {
        var profile = new FakeProfile("MeteorShower", ["meteor", "meteor-shower"]);
        var registry = new FamilyCertificationProfileRegistry([profile]);

        registry.Resolve("MeteorShower").Should().BeSameAs(profile);
        registry.Resolve("meteor").Should().BeSameAs(profile);
        registry.Resolve("METEOR-SHOWER").Should().BeSameAs(profile);
    }

    [Fact]
    public void RejectsUnsupportedAndEmptyEventTypes()
    {
        var registry = new FamilyCertificationProfileRegistry([]);
        registry.TryResolve("Unknown", out var profile).Should().BeFalse();
        profile.Should().BeNull();
        registry.Invoking(r => r.Resolve("Unknown")).Should().Throw<KeyNotFoundException>().WithMessage("*Unsupported certification family event type*");
        registry.Invoking(r => r.TryResolve(" ", out _)).Should().Throw<ArgumentException>().WithMessage("*EventType must be non-empty*");
    }

    [Fact]
    public void DetectsDuplicateAliasesDeterministically()
    {
        var first = new FakeProfile("A", ["Shared"]);
        var second = new FakeProfile("B", ["shared"]);
        Action act = () => _ = new FamilyCertificationProfileRegistry([second, first]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate certification family event type alias 'shared'*'A'*'B'*");
    }

    [Fact]
    public void ZeroProfileRegistrySupportsContainerResolution()
    {
        var registry = new FamilyCertificationProfileRegistry([]);
        registry.TryResolve("MeteorShower", out _).Should().BeFalse();
    }

    [Fact]
    public void EventTypeOnlyApiPreventsContentStrategyInfluencingResolution()
    {
        var profile = new FakeProfile("MeteorShower", ["MeteorShower"]);
        var registry = new FamilyCertificationProfileRegistry([profile]);

        registry.Resolve("MeteorShower").Should().BeSameAs(profile);
        typeof(IFamilyCertificationProfileRegistry).GetMethods().SelectMany(m => m.GetParameters()).Select(p => p.Name).Should().NotContain("contentStrategy");
        typeof(IFamilyCertificationProfileRegistry).GetMethods().Where(m => m.Name == nameof(IFamilyCertificationProfileRegistry.Resolve)).Should().OnlyContain(m => m.GetParameters().Single().Name == "eventType");
    }

    private sealed class FakeProfile(string familyId, IEnumerable<string> aliases) : IFamilyCertificationProfile
    {
        public string FamilyId { get; } = familyId;
        public IReadOnlySet<string> SupportedEventTypeAliases { get; } = aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        public string? CanonicalSemanticValueId => null;
        public IReadOnlyList<RequiredSemanticFactDefinition> GetRequiredFacts(FamilyCertificationContext context) => [];
        public IReadOnlyList<ForbiddenConceptDefinition> GetForbiddenConcepts(FamilyCertificationContext context) => [];
        public IReadOnlyList<StoryStructureRequirement> GetStoryRequirements(FamilyCertificationContext context) => [];
        public IReadOnlyList<BeatCoverageRequirement> GetBeatCoverageRequirements(FamilyCertificationContext context) => [];
        public IReadOnlyList<PhaseArtifactDefinition> GetAdditionalArtifacts(FamilyCertificationContext context) => [];
    }
}

public sealed class CgA1CertificationSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void SerializesEnumsRecordsNamingNullsAndCollectionsWithExistingWebOptions()
    {
        var result = new SemanticFactCertificationResult { FactId = "peak-time", Required = true, Resolved = true, BeatIds = ["hook"], Diagnostics = [] };
        var json = JsonSerializer.Serialize(result, JsonOptions);

        json.Should().Contain("\"factId\":");
        json.Should().Contain("\"required\":true");
        json.Should().Contain("\"beatIds\":[\"hook\"]");
        json.Should().Contain("\"diagnostics\":[]");
        json.Should().NotContain("sourcePath");

        var statusJson = JsonSerializer.Serialize(CertificationStatus.PassedWithWarnings, JsonOptions);
        statusJson.Should().Be("\"PassedWithWarnings\"");
        var reparsed = JsonSerializer.Deserialize<SemanticFactCertificationResult>(json, JsonOptions);
        reparsed.Should().NotBeNull();
        reparsed!.FactId.Should().Be("peak-time");
        reparsed.BeatIds.Should().ContainSingle().Which.Should().Be("hook");
        reparsed.Diagnostics.Should().NotBeNull().And.BeEmpty();
    }
}

public sealed class CgA1CertificationDiTests
{
    [Fact]
    public void FoundationRegistrationSucceedsAndDoesNotRegisterMissingConcreteServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExistingPipelineSentinel>();
        services.AddCgA1CertificationFoundation();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetRequiredService<IFamilyCertificationProfileRegistry>().Should().BeOfType<FamilyCertificationProfileRegistry>();
        provider.GetRequiredService<ExistingPipelineSentinel>().Should().NotBeNull();
        services.Count(d => d.ServiceType == typeof(ExistingPipelineSentinel)).Should().Be(1);
        services.Should().NotContain(d => d.ServiceType == typeof(IPhaseCertifier));
    }

    private sealed class ExistingPipelineSentinel;
}
