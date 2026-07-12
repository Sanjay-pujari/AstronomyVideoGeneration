using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using SemanticCapabilityDefinitionV1 = Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts.SemanticCapabilityDefinition;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;

public interface ISemanticCapabilityCatalogV1
{
    IReadOnlyCollection<SemanticCapabilityDefinitionV1> Definitions { get; }
    bool TryGet(SemanticCapabilityId id, out SemanticCapabilityDefinitionV1 definition);
    SemanticCapabilityDefinitionV1 GetRequired(SemanticCapabilityId id);
    LegacySemanticCapabilityResolution ResolveLegacyTerm(string term);
    SemanticCapabilityCatalogValidationResult Validate();
}
