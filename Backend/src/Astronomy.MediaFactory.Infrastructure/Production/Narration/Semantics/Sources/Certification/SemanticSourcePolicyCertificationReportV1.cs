using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Certification;
public sealed record SemanticSourcePolicyCertificationEntryV1(string FamilyId, string CapabilityId, bool Required, SemanticSourceCertificationStatusV1 Status, string DiagnosticMessage);
public sealed record SemanticSourcePolicyCertificationReportV1
{
    public SemanticSourcePolicyCertificationReportV1(string familyId, SemanticSourceCertificationStatusV1 status, IReadOnlyCollection<SemanticSourcePolicyCertificationEntryV1> entries, IReadOnlyCollection<string> optionalGaps, IReadOnlyCollection<string> blockers){FamilyId=familyId;Status=status;Entries=entries.ToImmutableArray();OptionalGaps=optionalGaps.ToImmutableArray();Blockers=blockers.ToImmutableArray();}
    [JsonConstructor] public SemanticSourcePolicyCertificationReportV1(string familyId, SemanticSourceCertificationStatusV1 status, ImmutableArray<SemanticSourcePolicyCertificationEntryV1> entries, ImmutableArray<string> optionalGaps, ImmutableArray<string> blockers){FamilyId=familyId;Status=status;Entries=entries.IsDefault?[]:entries;OptionalGaps=optionalGaps.IsDefault?[]:optionalGaps;Blockers=blockers.IsDefault?[]:blockers;}
    public string FamilyId{get;init;} public SemanticSourceCertificationStatusV1 Status{get;init;} public ImmutableArray<SemanticSourcePolicyCertificationEntryV1> Entries{get;init;} public ImmutableArray<string> OptionalGaps{get;init;} public ImmutableArray<string> Blockers{get;init;}
}
