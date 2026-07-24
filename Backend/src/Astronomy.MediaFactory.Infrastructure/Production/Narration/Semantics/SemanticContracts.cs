using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public interface ISemanticCapabilityCatalog
{
    IReadOnlyList<SemanticCapabilityDefinition> Capabilities { get; }
    SemanticCapabilityDefinition GetRequired(string capabilityId);
    bool TryGet(string capabilityId, out SemanticCapabilityDefinition definition);
    void Validate();
}

public interface ISemanticCapabilitySourceRegistry
{
    IReadOnlyList<ISemanticCapabilitySourceAdapter> Adapters { get; }
    IReadOnlyList<ISemanticCapabilitySourceAdapter> GetAdapters(string capabilityId);
    IReadOnlyList<string> ValidateCoverage(IEnumerable<AstronomyFamilyProfile> familyProfiles);
    IReadOnlyList<SemanticCapabilityCoverageRecord> ValidateCoverageDetailed(IEnumerable<AstronomyFamilyProfile> familyProfiles);
    void Validate();
}

public interface ISemanticCapabilitySourceAdapter
{
    string AdapterId { get; }
    string SupportedCapabilityId { get; }
    string SourceArtifact { get; }
    string SourcePath { get; }
    int Strength { get; }
    int Precedence { get; }
    string VerificationRule { get; }
    string RejectionReason { get; }
    bool TryExtract(SemanticCapabilitySourceContext context, out SemanticCapabilityCandidate candidate, out SemanticCapabilityRejection? rejection);
}

public interface ISemanticCapabilityResolver
{
    SemanticCapabilityResolution Resolve(string capabilityId, SemanticCapabilitySourceContext context, LanguageProfile languageProfile);
}

public sealed record SemanticCapabilityDefinition(string CapabilityId, IReadOnlyList<string> AcceptedAliases, int MinimumStrength, string Strictness, bool Localizable, bool Narratable, IReadOnlyList<string> ApprovedSourceAdapterIds, IReadOnlyList<string> ApprovedDerivationRuleIds, IReadOnlyList<string> ApprovedDomainKnowledgeFactTypes, bool EventSpecific = false);
public sealed record SemanticCapabilitySourceContext(string? FamilyProfileId, string? Format, JsonElement? ProductionRequest, JsonElement? LongDocumentaryContract, JsonElement? ShortDocumentaryContract, JsonElement? EditorialContract, JsonElement? StoryGraph, JsonElement? ProductionEventIntelligence, JsonElement? ObservationMetadata, JsonElement? QuestionAnswerSet, JsonElement? AstronomyDomainKnowledge = null);

public sealed record SemanticCapabilityCoverageRecord(string FamilyProfile, string Format, string BeatRole, string Capability, bool Required, bool CatalogRegistrationFound, IReadOnlyList<string> RegisteredAdapterIds, IReadOnlyList<string> ApprovedDerivationRuleIds, IReadOnlyList<string> ApprovedDomainProviderIds, bool ResolutionPathValid, string? FailureReason);
