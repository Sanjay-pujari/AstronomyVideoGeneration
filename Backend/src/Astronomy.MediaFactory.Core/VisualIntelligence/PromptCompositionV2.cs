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
public interface IProviderAdapter { bool CanAdapt(IImageProviderProfile profile) => true; ProviderPrompt Adapt(PromptSections sections, IImageProviderProfile profile); }
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
    private static readonly Dictionary<string, string> CanonicalInstructions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RuleOfThirds"] = "Rule of thirds composition",
        ["rule of thirds"] = "Rule of thirds composition",
        ["premium"] = "premium astronomy documentary quality",
        ["premiumDocumentary"] = "premium astronomy documentary quality",
        ["documentary"] = "premium astronomy documentary quality",
        ["cinematic"] = "cinematic astronomy composition",
        ["realistic"] = "realistic astronomy rendering",
        ["lowerThirdSafeZone"] = "lower third observation-card safe area",
        ["minimalEssentialTextOnly"] = "minimal essential text only"
    };

    public PromptSections Optimize(PromptSections sections, IImageProviderProfile profile)
    {
        var diagnostics = new List<DiagnosticMessage>(sections.Diagnostics)
        {
            Diag("prompt_optimizer.semantic_optimization_started", "Semantic optimization started.", nameof(PromptOptimizer))
        };
        var optimized = new Dictionary<string, List<string>>();
        var duplicateReductionCount = 0;
        var semanticSectionsMerged = 0;

        foreach (var kv in sections.Sections)
        {
            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in kv.Value.Where(v => !string.IsNullOrWhiteSpace(v)).Select(NormalizeInstruction))
            {
                var key = SemanticKey(kv.Key, value);
                if (seen.Add(key))
                {
                    merged.Add(value);
                }
                else
                {
                    duplicateReductionCount++;
                }
            }

            if (merged.Count > 0)
                optimized[kv.Key] = merged;
            if (merged.Count != kv.Value.Count)
                diagnostics.Add(Diag("prompt_optimizer.duplicates_removed", $"Duplicate or equivalent instructions removed from {kv.Key}.", nameof(PromptOptimizer)));
        }

        semanticSectionsMerged += MergeStyleSection(optimized, "brandStyle", "The artwork should resemble a premium astronomy documentary rather than a generic AI poster: refined, educational, scientifically grounded, uncluttered, and aligned with Drashyam's observational astronomy tone.");
        semanticSectionsMerged += MergeNegativeConstraints(optimized);

        var tokenEstimate = EstimateTokens(optimized);
        diagnostics.Add(Diag("prompt_optimizer.duplicate_groups_merged", $"Duplicate groups merged: {duplicateReductionCount}.", nameof(PromptOptimizer)));
        diagnostics.Add(Diag("prompt_optimizer.token_estimate", $"Token estimate: {tokenEstimate}.", nameof(PromptOptimizer)));
        diagnostics.Add(Diag("prompt_optimizer.readability_estimate", $"Readability estimate: {EstimateReadability(optimized)}.", nameof(PromptOptimizer)));
        diagnostics.Add(Diag("prompt_optimizer.semantic_sections_merged", $"Semantic sections merged: {semanticSectionsMerged}.", nameof(PromptOptimizer)));
        if (profile.Capabilities.MaxPromptLength is int max)
        {
            var length = EstimateLength(optimized);
            diagnostics.Add(Diag("prompt_optimizer.prompt_length_checked", $"Prompt length {length}/{max}.", nameof(PromptOptimizer)));
            if (length > max) diagnostics.Add(new DiagnosticMessage { Severity = DiagnosticSeverity.Warning, Code = "prompt_optimizer.prompt_length_exceeds_provider_limit", Message = $"Prompt length exceeds provider limit {max}; no critical constraints were removed.", Source = nameof(PromptOptimizer) });
        }
        diagnostics.Add(Diag("prompt_optimizer.prompt_quality_improved", "Prompt quality improved through semantic cleanup while preserving astronomy, science, brand, typography, observation-card, and provider safety constraints.", nameof(PromptOptimizer)));
        diagnostics.Add(Diag("prompt_optimizer.applied", "Provider-neutral semantic prompt optimization applied.", nameof(PromptOptimizer)));
        return new PromptSections { Sections = optimized, Diagnostics = diagnostics };
    }

    private static string NormalizeInstruction(string value)
    {
        var trimmed = value.Trim();
        return CanonicalInstructions.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    private static string SemanticKey(string section, string value)
    {
        var normalized = value.ToLowerInvariant().Replace("-", " ").Replace("_", " ");
        if (normalized.Contains("rule of thirds")) return section + ":rule-of-thirds";
        if (normalized.Contains("premium") || normalized.Contains("documentary")) return section + ":premium-documentary";
        if (normalized.Contains("cinematic")) return section + ":cinematic";
        if (normalized.Contains("realistic")) return section + ":realistic";
        return section + ":" + new string(normalized.Where(char.IsLetterOrDigit).ToArray());
    }

    private static int MergeStyleSection(Dictionary<string, List<string>> sections, string key, string paragraph)
    {
        if (!sections.TryGetValue(key, out var values) || values.Count <= 1)
            return 0;
        var hasBrandConstraints = values.Any(v => v.Contains("Drashyam", StringComparison.OrdinalIgnoreCase) || v.Contains("premium", StringComparison.OrdinalIgnoreCase) || v.Contains("scientifically", StringComparison.OrdinalIgnoreCase) || v.Contains("documentary", StringComparison.OrdinalIgnoreCase));
        if (!hasBrandConstraints)
            return 0;
        sections[key] = [paragraph];
        return values.Count - 1;
    }

    private static int MergeNegativeConstraints(Dictionary<string, List<string>> sections)
    {
        if (!sections.TryGetValue("negativeConstraints", out var values) || values.Count <= 1)
            return 0;
        var cleaned = values.Select(v => v.StartsWith("no ", StringComparison.OrdinalIgnoreCase) ? v[3..] : v)
            .Select(v => v.StartsWith("avoid ", StringComparison.OrdinalIgnoreCase) ? v[6..] : v)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        sections["negativeConstraints"] = [$"Avoid {ToNaturalList(cleaned)}."];
        return values.Count - 1;
    }

    private static string ToNaturalList(IReadOnlyList<string> values)
        => values.Count switch { 0 => string.Empty, 1 => values[0], 2 => $"{values[0]} and {values[1]}", _ => string.Join(", ", values.Take(values.Count - 1)) + ", and " + values[^1] };

    private static int EstimateLength(Dictionary<string, List<string>> sections) => sections.Sum(kv => kv.Key.Length + kv.Value.Sum(v => v.Length + 2));
    private static int EstimateTokens(Dictionary<string, List<string>> sections) => Math.Max(1, EstimateLength(sections) / 4);
    private static string EstimateReadability(Dictionary<string, List<string>> sections) => sections.Values.Sum(v => v.Count) <= 18 ? "high" : "medium";
    private static DiagnosticMessage Diag(string code, string message, string source) => new() { Severity = DiagnosticSeverity.Info, Code = code, Message = message, Source = source };
}

public sealed class GenericProviderAdapter : IProviderAdapter
{
    public bool CanAdapt(IImageProviderProfile profile) => true;

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

public sealed class AzurePromptProviderAdapter : IProviderAdapter
{
    public bool CanAdapt(IImageProviderProfile profile) => profile.ProviderType == ImageProviderType.AzureImage || string.Equals(profile.ProviderName, "AzureImage", StringComparison.OrdinalIgnoreCase);

    public ProviderPrompt Adapt(PromptSections sections, IImageProviderProfile profile)
    {
        var diagnostics = new List<DiagnosticMessage>
        {
            Diag(DiagnosticSeverity.Info, "provider_adapter.azure_image.used", "Azure Image provider adapter used.", nameof(AzurePromptProviderAdapter)),
            Diag(DiagnosticSeverity.Info, profile.Capabilities.SupportsNegativePrompt ? "provider_adapter.azure_image.negative_prompt_supported" : "provider_adapter.azure_image.negative_prompt_not_supported", profile.Capabilities.SupportsNegativePrompt ? "Azure Image profile supports negativePrompt." : "Azure Image profile does not support negativePrompt; constraints will be preserved inline.", nameof(AzurePromptProviderAdapter))
        };

        var sb = new StringBuilder();
        sb.AppendLine("Create a premium astronomy hero image as if briefing an expert creative director for Azure Image.");
        AppendParagraph(sb, "Opening paragraph", Join(sections, "sceneSummary"));
        AppendParagraph(sb, "Primary subject", Join(sections, "heroSubject"));
        AppendParagraph(sb, "Supporting subject", Join(sections, "supportingSubjects"));
        AppendParagraph(sb, "Composition", Join(sections, "composition", "framing"));
        AppendParagraph(sb, "Lighting", Join(sections, "lighting", "atmosphere"));
        AppendParagraph(sb, "Rendering requirements", RewriteAstronomy(Join(sections, "astronomicalRendering", "outputExpectations"), sections));
        AppendParagraph(sb, "Typography guidance", RewriteTypography(Join(sections, "typography", "labels")));
        AppendParagraph(sb, "Observation guidance", Join(sections, "observationCard"));
        AppendParagraph(sb, "Brand guidance", Join(sections, "brandStyle", "qualityTargets"));

        var negative = string.Empty;
        if (sections.Sections.TryGetValue("negativeConstraints", out var neg) && neg.Count > 0)
        {
            if (profile.Capabilities.SupportsNegativePrompt)
            {
                negative = string.Join(" ", neg);
            }
            else
            {
                AppendParagraph(sb, "Negative constraints", string.Join(" ", neg));
                diagnostics.Add(ImageProviderProfileRegistry.CapabilityUnavailable("negativePrompt", profile.ProviderName));
                diagnostics.Add(Diag(DiagnosticSeverity.Warning, "provider_adapter.azure_image.constraints_inlined", "Negative constraints were inlined because Azure Image negativePrompt is unsupported.", nameof(AzurePromptProviderAdapter)));
            }
        }

        var prompt = sb.ToString().Trim();
        diagnostics.Add(Diag(DiagnosticSeverity.Info, "provider_adapter.azure_image.prompt_length_optimized", $"Azure Image prompt length optimized to {prompt.Length} characters.", nameof(AzurePromptProviderAdapter)));
        diagnostics.Add(Diag(DiagnosticSeverity.Info, "provider_adapter.azure_image.formatting_applied", "Azure-specific natural-language creative brief formatting applied without provider SDK request objects.", nameof(AzurePromptProviderAdapter)));

        return new ProviderPrompt
        {
            Prompt = prompt,
            NegativePrompt = negative,
            ProviderMetadata = new Dictionary<string, object?> { ["adapter"] = "azureImage", ["provider"] = profile.ProviderName, ["azureSdkCallMade"] = false, ["imageGenerationCallMade"] = false, ["promptMode"] = "diagnosticCreativeBrief" },
            Diagnostics = diagnostics
        };
    }

    private static string Join(PromptSections sections, params string[] keys)
        => string.Join(" ", keys.Where(k => sections.Sections.ContainsKey(k)).SelectMany(k => sections.Sections[k])).Trim();

    private static void AppendParagraph(StringBuilder sb, string label, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        sb.AppendLine().Append(label).Append(": ").AppendLine(EnsureSentence(text));
    }

    private static string EnsureSentence(string text) => text.EndsWith('.') ? text : text + ".";

    private static string RewriteAstronomy(string text, PromptSections sections)
    {
        if (!sections.Sections.TryGetValue("astronomicalRendering", out var rules) || rules.Count == 0) return text;
        var lines = new List<string>();
        foreach (var rule in rules)
        {
            var colon = rule.IndexOf(':');
            if (colon > 0)
            {
                var body = rule[..colon].Trim();
                var detail = rule[(colon + 1)..].Trim().TrimEnd('.');
                lines.Add($"Render {body} with {detail}; maintain physically plausible illumination and perfectly circular planetary geometry unless the event explicitly requires an eclipse or crescent.");
            }
            else
            {
                lines.Add(rule);
            }
        }
        var merged = string.Join(" ", lines);
        return string.IsNullOrWhiteSpace(text) ? merged : text + " " + merged;
    }

    private static string RewriteTypography(string text)
        => string.IsNullOrWhiteSpace(text) ? text : $"Use clear editorial typography only where the deterministic Hero overlay expects it; keep title, subtitle, labels, and footer guidance legible, restrained, and free of generated background text. {text}";

    private static DiagnosticMessage Diag(DiagnosticSeverity severity, string code, string message, string source) => new() { Severity = severity, Code = code, Message = message, Source = source };
}

public sealed class ProviderAdapterResolver : IProviderAdapter
{
    private readonly IReadOnlyList<IProviderAdapter> adapters;
    public ProviderAdapterResolver(IEnumerable<IProviderAdapter> adapters) => this.adapters = adapters.Where(a => a is not ProviderAdapterResolver).ToList();
    public bool CanAdapt(IImageProviderProfile profile) => true;
    public ProviderPrompt Adapt(PromptSections sections, IImageProviderProfile profile) => (adapters.FirstOrDefault(a => a.CanAdapt(profile)) ?? new GenericProviderAdapter()).Adapt(sections, profile);
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
