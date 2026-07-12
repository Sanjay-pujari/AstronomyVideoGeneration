using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

public readonly record struct SemanticCapabilityId
{
    [JsonConstructor]
    public SemanticCapabilityId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Capability id cannot be blank.", nameof(value)) : value.Trim();
    }

    public string Value { get; }
    public override string ToString() => Value;
    public static implicit operator string(SemanticCapabilityId id) => id.Value;
}
