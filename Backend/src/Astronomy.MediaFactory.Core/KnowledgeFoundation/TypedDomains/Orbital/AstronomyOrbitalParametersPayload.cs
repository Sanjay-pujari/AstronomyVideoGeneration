using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

public sealed record AstronomyOrbitalParametersPayload : ITypedAstronomyKnowledgePayload, IEquatable<AstronomyOrbitalParametersPayload>
{
    public AstronomyOrbitalParametersPayload(AstronomyKnowledgeTypeId typeId, AstronomyOrbitalReferenceContext referenceContext, IEnumerable<AstronomyOrbitalParameter> parameters)
    {
        if (!typeId.IsValid) throw new ArgumentException("Orbital parameters payload type ID is required.", nameof(typeId));
        TypeId = typeId;
        ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
        Parameters = CopyParameters(parameters);
    }

    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Orbital;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.OrbitalParameter;
    public AstronomyKnowledgeTypeId TypeId { get; }
    public AstronomyOrbitalReferenceContext ReferenceContext { get; }
    public IReadOnlyList<AstronomyOrbitalParameter> Parameters { get; }

    public bool Equals(AstronomyOrbitalParametersPayload? other) => other is not null && TypeId == other.TypeId && ReferenceContext == other.ReferenceContext && Parameters.SequenceEqual(other.Parameters);
    public override int GetHashCode() => Parameters.Aggregate(HashCode.Combine(TypeId, ReferenceContext), HashCode.Combine);

    private static IReadOnlyList<AstronomyOrbitalParameter> CopyParameters(IEnumerable<AstronomyOrbitalParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var ordered = parameters.Select(parameter => parameter ?? throw new ArgumentException("Orbital parameters cannot contain null entries.", nameof(parameters))).OrderBy(parameter => parameter.ParameterId.Value, StringComparer.Ordinal).ThenBy(parameter => parameter.Qualifier).ThenBy(parameter => parameter.Epoch?.Kind).ThenBy(parameter => parameter.Epoch?.InstantUtc).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one orbital parameter is required.", nameof(parameters));
        if (ordered.GroupBy(parameter => new { parameter.ParameterId, parameter.Qualifier, parameter.Epoch }).Any(group => group.Count() > 1)) throw new ArgumentException("Orbital parameters must be unique by parameter ID, qualifier and epoch.", nameof(parameters));
        return Array.AsReadOnly(ordered);
    }
}
