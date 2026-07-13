using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Certification;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
public interface ISemanticSourcePolicyCatalogV1
{
    IReadOnlyCollection<SemanticSourcePolicyV1> Policies { get; }
    bool TryGet(SemanticCapabilityId capabilityId, out SemanticSourcePolicyV1 policy);
    SemanticSourcePolicyV1 GetRequired(SemanticCapabilityId capabilityId);
    SemanticSourcePolicyValidationResult Validate();
    bool IsSourceApproved(SemanticCapabilityId capabilityId, string sourceId);
    SemanticSourceApprovalResultV1 EvaluateSource(SemanticCapabilityId capabilityId, SemanticSourceDescriptorV1 sourceDescriptor);
    SemanticSourcePolicyCertificationReportV1 CertifyFamilyProfile(AstronomyFamilyProfileV1 profile);
}
