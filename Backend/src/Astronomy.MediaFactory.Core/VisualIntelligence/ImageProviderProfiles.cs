using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record ImageProviderCapabilities
{
    public string CapabilitiesVersion { get; init; } = VisualIntelligenceContractVersions.ProviderCapabilitiesVersion;
    public bool SupportsNegativePrompt { get; init; }
    public bool SupportsStructuredInput { get; init; }
    public bool SupportsJsonInput { get; init; }
    public bool SupportsTypography { get; init; }
    public bool SupportsImageReferences { get; init; }
    public bool SupportsMasking { get; init; }
    public bool SupportsSeed { get; init; }
    public bool SupportsAspectRatio { get; init; }
    public bool SupportsQualityOptions { get; init; }
    public bool SupportsTransparentBackground { get; init; }
    public bool SupportsImageEditing { get; init; }
    public bool SupportsMultipleImages { get; init; }
    public bool SupportsStylePresets { get; init; }
    public bool SupportsSafetyOptions { get; init; }
    public int? MaxPromptLength { get; init; }
    public int? MaxNegativePromptLength { get; init; }
    public List<string> SupportedAspectRatios { get; init; } = [];
    public List<string> SupportedOutputFormats { get; init; } = [];
    public List<string> SupportedQualityLevels { get; init; } = [];
    public Dictionary<string, object?> ProviderMetadata { get; init; } = [];
}

public interface IImageProviderProfile
{
    string ProviderName { get; }
    ImageProviderType ProviderType { get; }
    string ProviderProfileVersion { get; }
    ImageProviderCapabilities Capabilities { get; }
    string DefaultPromptStrategy { get; }
    IReadOnlyList<string> ProviderNotes { get; }
    IReadOnlyList<DiagnosticMessage> Diagnostics { get; }
}

public sealed record ImageProviderProfileResolution
{
    public IImageProviderProfile Profile { get; init; } = new GenericImageProviderProfile();
    public bool FallbackUsed { get; init; }
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public interface IImageProviderProfileRegistry
{
    void Register(IImageProviderProfile profile);
    ImageProviderProfileResolution Resolve(ImageProviderType providerType, bool throwIfUnknown = false);
    ImageProviderProfileResolution Resolve(string? providerName, bool throwIfUnknown = false);
    IReadOnlyCollection<IImageProviderProfile> GetRegisteredProfiles();
}

public sealed class GenericImageProviderProfile : IImageProviderProfile
{
    public string ProviderName { get; init; } = "generic";
    public ImageProviderType ProviderType { get; init; } = ImageProviderType.Unknown;
    public string ProviderProfileVersion { get; init; } = VisualIntelligenceContractVersions.GenericProviderProfileVersion;
    public ImageProviderCapabilities Capabilities { get; init; } = new()
    {
        SupportsNegativePrompt = false,
        SupportsStructuredInput = false,
        SupportsJsonInput = false,
        SupportsTypography = false,
        SupportsImageReferences = false,
        SupportsMasking = false,
        SupportsSeed = false,
        SupportsAspectRatio = false,
        SupportsQualityOptions = false,
        SupportsTransparentBackground = false,
        SupportsImageEditing = false,
        SupportsMultipleImages = false,
        SupportsStylePresets = false,
        SupportsSafetyOptions = false,
        SupportedOutputFormats = ["png", "jpg"],
        ProviderMetadata = new Dictionary<string, object?> { ["plainTextPromptSupported"] = true, ["fallbackProfile"] = true }
    };
    public string DefaultPromptStrategy { get; init; } = "plainText";
    public IReadOnlyList<string> ProviderNotes { get; init; } = ["Generic conservative fallback profile. No provider-specific image features are assumed."];
    public IReadOnlyList<DiagnosticMessage> Diagnostics { get; init; } = [new() { Severity = DiagnosticSeverity.Info, Code = "image_provider_profile.generic", Message = "Generic image provider profile loaded.", Source = nameof(GenericImageProviderProfile) }];
}

public sealed class ImageProviderProfileRegistry : IImageProviderProfileRegistry
{
    private readonly Dictionary<ImageProviderType, IImageProviderProfile> byType = [];
    private readonly Dictionary<string, IImageProviderProfile> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly IImageProviderProfile fallback;

    public ImageProviderProfileRegistry(IEnumerable<IImageProviderProfile> profiles)
    {
        fallback = profiles.OfType<GenericImageProviderProfile>().FirstOrDefault() ?? new GenericImageProviderProfile();
        Register(fallback);
        foreach (var profile in profiles.Where(p => !ReferenceEquals(p, fallback))) Register(profile);
    }

    public void Register(IImageProviderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.IsNullOrWhiteSpace(profile.ProviderName)) byName[profile.ProviderName] = profile;
        byType[profile.ProviderType] = profile;
    }

    public IReadOnlyCollection<IImageProviderProfile> GetRegisteredProfiles() => byName.Values.Distinct().ToList();

    public ImageProviderProfileResolution Resolve(ImageProviderType providerType, bool throwIfUnknown = false)
    {
        if (byType.TryGetValue(providerType, out var profile) && providerType != ImageProviderType.Unknown) return Resolved(profile, providerType.ToString());
        return Missing(providerType.ToString(), throwIfUnknown);
    }

    public ImageProviderProfileResolution Resolve(string? providerName, bool throwIfUnknown = false)
    {
        if (!string.IsNullOrWhiteSpace(providerName) && byName.TryGetValue(providerName.Trim(), out var profile) && profile.ProviderType != ImageProviderType.Unknown) return Resolved(profile, providerName.Trim());
        return Missing(providerName ?? string.Empty, throwIfUnknown);
    }

    private static ImageProviderProfileResolution Resolved(IImageProviderProfile profile, string requested) => new() { Profile = profile, Diagnostics = [Diag(DiagnosticSeverity.Info, "image_provider_profile.resolved", $"Image provider profile resolved for '{requested}'.", profile.ProviderName)] };

    private ImageProviderProfileResolution Missing(string requested, bool throwIfUnknown)
    {
        if (throwIfUnknown) throw new KeyNotFoundException($"Image provider profile not registered for '{requested}'.");
        return new ImageProviderProfileResolution { Profile = fallback, FallbackUsed = true, Diagnostics = [Diag(DiagnosticSeverity.Warning, "image_provider_profile.missing", $"Image provider profile missing for '{requested}'.", fallback.ProviderName), Diag(DiagnosticSeverity.Warning, "image_provider_profile.generic_fallback_used", "Generic fallback image provider profile used.", fallback.ProviderName), Diag(DiagnosticSeverity.Warning, "image_provider_profile.unsupported_provider_requested", $"Unsupported image provider requested: '{requested}'.", fallback.ProviderName)] };
    }

    public static DiagnosticMessage CapabilityUnavailable(string capability, string providerName) => Diag(DiagnosticSeverity.Warning, "image_provider_profile.capability_unavailable", $"Capability unavailable: {capability}.", providerName);
    private static DiagnosticMessage Diag(DiagnosticSeverity severity, string code, string message, string source) => new() { Severity = severity, Code = code, Message = message, Source = source };
}
