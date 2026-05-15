using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Publishing;

public static class YouTubeTokenResolver
{
    public const string TokenFileSource = "TokenFile";
    public const string AppSettingsSource = "AppSettings";
    public const string EnvironmentSource = "Environment";
    public const string MismatchWarning = "YouTube refresh token in token file differs from configured appsettings/env token. Using token file.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<YouTubeResolvedToken> ResolveAsync(YouTubeOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        var tokenFilePath = ResolveTokenFilePath(options);
        var tokenFileExists = File.Exists(tokenFilePath);
        var tokenFile = tokenFileExists
            ? await ReadTokenFileAsync(tokenFilePath, logger, cancellationToken)
            : null;

        var configuredRefreshToken = string.IsNullOrWhiteSpace(options.RefreshToken) ? null : options.RefreshToken.Trim();
        var configuredSource = DetermineConfiguredTokenSource(configuredRefreshToken);
        var fileRefreshToken = string.IsNullOrWhiteSpace(tokenFile?.RefreshToken) ? null : tokenFile.RefreshToken.Trim();
        var source = string.Empty;
        var refreshToken = string.Empty;

        if (!string.IsNullOrWhiteSpace(fileRefreshToken))
        {
            source = TokenFileSource;
            refreshToken = fileRefreshToken;
            if (!string.IsNullOrWhiteSpace(configuredRefreshToken) && !string.Equals(fileRefreshToken, configuredRefreshToken, StringComparison.Ordinal))
            {
                logger.LogWarning(MismatchWarning);
            }
        }
        else if (!string.IsNullOrWhiteSpace(configuredRefreshToken))
        {
            source = configuredSource;
            refreshToken = configuredRefreshToken;
        }

        var resolved = new YouTubeResolvedToken(
            RefreshToken: refreshToken,
            TokenSource: source,
            TokenFilePath: tokenFilePath,
            TokenFileExists: tokenFileExists,
            ChannelId: tokenFile?.ChannelId ?? string.Empty,
            ChannelTitle: tokenFile?.ChannelTitle ?? string.Empty);

        await WriteDiagnosticsAsync(options, resolved, cancellationToken);
        return resolved;
    }

    private static async Task<YouTubeTokenFileDetails?> ReadTokenFileAsync(string tokenFilePath, ILogger logger, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(tokenFilePath, cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var refreshToken = GetStringProperty(root, "refreshToken", "refresh_token");
            return new YouTubeTokenFileDetails(
                RefreshToken: refreshToken,
                ChannelId: GetStringProperty(root, "channelId", "channel_id"),
                ChannelTitle: GetStringProperty(root, "channelTitle", "channel_title"));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Unable to parse YouTube token file at {TokenFilePath}.", tokenFilePath);
            return null;
        }
    }

    private static string GetStringProperty(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    public static string ResolveTokenFilePath(YouTubeOptions options)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(options.TokenFilePath) ? "youtube-oauth-token.json" : options.TokenFilePath);

    public static string ResolveDiagnosticsPath(YouTubeOptions options)
        => Path.Combine(Path.GetDirectoryName(ResolveTokenFilePath(options))!, "youtube-token-source-diagnostics.json");

    public static string PreviewSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 8)
        {
            return "***";
        }

        return $"{trimmed[..8]}***";
    }

    public static string PreviewClientId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 12)
        {
            return $"{trimmed[..Math.Min(4, trimmed.Length)]}***";
        }

        return $"{trimmed[..8]}...{trimmed[^4..]}";
    }

    private static string DetermineConfiguredTokenSource(string? configuredRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(configuredRefreshToken))
        {
            return string.Empty;
        }

        var environmentToken = Environment.GetEnvironmentVariable("YouTube__RefreshToken");
        return !string.IsNullOrWhiteSpace(environmentToken) && string.Equals(environmentToken.Trim(), configuredRefreshToken, StringComparison.Ordinal)
            ? EnvironmentSource
            : AppSettingsSource;
    }

    private static async Task WriteDiagnosticsAsync(YouTubeOptions options, YouTubeResolvedToken resolved, CancellationToken cancellationToken)
    {
        var diagnosticsPath = ResolveDiagnosticsPath(options);
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsPath)!);
        var payload = new YouTubeTokenSourceDiagnostics(
            TokenSource: string.IsNullOrWhiteSpace(resolved.TokenSource) ? "Missing" : resolved.TokenSource,
            TokenFileExists: resolved.TokenFileExists,
            ChannelId: resolved.ChannelId,
            ChannelTitle: resolved.ChannelTitle,
            RefreshTokenPreview: PreviewSecret(resolved.RefreshToken),
            ClientIdPreview: PreviewClientId(options.ClientId),
            RedirectUri: options.RedirectUri,
            GeneratedUtc: DateTimeOffset.UtcNow);

        await using var stream = File.Create(diagnosticsPath);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
    }

    private sealed record YouTubeTokenFileDetails(
        string RefreshToken,
        string ChannelId,
        string ChannelTitle);

    private sealed record YouTubeTokenSourceDiagnostics(
        string TokenSource,
        bool TokenFileExists,
        string ChannelId,
        string ChannelTitle,
        string RefreshTokenPreview,
        string ClientIdPreview,
        string RedirectUri,
        DateTimeOffset GeneratedUtc);
}

public sealed record YouTubeResolvedToken(
    string RefreshToken,
    string TokenSource,
    string TokenFilePath,
    bool TokenFileExists,
    string ChannelId,
    string ChannelTitle);
