using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;

public interface ILegacySemanticCapabilityResolverV1
{
    LegacySemanticCapabilityResolution Resolve(string term);
    SemanticCapabilityId Canonicalize(SemanticCapabilityId capabilityId);
}

public sealed class LegacySemanticCapabilityResolverV1 : ILegacySemanticCapabilityResolverV1
{
    private readonly SemanticCapabilityCatalogV1 _catalog;

    public LegacySemanticCapabilityResolverV1() : this(new SemanticCapabilityCatalogV1()) { }

    public LegacySemanticCapabilityResolverV1(SemanticCapabilityCatalogV1 catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public LegacySemanticCapabilityResolution Resolve(string term) => _catalog.ResolveLegacyTerm(term);

    public SemanticCapabilityId Canonicalize(SemanticCapabilityId capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId.Value))
            throw new ArgumentException("Semantic capability ID must contain a non-empty value.", nameof(capabilityId));

        var normalized = capabilityId.Value.Trim();
        var resolution = Resolve(normalized);
        return resolution.CanonicalCapabilityId ?? new SemanticCapabilityId(normalized);
    }
}
