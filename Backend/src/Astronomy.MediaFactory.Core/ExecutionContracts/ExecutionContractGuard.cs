using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

internal static class ExecutionContractGuard
{
    internal static string RequireNonEmpty(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} must be non-empty.", name);
        return value.Trim();
    }

    internal static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static string NormalizeText(string? value) => value?.Trim() ?? string.Empty;
    internal static ImmutableArray<T> NormalizeArray<T>(ImmutableArray<T> values) => values.IsDefault ? ImmutableArray<T>.Empty : values;
    internal static ImmutableDictionary<string, string> NormalizeMetadata(ImmutableDictionary<string, string>? metadata) => metadata ?? ImmutableDictionary<string, string>.Empty;

    internal static ImmutableArray<string> NormalizeAliases(string familyId, ImmutableArray<string> aliases)
    {
        aliases = NormalizeArray(aliases);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias)) continue;
            var normalized = alias.Trim();
            if (string.Equals(normalized, familyId, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(normalized)) builder.Add(normalized);
        }
        return builder.ToImmutable();
    }

    internal static void RejectDuplicateRequirementIds(string category, IEnumerable<string> requirementIds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in requirementIds)
        {
            if (!seen.Add(id)) throw new ArgumentException($"Duplicate requirement id '{id}' in {category}.", category);
        }
    }
}
