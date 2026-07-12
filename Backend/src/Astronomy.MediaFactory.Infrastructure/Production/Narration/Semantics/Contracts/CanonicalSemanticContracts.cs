using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

/// <summary>Identifies the verification status for a resolved semantic fact.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SemanticVerificationStatus>))]
public enum SemanticVerificationStatus
{
    /// <summary>The fact has not been verified.</summary>
    Unverified,
    /// <summary>The fact was verified against an approved source or derivation rule.</summary>
    Verified,
    /// <summary>The fact was derived from verified inputs.</summary>
    Derived,
    /// <summary>The fact is unavailable from approved sources.</summary>
    Missing
}

/// <summary>Identifies whether a semantic fact is required for a contract.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SemanticFactRequiredness>))]
public enum SemanticFactRequiredness
{
    /// <summary>The fact is mandatory for the target beat or profile.</summary>
    Required,
    /// <summary>The fact is optional but may improve narration quality.</summary>
    Optional
}

/// <summary>Identifies the resolution status for a semantic capability.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SemanticCapabilityResolutionStatus>))]
public enum SemanticCapabilityResolutionStatus
{
    /// <summary>A canonical capability value was selected.</summary>
    Resolved,
    /// <summary>No approved source produced a value.</summary>
    Missing,
    /// <summary>Sources produced values, but all were rejected.</summary>
    Rejected,
    /// <summary>A configured substitute was applied.</summary>
    Substituted
}

/// <summary>Identifies the strictness policy for a semantic capability.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SemanticCapabilityStrictness>))]
public enum SemanticCapabilityStrictness
{
    /// <summary>Only approved sources or rules may satisfy the capability.</summary>
    Strict,
    /// <summary>The capability is event-specific and optional when absent.</summary>
    OptionalEventSpecific,
    /// <summary>Approved substitutions may satisfy the capability.</summary>
    Substitutable
}

/// <summary>Defines an approved semantic capability and its permitted resolution paths.</summary>
public sealed record SemanticCapabilityDefinition
{
    /// <summary>Creates an immutable semantic capability definition.</summary>
    public SemanticCapabilityDefinition(string capabilityId, IReadOnlyList<string> acceptedAliases, int minimumStrength, SemanticCapabilityStrictness strictness, bool localizable, bool narratable, IReadOnlyList<string> approvedSourceAdapterIds, IReadOnlyList<string> approvedDerivationRuleIds, IReadOnlyList<string> approvedDomainKnowledgeFactTypes, bool eventSpecific = false)
        : this(capabilityId, SemanticContractValidation.CopyNonEmpty(acceptedAliases, nameof(acceptedAliases)), minimumStrength, strictness, localizable, narratable, SemanticContractValidation.Copy(approvedSourceAdapterIds, nameof(approvedSourceAdapterIds)), SemanticContractValidation.Copy(approvedDerivationRuleIds, nameof(approvedDerivationRuleIds)), SemanticContractValidation.Copy(approvedDomainKnowledgeFactTypes, nameof(approvedDomainKnowledgeFactTypes)), eventSpecific)
    {
    }

    /// <summary>Creates an immutable semantic capability definition from JSON-bound immutable collections.</summary>
    [JsonConstructor]
    public SemanticCapabilityDefinition(string capabilityId, ImmutableArray<string> acceptedAliases, int minimumStrength, SemanticCapabilityStrictness strictness, bool localizable, bool narratable, ImmutableArray<string> approvedSourceAdapterIds, ImmutableArray<string> approvedDerivationRuleIds, ImmutableArray<string> approvedDomainKnowledgeFactTypes, bool eventSpecific = false)
    {
        CapabilityId = SemanticContractValidation.RequireText(capabilityId, nameof(capabilityId));
        AcceptedAliases = SemanticContractValidation.RequireNonEmpty(acceptedAliases, nameof(acceptedAliases));
        MinimumStrength = minimumStrength >= 0 ? minimumStrength : throw new ArgumentOutOfRangeException(nameof(minimumStrength), "Minimum strength must be non-negative.");
        Strictness = strictness;
        Localizable = localizable;
        Narratable = narratable;
        ApprovedSourceAdapterIds = SemanticContractValidation.RequireInitialized(approvedSourceAdapterIds, nameof(approvedSourceAdapterIds));
        ApprovedDerivationRuleIds = SemanticContractValidation.RequireInitialized(approvedDerivationRuleIds, nameof(approvedDerivationRuleIds));
        ApprovedDomainKnowledgeFactTypes = SemanticContractValidation.RequireInitialized(approvedDomainKnowledgeFactTypes, nameof(approvedDomainKnowledgeFactTypes));
        EventSpecific = eventSpecific;
    }

    /// <summary>Canonical stable capability identifier.</summary>
    public string CapabilityId { get; init; }
    /// <summary>Accepted stable aliases for deserializing or matching equivalent facts.</summary>
    public ImmutableArray<string> AcceptedAliases { get; init; }
    /// <summary>Minimum source strength required before a candidate may satisfy the capability.</summary>
    public int MinimumStrength { get; init; }
    /// <summary>Strictness policy used by resolvers.</summary>
    public SemanticCapabilityStrictness Strictness { get; init; }
    /// <summary>Whether the selected value may be localized.</summary>
    public bool Localizable { get; init; }
    /// <summary>Whether the selected value is safe for narration.</summary>
    public bool Narratable { get; init; }
    /// <summary>Approved source adapter identifiers.</summary>
    public ImmutableArray<string> ApprovedSourceAdapterIds { get; init; }
    /// <summary>Approved deterministic derivation rule identifiers.</summary>
    public ImmutableArray<string> ApprovedDerivationRuleIds { get; init; }
    /// <summary>Approved domain knowledge fact types.</summary>
    public ImmutableArray<string> ApprovedDomainKnowledgeFactTypes { get; init; }
    /// <summary>Whether the capability applies only to matching event families.</summary>
    public bool EventSpecific { get; init; }

    /// <inheritdoc />
    public bool Equals(SemanticCapabilityDefinition? other) =>
        other is not null &&
        CapabilityId == other.CapabilityId &&
        AcceptedAliases.SequenceEqual(other.AcceptedAliases) &&
        MinimumStrength == other.MinimumStrength &&
        Strictness == other.Strictness &&
        Localizable == other.Localizable &&
        Narratable == other.Narratable &&
        ApprovedSourceAdapterIds.SequenceEqual(other.ApprovedSourceAdapterIds) &&
        ApprovedDerivationRuleIds.SequenceEqual(other.ApprovedDerivationRuleIds) &&
        ApprovedDomainKnowledgeFactTypes.SequenceEqual(other.ApprovedDomainKnowledgeFactTypes) &&
        EventSpecific == other.EventSpecific;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CapabilityId);
        SemanticContractValidation.AddRangeHash(ref hash, AcceptedAliases);
        hash.Add(MinimumStrength);
        hash.Add(Strictness);
        hash.Add(Localizable);
        hash.Add(Narratable);
        SemanticContractValidation.AddRangeHash(ref hash, ApprovedSourceAdapterIds);
        SemanticContractValidation.AddRangeHash(ref hash, ApprovedDerivationRuleIds);
        SemanticContractValidation.AddRangeHash(ref hash, ApprovedDomainKnowledgeFactTypes);
        hash.Add(EventSpecific);
        return hash.ToHashCode();
    }
}

/// <summary>Represents a candidate value extracted for a semantic capability.</summary>
public sealed record SemanticCapabilityCandidate
{
    /// <summary>Creates an immutable semantic capability candidate.</summary>
    [JsonConstructor]
    public SemanticCapabilityCandidate(string source, string sourceField, object value, string strength)
    {
        Source = SemanticContractValidation.RequireText(source, nameof(source));
        SourceField = SemanticContractValidation.RequireText(sourceField, nameof(sourceField));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Strength = SemanticContractValidation.RequireText(strength, nameof(strength));
    }
    /// <summary>Approved source identifier.</summary>
    public string Source { get; init; }
    /// <summary>Source field or path that produced the value.</summary>
    public string SourceField { get; init; }
    /// <summary>Extracted candidate value.</summary>
    public object Value { get; init; }
    /// <summary>Candidate strength classification.</summary>
    public string Strength { get; init; }

    /// <inheritdoc />
    public bool Equals(SemanticCapabilityCandidate? other) =>
        other is not null &&
        Source == other.Source &&
        SourceField == other.SourceField &&
        SemanticContractValidation.ObjectEquals(Value, other.Value) &&
        Strength == other.Strength;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Source);
        hash.Add(SourceField);
        hash.Add(SemanticContractValidation.ObjectHash(Value));
        hash.Add(Strength);
        return hash.ToHashCode();
    }
}

/// <summary>Represents a rejected semantic capability source.</summary>
public sealed record SemanticCapabilityRejection
{
    /// <summary>Creates an immutable semantic capability rejection.</summary>
    [JsonConstructor]
    public SemanticCapabilityRejection(string source, string sourceField, string reason)
    {
        Source = SemanticContractValidation.RequireText(source, nameof(source));
        SourceField = SemanticContractValidation.RequireText(sourceField, nameof(sourceField));
        Reason = SemanticContractValidation.RequireText(reason, nameof(reason));
    }
    /// <summary>Rejected source identifier.</summary>
    public string Source { get; init; }
    /// <summary>Rejected source field or path.</summary>
    public string SourceField { get; init; }
    /// <summary>Reason the source was rejected.</summary>
    public string Reason { get; init; }
}

/// <summary>Represents an immutable semantic capability resolution result.</summary>
public sealed record SemanticCapabilityResolution
{
    /// <summary>Creates an immutable semantic capability resolution result.</summary>
    public SemanticCapabilityResolution(string capability, SemanticCapabilityResolutionStatus status, string? selectedSource, object? canonicalValue, string? speakableValue, IReadOnlyList<string> alternativesConsidered, IReadOnlyList<string> warnings, string capabilityStrength, IReadOnlyList<SemanticCapabilityCandidate> candidates, IReadOnlyList<SemanticCapabilityRejection> rejectedSources, IReadOnlyList<string> substitutionsApplied)
        : this(capability, status, selectedSource, canonicalValue, speakableValue, SemanticContractValidation.Copy(alternativesConsidered, nameof(alternativesConsidered)), SemanticContractValidation.Copy(warnings, nameof(warnings)), capabilityStrength, SemanticContractValidation.Copy(candidates, nameof(candidates)), SemanticContractValidation.Copy(rejectedSources, nameof(rejectedSources)), SemanticContractValidation.Copy(substitutionsApplied, nameof(substitutionsApplied)))
    {
    }

    /// <summary>Creates an immutable semantic capability resolution result from JSON-bound immutable collections.</summary>
    [JsonConstructor]
    public SemanticCapabilityResolution(string capability, SemanticCapabilityResolutionStatus status, string? selectedSource, object? canonicalValue, string? speakableValue, ImmutableArray<string> alternativesConsidered, ImmutableArray<string> warnings, string capabilityStrength, ImmutableArray<SemanticCapabilityCandidate> candidates, ImmutableArray<SemanticCapabilityRejection> rejectedSources, ImmutableArray<string> substitutionsApplied)
    {
        Capability = SemanticContractValidation.RequireText(capability, nameof(capability));
        Status = status;
        SelectedSource = selectedSource;
        CanonicalValue = canonicalValue;
        SpeakableValue = speakableValue;
        AlternativesConsidered = SemanticContractValidation.RequireInitialized(alternativesConsidered, nameof(alternativesConsidered));
        Warnings = SemanticContractValidation.RequireInitialized(warnings, nameof(warnings));
        CapabilityStrength = SemanticContractValidation.RequireText(capabilityStrength, nameof(capabilityStrength));
        Candidates = SemanticContractValidation.RequireInitialized(candidates, nameof(candidates));
        RejectedSources = SemanticContractValidation.RequireInitialized(rejectedSources, nameof(rejectedSources));
        SubstitutionsApplied = SemanticContractValidation.RequireInitialized(substitutionsApplied, nameof(substitutionsApplied));
    }
    /// <summary>Canonical capability identifier.</summary>
    public string Capability { get; init; }
    /// <summary>Resolution status.</summary>
    public SemanticCapabilityResolutionStatus Status { get; init; }
    /// <summary>Selected source identifier, when a source was selected.</summary>
    public string? SelectedSource { get; init; }
    /// <summary>Canonical resolved value, when available.</summary>
    public object? CanonicalValue { get; init; }
    /// <summary>Speakable resolved value, when available.</summary>
    public string? SpeakableValue { get; init; }
    /// <summary>Alternative values considered during resolution.</summary>
    public ImmutableArray<string> AlternativesConsidered { get; init; }
    /// <summary>Non-blocking warnings produced during resolution.</summary>
    public ImmutableArray<string> Warnings { get; init; }
    /// <summary>Selected capability strength.</summary>
    public string CapabilityStrength { get; init; }
    /// <summary>All accepted candidates considered by the resolver.</summary>
    public ImmutableArray<SemanticCapabilityCandidate> Candidates { get; init; }
    /// <summary>Rejected sources considered by the resolver.</summary>
    public ImmutableArray<SemanticCapabilityRejection> RejectedSources { get; init; }
    /// <summary>Substitutions applied during resolution.</summary>
    public ImmutableArray<string> SubstitutionsApplied { get; init; }

    /// <inheritdoc />
    public bool Equals(SemanticCapabilityResolution? other) =>
        other is not null &&
        Capability == other.Capability &&
        Status == other.Status &&
        SelectedSource == other.SelectedSource &&
        SemanticContractValidation.ObjectEquals(CanonicalValue, other.CanonicalValue) &&
        SpeakableValue == other.SpeakableValue &&
        AlternativesConsidered.SequenceEqual(other.AlternativesConsidered) &&
        Warnings.SequenceEqual(other.Warnings) &&
        CapabilityStrength == other.CapabilityStrength &&
        Candidates.SequenceEqual(other.Candidates) &&
        RejectedSources.SequenceEqual(other.RejectedSources) &&
        SubstitutionsApplied.SequenceEqual(other.SubstitutionsApplied);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Capability);
        hash.Add(Status);
        hash.Add(SelectedSource);
        hash.Add(SemanticContractValidation.ObjectHash(CanonicalValue));
        hash.Add(SpeakableValue);
        SemanticContractValidation.AddRangeHash(ref hash, AlternativesConsidered);
        SemanticContractValidation.AddRangeHash(ref hash, Warnings);
        hash.Add(CapabilityStrength);
        SemanticContractValidation.AddRangeHash(ref hash, Candidates);
        SemanticContractValidation.AddRangeHash(ref hash, RejectedSources);
        SemanticContractValidation.AddRangeHash(ref hash, SubstitutionsApplied);
        return hash.ToHashCode();
    }
}

/// <summary>Represents a resolved canonical semantic fact for narration.</summary>
public sealed record ResolvedSemanticFact
{
    /// <summary>Creates an immutable resolved semantic fact.</summary>
    public ResolvedSemanticFact(string factType, string factKey, object canonicalValue, string? unit, string semanticMeaning, string sourceArtifact, string sourceField, string? sourceBeatId, SemanticVerificationStatus verificationStatus, decimal confidence, SemanticFactRequiredness requiredness, string? localizedDisplayValue, string? speakableValue, string language, bool safeForNarration, string factOrigin = "Source", string? derivationRuleId = null, IReadOnlyList<string>? sourceInputs = null)
        : this(factType, factKey, canonicalValue, unit, semanticMeaning, sourceArtifact, sourceField, sourceBeatId, verificationStatus, confidence, requiredness, localizedDisplayValue, speakableValue, language, safeForNarration, factOrigin, derivationRuleId, sourceInputs is null ? null : SemanticContractValidation.Copy(sourceInputs, nameof(sourceInputs)))
    {
    }

    /// <summary>Creates an immutable resolved semantic fact from JSON-bound immutable collections.</summary>
    [JsonConstructor]
    public ResolvedSemanticFact(string factType, string factKey, object canonicalValue, string? unit, string semanticMeaning, string sourceArtifact, string sourceField, string? sourceBeatId, SemanticVerificationStatus verificationStatus, decimal confidence, SemanticFactRequiredness requiredness, string? localizedDisplayValue, string? speakableValue, string language, bool safeForNarration, string factOrigin = "Source", string? derivationRuleId = null, ImmutableArray<string>? sourceInputs = null)
    {
        FactType = SemanticContractValidation.RequireText(factType, nameof(factType));
        FactKey = SemanticContractValidation.RequireText(factKey, nameof(factKey));
        CanonicalValue = canonicalValue ?? throw new ArgumentNullException(nameof(canonicalValue));
        Unit = unit;
        SemanticMeaning = SemanticContractValidation.RequireText(semanticMeaning, nameof(semanticMeaning));
        SourceArtifact = SemanticContractValidation.RequireText(sourceArtifact, nameof(sourceArtifact));
        SourceField = SemanticContractValidation.RequireText(sourceField, nameof(sourceField));
        SourceBeatId = sourceBeatId;
        VerificationStatus = verificationStatus;
        Confidence = confidence is >= 0m and <= 1m ? confidence : throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
        Requiredness = requiredness;
        LocalizedDisplayValue = localizedDisplayValue;
        SpeakableValue = speakableValue;
        Language = SemanticContractValidation.RequireText(language, nameof(language));
        SafeForNarration = safeForNarration;
        FactOrigin = SemanticContractValidation.RequireText(factOrigin, nameof(factOrigin));
        DerivationRuleId = derivationRuleId;
        SourceInputs = sourceInputs is null ? null : SemanticContractValidation.RequireInitialized(sourceInputs.Value, nameof(sourceInputs));
    }
    /// <summary>Canonical fact type.</summary>
    public string FactType { get; init; }
    /// <summary>Stable fact key.</summary>
    public string FactKey { get; init; }
    /// <summary>Canonical fact value.</summary>
    public object CanonicalValue { get; init; }
    /// <summary>Unit associated with the canonical value, when applicable.</summary>
    public string? Unit { get; init; }
    /// <summary>Semantic meaning conveyed by the fact.</summary>
    public string SemanticMeaning { get; init; }
    /// <summary>Source artifact name.</summary>
    public string SourceArtifact { get; init; }
    /// <summary>Source field or path.</summary>
    public string SourceField { get; init; }
    /// <summary>Source beat identifier, when applicable.</summary>
    public string? SourceBeatId { get; init; }
    /// <summary>Verification status.</summary>
    public SemanticVerificationStatus VerificationStatus { get; init; }
    /// <summary>Confidence from zero through one.</summary>
    public decimal Confidence { get; init; }
    /// <summary>Requiredness classification.</summary>
    public SemanticFactRequiredness Requiredness { get; init; }
    /// <summary>Localized display value, when available.</summary>
    public string? LocalizedDisplayValue { get; init; }
    /// <summary>Speakable value, when available.</summary>
    public string? SpeakableValue { get; init; }
    /// <summary>Language code for localized values.</summary>
    public string Language { get; init; }
    /// <summary>Whether the fact may be used in narration.</summary>
    public bool SafeForNarration { get; init; }
    /// <summary>Fact origin classification.</summary>
    public string FactOrigin { get; init; }
    /// <summary>Derivation rule identifier, when the fact was derived.</summary>
    public string? DerivationRuleId { get; init; }
    /// <summary>Source input identifiers used for derivation.</summary>
    public ImmutableArray<string>? SourceInputs { get; init; }

    /// <inheritdoc />
    public bool Equals(ResolvedSemanticFact? other) =>
        other is not null &&
        FactType == other.FactType &&
        FactKey == other.FactKey &&
        SemanticContractValidation.ObjectEquals(CanonicalValue, other.CanonicalValue) &&
        Unit == other.Unit &&
        SemanticMeaning == other.SemanticMeaning &&
        SourceArtifact == other.SourceArtifact &&
        SourceField == other.SourceField &&
        SourceBeatId == other.SourceBeatId &&
        VerificationStatus == other.VerificationStatus &&
        Confidence == other.Confidence &&
        Requiredness == other.Requiredness &&
        LocalizedDisplayValue == other.LocalizedDisplayValue &&
        SpeakableValue == other.SpeakableValue &&
        Language == other.Language &&
        SafeForNarration == other.SafeForNarration &&
        FactOrigin == other.FactOrigin &&
        DerivationRuleId == other.DerivationRuleId &&
        SemanticContractValidation.NullableSequenceEqual(SourceInputs, other.SourceInputs);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FactType);
        hash.Add(FactKey);
        hash.Add(SemanticContractValidation.ObjectHash(CanonicalValue));
        hash.Add(Unit);
        hash.Add(SemanticMeaning);
        hash.Add(SourceArtifact);
        hash.Add(SourceField);
        hash.Add(SourceBeatId);
        hash.Add(VerificationStatus);
        hash.Add(Confidence);
        hash.Add(Requiredness);
        hash.Add(LocalizedDisplayValue);
        hash.Add(SpeakableValue);
        hash.Add(Language);
        hash.Add(SafeForNarration);
        hash.Add(FactOrigin);
        hash.Add(DerivationRuleId);
        SemanticContractValidation.AddNullableRangeHash(ref hash, SourceInputs);
        return hash.ToHashCode();
    }
}

/// <summary>Represents semantic facts resolved for one narration beat.</summary>
public sealed record ResolvedBeatFacts
{
    /// <summary>Creates immutable resolved beat facts.</summary>
    public ResolvedBeatFacts(string beatId, string beatRole, IReadOnlyList<ResolvedSemanticFact> facts)
        : this(beatId, beatRole, SemanticContractValidation.Copy(facts, nameof(facts)))
    {
    }

    /// <summary>Creates immutable resolved beat facts from a JSON-bound immutable collection.</summary>
    [JsonConstructor]
    public ResolvedBeatFacts(string beatId, string beatRole, ImmutableArray<ResolvedSemanticFact> facts)
    {
        BeatId = SemanticContractValidation.RequireText(beatId, nameof(beatId));
        BeatRole = SemanticContractValidation.RequireText(beatRole, nameof(beatRole));
        Facts = SemanticContractValidation.RequireInitialized(facts, nameof(facts));
    }
    /// <summary>Stable beat identifier.</summary>
    public string BeatId { get; init; }
    /// <summary>Semantic beat role.</summary>
    public string BeatRole { get; init; }
    /// <summary>Resolved facts for the beat.</summary>
    public ImmutableArray<ResolvedSemanticFact> Facts { get; init; }

    /// <inheritdoc />
    public bool Equals(ResolvedBeatFacts? other) =>
        other is not null &&
        BeatId == other.BeatId &&
        BeatRole == other.BeatRole &&
        Facts.SequenceEqual(other.Facts);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BeatId);
        hash.Add(BeatRole);
        SemanticContractValidation.AddRangeHash(ref hash, Facts);
        return hash.ToHashCode();
    }
}

/// <summary>Represents the full required semantic fact resolution output.</summary>
public sealed record RequiredSemanticFactResolutionResult
{
    /// <summary>Creates an immutable required semantic fact resolution result.</summary>
    public RequiredSemanticFactResolutionResult(IReadOnlyList<ResolvedBeatFacts> beats, SemanticResolutionDiagnostics diagnostics)
        : this(SemanticContractValidation.Copy(beats, nameof(beats)), diagnostics)
    {
    }

    /// <summary>Creates an immutable required semantic fact resolution result from a JSON-bound immutable collection.</summary>
    [JsonConstructor]
    public RequiredSemanticFactResolutionResult(ImmutableArray<ResolvedBeatFacts> beats, SemanticResolutionDiagnostics diagnostics)
    {
        Beats = SemanticContractValidation.RequireInitialized(beats, nameof(beats));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }
    /// <summary>Resolved beat facts.</summary>
    public ImmutableArray<ResolvedBeatFacts> Beats { get; init; }
    /// <summary>Serialization-safe diagnostics payload.</summary>
    public SemanticResolutionDiagnostics Diagnostics { get; init; }

    /// <inheritdoc />
    public bool Equals(RequiredSemanticFactResolutionResult? other) =>
        other is not null &&
        Beats.SequenceEqual(other.Beats) &&
        Diagnostics == other.Diagnostics;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        SemanticContractValidation.AddRangeHash(ref hash, Beats);
        hash.Add(Diagnostics);
        return hash.ToHashCode();
    }
}


/// <summary>Represents stable diagnostics for semantic resolution output.</summary>
public sealed record SemanticResolutionDiagnostics
{
    /// <summary>Creates immutable semantic resolution diagnostics.</summary>
    public SemanticResolutionDiagnostics(int warningCount, int missingRequiredCount, IReadOnlyList<string> warnings)
        : this(warningCount, missingRequiredCount, SemanticContractValidation.Copy(warnings, nameof(warnings)))
    {
    }

    /// <summary>Creates immutable semantic resolution diagnostics from a JSON-bound immutable collection.</summary>
    [JsonConstructor]
    public SemanticResolutionDiagnostics(int warningCount, int missingRequiredCount, ImmutableArray<string> warnings)
    {
        WarningCount = warningCount >= 0 ? warningCount : throw new ArgumentOutOfRangeException(nameof(warningCount), "Warning count must be non-negative.");
        MissingRequiredCount = missingRequiredCount >= 0 ? missingRequiredCount : throw new ArgumentOutOfRangeException(nameof(missingRequiredCount), "Missing required count must be non-negative.");
        Warnings = SemanticContractValidation.RequireInitialized(warnings, nameof(warnings));
    }

    /// <summary>Number of warnings produced during resolution.</summary>
    public int WarningCount { get; init; }
    /// <summary>Number of required facts that could not be resolved.</summary>
    public int MissingRequiredCount { get; init; }
    /// <summary>Diagnostics warning messages.</summary>
    public ImmutableArray<string> Warnings { get; init; }

    /// <inheritdoc />
    public bool Equals(SemanticResolutionDiagnostics? other) =>
        other is not null &&
        WarningCount == other.WarningCount &&
        MissingRequiredCount == other.MissingRequiredCount &&
        Warnings.SequenceEqual(other.Warnings);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(WarningCount);
        hash.Add(MissingRequiredCount);
        SemanticContractValidation.AddRangeHash(ref hash, Warnings);
        return hash.ToHashCode();
    }
}

/// <summary>Represents coverage validation for one family, format, beat role, and capability.</summary>
public sealed record SemanticCapabilityCoverageRecord
{
    /// <summary>Creates an immutable semantic capability coverage record.</summary>
    public SemanticCapabilityCoverageRecord(string familyProfile, string format, string beatRole, string capability, bool required, bool catalogRegistrationFound, IReadOnlyList<string> registeredAdapterIds, IReadOnlyList<string> approvedDerivationRuleIds, IReadOnlyList<string> approvedDomainProviderIds, bool resolutionPathValid, string? failureReason)
        : this(familyProfile, format, beatRole, capability, required, catalogRegistrationFound, SemanticContractValidation.Copy(registeredAdapterIds, nameof(registeredAdapterIds)), SemanticContractValidation.Copy(approvedDerivationRuleIds, nameof(approvedDerivationRuleIds)), SemanticContractValidation.Copy(approvedDomainProviderIds, nameof(approvedDomainProviderIds)), resolutionPathValid, failureReason)
    {
    }

    /// <summary>Creates an immutable semantic capability coverage record from JSON-bound immutable collections.</summary>
    [JsonConstructor]
    public SemanticCapabilityCoverageRecord(string familyProfile, string format, string beatRole, string capability, bool required, bool catalogRegistrationFound, ImmutableArray<string> registeredAdapterIds, ImmutableArray<string> approvedDerivationRuleIds, ImmutableArray<string> approvedDomainProviderIds, bool resolutionPathValid, string? failureReason)
    {
        FamilyProfile = SemanticContractValidation.RequireText(familyProfile, nameof(familyProfile));
        Format = SemanticContractValidation.RequireText(format, nameof(format));
        BeatRole = SemanticContractValidation.RequireText(beatRole, nameof(beatRole));
        Capability = SemanticContractValidation.RequireText(capability, nameof(capability));
        Required = required;
        CatalogRegistrationFound = catalogRegistrationFound;
        RegisteredAdapterIds = SemanticContractValidation.RequireInitialized(registeredAdapterIds, nameof(registeredAdapterIds));
        ApprovedDerivationRuleIds = SemanticContractValidation.RequireInitialized(approvedDerivationRuleIds, nameof(approvedDerivationRuleIds));
        ApprovedDomainProviderIds = SemanticContractValidation.RequireInitialized(approvedDomainProviderIds, nameof(approvedDomainProviderIds));
        ResolutionPathValid = resolutionPathValid;
        FailureReason = failureReason;
    }
    /// <summary>Family profile identifier.</summary>
    public string FamilyProfile { get; init; }
    /// <summary>Output format.</summary>
    public string Format { get; init; }
    /// <summary>Beat role.</summary>
    public string BeatRole { get; init; }
    /// <summary>Capability identifier.</summary>
    public string Capability { get; init; }
    /// <summary>Whether the capability is required.</summary>
    public bool Required { get; init; }
    /// <summary>Whether catalog registration exists.</summary>
    public bool CatalogRegistrationFound { get; init; }
    /// <summary>Registered adapter identifiers.</summary>
    public ImmutableArray<string> RegisteredAdapterIds { get; init; }
    /// <summary>Approved derivation rule identifiers.</summary>
    public ImmutableArray<string> ApprovedDerivationRuleIds { get; init; }
    /// <summary>Approved domain provider identifiers.</summary>
    public ImmutableArray<string> ApprovedDomainProviderIds { get; init; }
    /// <summary>Whether a resolution path exists.</summary>
    public bool ResolutionPathValid { get; init; }
    /// <summary>Failure reason, when invalid.</summary>
    public string? FailureReason { get; init; }

    /// <inheritdoc />
    public bool Equals(SemanticCapabilityCoverageRecord? other) =>
        other is not null &&
        FamilyProfile == other.FamilyProfile &&
        Format == other.Format &&
        BeatRole == other.BeatRole &&
        Capability == other.Capability &&
        Required == other.Required &&
        CatalogRegistrationFound == other.CatalogRegistrationFound &&
        RegisteredAdapterIds.SequenceEqual(other.RegisteredAdapterIds) &&
        ApprovedDerivationRuleIds.SequenceEqual(other.ApprovedDerivationRuleIds) &&
        ApprovedDomainProviderIds.SequenceEqual(other.ApprovedDomainProviderIds) &&
        ResolutionPathValid == other.ResolutionPathValid &&
        FailureReason == other.FailureReason;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FamilyProfile);
        hash.Add(Format);
        hash.Add(BeatRole);
        hash.Add(Capability);
        hash.Add(Required);
        hash.Add(CatalogRegistrationFound);
        SemanticContractValidation.AddRangeHash(ref hash, RegisteredAdapterIds);
        SemanticContractValidation.AddRangeHash(ref hash, ApprovedDerivationRuleIds);
        SemanticContractValidation.AddRangeHash(ref hash, ApprovedDomainProviderIds);
        hash.Add(ResolutionPathValid);
        hash.Add(FailureReason);
        return hash.ToHashCode();
    }
}

/// <summary>Validation helpers shared by immutable semantic contracts.</summary>
internal static class SemanticContractValidation
{
    /// <summary>Requires a non-empty text value.</summary>
    internal static string RequireText(string? value, string parameterName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value must not be null, empty, or whitespace.", parameterName) : value;
    /// <summary>Copies a required list into an immutable array snapshot.</summary>
    internal static ImmutableArray<T> Copy<T>(IReadOnlyList<T>? values, string parameterName) => values is null ? throw new ArgumentNullException(parameterName) : [.. values];
    /// <summary>Requires an initialized immutable array.</summary>
    internal static ImmutableArray<T> RequireInitialized<T>(ImmutableArray<T> values, string parameterName) => values.IsDefault ? throw new ArgumentNullException(parameterName) : values;
    /// <summary>Requires an initialized, non-empty string immutable array.</summary>
    internal static ImmutableArray<string> RequireNonEmpty(ImmutableArray<string> values, string parameterName)
    {
        var initialized = RequireInitialized(values, parameterName);
        if (initialized.Length == 0) throw new ArgumentException("At least one value is required.", parameterName);
        if (initialized.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Values must not contain null, empty, or whitespace entries.", parameterName);
        return initialized;
    }

    /// <summary>Copies a required non-empty string list into an immutable array snapshot.</summary>
    internal static ImmutableArray<string> CopyNonEmpty(IReadOnlyList<string>? values, string parameterName)
    {
        var copy = Copy(values, parameterName);
        if (copy.Length == 0) throw new ArgumentException("At least one value is required.", parameterName);
        if (copy.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Values must not contain null, empty, or whitespace entries.", parameterName);
        return copy;
    }

    internal static bool NullableSequenceEqual<T>(ImmutableArray<T>? left, ImmutableArray<T>? right) =>
        left.HasValue == right.HasValue && (!left.HasValue || left.Value.SequenceEqual(right!.Value));

    internal static void AddRangeHash<T>(ref HashCode hash, ImmutableArray<T> values)
    {
        hash.Add(values.Length);
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }

    internal static void AddNullableRangeHash<T>(ref HashCode hash, ImmutableArray<T>? values)
    {
        hash.Add(values.HasValue);
        if (values.HasValue)
        {
            AddRangeHash(ref hash, values.Value);
        }
    }

    internal static bool ObjectEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return NormalizeJsonComparable(left).Equals(NormalizeJsonComparable(right), StringComparison.Ordinal);
    }

    internal static int ObjectHash(object? value) => value is null ? 0 : StringComparer.Ordinal.GetHashCode(NormalizeJsonComparable(value));

    private static string NormalizeJsonComparable(object value) =>
        value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value, value.GetType());
}
