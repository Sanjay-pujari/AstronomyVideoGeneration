using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

[JsonConverter(typeof(JsonStringEnumConverter<FamilyRequirementLevelV1>))]
public enum FamilyRequirementLevelV1 { Required, Optional, FutureUnavailable }
[JsonConverter(typeof(JsonStringEnumConverter<FamilyMissingValueBehaviorV1>))]
public enum FamilyMissingValueBehaviorV1 { Block, OmitBeat, OmitCapability, UseEditorialFallback, FutureUnavailable }

public sealed record FamilySemanticRequirementV1
{
    public FamilySemanticRequirementV1(SemanticCapabilityId semanticCapabilityId, FamilyRequirementLevelV1 requirementLevel, FamilyMissingValueBehaviorV1 missingValueBehavior, IReadOnlyList<string> allowedEvidenceCategories, int minimumEvidenceStrength, bool mayOmit, bool blocksPhase7)
        : this(semanticCapabilityId, requirementLevel, missingValueBehavior, allowedEvidenceCategories.ToImmutableArray(), minimumEvidenceStrength, mayOmit, blocksPhase7) { }
    [JsonConstructor]
    public FamilySemanticRequirementV1(SemanticCapabilityId semanticCapabilityId, FamilyRequirementLevelV1 requirementLevel, FamilyMissingValueBehaviorV1 missingValueBehavior, ImmutableArray<string> allowedEvidenceCategories, int minimumEvidenceStrength, bool mayOmit, bool blocksPhase7)
    {
        SemanticCapabilityId = semanticCapabilityId;
        RequirementLevel = requirementLevel;
        MissingValueBehavior = missingValueBehavior;
        AllowedEvidenceCategories = allowedEvidenceCategories.IsDefault ? [] : allowedEvidenceCategories;
        MinimumEvidenceStrength = minimumEvidenceStrength;
        MayOmit = mayOmit;
        BlocksPhase7 = blocksPhase7;
    }
    public SemanticCapabilityId SemanticCapabilityId { get; init; }
    public FamilyRequirementLevelV1 RequirementLevel { get; init; }
    public FamilyMissingValueBehaviorV1 MissingValueBehavior { get; init; }
    public ImmutableArray<string> AllowedEvidenceCategories { get; init; }
    public int MinimumEvidenceStrength { get; init; }
    public bool MayOmit { get; init; }
    public bool BlocksPhase7 { get; init; }
    public bool Equals(FamilySemanticRequirementV1? other) => other is not null && SemanticCapabilityId.Equals(other.SemanticCapabilityId) && RequirementLevel == other.RequirementLevel && MissingValueBehavior == other.MissingValueBehavior && AllowedEvidenceCategories.SequenceEqual(other.AllowedEvidenceCategories) && MinimumEvidenceStrength == other.MinimumEvidenceStrength && MayOmit == other.MayOmit && BlocksPhase7 == other.BlocksPhase7;
    public override int GetHashCode() => AllowedEvidenceCategories.Aggregate(HashCode.Combine(SemanticCapabilityId, RequirementLevel, MissingValueBehavior, MinimumEvidenceStrength, MayOmit, BlocksPhase7), (h, x) => HashCode.Combine(h, x));
}
