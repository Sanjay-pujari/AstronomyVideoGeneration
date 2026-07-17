namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyContractResolution(
    FamilyContractResolutionStatus Status,
    string RequestedIdentity,
    string? RequestedDomainId,
    string? ResolvedDomainId,
    string? ResolvedFamilyId,
    FamilyContractMatchKind MatchedBy,
    FamilyExecutionContract? Contract,
    string DiagnosticMessage)
{
    public string? ContractVersion => Contract?.ContractVersion;
}
