using System.Text.Json;

namespace Astronomy.MediaFactory.Tests;

public sealed class VisualIntelligenceContractsTests
{
    [Fact]
    public void CreativeDirectionContract_Defaults_include_versions_and_safe_collections()
    {
        var contract = new CreativeDirectionContract();

        Assert.Equal("3.2G", contract.ContractVersion);
        Assert.Equal("3.2D", contract.Cdl.CdlVersion);
        Assert.Equal("3.2B", contract.BrandRules.BrandVersion);
        Assert.Equal("3.2C", contract.PlanetRenderingRules.RenderingRulesVersion);
        Assert.Equal("3.3H", contract.QualityTargets.QualityReportVersion);
        Assert.Empty(contract.NegativeConstraints.Scientific);
        Assert.Equal("en", contract.Language);
    }

    [Fact]
    public void Contracts_serialize_to_camel_case_json_and_deserialize_unknown_fields()
    {
        var contract = new CreativeDirectionContract
        {
            ContractId = "cdc_2026_001",
            SourceEventId = "event_2026_mars_moon_conjunction",
            EventFamily = EventFamily.PlanetConjunction,
            TargetPlatform = Platform.YouTubeThumbnail,
            AspectRatio = AspectRatio.Landscape16x9,
            VisualIntent = new VisualIntent { PrimarySubject = "Mars and Moon conjunction" },
            ProviderHints = new ProviderHints { PreferredProvider = ImageProviderType.AzureImage2 }
        };

        var json = JsonSerializer.Serialize(contract, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.Contains("\"contractVersion\":", json);
        Assert.Contains("\"eventFamily\":\"planetConjunction\"", json);
        Assert.Contains("\"targetPlatform\":\"youtubeThumbnail\"", json);
        Assert.Contains("\"aspectRatio\":\"16:9\"", json);
        Assert.DoesNotContain("ContractVersion", json);

        var withUnknownField = json.Insert(json.LastIndexOf('}'), ",\"futureField\":\"ignored\"");
        var reparsed = JsonSerializer.Deserialize<CreativeDirectionContract>(withUnknownField, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.NotNull(reparsed);
        Assert.Equal(EventFamily.PlanetConjunction, reparsed.EventFamily);
        Assert.Equal(ImageProviderType.AzureImage2, reparsed.ProviderHints.PreferredProvider);
    }

    [Fact]
    public void Prompt_quality_publication_and_diagnostics_round_trip_with_enum_strings()
    {
        var report = new QualityReport
        {
            QualityReportId = "qr_2026_001",
            ProviderName = ImageProviderType.AzureImage2,
            RecommendedDecision = PublicationDecisionStatus.PublishWithWarning,
            DimensionScores = [new QualityDimensionScore { Name = QualityCategory.TextReadability, Score = 0.72, Passed = false, Findings = ["Small text"] }],
            Diagnostics = [new DiagnosticMessage { Severity = DiagnosticSeverity.Warning, Code = "text.small", Message = "Text may be small.", Category = QualityCategory.TextReadability }]
        };

        var json = JsonSerializer.Serialize(report, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.Contains("\"qualityReportVersion\":\"3.3H\"", json);
        Assert.Contains("\"providerName\":\"azureImage2\"", json);
        Assert.Contains("\"recommendedDecision\":\"publishWithWarning\"", json);
        Assert.Contains("\"severity\":\"warning\"", json);

        var reparsed = JsonSerializer.Deserialize<QualityReport>(json, VisualIntelligenceJson.CreateSerializerOptions());
        Assert.Equal(PublicationDecisionStatus.PublishWithWarning, reparsed!.RecommendedDecision);
        Assert.Equal(QualityCategory.TextReadability, reparsed.DimensionScores[0].Name);
    }

    [Fact]
    public void Feature_flag_keys_are_present_and_default_off()
    {
        var keys = new[]
        {
            VisualIntelligenceFeatureFlags.UseVisualCreativeDirector,
            VisualIntelligenceFeatureFlags.UseCDL,
            VisualIntelligenceFeatureFlags.UseCreativeDirectionContract,
            VisualIntelligenceFeatureFlags.UsePromptComposerV2,
            VisualIntelligenceFeatureFlags.UseProviderProfiles,
            VisualIntelligenceFeatureFlags.UseQualityScoring,
            VisualIntelligenceFeatureFlags.UseQualityScoringBlocking,
            VisualIntelligenceFeatureFlags.UseExperimentalRenderingRules
        };

        Assert.Contains("UseVisualCreativeDirector", keys);
        Assert.Contains("UseCDL", keys);
        Assert.Contains("UseCreativeDirectionContract", keys);
        Assert.Contains("UsePromptComposerV2", keys);
        Assert.Contains("UseProviderProfiles", keys);
        Assert.Contains("UseQualityScoring", keys);
        Assert.Contains("UseQualityScoringBlocking", keys);
        Assert.Contains("UseExperimentalRenderingRules", keys);

        var options = new VisualIntelligenceOptions();
        Assert.False(options.UseVisualCreativeDirector);
        Assert.False(options.UseQualityScoringBlocking);
    }

    [Fact]
    public void Prompt_package_and_publication_decision_have_expected_version_and_defaults()
    {
        var package = new PromptPackage();
        var decision = new PublicationDecision();

        Assert.Equal("3.2E", package.PromptComposerVersion);
        Assert.Equal("3.2E-azure-image2-v1", package.ProviderProfileVersion);
        Assert.Empty(package.Diagnostics);
        Assert.Equal(PublicationDecisionStatus.Unknown, decision.Decision);
        Assert.Null(decision.FallbackReason);
    }
}
