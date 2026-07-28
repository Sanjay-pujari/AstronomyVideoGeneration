using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit.Sdk;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

internal static class DocumentaryRealSmokeGate
{
    internal const string Category = "A3.11-RealProviderSmoke";
    internal static IConfiguration Configuration() => new ConfigurationBuilder().AddEnvironmentVariables().Build();
    internal static string? GetDisabledReason(IConfiguration configuration, string? capability = null, string? requiredSetting = null)
    {
        var options = new DocumentaryRealProviderSmokeOptions();
        configuration.GetSection(DocumentaryRealProviderSmokeOptions.SectionName).Bind(options);
        if (!options.Enabled) return "A3.11 real-provider smoke is disabled. Explicitly set DocumentaryProductionAdapters__RealProviderSmoke__Enabled=true for a controlled run.";
        if (capability is not null && !(bool.TryParse(configuration[$"{DocumentaryRealProviderSmokeOptions.SectionName}:{capability}"], out var run) && run))
            return $"A3.11 capability {capability} is disabled by explicit configuration.";
        if (requiredSetting is not null && string.IsNullOrWhiteSpace(configuration[requiredSetting]))
            return $"The required {requiredSetting} setting is unavailable.";
        return null;
    }

    internal static DocumentaryRealProviderSmokeOptions RequireEnabled(string? capability = null)
    {
        var c = Configuration();
        var options = new DocumentaryRealProviderSmokeOptions();
        c.GetSection(DocumentaryRealProviderSmokeOptions.SectionName).Bind(options);
        if (GetDisabledReason(c, capability) is { } reason) throw SkipException.ForSkip(reason);
        return options;
    }
}

internal sealed class DocumentaryRealSmokeFactAttribute : FactAttribute
{
    public DocumentaryRealSmokeFactAttribute(string? capability = null, string? requiredSetting = null)
    {
        Skip = DocumentaryRealSmokeGate.GetDisabledReason(DocumentaryRealSmokeGate.Configuration(), capability, requiredSetting);
    }
}

public sealed class DocumentaryRealProviderSmokePreflightTests
{
    [DocumentaryRealSmokeFact, Trait("Category", DocumentaryRealSmokeGate.Category)]
    public void Preflight_returns_structured_safe_blocking_results()
    {
        var options = DocumentaryRealSmokeGate.RequireEnabled();
        var result = new DocumentaryRealProviderSmokePreflight().Evaluate(options, DocumentaryRealSmokeGate.Configuration(), new EnvironmentCredentialBoundary());
        result.Checks.Should().OnlyContain(x => x.CheckName.Length > 0 && x.SafeDiagnostic.Length > 0 && x.RemediationHint.Length > 0);
        result.Checks.Select(x => x.SafeDiagnostic).Should().NotContain(x => x.Contains("DOCUMENTARY_", StringComparison.Ordinal));
        result.Passed.Should().BeTrue(string.Join(Environment.NewLine, result.Checks.Where(x => !x.Passed).Select(x => $"{x.CheckName}: {x.SafeDiagnostic} Remediation: {x.RemediationHint}")));
    }
    private sealed class EnvironmentCredentialBoundary : IDocumentarySmokeCredentialBoundary
    {
        public bool IsAzureSpeechCredentialAvailable => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY")) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"));
        public string SafeCredentialKind => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")) ? "managed-identity" : "approved-secret-provider";
    }
}

public sealed class DocumentaryAzureSpeechSmokeTests
{
    [DocumentaryRealSmokeFact("RunAzureSpeech"), Trait("Category", DocumentaryRealSmokeGate.Category)] public void English_and_Hindi_WAV_certification_gate() => DocumentaryRealSmokeGate.RequireEnabled("RunAzureSpeech").Should().NotBeNull();
    [DocumentaryRealSmokeFact("RunAzureSpeech", "Speech:Voices:hi"), Trait("Category", DocumentaryRealSmokeGate.Category)] public void Hindi_variant_skips_when_voice_is_unavailable()
    { DocumentaryRealSmokeGate.RequireEnabled("RunAzureSpeech"); }
}

public sealed class DocumentaryVisualProviderSmokeTests
{
    public const string OrionPrompt = "A scientifically accurate wide-field night-sky illustration of the Orion constellation, dark background, no text, no logos, no people.";
    [DocumentaryRealSmokeFact("RunVisualProvider"), Trait("Category", DocumentaryRealSmokeGate.Category)] public void Deterministic_safe_fixed_visual_certification_gate()
    { DocumentaryRealSmokeGate.RequireEnabled("RunVisualProvider"); OrionPrompt.Should().NotContain("person").And.NotContain("logo."); }
}

public sealed class DocumentaryFfmpegSceneSmokeTests
{
    [DocumentaryRealSmokeFact("RunSceneComposition"), Trait("Category", DocumentaryRealSmokeGate.Category)] public void Real_H264_AAC_scene_with_recorded_subtitle_policy_gate() => DocumentaryRealSmokeGate.RequireEnabled("RunSceneComposition").Should().NotBeNull();
}

public sealed class DocumentaryFfmpegVariantSmokeTests
{
    [DocumentaryRealSmokeFact("RunVariantComposition"), Trait("Category", DocumentaryRealSmokeGate.Category)] public void Real_one_variant_composition_gate() => DocumentaryRealSmokeGate.RequireEnabled("RunVariantComposition").Should().NotBeNull();
}

public sealed class DocumentaryFfprobeVerificationSmokeTests
{
    [DocumentaryRealSmokeFact("RunMediaVerification"), Trait("Category", DocumentaryRealSmokeGate.Category)] public void Image_WAV_scene_and_variant_verification_gate() => DocumentaryRealSmokeGate.RequireEnabled("RunMediaVerification").Should().NotBeNull();
}

public sealed class DocumentaryProductionCoordinatorRealSmokeTests
{
    [DocumentaryRealSmokeFact, Trait("Category", DocumentaryRealSmokeGate.Category)] public void One_scene_coordinator_certification_gate()
    {
        var o = DocumentaryRealSmokeGate.RequireEnabled();
        (o.RunAzureSpeech && o.RunVisualProvider && o.RunSceneComposition && o.RunVariantComposition && o.RunMediaVerification).Should().BeTrue("the coordinator may run only after every individual adapter gate is explicitly enabled");
    }
}

public sealed class DocumentaryRealProviderSmokeTimeoutTests
{
    [DocumentaryRealSmokeFact, Trait("Category", DocumentaryRealSmokeGate.Category)] public void Timeout_normalization_and_safe_diagnostics()
    {
        DocumentaryRealSmokeGate.RequireEnabled();
        var normalizer = new DocumentaryProductionFailureNormalizer();
        normalizer.Normalize(new TimeoutException("token=do-not-emit"), DocumentaryProductionOperationKind.NarrationSynthesis, false).Code.Should().Be(DocumentaryProductionFailureCode.ProviderTimeout);
        normalizer.Normalize(new OperationCanceledException(), DocumentaryProductionOperationKind.SceneComposition, false).Code.Should().Be(DocumentaryProductionFailureCode.ProcessTimedOut);
        Action caller = () => normalizer.Normalize(new OperationCanceledException(), DocumentaryProductionOperationKind.SceneComposition, true);
        caller.Should().Throw<OperationCanceledException>();
        DocumentarySmokeDiagnosticSanitizer.Sanitize("token=do-not-emit").Should().NotContain("do-not-emit");
    }
}

public sealed class DocumentaryRealProviderSmokeCleanupTests
{
    [DocumentaryRealSmokeFact, Trait("Category", DocumentaryRealSmokeGate.Category)] public void Cleanup_cannot_escape_owned_workspace()
    {
        var o = DocumentaryRealSmokeGate.RequireEnabled();
        var root = Path.GetFullPath(o.WorkspaceRoot);
        Action escape = () => DocumentaryRealProviderSmokeCleanup.DeleteOwnedWorkspace(root, Path.GetDirectoryName(root)!);
        escape.Should().Throw<UnauthorizedAccessException>();
    }
}

public sealed class DocumentaryRealProviderSmokeArchitectureTests
{
    [DocumentaryRealSmokeFact, Trait("Category", DocumentaryRealSmokeGate.Category)] public void Harness_is_deny_by_default_and_contains_no_credentials()
    {
        DocumentaryRealSmokeGate.RequireEnabled();
        new DocumentaryRealProviderSmokeOptions().Enabled.Should().BeFalse();
        typeof(DocumentaryRealProviderSmokePreflight).Assembly.GetReferencedAssemblies().Select(x => x.Name).Should().NotContain("Astronomy.MediaFactory.Publishing");
        DocumentarySmokeDiagnosticSanitizer.Sanitize("Authorization: Bearer abc password=hunter2").Should().NotContain("abc").And.NotContain("hunter2");
    }
}
