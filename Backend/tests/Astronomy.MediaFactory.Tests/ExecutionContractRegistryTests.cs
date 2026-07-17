using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionContracts;

namespace Astronomy.MediaFactory.Tests;

public class ExecutionContractRegistryTests
{
    [Fact] public void ResolvesCanonicalFamilyIdCaseInsensitively() { var r = Registry(Family("MeteorShower", ["Meteors"])); var x = r.ResolveFamily("meteorshower"); Assert.Equal(FamilyContractResolutionStatus.Resolved, x.Status); Assert.Equal(FamilyContractMatchKind.CanonicalFamilyId, x.MatchedBy); }
    [Fact] public void ResolvesAliasCaseInsensitively() { var x = Registry(Family("MeteorShower", ["Meteors"])).ResolveFamily("METEORS"); Assert.Equal(FamilyContractResolutionStatus.Resolved, x.Status); Assert.Equal(FamilyContractMatchKind.Alias, x.MatchedBy); }
    [Fact] public void EmptyIdentityReturnsInvalidRequest() => Assert.Equal(FamilyContractResolutionStatus.InvalidRequest, Registry(Family("A")).ResolveFamily(" ").Status);
    [Fact] public void UnknownFamilyReturnsNotFound() => Assert.Equal(FamilyContractResolutionStatus.NotFound, Registry(Family("A")).ResolveFamily("missing").Status);

    [Fact]
    public void DomainFilterResolvesOnlyInsideSelectedDomain()
    {
        var registry = new ExecutionContractRegistry([Domain("A", Family("Shared")), Domain("B", Family("Shared"))]);
        Assert.Equal("B", registry.ResolveFamily("shared", "B").ResolvedDomainId);
        Assert.Equal(FamilyContractResolutionStatus.NotFound, registry.ResolveFamily("shared").Status);
    }

    [Fact] public void UnknownDomainReturnsNotFound() => Assert.Contains("not registered", Registry(Family("A")).ResolveFamily("A", "missing").DiagnosticMessage);
    [Fact] public void DuplicateDomainIdsAreRejected() => Assert.Throws<ArgumentException>(() => new ExecutionContractRegistry([Domain("A"), Domain("a")]));
    [Fact] public void DuplicateCanonicalFamilyIdsInSameDomainAreRejected() => Assert.Throws<ArgumentException>(() => new ExecutionContractRegistry([Domain("D", Family("A"), Family("a"))]));
    [Fact] public void DuplicateCanonicalFamilyIdsAcrossDomainsAreAllowedOnlyWithDomainQualifiedResolution() { var r = new ExecutionContractRegistry([Domain("D1", Family("A")), Domain("D2", Family("A"))]); Assert.Equal(FamilyContractResolutionStatus.NotFound, r.ResolveFamily("A").Status); Assert.True(r.TryResolveFamily("A", out var c, "D2")); Assert.Equal("A", c!.FamilyId); }
    [Fact] public void AliasConflictWithAnotherAliasIsRejected() => Assert.Throws<ArgumentException>(() => new ExecutionContractRegistry([Domain("D1", Family("A", ["alias"])), Domain("D2", Family("B", ["ALIAS"]))]));
    [Fact] public void AliasConflictWithCanonicalFamilyIdIsRejected() => Assert.Throws<ArgumentException>(() => new ExecutionContractRegistry([Domain("D1", Family("MeteorShower", ["Meteors"])), Domain("D2", Family("Meteors"))]));

    [Fact]
    public void RegistrySnapshotsInputCollections()
    {
        var builder = ImmutableArray.CreateBuilder<DomainExecutionContract>(); builder.Add(Domain("D", Family("A")));
        var registry = new ExecutionContractRegistry(builder); builder.Clear();
        Assert.Equal(FamilyContractResolutionStatus.Resolved, registry.ResolveFamily("A").Status);
    }

    [Fact]
    public void ResolutionResultsAreDeterministic()
    {
        var registry = Registry(Family("A", ["alias"]));
        Assert.Equal(registry.ResolveFamily("alias"), registry.ResolveFamily("ALIAS"));
    }

    [Fact]
    public void DormantAstronomyCatalogCreatesValidEmptyDomainRegistry()
    {
        var domain = AstronomyExecutionContractCatalog.Create();
        var registry = new ExecutionContractRegistry([domain]);
        Assert.Equal("Astronomy", registry.Domains.Single().DomainId);
        Assert.Empty(registry.Domains.Single().Families);
    }

    private static ExecutionContractRegistry Registry(FamilyExecutionContract family) => new([Domain("D", family)]);
    private static DomainExecutionContract Domain(string id, params FamilyExecutionContract[] families) => new(id, "v1", id, Families: families.ToImmutableArray());
    private static FamilyExecutionContract Family(string id, ImmutableArray<string> aliases = default) => new(id, "v1", id, Aliases: aliases);
}
