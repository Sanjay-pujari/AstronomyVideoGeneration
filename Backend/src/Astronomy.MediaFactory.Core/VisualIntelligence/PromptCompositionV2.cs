using System.Text;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record PromptSection(string Name, IReadOnlyList<string> Items);

public sealed record PromptSections
{
    public Dictionary<string, List<string>> Sections { get; init; } = [];
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public sealed record PromptComposerV2Result
{
    public VisualIntelligenceOrchestrationStatus Status { get; init; }
    public PromptPackage? PromptPackage { get; init; }
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public interface IPromptSectionBuilder { PromptSections Build(CDL? cdl, CreativeDirectionContract? contract); }
public interface IPromptOptimizer { PromptSections Optimize(PromptSections sections, IImageProviderProfile profile); }
public interface IProviderAdapter { ProviderPrompt Adapt(PromptSections sections, IImageProviderProfile profile); }
public interface IPromptPackageBuilder { PromptPackage Build(ProviderPrompt prompt, PromptSections sections, CreativeDirectionContract? contract, IImageProviderProfile profile, IEnumerable<DiagnosticMessage> diagnostics); }
public interface IPromptComposerV2 { Task<PromptComposerV2Result> ComposeAsync(CDL? cdl, CreativeDirectionContract? contract, ImageProviderType requestedProvider = ImageProviderType.Unknown, CancellationToken cancellationToken = default); }

public sealed record ProviderPrompt
{
    public string Prompt { get; init; } = string.Empty;
    public string NegativePrompt { get; init; } = string.Empty;
    public Dictionary<string, object?> ProviderMetadata { get; init; } = [];
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}

public sealed class PromptSectionBuilder : IPromptSectionBuilder
{
    private static readonly string[] Order = ["sceneSummary", "heroSubject", "supportingSubjects", "composition", "framing", "lighting", "atmosphere", "astronomicalRendering", "typography", "observationCard", "labels", "brandStyle", "qualityTargets", "negativeConstraints", "outputExpectations"];

    public PromptSections Build(CDL? cdl, CreativeDirectionContract? contract)
    {
        var map = Order.ToDictionary(k => k, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        if (contract is not null)
        {
            Add(map, "sceneSummary", contract.VisualIntent.NarrativeRole, contract.VisualIntent.Mood, contract.EventFamily.ToString(), contract.TargetPlatform.ToString(), contract.AspectRatio.ToString());
            Add(map, "heroSubject", contract.VisualIntent.PrimarySubject);
            Add(map, "supportingSubjects", contract.VisualIntent.SecondarySubjects);
            Add(map, "composition", contract.VisualIntent.Composition, contract.VisualIntent.CompositionStyle.ToString());
            Add(map, "astronomicalRendering", contract.PlanetRenderingRules.Subjects.Select(s => $"{s.BodyName}: {s.RequiredShape}; {s.ColorBehavior}; {s.SurfaceDetail}; {s.Illumination}; {s.ScalePolicy}; avoid {string.Join(", ", s.ForbiddenArtifacts)}"));
            Add(map, "astronomicalRendering", contract.PlanetRenderingRules.BackgroundRules.Select(kv => $"{kv.Key}: {kv.Value}"));
            Add(map, "typography", contract.TypographyRules.TypographySystem, contract.TypographyRules.TextPolicy);
            Add(map, "typography", contract.TypographyRules.AllowedTextElements.Select(x => $"allowed text: {x}"));
            Add(map, "observationCard", contract.ObservationCardRules.CardUsage, contract.ObservationCardRules.Placement, $"max fields: {contract.ObservationCardRules.MaxFields}");
            Add(map, "labels", contract.TypographyRules.LabelRules.Select(kv => $"{kv.Key}: {kv.Value}"));
            Add(map, "brandStyle", contract.BrandRules.BrandName, contract.BrandRules.VisualTone, contract.BrandRules.ClutterPolicy);
            Add(map, "brandStyle", contract.BrandRules.StylePrinciples);
            Add(map, "qualityTargets", contract.QualityTargets.Mode, $"overall threshold: {contract.QualityTargets.OverallThreshold}");
            Add(map, "negativeConstraints", contract.NegativeConstraints.Scientific.Concat(contract.NegativeConstraints.Brand).Concat(contract.NegativeConstraints.Typography).Concat(contract.NegativeConstraints.Provider));
            Add(map, "negativeConstraints", contract.TypographyRules.ForbiddenText.Select(x => $"forbidden text: {x}"));
        }
        foreach (var d in cdl?.Directives ?? contract?.Cdl.Directives ?? [])
        {
            var key = map.ContainsKey(d.Name) ? d.Name : d.Name switch { "visualHierarchy" => "composition", "creativeIntent" => "sceneSummary", _ => "outputExpectations" };
            Add(map, key, d.Value);
        }
        Add(map, "outputExpectations", "provider-neutral plain text image prompt", "preserve all scientific, brand, typography, and safety constraints");
        return new PromptSections { Sections = map.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value), Diagnostics = [Diag("prompt_sections.built", $"Prompt sections built: {map.Count(kv => kv.Value.Count > 0)}.", nameof(PromptSectionBuilder))] };
    }
    private static void Add(Dictionary<string, List<string>> map, string key, params string[] values) => Add(map, key, values.AsEnumerable());
    private static void Add(Dictionary<string, List<string>> map, string key, IEnumerable<string?> values) { if (!map.TryGetValue(key, out var list)) map[key] = list = []; foreach (var v in values.Where(v => !string.IsNullOrWhiteSpace(v))) list.Add(v!.Trim()); }
    private static DiagnosticMessage Diag(string code, string message, string source) => new() { Severity = DiagnosticSeverity.Info, Code = code, Message = message, Source = source };
}

public sealed class PromptOptimizer : IPromptOptimizer
{
    public PromptSections Optimize(PromptSections sections, IImageProviderProfile profile)
    {
        var diagnostics = new List<DiagnosticMessage>(sections.Diagnostics);
        var optimized = new Dictionary<string, List<string>>();
        foreach (var kv in sections.Sections)
        {
            var unique = kv.Value.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (unique.Count > 0) optimized[kv.Key] = unique;
            if (unique.Count != kv.Value.Count) diagnostics.Add(Diag("prompt_optimizer.duplicates_removed", $"Duplicate instructions removed from {kv.Key}.", nameof(PromptOptimizer)));
        }
        if (profile.Capabilities.MaxPromptLength is int max)
        {
            var length = EstimateLength(optimized);
            diagnostics.Add(Diag("prompt_optimizer.prompt_length_checked", $"Prompt length {length}/{max}.", nameof(PromptOptimizer)));
            if (length > max) diagnostics.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Warning, Code = "prompt_optimizer.prompt_length_exceeds_provider_limit", Message = $"Prompt length exceeds provider limit {max}; no critical constraints were removed.", Source = nameof(PromptOptimizer) });
        }
        diagnostics.Add(Diag("prompt_optimizer.applied", "Provider-neutral prompt optimization applied.", nameof(PromptOptimizer)));
        return new PromptSections { Sections = optimized, Diagnostics = diagnostics };
    }
    private static int EstimateLength(Dictionary<string, List<string>> sections) => sections.Sum(kv => kv.Key.Length + kv.Value.Sum(v => v.Length + 2));
    private static DiagnosticMessage Diag(string code, string message, string source) => new() { Severity = DiagnosticSeverity.Info, Code = code, Message = message, Source = source };
}

public sealed class GenericProviderAdapter : IProviderAdapter
{
    public ProviderPrompt Adapt(PromptSections sections, IImageProviderProfile profile)
    {
        var diagnostics = new List<DiagnosticMessage> { new() { Severity = DiagnosticSeverity.Info, Code = "provider_adapter.generic_used", Message = "Generic provider adapter used.", Source = nameof(GenericProviderAdapter) } };
        var sb = new StringBuilder();
        foreach (var kv in sections.Sections.Where(kv => kv.Key != "negativeConstraints")) sb.AppendLine($"{kv.Key}: {string.Join("; ", kv.Value)}");
        var negative = string.Empty;
        if (sections.Sections.TryGetValue("negativeConstraints", out var neg) && neg.Count > 0)
        {
            if (profile.Capabilities.SupportsNegativePrompt) negative = string.Join("; ", neg);
            else { sb.AppendLine($"negativeConstraints: {string.Join("; ", neg)}"); diagnostics.Add(ImageProviderProfileRegistry.CapabilityUnavailable("negativePrompt", profile.ProviderName)); diagnostics.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Warning, Code = "provider_adapter.negative_prompt_unsupported", Message = "Negative prompt is unsupported; constraints were preserved in the positive prompt.", Source = nameof(GenericProviderAdapter) }); }
        }
        return new ProviderPrompt { Prompt = sb.ToString().Trim(), NegativePrompt = negative, ProviderMetadata = new Dictionary<string, object?> { ["adapter"] = "generic", ["provider"] = profile.ProviderName }, Diagnostics = diagnostics };
    }
}

public sealed class PromptPackageBuilder : IPromptPackageBuilder
{
    public PromptPackage Build(ProviderPrompt prompt, PromptSections sections, CreativeDirectionContract? contract, IImageProviderProfile profile, IEnumerable<DiagnosticMessage> diagnostics) => new()
    {
        PromptComposerVersion = VisualIntelligenceContractVersions.PromptComposerVersion,
        PromptPackageId = $"prompt_{Guid.NewGuid():N}",
        ContractId = contract?.ContractId ?? string.Empty,
        ProviderName = profile.ProviderType,
        ProviderProfileVersion = profile.ProviderProfileVersion,
        PositivePrompt = prompt.Prompt,
        NegativePrompt = prompt.NegativePrompt,
        PromptSections = sections.Sections.ToDictionary(kv => kv.Key, kv => string.Join("; ", kv.Value)),
        ProviderParameters = prompt.ProviderMetadata,
        Diagnostics = diagnostics.Concat([new DiagnosticMessage { Severity = DiagnosticSeverity.Info, Code = "prompt_package.built", Message = "Prompt package built.", Source = nameof(PromptPackageBuilder) }]).ToList(),
        CdlVersion = contract?.Cdl.CdlVersion ?? VisualIntelligenceContractVersions.CdlVersion,
        BrandVersion = contract?.BrandRules.BrandVersion ?? VisualIntelligenceContractVersions.BrandVersion,
        RenderingVersion = contract?.PlanetRenderingRules.RenderingRulesVersion ?? VisualIntelligenceContractVersions.RenderingRulesVersion,
        QualityTargetVersion = contract?.QualityTargets.QualityReportVersion ?? VisualIntelligenceContractVersions.QualityReportVersion
    };
}

public sealed class PromptComposerV2 : IPromptComposerV2
{
    private readonly IOptions<VisualIntelligenceOptions> options; private readonly IPromptSectionBuilder sectionBuilder; private readonly IPromptOptimizer optimizer; private readonly IProviderAdapter adapter; private readonly IPromptPackageBuilder packageBuilder; private readonly IImageProviderProfileRegistry registry;
    public PromptComposerV2(IOptions<VisualIntelligenceOptions> options, IPromptSectionBuilder sectionBuilder, IPromptOptimizer optimizer, IProviderAdapter adapter, IPromptPackageBuilder packageBuilder, IImageProviderProfileRegistry registry) { this.options = options; this.sectionBuilder = sectionBuilder; this.optimizer = optimizer; this.adapter = adapter; this.packageBuilder = packageBuilder; this.registry = registry; }
    public Task<PromptComposerV2Result> ComposeAsync(CDL? cdl, CreativeDirectionContract? contract, ImageProviderType requestedProvider = ImageProviderType.Unknown, CancellationToken cancellationToken = default)
    {
        if (!options.Value.UsePromptComposerV2) { var d = new DiagnosticMessage { Severity = DiagnosticSeverity.Info, Code = "prompt_composer_v2.disabled", Message = "PromptComposerV2 disabled/skipped by feature flag.", Source = nameof(PromptComposerV2) }; return Task.FromResult(new PromptComposerV2Result { Status = VisualIntelligenceOrchestrationStatus.Disabled, Diagnostics = [d] }); }
        var resolution = registry.Resolve(requestedProvider);
        var diagnostics = new List<DiagnosticMessage>(resolution.Diagnostics) { new() { Severity = DiagnosticSeverity.Info, Code = "prompt_composer_v2.provider_profile_resolved", Message = $"Provider profile resolved: {resolution.Profile.ProviderName}.", Source = nameof(PromptComposerV2) } };
        var sections = sectionBuilder.Build(cdl, contract); diagnostics.AddRange(sections.Diagnostics);
        var optimized = optimizer.Optimize(sections, resolution.Profile); diagnostics.AddRange(optimized.Diagnostics.Except(sections.Diagnostics));
        var providerPrompt = adapter.Adapt(optimized, resolution.Profile); diagnostics.AddRange(providerPrompt.Diagnostics);
        var package = packageBuilder.Build(providerPrompt, optimized, contract, resolution.Profile, diagnostics);
        return Task.FromResult(new PromptComposerV2Result { Status = VisualIntelligenceOrchestrationStatus.Success, PromptPackage = package, Diagnostics = package.Diagnostics });
    }
}
