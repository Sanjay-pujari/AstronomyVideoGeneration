using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;

public interface ISemanticCapabilityCatalogV1
{
    IReadOnlyCollection<SemanticCapabilityDefinition> Definitions { get; }
    bool TryGet(SemanticCapabilityId id, out SemanticCapabilityDefinition definition);
    SemanticCapabilityDefinition GetRequired(SemanticCapabilityId id);
    LegacySemanticCapabilityResolution ResolveLegacyTerm(string term);
    SemanticCapabilityCatalogValidationResult Validate();
}
