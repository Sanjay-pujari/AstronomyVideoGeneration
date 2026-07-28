using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Astronomy.MediaFactory.ProductionAdapters;

public sealed class DocumentaryRealProviderSmokeOptions
{
    public const string SectionName = "DocumentaryProductionAdapters:RealProviderSmoke";
    public bool Enabled { get; set; }
    public bool RetainArtifacts { get; set; } = true;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public int MaximumExecutionMinutes { get; set; } = 10;
    public bool RunAzureSpeech { get; set; }
    public bool RunVisualProvider { get; set; }
    public bool RunSceneComposition { get; set; }
    public bool RunVariantComposition { get; set; }
    public bool RunMediaVerification { get; set; }
    public long MinimumFreeDiskBytes { get; set; } = 256L * 1024 * 1024;
    public bool PreserveCertificationReport { get; set; } = true;
}

public sealed record DocumentarySmokePreflightCheck(string CheckName, bool Passed, string SafeDiagnostic, bool Blocking, string RemediationHint);
public sealed record DocumentarySmokePreflightResult(IReadOnlyList<DocumentarySmokePreflightCheck> Checks)
{ public bool Passed => Checks.All(x => x.Passed || !x.Blocking); }

/// <summary>Reports availability without exposing credential values to the harness.</summary>
public interface IDocumentarySmokeCredentialBoundary
{ bool IsAzureSpeechCredentialAvailable { get; } string SafeCredentialKind { get; } }

public sealed class DocumentaryRealProviderSmokePreflight
{
    public DocumentarySmokePreflightResult Evaluate(DocumentaryRealProviderSmokeOptions options, IConfiguration configuration, IDocumentarySmokeCredentialBoundary credentials)
    {
        ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(configuration); ArgumentNullException.ThrowIfNull(credentials);
        var checks = new List<DocumentarySmokePreflightCheck>();
        Add(checks, "Explicit smoke switch", options.Enabled, "A3.11 switch is " + (options.Enabled ? "enabled." : "disabled."), true, "Explicitly enable it only for a controlled run.");
        var workspace = TryWorkspace(options.WorkspaceRoot, out var workspaceDiagnostic, out var freeBytes);
        Add(checks, "Writable workspace", workspace, workspaceDiagnostic, true, "Configure a dedicated writable WorkspaceRoot.");
        Add(checks, "Adequate free disk space", workspace && freeBytes >= options.MinimumFreeDiskBytes, workspace ? $"Workspace volume has {freeBytes} bytes available." : "Disk capacity was not inspected.", true, $"Free at least {options.MinimumFreeDiskBytes} bytes.");
        AddExecutable(checks, "FFmpeg executable", configuration["Rendering:FfmpegPath"] ?? "ffmpeg");
        AddExecutable(checks, "FFprobe executable", configuration["Rendering:FfprobePath"] ?? "ffprobe");
        var speechLocation = Has(configuration["AzureSpeech:Endpoint"]) || Has(configuration["Speech:Endpoint"]) || Has(configuration["AzureSpeech:Region"]) || Has(configuration["Speech:Region"]);
        Add(checks, "Azure Speech endpoint or region", speechLocation, "Azure Speech location configuration is " + Presence(speechLocation), true, "Configure the Azure Speech endpoint or region.");
        Add(checks, "Azure Speech credential boundary", credentials.IsAzureSpeechCredentialAvailable, credentials.IsAzureSpeechCredentialAvailable ? $"Credential available via {Sanitize(credentials.SafeCredentialKind)}." : "No credential is available through the approved boundary.", true, "Configure managed identity or the approved secret provider.");
        var visual = Has(configuration["AzureOpenAIForImage:Endpoint"]) && Has(configuration["AzureOpenAIForImage:ImageDeployment"]);
        Add(checks, "Visual provider configuration", visual, "Visual provider configuration is " + Presence(visual), true, "Configure a visual endpoint and deployment.");
        var noPublishing = !Enabled(configuration, "Publishing:Enabled") && !Enabled(configuration, "Upload:Enabled") && !Enabled(configuration, "Scheduler:Enabled") && !Enabled(configuration, "StorageUpload:Enabled");
        Add(checks, "Upload and publishing disabled", noPublishing, noPublishing ? "All prohibited outbound integration switches are disabled." : "A prohibited outbound integration is enabled.", true, "Disable upload, publishing, storage-upload, and scheduling.");
        Add(checks, "Production host enabled", Enabled(configuration, "DocumentaryProductionAdapters:Enabled"), "Production host switch was inspected.", true, "Enable the production adapter host.");
        var timeout = options.MaximumExecutionMinutes is >= 1 and <= 60 && int.TryParse(configuration["DocumentaryProductionExecutionHost:SceneCompositionTimeoutSeconds"], out var seconds) && seconds > 0;
        Add(checks, "Operation timeouts", timeout, "Bounded timeout configuration is " + Presence(timeout), true, "Configure bounded smoke and operation timeouts.");
        return new(new ReadOnlyCollection<DocumentarySmokePreflightCheck>(checks));
    }

    private static bool TryWorkspace(string configured, out string diagnostic, out long freeBytes)
    {
        freeBytes = 0;
        if (string.IsNullOrWhiteSpace(configured)) { diagnostic = "WorkspaceRoot is not configured."; return false; }
        try { var path = Path.GetFullPath(configured); Directory.CreateDirectory(path); var marker = Path.Combine(path, $".a311-{Guid.NewGuid():N}"); File.WriteAllText(marker, "probe"); File.Delete(marker); freeBytes = new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace; diagnostic = "Dedicated smoke workspace is writable."; return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { diagnostic = $"Workspace check failed ({e.GetType().Name})."; return false; }
    }
    private static void AddExecutable(List<DocumentarySmokePreflightCheck> checks, string name, string executable) { var available = ProcessOnPath(executable); Add(checks, name, available, available ? $"{name} is available." : $"{name} is unavailable.", true, $"Install {name} or configure its absolute path."); }
    private static bool ProcessOnPath(string executable) { if (Path.IsPathFullyQualified(executable)) return File.Exists(executable); return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(p => File.Exists(Path.Combine(p, executable)) || OperatingSystem.IsWindows() && File.Exists(Path.Combine(p, executable + ".exe"))); }
    private static bool Enabled(IConfiguration c, string key) => bool.TryParse(c[key], out var value) && value;
    private static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);
    private static string Presence(bool present) => present ? "present." : "missing.";
    private static void Add(List<DocumentarySmokePreflightCheck> list, string name, bool passed, string diagnostic, bool blocking, string remediation) => list.Add(new(name, passed, Sanitize(diagnostic), blocking, remediation));
    private static string Sanitize(string value) => DocumentarySmokeDiagnosticSanitizer.Sanitize(value);
}

public static partial class DocumentarySmokeDiagnosticSanitizer
{
    [GeneratedRegex("(?i)authorization\\s*[:=]\\s*bearer\\s+[^\\s,;]+|bearer\\s+[^\\s,;]+|(api[-_ ]?key|token|password|secret|connectionstring)\\s*[:=]\\s*[^\\s,;]+", RegexOptions.CultureInvariant)] private static partial Regex SecretPattern();
    public static string Sanitize(string? value) => string.IsNullOrEmpty(value) ? string.Empty : SecretPattern().Replace(value, "[REDACTED]");
}

public static class DocumentaryRealProviderSmokeCleanup
{
    public static void DeleteOwnedWorkspace(string configuredRoot, string candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot); ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot)); var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (target == Path.GetPathRoot(target) || !DocumentaryPathComparison.IsBelow(root, target)) throw new UnauthorizedAccessException("Cleanup target must be below the configured smoke workspace.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
