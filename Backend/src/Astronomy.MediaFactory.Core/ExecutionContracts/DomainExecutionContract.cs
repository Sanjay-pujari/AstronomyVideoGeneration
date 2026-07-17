namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record DomainExecutionContract(
    string DomainId,
    IReadOnlyList<FamilyExecutionContract> Families);
