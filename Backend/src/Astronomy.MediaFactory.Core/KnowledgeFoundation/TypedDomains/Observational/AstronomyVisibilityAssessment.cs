using System.Text.Json.Serialization;

using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public sealed record AstronomyVisibilityAssessment : IEquatable<AstronomyVisibilityAssessment>
{
    private const int MaxSummaryLength = 512;
    [JsonConstructor]
    public AstronomyVisibilityAssessment(AstronomyVisibilityStatus status, AstronomyVisibilityMethod method, IReadOnlyList<AstronomyVisibilityLimitation>? limitations = null, string? summary = null)
    {
        Status = EnumGuard.RequireDefined(status, nameof(status));
        Method = EnumGuard.RequireDefined(method, nameof(method));
        Limitations = CopyLimitations(limitations ?? []);
        if (summary is not null && string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Visibility assessment summary must not be blank when supplied.", nameof(summary));
        }
        Summary = TypedKnowledgeTextGuards.NormalizeOptionalText(summary, MaxSummaryLength, nameof(summary), "Visibility assessment summary");
    }
    public AstronomyVisibilityStatus Status { get; }
    public AstronomyVisibilityMethod Method { get; }
    public IReadOnlyList<AstronomyVisibilityLimitation> Limitations { get; }
    public string? Summary { get; }
    public bool Equals(AstronomyVisibilityAssessment? other) => other is not null && Status == other.Status && Method == other.Method && Limitations.SequenceEqual(other.Limitations) && Summary == other.Summary;
    public override int GetHashCode() => Limitations.Aggregate(HashCode.Combine(Status, Method, Summary), HashCode.Combine);
    private static IReadOnlyList<AstronomyVisibilityLimitation> CopyLimitations(IEnumerable<AstronomyVisibilityLimitation> limitations)
    {
        var ordered = limitations.Select(x => EnumGuard.RequireDefined(x, nameof(limitations))).OrderBy(x => x).ToArray();
        if (ordered.Distinct().Count() != ordered.Length) throw new ArgumentException("Visibility limitations must not contain duplicates.", nameof(limitations));
        if (ordered.Length > 1 && ordered.Contains(AstronomyVisibilityLimitation.None)) throw new ArgumentException("Visibility limitation None cannot be combined with other limitations.", nameof(limitations));
        return Array.AsReadOnly(ordered);
    }
}
