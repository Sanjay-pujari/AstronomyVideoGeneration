using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public sealed record AstronomyFamilyProfileV1
{
    public AstronomyFamilyProfileV1(string familyId, string profileVersion, string contentNature, IReadOnlyList<string> supportedEventTypes, IReadOnlyList<string> supportedFormats, FamilyNarrativeStructureV1 longFormStructure, FamilyNarrativeStructureV1 shortFormStructure, FamilyPolicyV1 policy, IReadOnlyList<string> aliases, bool activeInV1, FamilyProfileDiagnosticsMetadataV1? diagnosticsMetadata = null)
        : this(familyId, profileVersion, contentNature, supportedEventTypes.ToImmutableArray(), supportedFormats.ToImmutableArray(), longFormStructure, shortFormStructure, policy, aliases.ToImmutableArray(), activeInV1, diagnosticsMetadata) { }
    [JsonConstructor]
    public AstronomyFamilyProfileV1(string familyId, string profileVersion, string contentNature, ImmutableArray<string> supportedEventTypes, ImmutableArray<string> supportedFormats, FamilyNarrativeStructureV1 longFormStructure, FamilyNarrativeStructureV1 shortFormStructure, FamilyPolicyV1 policy, ImmutableArray<string> aliases, bool activeInV1, FamilyProfileDiagnosticsMetadataV1? diagnosticsMetadata = null)
    { FamilyId = familyId; ProfileVersion = profileVersion; ContentNature = contentNature; SupportedEventTypes = supportedEventTypes.IsDefault ? [] : supportedEventTypes; SupportedFormats = supportedFormats.IsDefault ? [] : supportedFormats; LongFormStructure = longFormStructure; ShortFormStructure = shortFormStructure; Policy = policy; Aliases = aliases.IsDefault ? [] : aliases; ActiveInV1 = activeInV1; DiagnosticsMetadata = diagnosticsMetadata; }
    public string FamilyId { get; init; }
    public string ProfileVersion { get; init; }
    public string ContentNature { get; init; }
    public ImmutableArray<string> SupportedEventTypes { get; init; }
    public ImmutableArray<string> SupportedFormats { get; init; }
    public FamilyNarrativeStructureV1 LongFormStructure { get; init; }
    public FamilyNarrativeStructureV1 ShortFormStructure { get; init; }
    public FamilyPolicyV1 Policy { get; init; }
    public ImmutableArray<string> Aliases { get; init; }
    public bool ActiveInV1 { get; init; }
    public FamilyProfileDiagnosticsMetadataV1? DiagnosticsMetadata { get; init; }
    public bool Equals(AstronomyFamilyProfileV1? other) => other is not null && FamilyId == other.FamilyId && ProfileVersion == other.ProfileVersion && ContentNature == other.ContentNature && SupportedEventTypes.SequenceEqual(other.SupportedEventTypes) && SupportedFormats.SequenceEqual(other.SupportedFormats) && Equals(LongFormStructure, other.LongFormStructure) && Equals(ShortFormStructure, other.ShortFormStructure) && Equals(Policy, other.Policy) && Aliases.SequenceEqual(other.Aliases) && ActiveInV1 == other.ActiveInV1 && Equals(DiagnosticsMetadata, other.DiagnosticsMetadata);
    public override int GetHashCode() { var h = HashCode.Combine(FamilyId, ProfileVersion, ContentNature, LongFormStructure, ShortFormStructure, Policy, ActiveInV1, DiagnosticsMetadata); foreach (var x in SupportedEventTypes) h = HashCode.Combine(h, x); foreach (var x in SupportedFormats) h = HashCode.Combine(h, x); foreach (var x in Aliases) h = HashCode.Combine(h, x); return h; }
}
public sealed record FamilyProfileDiagnosticsMetadataV1(string SourceSprint, string CertificationState);
