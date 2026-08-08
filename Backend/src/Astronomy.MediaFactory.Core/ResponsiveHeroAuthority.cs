using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public static class Phase11ReasonCodes
{
    public const string Accepted = "P11_HERO_ASSET_AUTHORITY_ACCEPTED";
    public const string NotRequested = "P11_HERO_ASSET_NOT_REQUESTED";
    public const string Phase10Missing = "P11_PHASE10_AUTHORITY_MISSING";
    public const string Phase10Invalid = "P11_PHASE10_AUTHORITY_INVALID";
    public const string Phase10NotCommitted = "P11_PHASE10_NOT_COMMITTED";
    public const string Phase10NotReady = "P11_PHASE10_NOT_DOWNSTREAM_READY";
    public const string Phase8Invalid = "P11_PHASE8_AUTHORITY_INVALID";
    public const string NoSource = "P11_NO_CERTIFIED_HERO_SOURCE";
}

public sealed record ResponsiveHeroRequest(string OutputRoot, string PlanId, string EventId,
    string Language, string Title, string? Subtitle, string EventFamily, bool OverwriteExisting);

public sealed record ResponsiveHeroResult(string ReasonCode, string Reason,
    string ManifestChecksum, IReadOnlyList<string> InputFiles, IReadOnlyList<string> OutputFiles)
{
    public string ManifestValidationStatus { get; init; } = "Valid";
    public string ValidationStatus { get; init; } = "Valid";
    public bool PublicationCommitted { get; init; }
    public bool SemanticValidationPassed { get; init; }
    public bool ChecksumValidationPassed { get; init; }
    public bool ManifestValidationPassed { get; init; }
    public bool CommittedStateValidationPassed { get; init; }
    public bool DownstreamReady { get; init; }
    public JsonElement? HeroAuthorityDiagnostics { get; init; }
}

public interface IResponsiveHeroAuthorityService
{
    Task<ResponsiveHeroResult> PublishAsync(ResponsiveHeroRequest request, CancellationToken cancellationToken);
}
