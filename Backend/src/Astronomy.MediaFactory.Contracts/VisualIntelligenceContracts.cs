using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Contracts;

public static class VisualIntelligenceContractVersions
{
    public const string ContractVersion = "3.2G";
    public const string CdlVersion = "3.2D";
    public const string BrandVersion = "3.2B";
    public const string RenderingRulesVersion = "3.2C";
    public const string PromptComposerVersion = "3.2E";
    public const string ProviderProfileVersion = "3.2E-azure-image2-v1";
    public const string GenericProviderProfileVersion = "3.3E-generic-provider-profile-v1";
    public const string AzureImageProviderProfileVersion = "3.3G-azure-image-provider-profile-v1";
    public const string ProviderCapabilitiesVersion = "3.3E-provider-capabilities-v1";
    public const string QualityReportVersion = "3.3H";
}

public static class VisualIntelligenceFeatureFlags
{
    public const string SectionName = "VisualIntelligence";
    public const string UseVisualCreativeDirector = nameof(UseVisualCreativeDirector);
    public const string UseCDL = nameof(UseCDL);
    public const string UseCreativeDirectionContract = nameof(UseCreativeDirectionContract);
    public const string UsePromptComposerV2 = nameof(UsePromptComposerV2);
    public const string UseProviderProfiles = nameof(UseProviderProfiles);
    public const string UseQualityScoring = nameof(UseQualityScoring);
    public const string UseQualityScoringBlocking = nameof(UseQualityScoringBlocking);
    public const string UseExperimentalRenderingRules = nameof(UseExperimentalRenderingRules);
    public const string UseHeroPromptV4 = nameof(UseHeroPromptV4);
    public const string UseHeroImageV4Comparison = nameof(UseHeroImageV4Comparison);
    public const string Enabled = nameof(Enabled);
    public const string WriteDiagnostics = nameof(WriteDiagnostics);
    public const string DiagnosticsOutputPath = nameof(DiagnosticsOutputPath);
    public const string DefaultProvider = nameof(DefaultProvider);
    public const string ObservationMode = nameof(ObservationMode);
}


public enum OutputArtifactName
{
    HeroReview,
    HeroLayoutValidation,
    HeroGenerationDiagnostics,
    HeroSceneManifest,
    VisualPromptDiagnostics,
    HeroPromptComparison,
    HeroMigrationReport,
    HeroV3Prompt,
    HeroV4Prompt,
    HeroIntelligenceContract,
    HeroFinal,
    HeroArtifactManifest,
    GalleryIntelligenceContract,
    GalleryEditorialSequence,
    GalleryReview,
    GalleryArtifactManifest
}

public static class OutputArtifactRegistry
{
    private static readonly IReadOnlyDictionary<OutputArtifactName, string> PrimaryRelativePaths = new Dictionary<OutputArtifactName, string>
    {
        [OutputArtifactName.HeroReview] = Path.Combine("hero", "diagnostics", "hero-review.json"),
        [OutputArtifactName.HeroLayoutValidation] = Path.Combine("hero", "diagnostics", "hero-layout-validation.json"),
        [OutputArtifactName.HeroGenerationDiagnostics] = Path.Combine("hero", "diagnostics", "hero-generation-diagnostics.json"),
        [OutputArtifactName.HeroSceneManifest] = Path.Combine("hero", "diagnostics", "hero-scene-manifest.json"),
        [OutputArtifactName.VisualPromptDiagnostics] = Path.Combine("hero", "diagnostics", "visual-prompt-diagnostics.json"),
        [OutputArtifactName.HeroPromptComparison] = Path.Combine("hero", "comparison", "hero-prompt-comparison.json"),
        [OutputArtifactName.HeroMigrationReport] = Path.Combine("hero", "comparison", "hero-migration-report.json"),
        [OutputArtifactName.HeroV3Prompt] = Path.Combine("hero", "comparison", "hero-v3-prompt.txt"),
        [OutputArtifactName.HeroV4Prompt] = Path.Combine("hero", "comparison", "hero-v4-prompt.txt"),
        [OutputArtifactName.HeroIntelligenceContract] = Path.Combine("hero", "diagnostics", "HeroIntelligenceContract.json"),
        [OutputArtifactName.HeroFinal] = Path.Combine("hero", "hero-final.png"),
        [OutputArtifactName.HeroArtifactManifest] = Path.Combine("hero", "HeroArtifactManifest.json"),
        [OutputArtifactName.GalleryIntelligenceContract] = Path.Combine("gallery", "diagnostics", "GalleryIntelligenceContract.json"),
        [OutputArtifactName.GalleryEditorialSequence] = Path.Combine("gallery", "diagnostics", "GalleryEditorialSequence.json"),
        [OutputArtifactName.GalleryReview] = Path.Combine("gallery", "diagnostics", "GalleryReview.json"),
        [OutputArtifactName.GalleryArtifactManifest] = Path.Combine("gallery", "GalleryArtifactManifest.json")
    };

    private static readonly IReadOnlyDictionary<OutputArtifactName, string> LegacyRelativePaths = new Dictionary<OutputArtifactName, string>
    {
        [OutputArtifactName.HeroReview] = Path.Combine("hero", "hero-review.json"),
        [OutputArtifactName.HeroLayoutValidation] = Path.Combine("hero", "hero-layout-validation.json"),
        [OutputArtifactName.HeroGenerationDiagnostics] = Path.Combine("hero", "hero-generation-diagnostics.json"),
        [OutputArtifactName.HeroSceneManifest] = Path.Combine("hero", "hero-scene-manifest.json"),
        [OutputArtifactName.VisualPromptDiagnostics] = Path.Combine("hero", "visual-prompt-diagnostics.json"),
        [OutputArtifactName.HeroPromptComparison] = Path.Combine("hero", "hero-prompt-comparison.json"),
        [OutputArtifactName.HeroMigrationReport] = Path.Combine("hero", "hero-migration-report.json"),
        [OutputArtifactName.HeroV3Prompt] = Path.Combine("hero", "hero-v3-prompt.txt"),
        [OutputArtifactName.HeroV4Prompt] = Path.Combine("hero", "hero-v4-prompt.txt"),
        [OutputArtifactName.HeroIntelligenceContract] = Path.Combine("hero", "diagnostics", "HeroIntelligenceContract.json"),
        [OutputArtifactName.HeroFinal] = Path.Combine("hero", "hero-final.png"),
        [OutputArtifactName.HeroArtifactManifest] = Path.Combine("hero", "HeroArtifactManifest.json"),
        [OutputArtifactName.GalleryIntelligenceContract] = Path.Combine("gallery", "diagnostics", "GalleryIntelligenceContract.json"),
        [OutputArtifactName.GalleryEditorialSequence] = Path.Combine("gallery", "diagnostics", "GalleryEditorialSequence.json"),
        [OutputArtifactName.GalleryReview] = Path.Combine("gallery", "diagnostics", "GalleryReview.json"),
        [OutputArtifactName.GalleryArtifactManifest] = Path.Combine("gallery", "GalleryArtifactManifest.json")
    };

    public static string GetRelativePath(OutputArtifactName artifactName) => PrimaryRelativePaths[artifactName];

    public static string GetLegacyRelativePath(OutputArtifactName artifactName) => LegacyRelativePaths[artifactName];

    public static string GetPath(string outputRoot, OutputArtifactName artifactName)
        => Path.Combine(outputRoot, GetRelativePath(artifactName));

    public static string GetLegacyPath(string outputRoot, OutputArtifactName artifactName)
        => Path.Combine(outputRoot, GetLegacyRelativePath(artifactName));

    public static string ResolveExistingPath(string outputRoot, OutputArtifactName artifactName)
    {
        var primary = GetPath(outputRoot, artifactName);
        if (File.Exists(primary)) return primary;
        var legacy = GetLegacyPath(outputRoot, artifactName);
        return File.Exists(legacy) ? legacy : primary;
    }

    public static IReadOnlyList<OutputArtifactName> GetExpectedHeroValidationArtifacts(OutputArtifactsOptions options)
    {
        var artifacts = new List<OutputArtifactName> { OutputArtifactName.HeroFinal };
        if (options.ShouldWriteDiagnostics)
            artifacts.AddRange([OutputArtifactName.HeroReview, OutputArtifactName.HeroLayoutValidation, OutputArtifactName.HeroGenerationDiagnostics, OutputArtifactName.HeroSceneManifest, OutputArtifactName.VisualPromptDiagnostics, OutputArtifactName.HeroIntelligenceContract]);
        if (options.ShouldWriteComparison)
            artifacts.AddRange([OutputArtifactName.HeroPromptComparison, OutputArtifactName.HeroMigrationReport, OutputArtifactName.HeroV3Prompt, OutputArtifactName.HeroV4Prompt]);
        return artifacts;
    }

    public static HeroArtifactManifest CreateHeroArtifactManifest(string outputRoot, OutputArtifactsOptions options)
    {
        var artifacts = Enum.GetValues<OutputArtifactName>()
            .Where(name => name.ToString().StartsWith("Hero", StringComparison.Ordinal) || name == OutputArtifactName.VisualPromptDiagnostics)
            .Where(name => name != OutputArtifactName.HeroArtifactManifest)
            .ToDictionary(name => name.ToString(), name => GetPath(outputRoot, name), StringComparer.OrdinalIgnoreCase);
        return new HeroArtifactManifest("4.0D.2", options.Mode.ToString(), GetExpectedHeroValidationArtifacts(options).Select(name => name.ToString()).ToArray(), artifacts);
    }

    public static GalleryArtifactManifest CreateGalleryArtifactManifest(string outputRoot, OutputArtifactsOptions options)
    {
        var artifacts = new[]
            {
                OutputArtifactName.GalleryIntelligenceContract,
                OutputArtifactName.GalleryEditorialSequence,
                OutputArtifactName.GalleryReview
            }
            .ToDictionary(name => name.ToString(), name => GetPath(outputRoot, name), StringComparer.OrdinalIgnoreCase);
        return new GalleryArtifactManifest("4.5A", options.Mode.ToString(), artifacts.Keys.ToArray(), artifacts);
    }

    public static string GetManifestPath(string outputRoot) => GetPath(outputRoot, OutputArtifactName.HeroArtifactManifest);

    public static bool TryReadHeroArtifactManifest(string outputRoot, out HeroArtifactManifest manifest)
    {
        var path = GetManifestPath(outputRoot);
        if (File.Exists(path))
        {
            manifest = JsonSerializer.Deserialize<HeroArtifactManifest>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? HeroArtifactManifest.Empty;
            return manifest.Artifacts.Count > 0;
        }

        manifest = HeroArtifactManifest.Empty;
        return false;
    }

    public static string ResolvePathFromManifestOrLegacy(string outputRoot, OutputArtifactName artifactName)
    {
        if (TryReadHeroArtifactManifest(outputRoot, out var manifest) && manifest.Artifacts.TryGetValue(artifactName.ToString(), out var path) && !string.IsNullOrWhiteSpace(path))
            return path;
        return ResolveExistingPath(outputRoot, artifactName);
    }
}

public sealed record HeroArtifactManifest(string Version, string OutputArtifactMode, IReadOnlyList<string> ExpectedArtifacts, IReadOnlyDictionary<string, string> Artifacts)
{
    public static HeroArtifactManifest Empty { get; } = new("4.0D.2", string.Empty, Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public sealed record GalleryArtifactManifest(string Version, string OutputArtifactMode, IReadOnlyList<string> ExpectedArtifacts, IReadOnlyDictionary<string, string> Artifacts)
{
    public static GalleryArtifactManifest Empty { get; } = new("4.5A", string.Empty, Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public enum OutputArtifactMode { Production = 0, Development = 1, CI = 2, Debug = 3 }

public sealed class OutputArtifactsOptions
{
    public const string SectionName = "OutputArtifacts";
    public OutputArtifactMode Mode { get; init; } = OutputArtifactMode.Development;
    public bool WriteDiagnostics { get; init; } = true;
    public bool WriteComparison { get; init; } = true;
    public bool WriteIntermediateFiles { get; init; }
    public bool CleanupTemporaryFiles { get; init; } = true;

    public bool ShouldWriteDiagnostics => Mode switch
    {
        OutputArtifactMode.Production => WriteDiagnostics,
        OutputArtifactMode.CI => WriteDiagnostics,
        _ => true
    };

    public bool ShouldWriteComparison => Mode switch
    {
        OutputArtifactMode.Production => WriteComparison,
        OutputArtifactMode.CI => false,
        _ => true
    };
}

public sealed class VisualIntelligenceOptions
{
    public const string SectionName = VisualIntelligenceFeatureFlags.SectionName;
    public bool Enabled { get; init; }
    public bool WriteDiagnostics { get; init; }
    public string DiagnosticsOutputPath { get; init; } = string.Empty;
    public ImageProviderType DefaultProvider { get; init; } = ImageProviderType.Unknown;
    public bool ObservationMode { get; init; } = true;
    public bool UseVisualCreativeDirector { get; init; }
    public bool UseCDL { get; init; }
    public bool UseCreativeDirectionContract { get; init; }
    public bool UsePromptComposerV2 { get; init; }
    public bool UseProviderProfiles { get; init; }
    public bool UseQualityScoring { get; init; }
    public bool UseQualityScoringBlocking { get; init; }
    public bool UseExperimentalRenderingRules { get; init; }
    public bool UseHeroPromptV4 { get; init; }
    public bool UseHeroImageV4Comparison { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<EventFamily>))]
public enum EventFamily { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("planetConjunction")] PlanetConjunction, [JsonStringEnumMemberName("lunarEvent")] LunarEvent, [JsonStringEnumMemberName("solarEvent")] SolarEvent, [JsonStringEnumMemberName("meteorShower")] MeteorShower, [JsonStringEnumMemberName("eclipse")] Eclipse, [JsonStringEnumMemberName("comet")] Comet, [JsonStringEnumMemberName("deepSkyObject")] DeepSkyObject, [JsonStringEnumMemberName("planetOpposition")] PlanetOpposition, [JsonStringEnumMemberName("spaceNews")] SpaceNews }
[JsonConverter(typeof(JsonStringEnumConverter<Platform>))]
public enum Platform { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("youtubeThumbnail")] YouTubeThumbnail, [JsonStringEnumMemberName("youtubeLongForm")] YouTubeLongForm, [JsonStringEnumMemberName("youtubeShorts")] YouTubeShorts, [JsonStringEnumMemberName("instagramReel")] InstagramReel, [JsonStringEnumMemberName("facebookReel")] FacebookReel, [JsonStringEnumMemberName("gallery")] Gallery, [JsonStringEnumMemberName("hero")] Hero }
[JsonConverter(typeof(JsonStringEnumConverter<AspectRatio>))]
public enum AspectRatio { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("16:9")] Landscape16x9, [JsonStringEnumMemberName("9:16")] Portrait9x16, [JsonStringEnumMemberName("1:1")] Square1x1, [JsonStringEnumMemberName("4:3")] Classic4x3 }
[JsonConverter(typeof(JsonStringEnumConverter<CreativeStyle>))]
public enum CreativeStyle { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("premiumDocumentary")] PremiumDocumentary, [JsonStringEnumMemberName("cinematicRealism")] CinematicRealism, [JsonStringEnumMemberName("scientificIllustration")] ScientificIllustration, [JsonStringEnumMemberName("educationalClarity")] EducationalClarity, [JsonStringEnumMemberName("minimalist")] Minimalist }
[JsonConverter(typeof(JsonStringEnumConverter<CompositionStyle>))]
public enum CompositionStyle { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("heroSubject")] HeroSubject, [JsonStringEnumMemberName("ruleOfThirds")] RuleOfThirds, [JsonStringEnumMemberName("centeredSubject")] CenteredSubject, [JsonStringEnumMemberName("splitComposition")] SplitComposition, [JsonStringEnumMemberName("lowerThirdObservationCard")] LowerThirdObservationCard, [JsonStringEnumMemberName("wideNegativeSpace")] WideNegativeSpace }
[JsonConverter(typeof(JsonStringEnumConverter<PublicationDecisionStatus>))]
public enum PublicationDecisionStatus { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("publish")] Publish, [JsonStringEnumMemberName("publishWithWarning")] PublishWithWarning, [JsonStringEnumMemberName("block")] Block, [JsonStringEnumMemberName("regenerate")] Regenerate, [JsonStringEnumMemberName("fallback")] Fallback, [JsonStringEnumMemberName("approved")] Approved, [JsonStringEnumMemberName("approvedWithWarning")] ApprovedWithWarning, [JsonStringEnumMemberName("needsRegeneration")] NeedsRegeneration, [JsonStringEnumMemberName("needsManualReview")] NeedsManualReview, [JsonStringEnumMemberName("rejected")] Rejected, [JsonStringEnumMemberName("skipped")] Skipped }
[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverity>))]
public enum DiagnosticSeverity { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("info")] Info, [JsonStringEnumMemberName("warning")] Warning, [JsonStringEnumMemberName("error")] Error, [JsonStringEnumMemberName("blocking")] Blocking }
[JsonConverter(typeof(JsonStringEnumConverter<ImageProviderType>))]
public enum ImageProviderType { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("azureImage2")] AzureImage2 = 1, [JsonStringEnumMemberName("openAiImage")] OpenAiImage = 2, [JsonStringEnumMemberName("localRenderer")] LocalRenderer = 3, [JsonStringEnumMemberName("externalProvider")] ExternalProvider = 4, [JsonStringEnumMemberName("azureImage")] AzureImage = 5 }
[JsonConverter(typeof(JsonStringEnumConverter<QualityCategory>))]
public enum QualityCategory { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("creativeIntentMatch")] CreativeIntentMatch, [JsonStringEnumMemberName("astronomicalPlausibility")] AstronomicalPlausibility, [JsonStringEnumMemberName("brandCompliance")] BrandCompliance, [JsonStringEnumMemberName("textReadability")] TextReadability, [JsonStringEnumMemberName("platformSuitability")] PlatformSuitability, [JsonStringEnumMemberName("providerCompliance")] ProviderCompliance }

[JsonConverter(typeof(JsonStringEnumConverter<CreativeQualityCategory>))]
public enum CreativeQualityCategory
{
    [JsonStringEnumMemberName("unknown")] Unknown = 0,
    [JsonStringEnumMemberName("astronomicalAccuracy")] AstronomicalAccuracy,
    [JsonStringEnumMemberName("planetRenderingAccuracy")] PlanetRenderingAccuracy,
    [JsonStringEnumMemberName("brandConsistency")] BrandConsistency,
    [JsonStringEnumMemberName("composition")] Composition,
    [JsonStringEnumMemberName("visualHierarchy")] VisualHierarchy,
    [JsonStringEnumMemberName("typography")] Typography,
    [JsonStringEnumMemberName("observationCard")] ObservationCard,
    [JsonStringEnumMemberName("labelQuality")] LabelQuality,
    [JsonStringEnumMemberName("platformOptimization")] PlatformOptimization,
    [JsonStringEnumMemberName("readability")] Readability,
    [JsonStringEnumMemberName("scientificCredibility")] ScientificCredibility,
    [JsonStringEnumMemberName("documentaryAesthetic")] DocumentaryAesthetic,
    [JsonStringEnumMemberName("overallProductionQuality")] OverallProductionQuality
}

[JsonConverter(typeof(JsonStringEnumConverter<VisualIntelligenceFeatureFlagName>))]
public enum VisualIntelligenceFeatureFlagName { [JsonStringEnumMemberName("unknown")] Unknown = 0, [JsonStringEnumMemberName("useVisualCreativeDirector")] UseVisualCreativeDirector, [JsonStringEnumMemberName("useCDL")] UseCDL, [JsonStringEnumMemberName("useCreativeDirectionContract")] UseCreativeDirectionContract, [JsonStringEnumMemberName("usePromptComposerV2")] UsePromptComposerV2, [JsonStringEnumMemberName("useProviderProfiles")] UseProviderProfiles, [JsonStringEnumMemberName("useQualityScoring")] UseQualityScoring, [JsonStringEnumMemberName("useQualityScoringBlocking")] UseQualityScoringBlocking, [JsonStringEnumMemberName("useExperimentalRenderingRules")] UseExperimentalRenderingRules, [JsonStringEnumMemberName("useHeroPromptV4")] UseHeroPromptV4 }

public sealed record CDL
{
    public string CdlVersion { get; init; } = VisualIntelligenceContractVersions.CdlVersion;
    public string DocumentId { get; init; } = string.Empty;
    public List<CdlDirective> Directives { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}

public sealed record CdlDirective(string Name = "", string Value = "", int Priority = 0);

public sealed record CreativeDirectionContract
{
    public string ContractVersion { get; init; } = VisualIntelligenceContractVersions.ContractVersion;
    public string ContractId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string SourceEventId { get; init; } = string.Empty;
    public EventFamily EventFamily { get; init; } = EventFamily.Unknown;
    public Platform TargetPlatform { get; init; } = Platform.Unknown;
    public string Language { get; init; } = "en";
    public AspectRatio AspectRatio { get; init; } = AspectRatio.Unknown;
    public VisualIntent VisualIntent { get; init; } = new();
    public CDL Cdl { get; init; } = new();
    public BrandRules BrandRules { get; init; } = new();
    public PlanetRenderingRules PlanetRenderingRules { get; init; } = new();
    public TypographyRules TypographyRules { get; init; } = new();
    public ObservationCardRules ObservationCardRules { get; init; } = new();
    public ProviderHints ProviderHints { get; init; } = new();
    public QualityTargets QualityTargets { get; init; } = new();
    public NegativeConstraints NegativeConstraints { get; init; } = new();
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}

public sealed record VisualIntent
{
    public string PrimarySubject { get; init; } = string.Empty;
    public List<string> SecondarySubjects { get; init; } = [];
    public string NarrativeRole { get; init; } = string.Empty;
    public string Mood { get; init; } = string.Empty;
    public string Composition { get; init; } = string.Empty;
    public CreativeStyle CreativeStyle { get; init; } = CreativeStyle.Unknown;
    public CompositionStyle CompositionStyle { get; init; } = CompositionStyle.Unknown;
}

public sealed record BrandRules
{
    public string BrandVersion { get; init; } = VisualIntelligenceContractVersions.BrandVersion;
    public string BrandName { get; init; } = "Drashyam";
    public string VisualTone { get; init; } = "premiumDocumentary";
    public ColorPalette ColorPalette { get; init; } = new();
    public List<string> StylePrinciples { get; init; } = [];
    public LogoPolicy LogoPolicy { get; init; } = new();
    public string ClutterPolicy { get; init; } = "minimal";
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}
public sealed record ColorPalette { public List<string> Primary { get; init; } = []; public List<string> Accent { get; init; } = []; public List<string> Avoid { get; init; } = []; }
public sealed record LogoPolicy { public string Usage { get; init; } = "optional"; public string Placement { get; init; } = "safeCornerOnly"; public double MinimumContrast { get; init; } = 4.5; }

public sealed record PlanetRenderingRules
{
    public string RenderingRulesVersion { get; init; } = VisualIntelligenceContractVersions.RenderingRulesVersion;
    public EventFamily EventFamily { get; init; } = EventFamily.Unknown;
    public List<PlanetRenderingSubjectRule> Subjects { get; init; } = [];
    public Dictionary<string, string> BackgroundRules { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}
public sealed record PlanetRenderingSubjectRule
{
    public string BodyName { get; init; } = string.Empty; public string BodyType { get; init; } = string.Empty; public string RequiredShape { get; init; } = string.Empty; public string ColorBehavior { get; init; } = string.Empty; public string SurfaceDetail { get; init; } = string.Empty; public string Illumination { get; init; } = string.Empty; public string ScalePolicy { get; init; } = string.Empty; public List<string> ForbiddenArtifacts { get; init; } = [];
}

public sealed record TypographyRules
{
    public string BrandVersion { get; init; } = VisualIntelligenceContractVersions.BrandVersion;
    public string TypographySystem { get; init; } = "drashyamPremiumSans";
    public string TextPolicy { get; init; } = "minimalEssentialTextOnly";
    public List<string> AllowedTextElements { get; init; } = [];
    public Dictionary<string, object?> TitleRules { get; init; } = [];
    public Dictionary<string, object?> LabelRules { get; init; } = [];
    public List<string> ForbiddenText { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}

public sealed record ObservationCardRules
{
    public string BrandVersion { get; init; } = VisualIntelligenceContractVersions.BrandVersion;
    public string CardUsage { get; init; } = "optionalWhenHelpful";
    public string Placement { get; init; } = "lowerThirdSafeZone";
    public int MaxFields { get; init; } = 4;
    public List<string> AllowedFields { get; init; } = [];
    public Dictionary<string, object?> VisualStyle { get; init; } = [];
    public Dictionary<string, object?> DataIntegrity { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}

public sealed record ProviderHints
{
    public string ProviderProfileVersion { get; init; } = VisualIntelligenceContractVersions.ProviderProfileVersion;
    public ImageProviderType PreferredProvider { get; init; } = ImageProviderType.Unknown;
    public List<string> CapabilitiesRequired { get; init; } = [];
    public string PromptStyle { get; init; } = string.Empty;
    public Dictionary<string, object?> RenderingHints { get; init; } = [];
    public Dictionary<string, object?> ProviderParameters { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}

public sealed record QualityTargets
{
    public string QualityReportVersion { get; init; } = VisualIntelligenceContractVersions.QualityReportVersion;
    public string Mode { get; init; } = "observation";
    public double OverallThreshold { get; init; } = 0.82;
    public double BlockingThreshold { get; init; } = 0.65;
    public List<QualityTargetDimension> Dimensions { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}
public sealed record QualityTargetDimension { public QualityCategory Name { get; init; } = QualityCategory.Unknown; public double MinimumScore { get; init; } public double Weight { get; init; } public bool Blocking { get; init; } }

public sealed record NegativeConstraints
{
    public List<string> Scientific { get; init; } = [];
    public List<string> Brand { get; init; } = [];
    public List<string> Typography { get; init; } = [];
    public List<string> Provider { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}

public sealed record PromptPackage
{
    public string PromptComposerVersion { get; init; } = VisualIntelligenceContractVersions.PromptComposerVersion;
    public string PromptPackageId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ContractId { get; init; } = string.Empty;
    public ImageProviderType ProviderName { get; init; } = ImageProviderType.Unknown;
    public string ProviderProfileVersion { get; init; } = VisualIntelligenceContractVersions.ProviderProfileVersion;
    public string CdlVersion { get; init; } = VisualIntelligenceContractVersions.CdlVersion;
    public string BrandVersion { get; init; } = VisualIntelligenceContractVersions.BrandVersion;
    public string RenderingVersion { get; init; } = VisualIntelligenceContractVersions.RenderingRulesVersion;
    public string QualityTargetVersion { get; init; } = VisualIntelligenceContractVersions.QualityReportVersion;
    public string PositivePrompt { get; init; } = string.Empty;
    public string NegativePrompt { get; init; } = string.Empty;
    public Dictionary<string, string> PromptSections { get; init; } = [];
    public Dictionary<string, object?> ProviderParameters { get; init; } = [];
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}

public sealed record QualityReport
{
    public string QualityReportVersion { get; init; } = VisualIntelligenceContractVersions.QualityReportVersion;
    public string QualityReportId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ContractId { get; init; } = string.Empty;
    public string PromptPackageId { get; init; } = string.Empty;
    public ImageProviderType ProviderName { get; init; } = ImageProviderType.Unknown;
    public string ProviderProfileVersion { get; init; } = VisualIntelligenceContractVersions.ProviderProfileVersion;
    public string Mode { get; init; } = "observation";
    public double OverallScore { get; init; }
    public double Confidence { get; init; }
    public PublicationDecisionStatus PublicationDecision { get; init; } = PublicationDecisionStatus.Unknown;
    public List<CreativeQualityCategoryScore> CategoryScores { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<string> CriticalIssues { get; init; } = [];
    public List<string> Recommendations { get; init; } = [];
    public Dictionary<string, object?> ProviderInformation { get; init; } = [];
    public Dictionary<string, string> Versions { get; init; } = [];
    public List<QualityDimensionScore> DimensionScores { get; init; } = [];
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
    public PublicationDecisionStatus RecommendedDecision { get; init; } = PublicationDecisionStatus.Unknown;
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}
public sealed record CreativeQualityCategoryScore { public CreativeQualityCategory Name { get; init; } = CreativeQualityCategory.Unknown; public double Score { get; init; } public bool Passed { get; init; } public List<string> Findings { get; init; } = []; }
public sealed record QualityDimensionScore { public QualityCategory Name { get; init; } = QualityCategory.Unknown; public double Score { get; init; } public bool Passed { get; init; } public List<string> Findings { get; init; } = []; }

public sealed record PublicationDecision
{
    public string DecisionId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ContractId { get; init; } = string.Empty;
    public string QualityReportId { get; init; } = string.Empty;
    public PublicationDecisionStatus Decision { get; init; } = PublicationDecisionStatus.Unknown;
    public string Reason { get; init; } = string.Empty;
    public bool Blocking { get; init; }
    public bool FallbackApplied { get; init; }
    public string? FallbackReason { get; init; }
    public bool RequiresHumanReview { get; init; }
    public List<DiagnosticMessage> Diagnostics { get; init; } = [];
    public Dictionary<string, object?> ExtensionFields { get; init; } = [];
}

public sealed record DiagnosticMessage
{
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Info;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Source { get; init; }
    public QualityCategory Category { get; init; } = QualityCategory.Unknown;
    public Dictionary<string, object?> Metadata { get; init; } = [];
}

public static class VisualIntelligenceJson
{
    public static JsonSerializerOptions CreateSerializerOptions(bool writeIndented = false) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = writeIndented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
