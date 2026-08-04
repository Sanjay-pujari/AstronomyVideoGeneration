using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Temporary production telemetry around canonical field-path entry points.</summary>
internal static partial class Phase7CanonicalFieldPathDiagnostics
{
    [GeneratedRegex(@"\[(?:\d+|\*?)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ArrayOrdinal();

    internal static string Canonicalize(string rawPath, string caller, string payloadSource,
        string adapter, string? traditionName = null, string? fieldName = null)
    {
        var normalizedPath = PreviewNormalization(rawPath);
        Write("before", rawPath, normalizedPath, caller, payloadSource, adapter, traditionName, fieldName, null);
        try
        {
            return Phase7CanonicalFieldPathPolicy.Canonicalize(rawPath);
        }
        catch (ArgumentException ex) when (ex.ParamName == "value")
        {
            Write("rejected", rawPath, normalizedPath, caller, payloadSource, adapter, traditionName, fieldName, ex.Message);
            throw;
        }
    }

    private static string PreviewNormalization(string rawPath) => rawPath is null
        ? ""
        : ArrayOrdinal().Replace(rawPath.Trim().Replace('\\', '.').Replace('/', '.'), "");

    private static void Write(string outcome, string rawPath, string normalizedPath, string caller,
        string payloadSource, string adapter, string? traditionName, string? fieldName, string? exception)
    {
        // stderr is intentional: this diagnostic must survive failures before a scoped ILogger exists.
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            diagnostic = "P7_CANONICAL_FIELD_PATH_TEMPORARY",
            outcome,
            rawPath,
            normalizedPath,
            caller,
            payloadSource,
            adapter,
            traditionName,
            fieldName,
            exception
        }));
    }
}
