namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public interface IExecutionContractRegistry
{
    IReadOnlyCollection<DomainExecutionContract> Domains { get; }
    FamilyContractResolution ResolveFamily(string familyIdOrAlias, string? domainId = null);
    bool TryResolveFamily(string familyIdOrAlias, out FamilyExecutionContract? contract, string? domainId = null);
}
