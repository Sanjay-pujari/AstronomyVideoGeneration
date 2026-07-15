using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;

[JsonConverter(typeof(JsonStringEnumConverter<FamilyProfileCompatibilityMappingKind>))]
public enum FamilyProfileCompatibilityMappingKind
{
    EXACT_LEGACY_CAPABILITY,
    EXPANDED_TO_LEGACY_REQUIREMENTS,
    OPTIONAL_COMPATIBILITY_OMISSION,
    UNSUPPORTED_FOR_CURRENT_RUNTIME
}

public sealed record FamilyProfileCompatibilityMapping(
    string FamilyId,
    string V1CapabilityId,
    FamilyProfileCompatibilityMappingKind MappingKind,
    IReadOnlyList<string> LegacyRequirements,
    bool Required,
    string Diagnostic,
    string RequirementLevel,
    string MissingValueBehavior,
    IReadOnlyList<string> AllowedSources,
    int MinimumConfidence,
    bool EventSpecific,
    bool MayOmit,
    bool BlocksPhase7,
    IReadOnlyList<string> LongBeatRoles,
    IReadOnlyList<string> ShortBeatRoles);

public sealed record FamilyProfileCompatibilityDiagnostics(
    string InputEventType,
    string CanonicalEventType,
    string CanonicalFamilyId,
    string V1ProfileId,
    bool AliasApplied,
    string CompatibilityAdapterId,
    string GeneratedLegacyFamilyId,
    IReadOnlyList<string> GeneratedLegacyRequirements,
    IReadOnlyList<string> OmittedOptionalRequirements,
    IReadOnlyList<FamilyProfileCompatibilityMapping> UnsupportedMappings,
    IReadOnlyList<string> BlockingCompatibilityErrors,
    string ResolutionAuthority,
    IReadOnlyList<FamilyProfileCompatibilityMapping> Mappings,
    int? MinimumObjectCountPolicy,
    IReadOnlyList<string> SupportedFormats,
    IReadOnlyList<string> LongBeatRoles,
    IReadOnlyList<string> ShortBeatRoles);
