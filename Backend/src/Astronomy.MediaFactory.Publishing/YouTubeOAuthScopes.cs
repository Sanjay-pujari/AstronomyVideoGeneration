namespace Astronomy.MediaFactory.Publishing;

/// <summary>Canonical OAuth scope policy shared by setup, health checks, and publishing preflight.</summary>
public static class YouTubeOAuthScopes
{
    public const string Upload = "https://www.googleapis.com/auth/youtube.upload";
    public const string Readonly = "https://www.googleapis.com/auth/youtube.readonly";
    public const string CaptionManagement = "https://www.googleapis.com/auth/youtube.force-ssl";

    public static readonly IReadOnlyList<string> VideoPublishing = [Upload, Readonly];
    public static readonly IReadOnlyList<string> VideoAndCaptionPublishing = [Upload, Readonly, CaptionManagement];

    public static IReadOnlyList<string> Missing(string? grantedScopes, bool captionsRequired)
    {
        var granted = (grantedScopes ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required = captionsRequired ? VideoAndCaptionPublishing : VideoPublishing;
        return required.Where(scope => !granted.Contains(scope)).ToArray();
    }
}
