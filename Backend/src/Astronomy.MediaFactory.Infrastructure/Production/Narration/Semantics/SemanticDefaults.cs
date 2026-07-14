using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Collection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Evaluation;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Selection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public static class SemanticDefaults
{
    public static ISemanticCapabilityCatalog SemanticCapabilityCatalog { get; } = new SemanticCapabilityCatalog();
    public static ISemanticCapabilitySourceRegistry SemanticCapabilitySourceRegistry { get; } = new SemanticCapabilitySourceRegistry(SemanticCapabilityCatalog);
    public static ISemanticCapabilityResolver SemanticCapabilityResolver { get; } = new SemanticCapabilityResolver(SemanticCapabilityCatalog, SemanticCapabilitySourceRegistry);
    public static IAstronomyDomainKnowledgeProvider DomainKnowledgeProvider { get; } = new AstronomyDomainKnowledgeProvider();
    public static ISemanticSourcePolicyCatalogV1 SemanticSourcePolicyCatalogV1 { get; } = new SemanticSourcePolicyCatalogV1();
    public static ISemanticSourceAdapterRegistryV1 SemanticSourceAdapterRegistryV1 { get; } = new SemanticSourceAdapterRegistryV1();
    public static ISemanticCandidateCollectorV1 SemanticCandidateCollectorV1 { get; } = new SemanticCandidateCollectorV1(SemanticSourcePolicyCatalogV1, SemanticSourceAdapterRegistryV1);
    public static ISemanticCandidateEvaluatorV1 SemanticCandidateEvaluatorV1 { get; } = new SemanticCandidateEvaluatorV1();
    public static ISemanticConflictAnalyzerV1 SemanticConflictAnalyzerV1 { get; } = new SemanticConflictAnalyzerV1();
    public static ISemanticCandidateSelectorV1 SemanticCandidateSelectorV1 { get; } = new SemanticCandidateSelectorV1();
    public static ISemanticResolutionEngineV1 SemanticResolutionEngineV1 { get; } = new SemanticResolutionEngineV1(SemanticCandidateCollectorV1, SemanticCandidateEvaluatorV1, SemanticConflictAnalyzerV1, SemanticCandidateSelectorV1);
    public static IRequiredSemanticFactResolver RequiredSemanticFactResolver { get; } = new RequiredSemanticFactResolver(SemanticCapabilityResolver, DomainKnowledgeProvider, SemanticResolutionEngineV1);
    public static INarrationRealizer NarrationRealizer { get; } = new NarrationRealizer();
    public static IAstronomyFamilyProfileResolver FamilyProfileResolver { get; } = new AstronomyFamilyProfileResolver();
}
