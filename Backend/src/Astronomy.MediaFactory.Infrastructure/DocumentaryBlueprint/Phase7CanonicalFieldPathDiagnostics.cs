using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Compatibility entry point for governed canonical field-path validation.</summary>
internal static class Phase7CanonicalFieldPathDiagnostics
{
    internal static string Canonicalize(string rawPath, string caller, string payloadSource,
        string adapter, string? traditionName = null, string? fieldName = null)
        => Phase7CanonicalFieldPathPolicy.Canonicalize(rawPath);
}
