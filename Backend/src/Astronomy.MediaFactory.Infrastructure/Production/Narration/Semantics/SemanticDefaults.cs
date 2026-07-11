using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public static class SemanticDefaults
{
    public static ISemanticCapabilityCatalog SemanticCapabilityCatalog { get; } = new SemanticCapabilityCatalog();
    public static ISemanticCapabilitySourceRegistry SemanticCapabilitySourceRegistry { get; } = new SemanticCapabilitySourceRegistry(SemanticCapabilityCatalog);
    public static ISemanticCapabilityResolver SemanticCapabilityResolver { get; } = new SemanticCapabilityResolver(SemanticCapabilityCatalog, SemanticCapabilitySourceRegistry);
    public static IAstronomyDomainKnowledgeProvider DomainKnowledgeProvider { get; } = new AstronomyDomainKnowledgeProvider();
    public static IRequiredSemanticFactResolver RequiredSemanticFactResolver { get; } = new RequiredSemanticFactResolver(SemanticCapabilityResolver, DomainKnowledgeProvider);
    public static INarrationRealizer NarrationRealizer { get; } = new NarrationRealizer();
    public static IAstronomyFamilyProfileResolver FamilyProfileResolver { get; } = new AstronomyFamilyProfileResolver();
}
