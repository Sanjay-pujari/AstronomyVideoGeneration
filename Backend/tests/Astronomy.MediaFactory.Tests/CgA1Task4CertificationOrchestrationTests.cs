using System.Text.Json;
using Astronomy.MediaFactory.Core.Certification;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CgA1Task4CertificationOrchestrationTests
{
    [Fact]
    public async Task Coordinator_executes_selected_phases_once_in_numeric_order_and_writes_reports()
    {
        using var temp = new TempWorkspace();
        var calls = new List<int>();
        var services = new ServiceCollection();
        services.AddSingleton<IFamilyCertificationProfile>(new TestProfile("MeteorShower", "meteor_shower"));
        services.AddSingleton<IFamilyCertificationProfileRegistry, FamilyCertificationProfileRegistry>();
        services.AddSingleton<ICertificationPathService, CertificationPathService>();
        services.AddSingleton<ICertificationOutputLock, CertificationOutputLock>();
        services.AddSingleton<ICertificationSummaryAggregator, CertificationSummaryAggregator>();
        services.AddSingleton<ISemanticFactCatalog, CertificationSemanticFactCatalog>();
        services.AddSingleton<ICertificationDashboardMapper, CertificationDashboardMapper>();
        services.AddSingleton<ICertificationReportWriter, CertificationReportWriter>();
        services.AddSingleton<ICertificationCoordinator, CertificationCoordinator>();
        services.AddSingleton<IPhaseCertifier>(new StubCertifier(3, calls, CertificationStatus.Passed));
        services.AddSingleton<IPhaseCertifier>(new StubCertifier(2, calls, CertificationStatus.Passed));
        var coordinator = services.BuildServiceProvider().GetRequiredService<ICertificationCoordinator>();

        var summary = await coordinator.CertifyAsync(Context(temp.Path, 2, 3, "meteor_shower"), CancellationToken.None);

        calls.Should().Equal(2, 3);
        summary.Phases.Select(p => p.PhaseNumber).Should().Equal(2, 3);
        summary.FamilyId.Should().Be("MeteorShower");
        File.Exists(Path.Combine(temp.Path, "certification", "phase-02-certification.json")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "certification", "phase-03-certification.json")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "certification", "phase-01-certification.json")).Should().BeFalse();
        File.Exists(Path.Combine(temp.Path, "certification", "certification-summary.json")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "certification", "certification-dashboard.json")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "certification", "certification-report.md")).Should().BeTrue();
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temp.Path, "certification", "certification-summary.json")));
        doc.RootElement.GetProperty("schemaVersion").GetString().Should().Be("cg-a1-certification.v1");
        doc.RootElement.GetProperty("certificationDecision").GetString().Should().Be("Certified");
    }

    [Fact]
    public async Task Coordinator_captures_unexpected_phase_exception_and_continues()
    {
        using var temp = new TempWorkspace();
        var calls = new List<int>();
        var coordinator = Build([new ThrowingCertifier(1, calls), new StubCertifier(2, calls, CertificationStatus.Passed)]);
        var summary = await coordinator.CertifyAsync(Context(temp.Path, 1, 2, "PlanetConjunction"), CancellationToken.None);
        calls.Should().Equal(1, 2);
        summary.Phases.Single(p => p.PhaseNumber == 1).Issues.Should().Contain(i => i.Code == "CERT.PhaseExecutionException");
        summary.CertificationDecision.Should().Be(CertificationDecision.NotCertified);
        summary.PublicationDecision.Should().Be(PublicationDecision.DoNotPublish);
    }

    [Fact]
    public void Coordinator_validates_missing_and_duplicate_certifiers()
    {
        using var temp = new TempWorkspace();
        Build([new StubCertifier(1, [], CertificationStatus.Passed)]).Invoking(c => c.CertifyAsync(Context(temp.Path, 1, 2, "MeteorShower"), CancellationToken.None).GetAwaiter().GetResult()).Should().Throw<InvalidOperationException>().WithMessage("*Missing phase certifier*2*");
        Build([new StubCertifier(1, [], CertificationStatus.Passed), new StubCertifier(1, [], CertificationStatus.Passed)]).Invoking(c => c.CertifyAsync(Context(temp.Path, 1, 1, "MeteorShower"), CancellationToken.None).GetAwaiter().GetResult()).Should().Throw<InvalidOperationException>().WithMessage("*Duplicate phase certifier*1*");
    }

    [Fact]
    public void Aggregation_keeps_quality_independent_from_technical_certification()
    {
        var context = Context(Path.GetTempPath(), 1, 1, "MeteorShower");
        var summary = new CertificationSummaryAggregator().Aggregate(context, "MeteorShower", [new PhaseCertificationResult { PhaseNumber=7, PhaseName="P7", StructuralStatus=CertificationStatus.Passed, SemanticStatus=CertificationStatus.Passed, QualityStatus=CertificationStatus.Failed, GeneratedUtc=DateTimeOffset.UtcNow }], DateTimeOffset.UtcNow);
        summary.StructuralStatus.Should().Be(CertificationStatus.Passed);
        summary.SemanticStatus.Should().Be(CertificationStatus.Passed);
        summary.QualityStatus.Should().Be(CertificationStatus.Failed);
        summary.CertificationDecision.Should().Be(CertificationDecision.Certified);
        summary.PublicationDecision.Should().Be(PublicationDecision.DoNotPublish);
    }

    [Fact]
    public async Task Coordinator_propagates_cancellation()
    {
        using var temp = new TempWorkspace();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var coordinator = Build([new StubCertifier(1, [], CertificationStatus.Passed)]);
        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.CertifyAsync(Context(temp.Path, 1, 1, "MeteorShower"), cts.Token));
    }

    private static ICertificationCoordinator Build(IReadOnlyList<IPhaseCertifier> certifiers)
    {
        var services = new ServiceCollection().AddCgA1CertificationFoundation();
        services.AddSingleton<IEnumerable<IPhaseCertifier>>(certifiers);
        return new CertificationCoordinator(certifiers, services.BuildServiceProvider().GetRequiredService<IFamilyCertificationProfileRegistry>(), services.BuildServiceProvider().GetRequiredService<ICertificationReportWriter>(), new CertificationSummaryAggregator(), new CertificationOutputLock());
    }
    private static FamilyCertificationContext Context(string root, int start, int end, string eventType) => new() { OutputRoot=root, ValidationRoot=Path.Combine(root,"validation"), PlanId="plan", EventTitle="event", EventType=eventType, Language="en", RegionId="US", RequestedStartPhase=start, RequestedEndPhase=end };
    private sealed record TestProfile(string FamilyId, params string[] Aliases) : IFamilyCertificationProfile { public IReadOnlySet<string> SupportedEventTypeAliases { get; } = Aliases.ToHashSet(StringComparer.OrdinalIgnoreCase); public string? CanonicalSemanticValueId => null; public IReadOnlyList<RequiredSemanticFactDefinition> GetRequiredFacts(FamilyCertificationContext context)=>[]; public IReadOnlyList<ForbiddenConceptDefinition> GetForbiddenConcepts(FamilyCertificationContext context)=>[]; public IReadOnlyList<StoryStructureRequirement> GetStoryRequirements(FamilyCertificationContext context)=>[]; public IReadOnlyList<BeatCoverageRequirement> GetBeatCoverageRequirements(FamilyCertificationContext context)=>[]; public IReadOnlyList<PhaseArtifactDefinition> GetAdditionalArtifacts(FamilyCertificationContext context)=>[]; }
    private sealed class StubCertifier(int phase, List<int> calls, CertificationStatus status) : IPhaseCertifier { public int PhaseNumber => phase; public Task<PhaseCertificationResult> CertifyAsync(FamilyCertificationContext context, CancellationToken cancellationToken){ calls.Add(phase); return Task.FromResult(new PhaseCertificationResult{PhaseNumber=phase,PhaseName=$"P{phase}",StructuralStatus=status,SemanticStatus=phase==7?status:CertificationStatus.NotEvaluated,QualityStatus=phase==7?status:CertificationStatus.NotApplicable,GeneratedUtc=DateTimeOffset.UtcNow}); } }
    private sealed class ThrowingCertifier(int phase, List<int> calls) : IPhaseCertifier { public int PhaseNumber => phase; public Task<PhaseCertificationResult> CertifyAsync(FamilyCertificationContext context, CancellationToken cancellationToken){ calls.Add(phase); throw new InvalidOperationException("boom secret-token-redacted"); } }
    private sealed class TempWorkspace : IDisposable { public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cg-a1-task4-" + Guid.NewGuid().ToString("N")); public TempWorkspace() => Directory.CreateDirectory(Path); public void Dispose(){ if(Directory.Exists(Path)) Directory.Delete(Path, true); } }
}
