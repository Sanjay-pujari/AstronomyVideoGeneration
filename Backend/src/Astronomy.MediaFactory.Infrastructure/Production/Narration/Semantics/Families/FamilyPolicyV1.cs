using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public sealed record FamilyPolicyV1
{
    [JsonConstructor]
    public FamilyPolicyV1(int? minimumObjectCount = null, bool eventSpecificTimingRequired = true) { MinimumObjectCount = minimumObjectCount; EventSpecificTimingRequired = eventSpecificTimingRequired; }
    public int? MinimumObjectCount { get; init; }
    public bool EventSpecificTimingRequired { get; init; }
}
