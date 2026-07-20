using System.Text.Json;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

internal static class AstronomyJsonObjectReader
{
    public static JsonElement RequireObject(JsonElement element, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{description} must be a JSON object.");
        }

        return element;
    }

    public static void EnsureNoDuplicateProperties(JsonElement element)
    {
        RequireObject(element, "JSON value");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new JsonException($"Duplicate JSON property '{property.Name}' is not allowed.");
            }
        }
    }

    public static T DeserializeRequired<T>(JsonElement root, string clrPropertyName, JsonSerializerOptions options)
    {
        var jsonPropertyName = GetJsonPropertyName(clrPropertyName, options);
        if (!TryGetProperty(root, jsonPropertyName, options, out var property))
        {
            throw new JsonException($"Required JSON property '{jsonPropertyName}' is missing.");
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException($"Required JSON property '{jsonPropertyName}' cannot be null.");
        }

        try
        {
            return property.Deserialize<T>(options) ?? throw new JsonException($"Required JSON property '{jsonPropertyName}' cannot be null.");
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            throw new JsonException($"JSON property '{jsonPropertyName}' is invalid.", exception);
        }
    }

    public static T? DeserializeOptional<T>(JsonElement root, string clrPropertyName, JsonSerializerOptions options)
    {
        var jsonPropertyName = GetJsonPropertyName(clrPropertyName, options);
        if (!TryGetProperty(root, jsonPropertyName, options, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        try
        {
            return property.Deserialize<T>(options);
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            throw new JsonException($"JSON property '{jsonPropertyName}' is invalid.", exception);
        }
    }

    public static string GetJsonPropertyName(string clrPropertyName, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(clrPropertyName) ?? clrPropertyName;

    private static bool TryGetProperty(JsonElement root, string jsonPropertyName, JsonSerializerOptions options, out JsonElement property)
    {
        if (root.TryGetProperty(jsonPropertyName, out property))
        {
            return true;
        }

        if (!options.PropertyNameCaseInsensitive)
        {
            return false;
        }

        foreach (var candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, jsonPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        return false;
    }
}
