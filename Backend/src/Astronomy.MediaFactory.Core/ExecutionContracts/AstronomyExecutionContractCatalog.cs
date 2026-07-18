using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public static class AstronomyExecutionContractCatalog
{
    public static DomainExecutionContract Create() => new(
        DomainId: "Astronomy",
        ContractVersion: "AstronomyExecutionContracts-v1",
        DisplayName: "Astronomy Media Factory",
        Description: "Declarative execution contracts for astronomy content families.",
        Families: ImmutableArray.Create(MeteorShowerExecutionContractFactory.Create()),
        Metadata: ImmutableDictionary<string, string>.Empty
            .Add("frameworkMilestone", "2C")
            .Add("validationMode", "shadow")
            .Add("runtimeAuthority", "production"));
}
