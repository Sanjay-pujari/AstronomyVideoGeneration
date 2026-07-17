using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionContracts;

namespace Astronomy.MediaFactory.Tests;

public class ExecutionContractModelTests
{
    [Fact] public void DomainContractRejectsEmptyDomainId() => Assert.Throws<ArgumentException>(() => new DomainExecutionContract(" ", "v1", "Domain"));
    [Fact] public void DomainContractRejectsEmptyContractVersion() => Assert.Throws<ArgumentException>(() => new DomainExecutionContract("Domain", " ", "Domain"));
    [Fact] public void FamilyContractRejectsEmptyFamilyId() => Assert.Throws<ArgumentException>(() => new FamilyExecutionContract(" ", "v1", "Family"));
    [Fact] public void FamilyContractRejectsEmptyContractVersion() => Assert.Throws<ArgumentException>(() => new FamilyExecutionContract("Family", " ", "Family"));

    [Fact]
    public void FamilyAliasesAreTrimmedDeduplicatedAndCanonicalAliasIsRemoved()
    {
        var contract = new FamilyExecutionContract("MeteorShower", "v1", "Meteor Shower", Aliases: [" meteors ", "METEORS", "MeteorShower", " "]);
        Assert.Single(contract.Aliases);
        Assert.Equal("meteors", contract.Aliases[0]);
    }

    [Fact]
    public void DefaultImmutableCollectionsNormalizeToEmpty()
    {
        var domain = new DomainExecutionContract("Domain", "v1", "Domain");
        var family = new FamilyExecutionContract("Family", "v1", "Family");
        Assert.Empty(domain.Families); Assert.Empty(domain.Metadata); Assert.Empty(family.InputRequirements); Assert.Empty(family.Metadata);
    }

    [Fact]
    public void DuplicateRequirementIdsAreRejectedAcrossCategories()
    {
        var input = new FamilyInputRequirement("same", "input");
        var semantic = new FamilySemanticRequirement("same", "capability");
        Assert.Throws<ArgumentException>(() => new FamilyExecutionContract("Family", "v1", "Family", InputRequirements: [input], SemanticRequirements: [semantic]));
    }

    [Fact]
    public void RequirementModelsRejectEmptyRequirementId()
    {
        Assert.Throws<ArgumentException>(() => new FamilyInputRequirement(" ", "input"));
        Assert.Throws<ArgumentException>(() => new FamilySemanticRequirement(" ", "capability"));
        Assert.Throws<ArgumentException>(() => new FamilyProjectionRequirement(" ", "capability", "fact"));
        Assert.Throws<ArgumentException>(() => new FamilyPhaseArtifactRequirement(" ", "phase", "artifact", "path"));
        Assert.Throws<ArgumentException>(() => new FamilyValidationRequirement(" ", "rule", FamilyValidationBoundary.PreExecution, FamilyValidationSeverity.Blocking));
    }

    [Fact]
    public void RequirementModelsPreserveMetadataImmutably()
    {
        var metadata = ImmutableDictionary<string, string>.Empty.Add("key", "value");
        var requirement = new FamilyInputRequirement("input-required", "input", Metadata: metadata);
        metadata = metadata.SetItem("key", "changed");
        Assert.Equal("value", requirement.Metadata["key"]);
    }
}
