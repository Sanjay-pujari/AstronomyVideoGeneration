using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public static class AstronomyExecutionContractCatalog
{
    public static DomainExecutionContract Create() => new(
        DomainId: "Astronomy",
        ContractVersion: "AstronomyExecutionContracts-v1",
        DisplayName: "Astronomy Media Factory",
        Description: "Declarative execution contracts for astronomy content families.",
        Families: ImmutableArray<FamilyExecutionContract>.Empty,
        Metadata: ImmutableDictionary<string, string>.Empty
            .Add("frameworkStatus", "dormant")
            .Add("runtimeWiring", "none")
            .Add("milestone", "2A"));
}
