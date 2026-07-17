using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record DomainExecutionContract
{
    public DomainExecutionContract(string DomainId, string ContractVersion, string DisplayName, string Description = "", ImmutableArray<FamilyExecutionContract> Families = default, ImmutableDictionary<string, string>? Metadata = null)
    {
        this.DomainId = ExecutionContractGuard.RequireNonEmpty(DomainId, nameof(DomainId));
        this.ContractVersion = ExecutionContractGuard.RequireNonEmpty(ContractVersion, nameof(ContractVersion));
        this.DisplayName = ExecutionContractGuard.NormalizeText(DisplayName);
        this.Description = ExecutionContractGuard.NormalizeText(Description);
        this.Families = ExecutionContractGuard.NormalizeArray(Families);
        this.Metadata = ExecutionContractGuard.NormalizeMetadata(Metadata);
    }
    public string DomainId { get; init; }
    public string ContractVersion { get; init; }
    public string DisplayName { get; init; }
    public string Description { get; init; }
    public ImmutableArray<FamilyExecutionContract> Families { get; init; }
    public ImmutableDictionary<string, string> Metadata { get; init; }
}
