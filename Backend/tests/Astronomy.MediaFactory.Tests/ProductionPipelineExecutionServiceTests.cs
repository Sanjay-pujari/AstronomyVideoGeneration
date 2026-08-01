using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelineExecutionServiceTests
{
    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(false, 3, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, true)]
    [InlineData(true, 20, true)]
    public void GenericOverwriteCleanupPolicy_protects_phase1(bool overwriteExisting, int startPhaseNo, bool expected)
    {
        Assert.Equal(expected, ProductionPipelineExecutionService.ShouldRunGenericOverwriteCleanup(overwriteExisting, startPhaseNo));
    }

    [Fact]
    public void Protected_upstream_target_causes_RC2_UPSTREAM_PHASE_MUTATION_ATTEMPT()
    {
        var target = new PhaseOutputTarget(2, "/workspace/02-intelligence", true, "02-intelligence", "Authority", "Phase2", true, false, false, false, true);
        var error = Assert.Throws<InvalidOperationException>(() => UpstreamPhaseMutationGuard.AssertAllowed(3, target, "overwrite-cleanup"));
        Assert.Contains("RC2_UPSTREAM_PHASE_MUTATION_ATTEMPT", error.Message, StringComparison.Ordinal);
        Assert.Contains("startPhaseNo=3", error.Message, StringComparison.Ordinal);
        Assert.Contains("targetPhaseNo=2", error.Message, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("Solar Eclipse", "TOTAL SOLAR ECLIPSE", "A solar eclipse is rare and dramatic because Moon and Sun align from our viewpoint.")]
    [InlineData("Planetary Conjunction", "LOOK FOR JUPITER AND VENUS", "Jupiter and Venus form a conjunction, an apparent alignment in Udaipur sky.")]
    [InlineData("Meteor Shower", "DON'T MISS THIS PEAK", "Watch during 2026-08-12 23:30 using certified eye protection throughout.")]
    [InlineData("Lunar Eclipse", "WATCH THE BLOOD MOON", "The Moon will turn red when Earth blocks sunlight from reaching the lunar surface.")]
    [InlineData("Comet", "SEE THE COMET TONIGHT", "Look toward the western sky, about one-third above the horizon.")]
    [InlineData("Planetary Alignment", "LOOK UP TONIGHT", "Mars, Jupiter, Venus, and Saturn are spread across the morning sky because their orbits line up from our viewpoint.")]
    public void ValidateCinematicHeroOverlayText_UsesHookPolicyForEventFamilyHooks(string eventFamily, string validHook, string invalidHook)
    {
        var validDiagnostics = InvokeRoleValidation("Hook", validHook, "", eventFamily, 8);
        var invalidDiagnostics = InvokeRoleValidation("Narration", invalidHook, "", eventFamily, 80);

        Assert.True(ReadBool(validDiagnostics, "FinalDecision"));
        Assert.Equal("Hook", ReadString(validDiagnostics, "Role"));
        Assert.Equal("OverlayRole:Hook", ReadString(validDiagnostics, "Policy"));
        Assert.Equal(validHook.Replace('’', '\''), ReadString(validDiagnostics, "NormalizedHookText"));
        Assert.Equal(eventFamily, ReadString(validDiagnostics, "EventFamily"));
        Assert.InRange(ReadInt(validDiagnostics, "WordCount"), 1, 8);
        Assert.True(ReadBool(validDiagnostics, "FitsSafeArea"));
        Assert.False(ReadBool(validDiagnostics, "IsSentenceLike"));
        Assert.Equal(string.Empty, ReadString(validDiagnostics, "RejectedReason"));

        Assert.False(ReadBool(invalidDiagnostics, "FinalDecision"));
        Assert.Equal("Narration", ReadString(invalidDiagnostics, "Role"));
        Assert.Equal("Narration", ReadString(invalidDiagnostics, "Policy"));
        Assert.Equal(eventFamily, ReadString(invalidDiagnostics, "EventFamily"));
        Assert.True(ReadBool(invalidDiagnostics, "IsSentenceLike"));
        Assert.Contains("narration", ReadString(invalidDiagnostics, "RejectedReason"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("LOOK UP")]
    [InlineData("LOOK WEST")]
    [InlineData("LOOK FOR JUPITER")]
    [InlineData("LOOK FOR JUPITER AND VENUS")]
    [InlineData("WATCH THE BLOOD MOON")]
    [InlineData("PEAK NIGHT")]
    [InlineData("SEE VENUS AND JUPITER")]
    public void ValidateCinematicHeroOverlayText_DoesNotRejectShortGuideOrCtaLanguage(string hook)
    {
        var diagnostics = InvokeRoleValidation("Hook", hook, "", "Generic", 8);

        Assert.True(ReadBool(diagnostics, "FinalDecision"));
        Assert.True(ReadBool(diagnostics, "FitsSafeArea"));
        Assert.Equal(string.Empty, ReadString(diagnostics, "RejectedReason"));
    }


    [Fact]
    public void ValidateCinematicHeroOverlayText_UsesRenderedLayoutFitForPlanetConjunctionHookWithSubtitle()
    {
        var diagnostics = InvokeRoleValidationWithRenderedLayout(
            "Hook",
            "LOOK FOR JUPITER AND VENUS",
            "Planet conjunction in the western evening sky tonight",
            "Planetary Conjunction",
            8,
            true);

        Assert.True(ReadBool(diagnostics, "FinalDecision"));
        Assert.Equal(5, ReadInt(diagnostics, "WordCount"));
        Assert.True(ReadBool(diagnostics, "FitsSafeArea"));
        Assert.False(ReadBool(diagnostics, "IsSentenceLike"));
        Assert.Equal(string.Empty, ReadString(diagnostics, "RejectedReason"));
    }


    [Fact]
    public void ValidateCinematicHeroOverlayText_PlanetConjunctionLookForHookPassesWhenRenderedDiagnosticsPass()
    {
        var layoutPath = Path.Combine(Path.GetTempPath(), "hero-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(layoutPath, """
            {
              "isValid": true,
              "compositionReports": [
                { "variant": "Landscape", "status": "PASS", "issues": [], "requiresRegeneration": false }
              ],
              "heroOverlayDiagnostics": {
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleMetadataOverlap": false,
                "heroTextSafeAreaPassed": true,
                "safeArea": true
              }
            }
            """);

            var renderedFitsMethod = typeof(ProductionPipelineExecutionService).GetMethod("CinematicHeroRenderedLayoutFits", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(renderedFitsMethod);

            var renderedLayoutFits = (bool)renderedFitsMethod!.Invoke(null, new object?[] { layoutPath })!;
            var diagnostics = InvokeRoleValidationWithRenderedLayout(
                "Hook",
                "LOOK FOR JUPITER AND VENUS",
                "The conjunction will happen when the planets appear about one-third above the horizon",
                "PLANET_CONJUNCTION",
                8,
                renderedLayoutFits);

            Assert.True(renderedLayoutFits);
            Assert.True(ReadBool(diagnostics, "FinalDecision"));
            Assert.Equal("PLANET_CONJUNCTION", ReadString(diagnostics, "EventFamily"));
            Assert.Equal(5, ReadInt(diagnostics, "WordCount"));
            Assert.False(ReadBool(diagnostics, "IsSentenceLike"));
            Assert.True(ReadBool(diagnostics, "FitsSafeArea"));
            Assert.Equal(string.Empty, ReadString(diagnostics, "RejectedReason"));
        }
        finally
        {
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
        }
    }


    [Fact]
    public void ValidateCinematicHeroOverlayText_PlanetConjunctionHookPassesWithActualRenderedDiagnostics()
    {
        var layoutPath = Path.Combine(Path.GetTempPath(), "hero-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(layoutPath, """
            {
              "eventFamily": "PLANET_CONJUNCTION",
              "isValid": true,
              "compositionReports": [
                { "variant": "Landscape", "status": "PASS", "issues": [], "requiresRegeneration": false }
              ],
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": "false",
                "heroTitleSubtitleOverlap": "false",
                "heroTitleMetadataOverlap": "false",
                "heroTitleClipped": "false",
                "heroSubtitleClipped": "false",
                "heroTitleOverflowDetected": "false",
                "heroTextSafeAreaPassed": "true",
                "heroTitleSafeAreaPassed": "true",
                "safeArea": "true"
              }
            }
            """);

            var renderedFitsMethod = typeof(ProductionPipelineExecutionService).GetMethod("CinematicHeroRenderedLayoutFits", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(renderedFitsMethod);
            var renderedLayoutFits = (bool)renderedFitsMethod!.Invoke(null, new object?[] { layoutPath })!;

            var diagnostics = InvokeRoleValidationWithRenderedLayout(
                "Hook",
                "LOOK FOR JUPITER AND VENUS",
                "",
                "PLANET_CONJUNCTION",
                8,
                renderedLayoutFits);

            Assert.True(renderedLayoutFits);
            Assert.True(ReadBool(diagnostics, "FinalDecision"));
            Assert.Equal("PLANET_CONJUNCTION", ReadString(diagnostics, "EventFamily"));
            Assert.Equal(5, ReadInt(diagnostics, "WordCount"));
            Assert.False(ReadBool(diagnostics, "IsSentenceLike"));
            Assert.True(ReadBool(diagnostics, "IsGuideInstructionLike"));
            Assert.True(ReadBool(diagnostics, "FitsSafeArea"));
            Assert.Equal(string.Empty, ReadString(diagnostics, "RejectedReason"));
        }
        finally
        {
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
        }
    }

    [Fact]
    public void CinematicHeroRenderedLayoutFits_UsesCanonicalHeroOverlayDiagnosticsForPassingLayoutValidation()
    {
        var layoutPath = Path.Combine(Path.GetTempPath(), "hero-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(layoutPath, """
            {
              "isValid": true,
              "compositionReports": [
                { "variant": "Landscape", "status": "PASS", "issues": [], "requiresRegeneration": false }
              ],
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTextSafeAreaPassed": true,
                "heroTitleSafeAreaPassed": true,
                "safeArea": true
              }
            }
            """);

            var renderedFitsMethod = typeof(ProductionPipelineExecutionService).GetMethod("CinematicHeroRenderedLayoutFits", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(renderedFitsMethod);
            var renderedLayoutFits = (bool)renderedFitsMethod!.Invoke(null, new object?[] { layoutPath })!;
            var diagnostics = InvokeRoleValidationWithRenderedLayout(
                "Hook",
                "LOOK FOR JUPITER AND VENUS",
                "The conjunction will happen when the planets appear about one-third above the horizon",
                "PLANET_CONJUNCTION",
                8,
                renderedLayoutFits);

            Assert.True(renderedLayoutFits);
            Assert.True(ReadBool(diagnostics, "FinalDecision"));
            Assert.Equal(string.Empty, ReadString(diagnostics, "RejectedReason"));
        }
        finally
        {
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
        }
    }


    [Theory]
    [InlineData(false, false, true, true, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, false, false)]
    public void CinematicHeroRenderedLayoutFits_RejectsInvalidClippedOrUnsafeLayout(bool isValid, bool heroTitleClipped, bool heroTextSafeAreaPassed, bool compositionReportPasses, bool expected)
    {
        var layoutPath = Path.Combine(Path.GetTempPath(), "hero-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var status = compositionReportPasses ? "PASS" : "FAIL";
            File.WriteAllText(layoutPath, $$"""
            {
              "isValid": {{isValid.ToString().ToLowerInvariant()}},
              "compositionReports": [
                { "variant": "Landscape", "status": "{{status}}", "issues": [], "requiresRegeneration": false }
              ],
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": {{heroTitleClipped.ToString().ToLowerInvariant()}},
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTextSafeAreaPassed": {{heroTextSafeAreaPassed.ToString().ToLowerInvariant()}},
                "safeArea": true
              }
            }
            """);

            var renderedFitsMethod = typeof(ProductionPipelineExecutionService).GetMethod("CinematicHeroRenderedLayoutFits", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(renderedFitsMethod);

            var renderedLayoutFits = (bool)renderedFitsMethod!.Invoke(null, new object?[] { layoutPath })!;

            Assert.Equal(expected, renderedLayoutFits);
        }
        finally
        {
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
        }
    }


    [Fact]
    public void ValidateHeroVisualStyle_TrustsPassingHeroLayoutValidationContractForPlanetConjunctionHook()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hero-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var compositionPath = Path.Combine(tempRoot, "hero-composition-model.json");
        var blueprintPath = Path.Combine(tempRoot, "hero-asset-blueprint.json");
        var layoutPath = Path.Combine(tempRoot, "hero-layout-validation.json");

        try
        {
            File.WriteAllText(compositionPath, """
            {
              "hookBlock": { "text": "LOOK FOR JUPITER AND VENUS" },
              "directionBlock": { "text": "The conjunction will happen when the planets appear about one-third above the horizon" },
              "timingBlock": { "text": "Tonight after sunset" },
              "ctaBlock": { "text": "Look west" }
            }
            """);
            File.WriteAllText(blueprintPath, """
            {
              "heroContract": "CinematicHero",
              "eventFamily": "PLANET_CONJUNCTION"
            }
            """);
            File.WriteAllText(layoutPath, """
            {
              "eventFamily": "PLANET_CONJUNCTION",
              "isValid": true,
              "variants": [
                { "variant": "Landscape", "fileName": "hero-final.png" }
              ],
              "compositionReports": [
                { "variant": "Landscape", "status": "PASS", "issues": [], "requiresRegeneration": false }
              ],
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTextSafeAreaPassed": true,
                "heroTitleSafeAreaPassed": true,
                "safeArea": true
              }
            }
            """);

            var method = GetPrivateStaticMethod("ValidateHeroVisualStyle", typeof(string), typeof(string), typeof(string), typeof(bool));
            var exception = Record.Exception(() => method.Invoke(null, [compositionPath, blueprintPath, layoutPath, true]));

            Assert.Null(exception);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }


    [Fact]
    public void ValidateHeroVisualStyle_TreatsGuideBlocksAsGuideHeroBeforeCinematicRendererSelection()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hero-guide-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var compositionPath = Path.Combine(tempRoot, "hero-composition-model.json");
        var blueprintPath = Path.Combine(tempRoot, "hero-asset-blueprint.json");
        var layoutPath = Path.Combine(tempRoot, "hero-layout-validation.json");

        try
        {
            File.WriteAllText(compositionPath, "{}");
            File.WriteAllText(blueprintPath, "{}");
            File.WriteAllText(layoutPath, """
            {
              "rendererPathSelected": "AzureHeroRendererV2",
              "isValid": true,
              "renderedBlocks": ["Title", "Subtitle", "Direction", "Timing", "CTA"],
              "variants": [
                { "variant": "Landscape", "fileName": "hero-final.png" }
              ],
              "compositionReports": [
                { "variant": "Landscape", "status": "PASS", "issues": [], "requiresRegeneration": false }
              ],
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTextSafeAreaPassed": true,
                "heroTitleSafeAreaPassed": true,
                "safeArea": true
              }
            }
            """);

            var method = GetPrivateStaticMethod("ValidateHeroVisualStyle", typeof(string), typeof(string), typeof(string), typeof(bool));
            var exception = Record.Exception(() => method.Invoke(null, [compositionPath, blueprintPath, layoutPath, true]));

            Assert.Null(exception);
            using var doc = JsonDocument.Parse(File.ReadAllText(layoutPath));
            Assert.Equal("GuideHero", doc.RootElement.GetProperty("heroContract").GetString());
            Assert.Equal("GuideHero", doc.RootElement.GetProperty("validatorContract").GetString());
            Assert.Equal("GuideHero", doc.RootElement.GetProperty("validationProfileUsed").GetString());
            Assert.Empty(doc.RootElement.GetProperty("forbiddenBlocks").EnumerateArray());
            Assert.Equal(string.Empty, doc.RootElement.GetProperty("failureBranchName").GetString());
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateHeroVisualStyle_DefaultsEmptyHeroContractToGuideHero()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hero-default-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var compositionPath = Path.Combine(tempRoot, "hero-composition-model.json");
        var blueprintPath = Path.Combine(tempRoot, "hero-asset-blueprint.json");
        var layoutPath = Path.Combine(tempRoot, "hero-layout-validation.json");

        try
        {
            File.WriteAllText(compositionPath, "{}");
            File.WriteAllText(blueprintPath, "{}");
            File.WriteAllText(layoutPath, PassingLayoutJson("""
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTextSafeAreaPassed": true,
                "heroTitleSafeAreaPassed": true,
                "safeArea": true
              },
            """));

            var method = GetPrivateStaticMethod("ValidateHeroVisualStyle", typeof(string), typeof(string), typeof(string), typeof(bool));
            var exception = Record.Exception(() => method.Invoke(null, [compositionPath, blueprintPath, layoutPath, true]));

            Assert.Null(exception);
            using var doc = JsonDocument.Parse(File.ReadAllText(layoutPath));
            Assert.Equal("GuideHero", doc.RootElement.GetProperty("heroContract").GetString());
            Assert.Equal("GuideHero", doc.RootElement.GetProperty("validatorContract").GetString());
            Assert.Equal("GuideHero", doc.RootElement.GetProperty("validationProfileUsed").GetString());
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }


    [Fact]
    public void SummarizeHeroLayoutValidation_FallsBackToHeroGenerationDiagnosticsWhenLayoutOmitsOverlayDiagnostics()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hero-summary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var layoutPath = Path.Combine(tempRoot, "hero-layout-validation.json");
            File.WriteAllText(layoutPath, PassingLayoutJson(heroOverlayDiagnostics: null));
            File.WriteAllText(Path.Combine(tempRoot, "hero-generation-diagnostics.json"), """
            {
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTitleSafeAreaPassed": true,
                "safeArea": true
              }
            }
            """);

            var summary = InvokeHeroLayoutSummary(layoutPath);

            Assert.True(ReadBool(summary, "Passed"));
            Assert.Contains("hero-generation-diagnostics.json", ReadString(summary, "Summary"));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SummarizeHeroLayoutValidation_AcceptsEquivalentSafeAreaSignalsWhenHeroTextSafeAreaIsMissing()
    {
        var layoutPath = Path.Combine(Path.GetTempPath(), "hero-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(layoutPath, PassingLayoutJson("""
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTitleSafeAreaPassed": true,
                "safeArea": true
              },
            """));

            var summary = InvokeHeroLayoutSummary(layoutPath);

            Assert.True(ReadBool(summary, "Passed"));
        }
        finally
        {
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
        }
    }

    [Theory]
    [InlineData("heroTitleClipped", true)]
    [InlineData("heroTitleMetadataOverlap", true)]
    public void SummarizeHeroLayoutValidation_RejectsClippingOverflowOrOverlap(string diagnosticName, bool diagnosticValue)
    {
        var layoutPath = Path.Combine(Path.GetTempPath(), "hero-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(layoutPath, PassingLayoutJson($$"""
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTitleSafeAreaPassed": true,
                "safeArea": true,
                "{{diagnosticName}}": {{diagnosticValue.ToString().ToLowerInvariant()}}
              },
            """));

            var summary = InvokeHeroLayoutSummary(layoutPath);

            Assert.False(ReadBool(summary, "Passed"));
        }
        finally
        {
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
        }
    }

    [Fact]
    public void SummarizeHeroLayoutValidation_RejectsInvalidLayoutEvenWhenDiagnosticsPass()
    {
        var layoutPath = Path.Combine(Path.GetTempPath(), "hero-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(layoutPath, PassingLayoutJson("""
              "heroOverlayDiagnostics": {
                "heroTextOverlapDetected": false,
                "heroTitleSubtitleOverlap": false,
                "heroTitleMetadataOverlap": false,
                "heroTitleClipped": false,
                "heroSubtitleClipped": false,
                "heroTitleOverflowDetected": false,
                "heroTitleSafeAreaPassed": true,
                "safeArea": true
              },
            """, isValid: false));

            var summary = InvokeHeroLayoutSummary(layoutPath);

            Assert.False(ReadBool(summary, "Passed"));
        }
        finally
        {
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
        }
    }

    [Fact]
    public void ResolveHeroEventFamily_UsesLayoutValidationEventFamilyWhenCompositionAndBlueprintOmitIt()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hero-family-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var blueprintPath = Path.Combine(tempRoot, "blueprint.json");
        var compositionPath = Path.Combine(tempRoot, "composition.json");
        var layoutPath = Path.Combine(tempRoot, "layout.json");

        try
        {
            File.WriteAllText(blueprintPath, "{}");
            File.WriteAllText(compositionPath, "{}");
            File.WriteAllText(layoutPath, """
            {
              "validation": {
                "eventFamily": "PLANET_CONJUNCTION"
              }
            }
            """);

            var method = GetPrivateStaticMethod("ResolveHeroEventFamily", typeof(string[]));
            var eventFamily = (string)method.Invoke(null, new object[] { new[] { blueprintPath, compositionPath, layoutPath } })!;

            Assert.Equal("PLANET_CONJUNCTION", eventFamily);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateCinematicHeroOverlayText_NarrationRoleRejectsLongNarrationSentenceEvenWhenRenderedLayoutFits()
    {
        var diagnostics = InvokeRoleValidationWithRenderedLayout(
            "Narration",
            "Jupiter and Venus form a conjunction, an apparent alignment in the western evening sky.",
            "",
            "Planetary Conjunction",
            80,
            true);

        Assert.False(ReadBool(diagnostics, "FinalDecision"));
        Assert.True(ReadBool(diagnostics, "IsSentenceLike"));
        Assert.Contains("narration", ReadString(diagnostics, "RejectedReason"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Solar Eclipse", "LOOK FOR THE ECLIPSE")]
    [InlineData("Planetary Conjunction", "LOOK FOR JUPITER AND VENUS")]
    [InlineData("Meteor Shower", "LOOK FOR METEORS")]
    [InlineData("Lunar Eclipse", "LOOK FOR THE BLOOD MOON")]
    [InlineData("Comet", "LOOK FOR THE COMET")]
    [InlineData("Planetary Alignment", "LOOK FOR PLANETS")]
    public void ValidateCinematicHeroOverlayText_EventFamilyHooksUseHookValidatorNotNarrationValidator(string eventFamily, string hook)
    {
        var diagnostics = InvokeRoleValidationWithRenderedLayout("Hook", hook, "", eventFamily, 8, true);

        Assert.True(ReadBool(diagnostics, "FinalDecision"));
        Assert.Equal("Hook", ReadString(diagnostics, "Role"));
        Assert.Equal("OverlayRole:Hook", ReadString(diagnostics, "Policy"));
        Assert.False(ReadBool(diagnostics, "IsSentenceLike"));
        Assert.Equal(eventFamily, ReadString(diagnostics, "EventFamily"));
    }

    private static object InvokeRoleValidation(string roleName, string text, string visibleText, string eventFamily, int maxWords)
    {
        var role = ParseHeroOverlayRole(roleName);
        var method = GetPrivateStaticMethod("ValidateCinematicHeroOverlayText", role.GetType(), typeof(string), typeof(string), typeof(string), typeof(int));
        return method.Invoke(null, [role, text, visibleText, eventFamily, maxWords])!;
    }

    private static object InvokeRoleValidationWithRenderedLayout(string roleName, string text, string visibleText, string eventFamily, int maxWords, bool renderedLayoutFits)
    {
        var role = ParseHeroOverlayRole(roleName);
        var method = GetPrivateStaticMethod("ValidateCinematicHeroOverlayTextWithRenderedLayout", role.GetType(), typeof(string), typeof(string), typeof(string), typeof(int), typeof(bool));
        return method.Invoke(null, [role, text, visibleText, eventFamily, maxWords, renderedLayoutFits])!;
    }


    private static object InvokeHeroLayoutSummary(string layoutPath)
    {
        var method = GetPrivateStaticMethod("SummarizeHeroLayoutValidation", typeof(string));
        return method.Invoke(null, [layoutPath])!;
    }

    private static string PassingLayoutJson(string? heroOverlayDiagnostics, bool isValid = true)
        => $$"""
        {
          "eventFamily": "PLANET_CONJUNCTION",
          "isValid": {{isValid.ToString().ToLowerInvariant()}},
          {{heroOverlayDiagnostics ?? string.Empty}}
          "variants": [
            { "variant": "Landscape", "fileName": "hero-final.png" }
          ],
          "compositionReports": [
            { "variant": "Landscape", "status": "PASS", "issues": [], "requiresRegeneration": false }
          ],
          "objectsVisible": true,
          "errors": [],
          "contractMismatch": false
        }
        """;

    private static MethodInfo GetPrivateStaticMethod(string name, params Type[] parameterTypes)
    {
        var methods = typeof(ProductionPipelineExecutionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(candidate => candidate.Name == name)
            .Where(candidate => candidate.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .SequenceEqual(parameterTypes))
            .ToArray();

        Assert.True(
            methods.Length == 1,
            $"Expected exactly one private static overload for {name}(" +
            string.Join(", ", parameterTypes.Select(type => type.Name)) +
            $"), but found {methods.Length}.");
        return methods[0];
    }

    private static MethodInfo GetPrivateInstanceMethod(string name, params Type[] parameterTypes)
    {
        var methods = typeof(ProductionPipelineExecutionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(candidate => candidate.Name == name)
            .Where(candidate => candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes))
            .ToArray();

        Assert.True(
            methods.Length == 1,
            $"Expected exactly one private instance overload for {name}(" +
            string.Join(", ", parameterTypes.Select(type => type.Name)) +
            $"), but found {methods.Length}.");
        return methods[0];
    }

    private static System.Collections.IEnumerable InvokeReadVideoAssemblyItems(
        string planRoot, string language, JsonNode motionRoot, JsonNode ttsRoot, string format, int expectedCount,
        IReadOnlyList<string> oldPaths, List<string> missingSceneImages, List<string> missingAudioFiles, List<string> oldPathUsageReasons)
    {
        var serviceType = typeof(ProductionPipelineExecutionService);
        var manifestType = serviceType.GetNestedType("StoryFrameV4Manifest", BindingFlags.NonPublic)!;
        var resolvedMappingListType = typeof(List<>).MakeGenericType(serviceType.GetNestedType("StoryFrameV4ResolvedFrameMapping", BindingFlags.NonPublic)!);
        var unresolvedSceneListType = typeof(List<>).MakeGenericType(serviceType.GetNestedType("StoryFrameV4UnresolvedScene", BindingFlags.NonPublic)!);
        var method = GetPrivateStaticMethod("ReadVideoAssemblyItems", typeof(string), typeof(string), typeof(JsonNode), typeof(JsonNode), typeof(string), typeof(int), typeof(IReadOnlyList<string>), typeof(List<string>), typeof(List<string>), typeof(List<string>), typeof(bool), manifestType, resolvedMappingListType, unresolvedSceneListType);

        return (System.Collections.IEnumerable)method.Invoke(null,
            [planRoot, language, motionRoot, ttsRoot, format, expectedCount, oldPaths, missingSceneImages, missingAudioFiles, oldPathUsageReasons,
             false, null, Activator.CreateInstance(resolvedMappingListType), Activator.CreateInstance(unresolvedSceneListType)])!;
    }

    private static object ParseHeroOverlayRole(string roleName)
    {
        var roleType = typeof(ProductionPipelineExecutionService).GetNestedType("HeroOverlayRole", BindingFlags.NonPublic);
        Assert.NotNull(roleType);
        return Enum.Parse(roleType!, roleName);
    }

    private static bool ReadBool(object source, string propertyName)
        => (bool)source.GetType().GetProperty(propertyName)!.GetValue(source)!;

    private static int ReadInt(object source, string propertyName)
        => (int)source.GetType().GetProperty(propertyName)!.GetValue(source)!;

    private static string ReadString(object source, string propertyName)
        => (string)source.GetType().GetProperty(propertyName)!.GetValue(source)!;

    private static string[] NormalizeNarrationTokens(string value)
        => Regex.Split(
                Regex.Replace(value, @"\s+", " ").Trim(),
                @"\s+")
            .Where(token => token.Length > 0)
            .ToArray();

    [Fact]
    public void ResolvePhase15SrtPath_PrefersLanguageScopedSrtOverLegacyUnscopedPath()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase15-srt-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scopedRoot = Path.Combine(planRoot, "narration", "subtitles", "en");
            var legacyRoot = Path.Combine(planRoot, "narration", "subtitles");
            Directory.CreateDirectory(scopedRoot);
            File.WriteAllText(Path.Combine(scopedRoot, "short.srt"), "scoped");
            File.WriteAllText(Path.Combine(legacyRoot, "short.srt"), "legacy");

            var method = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15SrtPath", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var result = (string)method!.Invoke(null, new object?[] { planRoot, "en", "short" })!;

            Assert.Equal(Path.Combine(planRoot, "narration", "subtitles", "en", "short.srt"), result);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void ResolvePhase15SrtPath_UsesCanonicalLanguageScopedPathEvenWhenLegacyUnscopedExists()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase15-srt-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacyRoot = Path.Combine(planRoot, "narration", "subtitles");
            Directory.CreateDirectory(legacyRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "long.srt"), "legacy");

            var method = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15SrtPath", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var result = (string)method!.Invoke(null, new object?[] { planRoot, "en", "long" })!;

            Assert.Equal(Path.Combine(planRoot, "narration", "subtitles", "en", "long.srt"), result);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void FirstNonEmpty_ReturnsEmptyString_WhenAllCandidatesAreMissing()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("FirstNonEmpty", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object?[] { new string?[] { null, "", "   " } });

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Phase19CinematicDiagnostics_TrustsPhase18VideoDiagnostics()
    {
        var diagnostics = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            cinematicOutroEnabled = true,
            cinematicOutroDurationSec = 4.0,
            fadeToBlackEnabled = true,
            fadeToBlackDurationSec = 1.0
        }));

        Assert.True(InvokePhase18DiagnosticsValidator("IsPhase18CinematicOutroValidated", diagnostics));
        Assert.True(InvokePhase18DiagnosticsValidator("IsPhase18FadeToBlackValidated", diagnostics));
    }

    [Fact]
    public void Phase19CinematicDiagnostics_RejectsInsufficientPhase18Durations()
    {
        var diagnostics = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            cinematicOutroEnabled = true,
            cinematicOutroDurationSec = 3.99,
            fadeToBlackEnabled = true,
            fadeToBlackDurationSec = 0.99
        }));

        Assert.False(InvokePhase18DiagnosticsValidator("IsPhase18CinematicOutroValidated", diagnostics));
        Assert.False(InvokePhase18DiagnosticsValidator("IsPhase18FadeToBlackValidated", diagnostics));
    }



    [Fact]
    public void Phase15VisualSceneIdResolution_MapsCueIdsBackToNarrationSceneFiles()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase15-scene-id-map-" + Guid.NewGuid().ToString("N"));
        try
        {
            var narrationRoot = Path.Combine(planRoot, "narration", "short");
            var metadataRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
            Directory.CreateDirectory(narrationRoot);
            Directory.CreateDirectory(metadataRoot);
            File.WriteAllText(Path.Combine(metadataRoot, "scene-timeline-metadata.json"), JsonSerializer.Serialize(new
            {
                scenes = new[]
                {
                    new { sceneId = "001-hook" },
                    new { sceneId = "002-cause" }
                }
            }));
            File.WriteAllText(Path.Combine(narrationRoot, "001-hook.txt"), "Look west after sunset for the bright Moon near the horizon. Keep watching as the sky darkens and nearby stars appear. Use binoculars only after you have found the scene with your eyes.");
            File.WriteAllText(Path.Combine(narrationRoot, "002-cause.txt"), "This happens because orbits line up in our sky.");

            var srt = """
1
00:00:00,000 --> 00:00:01,000
Look west after sunset for the bright Moon near the horizon.

2
00:00:01,000 --> 00:00:02,000
Keep watching as the sky darkens and nearby stars appear.

3
00:00:02,000 --> 00:00:03,000
Use binoculars only after you have found the scene with your eyes.

4
00:00:03,000 --> 00:00:04,000
This happens because orbits line up in our sky.
""";
            var parseMethod = typeof(ProductionPipelineExecutionService).GetMethod("ParseSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var resolveMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdsForSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseMethod);
            Assert.NotNull(resolveMethod);

            var blocks = parseMethod!.Invoke(null, [srt]);
            var sceneIds = ((System.Collections.IEnumerable)resolveMethod!.Invoke(null, [planRoot, "en", "short", blocks])!).Cast<string>().ToArray();

            Assert.Equal(new[] { "001-hook", "001-hook", "001-hook", "002-cause" }, sceneIds);
            Assert.Equal(4, sceneIds.Length);
            Assert.Equal(2, sceneIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void Phase15VisualSceneIdResolution_UsesLocalizedNarrationSceneFiles()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase15-hi-scene-id-map-" + Guid.NewGuid().ToString("N"));
        try
        {
            var narrationRoot = Path.Combine(planRoot, "narration", "hi", "short");
            var metadataRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
            Directory.CreateDirectory(narrationRoot);
            Directory.CreateDirectory(metadataRoot);
            File.WriteAllText(Path.Combine(metadataRoot, "scene-timeline-metadata.json"), JsonSerializer.Serialize(new
            {
                scenes = new[]
                {
                    new { sceneId = "001-hook" },
                    new { sceneId = "002-cause" },
                    new { sceneId = "003-guide" },
                    new { sceneId = "004-time" },
                    new { sceneId = "005-close" }
                }
            }));

            File.WriteAllText(Path.Combine(narrationRoot, "001-hook.txt"), "पहला दृश्य शुरू होता है। आसमान धीरे धीरे बदलता है।");
            File.WriteAllText(Path.Combine(narrationRoot, "002-cause.txt"), "दूसरा दृश्य कारण समझाता है। यह दूरी का भ्रम है।");
            File.WriteAllText(Path.Combine(narrationRoot, "003-guide.txt"), "तीसरा दृश्य दिशा बताता है। पश्चिम की ओर देखें।");
            File.WriteAllText(Path.Combine(narrationRoot, "004-time.txt"), "चौथा दृश्य सही समय बताता है। सूर्यास्त के बाद रुकें।");
            File.WriteAllText(Path.Combine(narrationRoot, "005-close.txt"), "पांचवां दृश्य यादगार अंत देता है। फिर आसमान को याद रखें।");

            var srt = """
1
00:00:00,000 --> 00:00:01,000
पहला दृश्य शुरू होता है।

2
00:00:01,000 --> 00:00:02,000
आसमान धीरे धीरे बदलता है।

3
00:00:02,000 --> 00:00:03,000
दूसरा दृश्य कारण समझाता है।

4
00:00:03,000 --> 00:00:04,000
यह दूरी का भ्रम है।

5
00:00:04,000 --> 00:00:05,000
तीसरा दृश्य दिशा बताता है।

6
00:00:05,000 --> 00:00:06,000
पश्चिम की ओर देखें।

7
00:00:06,000 --> 00:00:07,000
चौथा दृश्य सही समय बताता है।

8
00:00:07,000 --> 00:00:08,000
सूर्यास्त के बाद रुकें।

9
00:00:08,000 --> 00:00:09,000
पांचवां दृश्य यादगार अंत देता है।

10
00:00:09,000 --> 00:00:10,000
फिर आसमान को याद रखें।
""";
            var parseMethod = typeof(ProductionPipelineExecutionService).GetMethod("ParseSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var resolveMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdsForSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseMethod);
            Assert.NotNull(resolveMethod);

            var blocks = parseMethod!.Invoke(null, [srt]);
            var sceneIds = ((System.Collections.IEnumerable)resolveMethod!.Invoke(null, [planRoot, "hi", "short", blocks])!).Cast<string>().ToArray();

            Assert.Equal(10, sceneIds.Length);
            Assert.Equal(5, sceneIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(new[] { "001-hook", "001-hook", "002-cause", "002-cause", "003-guide", "003-guide", "004-time", "004-time", "005-close", "005-close" }, sceneIds);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Theory]
    [InlineData("SolarEclipse", "en", "long", "004-eclipse-geometry")]
    [InlineData("MoonEvent", "hi", "short", "001-hook")]
    [InlineData("MeteorShower", "en", "long", "006-radiant-guide")]
    [InlineData("PlanetGrouping", "en", "short", "003-guide")]
    [InlineData("GenericAstronomyEvent", "en", "short", "002-cause")]
    public void Phase15VisualSceneIdResolution_UsesNarrationFileLineageForEventFamilies(string eventFamily, string language, string format, string sceneId)
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase15-event-family-scene-id-map-" + Guid.NewGuid().ToString("N"));
        try
        {
            var narrationRoot = Path.Combine(planRoot, "narration", language, format);
            var metadataRoot = Path.Combine(planRoot, "scene-assets-v3", format);
            Directory.CreateDirectory(narrationRoot);
            Directory.CreateDirectory(metadataRoot);
            File.WriteAllText(Path.Combine(metadataRoot, "scene-timeline-metadata.json"), JsonSerializer.Serialize(new
            {
                scenes = new[] { new { sceneId } }
            }));
            File.WriteAllText(Path.Combine(narrationRoot, $"{sceneId}.txt"), "First cue for the visual scene. Second cue for the same visual scene.");

            var srt = """
1
00:00:00,000 --> 00:00:01,000
First cue for the visual scene.

2
00:00:01,000 --> 00:00:02,000
Second cue for the same visual scene.
""";
            var parseMethod = typeof(ProductionPipelineExecutionService).GetMethod("ParseSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var resolveMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdsForSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var lineageMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdLineage", BindingFlags.NonPublic | BindingFlags.Static);
            var validateMethod = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase15SceneIdLineage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseMethod);
            Assert.NotNull(resolveMethod);
            Assert.NotNull(lineageMethod);
            Assert.NotNull(validateMethod);

            var blocks = parseMethod!.Invoke(null, [srt]);
            var sceneIds = ((System.Collections.IEnumerable)resolveMethod!.Invoke(null, [planRoot, language, format, blocks])!).Cast<string>().ToArray();
            var lineage = lineageMethod!.Invoke(null, [planRoot, language, format, blocks]);
            var errors = ((System.Collections.IEnumerable)validateMethod!.Invoke(null, [eventFamily, language, format, lineage])!).Cast<string>().ToArray();

            Assert.Equal(new[] { sceneId, sceneId }, sceneIds);
            Assert.Empty(errors);
            Assert.DoesNotContain(sceneIds, id => Regex.IsMatch(id, @"^\d+$"));
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void Phase15SceneIdLineageValidation_RejectsNumericCueSceneIdsThatDoNotMatchVisualScenes()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase15-numeric-scene-id-reject-" + Guid.NewGuid().ToString("N"));
        try
        {
            var metadataRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
            Directory.CreateDirectory(metadataRoot);
            File.WriteAllText(Path.Combine(metadataRoot, "scene-timeline-metadata.json"), JsonSerializer.Serialize(new
            {
                scenes = new[] { new { sceneId = "001-hook" } }
            }));

            var srt = """
1
00:00:00,000 --> 00:00:01,000
First cue.
""";
            var parseMethod = typeof(ProductionPipelineExecutionService).GetMethod("ParseSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var lineageMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdLineage", BindingFlags.NonPublic | BindingFlags.Static);
            var validateMethod = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase15SceneIdLineage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseMethod);
            Assert.NotNull(lineageMethod);
            Assert.NotNull(validateMethod);

            var blocks = parseMethod!.Invoke(null, [srt]);
            var lineage = lineageMethod!.Invoke(null, [planRoot, "en", "short", blocks]);
            var errors = ((System.Collections.IEnumerable)validateMethod!.Invoke(null, ["SolarEclipse", "en", "short", lineage])!).Cast<string>().ToArray();

            Assert.Contains(errors, error => error.Contains("numeric cue sceneId", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(errors, error => error.Contains("missingVisualSceneIds=[001-hook]", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(errors, error => error.Contains("extraTimelineSceneIds=[1]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }


    [Fact]
    public void Phase15SceneIdLineageResolution_MapsNumericSrtCuesBySceneDurationRanges()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase15-duration-range-lineage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var metadataRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
            var timingRoot = Path.Combine(planRoot, "timing");
            Directory.CreateDirectory(metadataRoot);
            Directory.CreateDirectory(timingRoot);
            var visualSceneIds = new[] { "001-hook", "002-cause" };
            File.WriteAllText(Path.Combine(metadataRoot, "scene-timeline-metadata.json"), JsonSerializer.Serialize(new
            {
                scenes = visualSceneIds.Select(sceneId => new { sceneId }).ToArray()
            }));
            File.WriteAllText(Path.Combine(timingRoot, "scene-duration-plan.json"), JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { sceneId = "001-hook", audioDurationSec = 2.0, sceneDurationSec = 2.0 },
                        new { sceneId = "002-cause", audioDurationSec = 3.0, sceneDurationSec = 3.0 }
                    }
                }
            }));

            var srt = """
1
00:00:00,000 --> 00:00:01,000
First cue.

2
00:00:01,000 --> 00:00:02,000
Second cue.

3
00:00:02,000 --> 00:00:03,500
Third cue.

4
00:00:03,500 --> 00:00:05,000
Fourth cue.
""";
            var parseMethod = typeof(ProductionPipelineExecutionService).GetMethod("ParseSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var resolveMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdsForSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var lineageMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdLineage", BindingFlags.NonPublic | BindingFlags.Static);
            var validateMethod = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase15SceneIdLineage", BindingFlags.NonPublic | BindingFlags.Static);
            var diagnosticsMethod = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase15SceneIdLineageDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseMethod);
            Assert.NotNull(resolveMethod);
            Assert.NotNull(lineageMethod);
            Assert.NotNull(validateMethod);
            Assert.NotNull(diagnosticsMethod);

            var blocks = parseMethod!.Invoke(null, [srt]);
            var sceneIds = ((System.Collections.IEnumerable)resolveMethod!.Invoke(null, [planRoot, "en", "short", blocks])!).Cast<string>().ToArray();
            var lineage = lineageMethod!.Invoke(null, [planRoot, "en", "short", blocks]);
            var errors = ((System.Collections.IEnumerable)validateMethod!.Invoke(null, ["MeteorShower", "en", "short", lineage])!).Cast<string>().ToArray();
            var diagnosticsJson = JsonSerializer.Serialize(diagnosticsMethod!.Invoke(null, ["MeteorShower", "en", "short", lineage]));
            var diagnostics = JsonNode.Parse(diagnosticsJson)!;
            var cues = diagnostics["cues"]!.AsArray();

            Assert.Equal(new[] { "001-hook", "001-hook", "002-cause", "002-cause" }, sceneIds);
            Assert.Empty(errors);
            Assert.Equal(visualSceneIds, diagnostics["distinctTimelineSceneIds"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
            Assert.Equal("1", cues[0]!["cueId"]!.GetValue<string>());
            Assert.Equal("001-hook", cues[0]!["parentSceneId"]!.GetValue<string>());
            Assert.Equal("001-hook", cues[0]!["visualSceneId"]!.GetValue<string>());
            Assert.Equal("001-hook", cues[0]!["resolvedParentSceneId"]!.GetValue<string>());
            Assert.Equal("scene-duration-range", cues[0]!["mappingSource"]!.GetValue<string>());
            Assert.True(cues[0]!["numericCueIdIgnoredForSceneLineage"]!.GetValue<bool>());
            Assert.False(cues[0]!["numericSceneIdRejected"]!.GetValue<bool>());
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }


    [Fact]
    public void Phase15VisualSceneIdResolution_MapsHindiOptionsBasedCuesToParentVisualScenes()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase15-hi-options-lineage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var narrationRoot = Path.Combine(planRoot, "narration", "hi", "short");
            var metadataRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
            Directory.CreateDirectory(narrationRoot);
            Directory.CreateDirectory(metadataRoot);
            var visualSceneIds = new[] { "001-hook", "002-cause" };
            File.WriteAllText(Path.Combine(metadataRoot, "scene-timeline-metadata.json"), JsonSerializer.Serialize(new
            {
                scenes = visualSceneIds.Select(sceneId => new { sceneId }).ToArray()
            }));
            File.WriteAllText(Path.Combine(narrationRoot, "001-hook.txt"), "आज रात आसमान में उल्का वर्षा बेहद चमकीली दिखेगी सब लोग देखेंगे");
            File.WriteAllText(Path.Combine(narrationRoot, "002-cause.txt"), "धरती जब धूल की धारा से गुजरती है तब उल्काएं चमकती हैं खूब");

            var srt = """
1
00:00:00,000 --> 00:00:01,500
आज रात आसमान में उल्का वर्षा बेहद

2
00:00:01,500 --> 00:00:03,000
चमकीली दिखेगी सब लोग देखेंगे

3
00:00:03,000 --> 00:00:04,500
धरती जब धूल की धारा से गुजरती है

4
00:00:04,500 --> 00:00:06,000
तब उल्काएं चमकती हैं खूब
""";
            var parseMethod = typeof(ProductionPipelineExecutionService).GetMethod("ParseSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var resolveMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdsForSrtBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var lineageMethod = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase15VisualSceneIdLineage", BindingFlags.NonPublic | BindingFlags.Static);
            var validateMethod = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase15SceneIdLineage", BindingFlags.NonPublic | BindingFlags.Static);
            var diagnosticsMethod = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase15SceneIdLineageDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseMethod);
            Assert.NotNull(resolveMethod);
            Assert.NotNull(lineageMethod);
            Assert.NotNull(validateMethod);
            Assert.NotNull(diagnosticsMethod);

            var blocks = parseMethod!.Invoke(null, [srt]);
            var sceneIds = ((System.Collections.IEnumerable)resolveMethod!.Invoke(null, [planRoot, "hi", "short", blocks])!).Cast<string>().ToArray();
            var lineage = lineageMethod!.Invoke(null, [planRoot, "hi", "short", blocks]);
            var errors = ((System.Collections.IEnumerable)validateMethod!.Invoke(null, ["MeteorShower", "hi", "short", lineage])!).Cast<string>().ToArray();
            var diagnosticsJson = JsonSerializer.Serialize(diagnosticsMethod!.Invoke(null, ["MeteorShower", "hi", "short", lineage]));
            var diagnostics = JsonNode.Parse(diagnosticsJson)!;
            var distinctTimelineSceneIds = diagnostics["distinctTimelineSceneIds"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

            Assert.Equal(new[] { "001-hook", "001-hook", "002-cause", "002-cause" }, sceneIds);
            Assert.Equal(visualSceneIds, distinctTimelineSceneIds);
            Assert.Empty(errors);
            Assert.DoesNotContain(sceneIds, id => Regex.IsMatch(id, @"^\d+$"));
            var cues = diagnostics["cues"]!.AsArray();
            Assert.Equal("1", cues[0]!["cueId"]!.GetValue<string>());
            Assert.Equal("001-hook", cues[0]!["parentSceneId"]!.GetValue<string>());
            Assert.Equal("001-hook", cues[0]!["visualSceneId"]!.GetValue<string>());
            Assert.Contains("options-based", cues[0]!["cueSceneMappingSource"]!.GetValue<string>());
            Assert.Equal("OptionsBased", diagnostics["subtitleSplitter"]!.GetValue<string>());
            Assert.Equal(4, diagnostics["reconstructedCueCount"]!.GetValue<int>());
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void Phase16SubtitleRegeneration_UsesCueLevelTtsTimelineDurations()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase16-tts-srt-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts"));
            var timelinePath = Path.Combine(planRoot, "tts", "tts-timeline.json");
            File.WriteAllText(timelinePath, JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001", cueIndex = 1, cueText = "First cue.", audioDurationSec = 5.352 },
                        new { format = "short", sceneId = "002", cueIndex = 2, cueText = "Second cue.", audioDurationSec = 1.25 }
                    }
                },
                @long = new
                {
                    items = new[]
                    {
                        new { format = "long", sceneId = "001", cueIndex = 1, cueText = "Long first cue.", audioDurationSec = 2.0 }
                    }
                }
            }));

            var method = GetPrivateStaticMethod("RegenerateNarrationSubtitlesFromTtsTimeline", typeof(string), typeof(string));
            Assert.NotNull(method);
            method!.Invoke(null, [planRoot, "en"]);

            var expectedScopedSrtPath = Path.Combine(planRoot, "narration", "subtitles", "en", "short.srt");
            var obsoleteUnscopedSrtPath = Path.Combine(planRoot, "narration", "subtitles", "short.srt");
            Assert.True(File.Exists(expectedScopedSrtPath));
            Assert.False(File.Exists(obsoleteUnscopedSrtPath));

            var shortSrt = File.ReadAllText(expectedScopedSrtPath);
            Assert.Contains("00:00:00,000 --> 00:00:05,352", shortSrt);
            Assert.Contains("00:00:05,352 --> 00:00:06,602", shortSrt);
            Assert.Contains("First cue.", shortSrt);
            Assert.Contains("Second cue.", shortSrt);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }


    [Fact]
    public void Phase16SceneDurationPlan_GroupsCueLevelTtsDurationsByScene()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase16-scene-duration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var metadataRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
            Directory.CreateDirectory(metadataRoot);
            File.WriteAllText(Path.Combine(metadataRoot, "scene-timeline-metadata.json"), JsonSerializer.Serialize(new
            {
                scenes = new[]
                {
                    new { sceneId = "001-hook", recommendedMotion = "push-in" },
                    new { sceneId = "002-cause", recommendedMotion = "pan" }
                }
            }));

            var ttsRoot = JsonNode.Parse(JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001", cueIndex = 1, audioPath = "tts/short/001-001.mp3", audioDurationSec = 5.352 },
                        new { format = "short", sceneId = "001", cueIndex = 2, audioPath = "tts/short/001-002.mp3", audioDurationSec = 1.25 },
                        new { format = "short", sceneId = "002-cause", cueIndex = 3, audioPath = "tts/short/002-001.mp3", audioDurationSec = 7.0 }
                    }
                }
            }))!;
            var missingDurationItems = new List<string>();
            var cueDiagnosticsType = typeof(ProductionPipelineExecutionService).GetNestedType("CueAudioDurationResolution", BindingFlags.NonPublic)!;
            var cueDiagnosticsListType = typeof(List<>).MakeGenericType(cueDiagnosticsType);
            var method = GetPrivateInstanceMethod("BuildSceneDurationPlanItemsAsync", typeof(string), typeof(JsonNode), typeof(string), typeof(string), typeof(int), typeof(double), typeof(double), typeof(double), typeof(List<string>), cueDiagnosticsListType, typeof(CancellationToken));
            var service = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ProductionPipelineExecutionService));
            var task = (Task)method.Invoke(service, [planRoot, ttsRoot, "short", Path.Combine(metadataRoot, "scene-timeline-metadata.json"), 2, 12.0, 0.0, 0.5, missingDurationItems, null, CancellationToken.None])!;
            task.GetAwaiter().GetResult();
            var result = ((System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!).Cast<object>().ToArray();

            Assert.Empty(missingDurationItems);
            Assert.Equal(2, result.Length);
            Assert.Equal(6.602, ReadDouble(result[0], "AudioDurationSec"), 3);
            Assert.Equal(6.602, ReadDouble(result[0], "SceneDurationSec"), 3);
            Assert.Equal(7.0, ReadDouble(result[1], "AudioDurationSec"), 3);
            Assert.Equal(7.0, ReadDouble(result[1], "SceneDurationSec"), 3);
            Assert.Equal(13.602, result.Sum(item => ReadDouble(item, "SceneDurationSec")), 3);
            Assert.Equal(13.602, result.Sum(item => ReadDouble(item, "AudioDurationSec")), 3);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }

        static double ReadDouble(object item, string propertyName)
            => (double)item.GetType().GetProperty(propertyName)!.GetValue(item)!;
    }


    [Fact]
    public void Phase16CueTimelineDiagnostics_CountsAllCueLevelTtsItems()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase16-cue-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts"));
            var timelinePath = Path.Combine(planRoot, "tts", "tts-timeline.json");
            File.WriteAllText(timelinePath, JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new object[]
                    {
                        new { format = "short", sceneId = "001-hook", cues = new[]
                        {
                            new { cueIndex = 1, cueText = "First cue.", audioDurationSec = 5.352 },
                            new { cueIndex = 2, cueText = "Second cue.", audioDurationSec = 1.25 }
                        }},
                        new { format = "short", sceneId = "002-cause", cueIndex = 3, cueText = "Third cue.", durationSec = 7.0 }
                    }
                },
                @long = new
                {
                    cueItems = new[]
                    {
                        new { format = "long", sceneId = "001", cueIndex = 1, cueText = "Long first cue.", audioDurationSec = 2.0 },
                        new { format = "long", sceneId = "001", cueIndex = 2, cueText = "Long second cue.", audioDurationSec = 3.5 }
                    }
                }
            }));

            var countMethod = typeof(ProductionPipelineExecutionService).GetMethod("CountCueLevelTtsTimelineItems", BindingFlags.NonPublic | BindingFlags.Static);
            var sumMethod = typeof(ProductionPipelineExecutionService).GetMethod("SumCueLevelTtsTimelineDurations", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(countMethod);
            Assert.NotNull(sumMethod);

            Assert.Equal(3, (int)countMethod!.Invoke(null, [timelinePath, "short"])!);
            Assert.Equal(13.602, (double)sumMethod!.Invoke(null, [timelinePath, "short"])!, 3);
            Assert.Equal(2, (int)countMethod.Invoke(null, [timelinePath, "long"])!);
            Assert.Equal(5.5, (double)sumMethod!.Invoke(null, [timelinePath, "long"])!, 3);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void Phase18VisualDurations_GroupCueLevelTtsTimelineDurationsByScene()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase18-visual-duration-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts"));
            File.WriteAllText(Path.Combine(planRoot, "tts", "tts-timeline.json"), JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001-hook", cueIndex = 1, cueText = "First cue.", audioDurationSec = 5.352 },
                        new { format = "short", sceneId = "001-hook", cueIndex = 2, cueText = "Second cue.", audioDurationSec = 1.25 },
                        new { format = "short", sceneId = "002-cause", cueIndex = 3, cueText = "Third cue.", audioDurationSec = 7.0 }
                    }
                },
                @long = new
                {
                    items = new[]
                    {
                        new { format = "long", sceneId = "001", cueIndex = 1, cueText = "Long first cue.", audioDurationSec = 2.0 }
                    }
                }
            }));

            var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildCueLevelSceneDurationsFromTtsTimeline", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var durations = (IReadOnlyDictionary<string, double>)method!.Invoke(null, [planRoot, "short", "en"])!;

            Assert.Equal(6.602, durations["001-hook"], 3);
            Assert.Equal(7.0, durations["002-cause"], 3);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void Phase18VisualAssembly_KeepsSceneStructureWhileExpandingSceneDurations()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase18-scene-structure-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts"));
            File.WriteAllText(Path.Combine(planRoot, "tts", "tts-timeline.json"), JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001-hook", cueIndex = 1, cueText = "First cue.", audioDurationSec = 5.352 },
                        new { format = "short", sceneId = "001-hook", cueIndex = 2, cueText = "Second cue.", audioDurationSec = 1.25 },
                        new { format = "short", sceneId = "002-cause", cueIndex = 3, cueText = "Third cue.", audioDurationSec = 7.0 }
                    }
                }
            }));

            var motionRoot = JsonNode.Parse(JsonSerializer.Serialize(new
            {
                motionVersion = "V2",
                @short = new
                {
                    items = new[]
                    {
                        new { sceneId = "1-hook", imagePath = Path.Combine(planRoot, "scene-assets-v3", "short", "001.png"), durationSec = 2.0 },
                        new { sceneId = "002-cause", imagePath = Path.Combine(planRoot, "scene-assets-v3", "short", "002.png"), durationSec = 3.0 }
                    }
                }
            }))!;
            var ttsRoot = JsonNode.Parse(File.ReadAllText(Path.Combine(planRoot, "tts", "tts-timeline.json")))!;
            var missingSceneImages = new List<string>();
            var missingAudioFiles = new List<string>();
            var oldPathUsageReasons = new List<string>();
            var items = InvokeReadVideoAssemblyItems(planRoot, "en", motionRoot, ttsRoot, "short", 5, Array.Empty<string>(), missingSceneImages, missingAudioFiles, oldPathUsageReasons)
                .Cast<object>()
                .ToArray();

            Assert.Equal(2, items.Length);
            Assert.Equal(6.602, ReadSceneDuration(items[0]), 3);
            Assert.Equal(7.0, ReadSceneDuration(items[1]), 3);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }

        static double ReadSceneDuration(object item)
            => (double)item.GetType().GetProperty("SceneDurationSec")!.GetValue(item)!;
    }


    [Fact]
    public void Phase18SubtitleValidation_UsesParentSceneDurationsForEnglishSceneLevelTts()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase18-en-scene-subtitle-validation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts", "en"));
            Directory.CreateDirectory(Path.Combine(planRoot, "narration", "short"));
            Directory.CreateDirectory(Path.Combine(planRoot, "subtitles", "en"));
            File.WriteAllText(Path.Combine(planRoot, "narration", "short", "001-hook.txt"), "First display cue. Second display cue.");
            File.WriteAllText(Path.Combine(planRoot, "tts", "en", "tts-timeline.json"), JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001-hook", cueIndex = 1, cueText = "First display cue. Second display cue.", audioPath = "tts/en/short/001-hook.mp3", audioDurationSec = 4.0 }
                    }
                }
            }));
            var srtPath = Path.Combine(planRoot, "subtitles", "en", "short.srt");
            File.WriteAllText(srtPath, """
1
00:00:00,000 --> 00:00:02,000
First display cue.

2
00:00:02,000 --> 00:00:04,000
Second display cue.
""");

            var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidateCueLevelSubtitleSync", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var result = method!.Invoke(null, new object?[] { planRoot, "short", srtPath, "en" })!;

            Assert.True((bool)result.GetType().GetProperty("Passed")!.GetValue(result)!);
            Assert.Equal("Phase18SceneBasedParentSceneTtsDurations", (string)result.GetType().GetProperty("TimingSource")!.GetValue(result)!);
            Assert.Equal(2, (int)result.GetType().GetProperty("CueCount")!.GetValue(result)!);
            Assert.Equal(0, (double)result.GetType().GetProperty("MaxCueDriftMs")!.GetValue(result)!);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void Phase18VisualAssembly_UsesRequestedLanguageTtsTimelineForDurationExpansion()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase18-hi-duration-expansion-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts", "en"));
            Directory.CreateDirectory(Path.Combine(planRoot, "tts", "hi"));
            File.WriteAllText(Path.Combine(planRoot, "tts", "en", "tts-timeline.json"), JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001-hook", cueIndex = 1, cueText = "English cue.", audioDurationSec = 2.0 }
                    }
                }
            }));
            File.WriteAllText(Path.Combine(planRoot, "tts", "hi", "tts-timeline.json"), JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001-hook", cueIndex = 1, cueText = "Hindi cue 1.", audioDurationSec = 20.0 },
                        new { format = "short", sceneId = "001-hook", cueIndex = 2, cueText = "Hindi cue 2.", audioDurationSec = 25.552 }
                    }
                }
            }));

            var motionRoot = JsonNode.Parse(JsonSerializer.Serialize(new
            {
                motionVersion = "V2",
                @short = new
                {
                    items = new[]
                    {
                        new { sceneId = "001-hook", imagePath = Path.Combine(planRoot, "scene-assets-v3", "short", "001.png"), durationSec = 2.0 }
                    }
                }
            }))!;
            var ttsRoot = JsonNode.Parse(File.ReadAllText(Path.Combine(planRoot, "tts", "hi", "tts-timeline.json")))!;
            var missingSceneImages = new List<string>();
            var missingAudioFiles = new List<string>();
            var oldPathUsageReasons = new List<string>();
            var items = InvokeReadVideoAssemblyItems(planRoot, "hi", motionRoot, ttsRoot, "short", 5, Array.Empty<string>(), missingSceneImages, missingAudioFiles, oldPathUsageReasons)
                .Cast<object>()
                .ToArray();

            Assert.Single(items);
            Assert.Equal(45.552, ReadSceneDuration(items[0]), 3);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }

        static double ReadSceneDuration(object item)
            => (double)item.GetType().GetProperty("SceneDurationSec")!.GetValue(item)!;
    }

    [Fact]
    public void Phase18VisualAssembly_MatchesNumericPrefixTtsSceneDurations()
    {
        var cueDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["001"] = 6.602,
            ["002"] = 7.0,
            ["003"] = 9.5
        };

        var method = typeof(ProductionPipelineExecutionService).GetMethod("MatchCueLevelSceneDuration", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var match = method!.Invoke(null, [cueDurations, "001-hook", 2.0])!;

        Assert.Equal("001-hook", ReadString(match, "RenderSceneId"));
        Assert.Equal("001-hook", ReadString(match, "NormalizedRenderSceneId"));
        Assert.Equal("001", ReadString(match, "MatchedTtsSceneId"));
        Assert.Equal("NumericPrefix", ReadString(match, "MatchMode"));
        Assert.Equal(6.602, ReadDouble(match, "GroupedCueDurationSec"), 3);
        Assert.Equal(2.0, ReadDouble(match, "OriginalSceneDurationSec"), 3);
        Assert.Equal(6.602, ReadDouble(match, "ExpandedSceneDurationSec"), 3);

        static string? ReadString(object item, string propertyName)
            => (string?)item.GetType().GetProperty(propertyName)!.GetValue(item);

        static double ReadDouble(object item, string propertyName)
            => (double)item.GetType().GetProperty(propertyName)!.GetValue(item)!;
    }

    [Fact]
    public void Phase14SubtitleSegmentation_SplitsNarrationIntoReadableCues()
    {
        const string narration = "Tonight, look low in the western sky after sunset. Venus appears bright, Jupiter sits nearby, and the Moon gives you a simple landmark. Pause for a moment and let your eyes adjust before you scan again.";

        var splitMethod = GetPrivateStaticMethod("SplitSubtitleChunks", typeof(string));
        var wrapMethod = GetPrivateStaticMethod("WrapSubtitleChunk", typeof(string));

        Assert.NotNull(splitMethod);
        Assert.NotNull(wrapMethod);

        var chunks = (IReadOnlyList<string>)splitMethod!.Invoke(null, [narration])!;
        var wrapped = chunks.Select(chunk => (IReadOnlyList<string>)wrapMethod!.Invoke(null, [chunk])!).ToArray();

        Assert.True(chunks.Count > 1);
        Assert.Equal(narration, string.Join(" ", chunks));
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 84));
        Assert.All(wrapped, lines =>
        {
            Assert.InRange(lines.Count, 1, 2);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, 42));
        });
    }

    [Fact]
    public void Phase14HindiSubtitleOptions_UseConservativeCueTimingWithoutChangingEnglishDefaults()
    {
        var normalizeMethod = typeof(ProductionPipelineExecutionService).GetMethod("NormalizePhase14SubtitleTtsOptions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(normalizeMethod);

        var english = (SubtitleTtsOptions)normalizeMethod!.Invoke(null, new object?[] { null, "en" })!;
        var hindi = (SubtitleTtsOptions)normalizeMethod!.Invoke(null, new object?[] { null, "hi" })!;

        Assert.Equal(42, english.SubtitleMaxCharsPerLine);
        Assert.Equal(1200, english.SubtitleMinCueDurationMs);
        Assert.Equal(4200, english.SubtitleMaxCueDurationMs);
        Assert.Equal(14d, english.ReadingSpeedCharsPerSecond);

        Assert.InRange(hindi.SubtitleMaxCharsPerLine, 30, 34);
        Assert.Equal(1400, hindi.SubtitleMinCueDurationMs);
        Assert.Equal(4000, hindi.SubtitleMaxCueDurationMs);
        Assert.InRange(hindi.ReadingSpeedCharsPerSecond, 8, 10);
    }

    [Fact]
    public void Phase14HindiSubtitleSegmentation_UsesConservativeLineLength()
    {
        const string narration = "आज रात पश्चिमी क्षितिज के पास चमकते शुक्र को देखें। चंद्रमा पास में होगा और आपको दिशा पहचानने में मदद करेगा।";

        var normalizeMethod = typeof(ProductionPipelineExecutionService).GetMethod("NormalizePhase14SubtitleTtsOptions", BindingFlags.NonPublic | BindingFlags.Static);
        var splitMethod = GetPrivateStaticMethod("SplitSubtitleChunks", typeof(string), typeof(SubtitleTtsOptions));
        var wrapMethod = GetPrivateStaticMethod("WrapSubtitleChunk", typeof(string), typeof(SubtitleTtsOptions));

        Assert.NotNull(normalizeMethod);
        Assert.NotNull(splitMethod);
        Assert.NotNull(wrapMethod);

        var options = (SubtitleTtsOptions)normalizeMethod!.Invoke(null, new object?[] { null, "hi" })!;
        var chunks = (IReadOnlyList<string>)splitMethod!.Invoke(null, [narration, options])!;
        var wrapped = chunks.Select(chunk => (IReadOnlyList<string>)wrapMethod!.Invoke(null, [chunk, options])!).ToArray();

        Assert.True(chunks.Count > 1);
        Assert.Equal(narration, string.Join(" ", chunks));
        Assert.All(wrapped, lines =>
        {
            Assert.InRange(lines.Count, 1, 2);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, 34));
        });
    }

    [Fact]
    public void Phase14SubtitleSegmentation_DoesNotSplitInsideWords()
    {
        const string narration = "Tonight, turn your attention to the western horizon as a planetary conjunction gathers after sunset. Keep watching while Venus and Jupiter settle lower together.";

        var splitMethod = GetPrivateStaticMethod("SplitSubtitleChunks", typeof(string));
        var wrapMethod = GetPrivateStaticMethod("WrapSubtitleChunk", typeof(string));

        Assert.NotNull(splitMethod);
        Assert.NotNull(wrapMethod);

        var chunks = (IReadOnlyList<string>)splitMethod!.Invoke(null, [narration])!;
        var wrapped = chunks.Select(chunk => (IReadOnlyList<string>)wrapMethod!.Invoke(null, [chunk])!).ToArray();
        var reconstructed = string.Join(" ", chunks);
        var srtText = string.Join(" ", wrapped.SelectMany(lines => lines));

        Assert.Equal(narration, reconstructed);
        Assert.Equal(narration, srtText);
        Assert.Equal(NormalizeNarrationTokens(narration), NormalizeNarrationTokens(srtText));
        Assert.DoesNotContain("planet ary", srtText);
        Assert.DoesNotContain("planet\nary", string.Join("\n", wrapped.SelectMany(lines => lines)));
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 84));
        Assert.All(wrapped, lines =>
        {
            Assert.InRange(lines.Count, 1, 2);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, 42));
        });
    }

    [Fact]
    public void Phase14SubtitleSegmentation_SplitsLongPhrasesOnlyAtWhitespace()
    {
        const string narration = "Tonight, turn your attention carefully toward the western horizon while the planetary conjunction keeps glowing after sunset.";

        var splitMethod = GetPrivateStaticMethod("SplitSubtitleChunks", typeof(string));
        var wrapMethod = GetPrivateStaticMethod("WrapSubtitleChunk", typeof(string));

        Assert.NotNull(splitMethod);
        Assert.NotNull(wrapMethod);

        var chunks = (IReadOnlyList<string>)splitMethod!.Invoke(null, [narration])!;
        var wrapped = chunks.Select(chunk => (IReadOnlyList<string>)wrapMethod!.Invoke(null, [chunk])!).ToArray();
        var srtText = string.Join(" ", wrapped.SelectMany(lines => lines));

        Assert.Equal(narration, string.Join(" ", chunks));
        Assert.Equal(narration, srtText);
        Assert.Equal(NormalizeNarrationTokens(narration), NormalizeNarrationTokens(srtText));
        Assert.DoesNotContain("planet ary", srtText);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 84));
        Assert.All(wrapped, lines =>
        {
            Assert.InRange(lines.Count, 1, 2);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, 42));
        });
    }

    [Fact]
    public void Phase18MotionV2Strength_RequestExperimentalDoesNotOverrideDefaultPlan()
    {
        Assert.Equal("Default", InvokePhase18MotionV2StrengthResolver("Experimental", "Default"));
    }

    [Fact]
    public void Phase18MotionV2Strength_DetectsRequestExperimentalDefaultDiagnosticsMismatch()
    {
        Assert.True(InvokePhase18MotionV2StrengthMismatch("Experimental", "Default"));
        Assert.False(InvokePhase18MotionV2StrengthMismatch("Experimental", "Experimental"));
        Assert.False(InvokePhase18MotionV2StrengthMismatch(null, "Default"));
    }

    [Fact]
    public void Phase18MotionV2Strength_UsesPlanBeforeDefaultWhenRequestIsNotExperimental()
    {
        Assert.Equal("Experimental", InvokePhase18MotionV2StrengthResolver(null, "Experimental"));
        Assert.Equal("Default", InvokePhase18MotionV2StrengthResolver(null, null));
    }

    [Fact]
    public void Phase18MotionV2Strength_WarnsWhenRequestOverridesDefaultPlan()
    {
        Assert.True(InvokePhase18MotionV2StrengthOverrideWarning("Experimental", "Default"));
        Assert.False(InvokePhase18MotionV2StrengthOverrideWarning("Experimental", "Experimental"));
        Assert.False(InvokePhase18MotionV2StrengthOverrideWarning(null, "Default"));
    }

    [Fact]
    public void PhaseGating_NamedFullMoonShortOnly_SkipsLongNarration()
    {
        var context = CreateContext("NamedFullMoon", ["ShortVideo"]);

        Assert.True(IsPhaseRequired(context, 13));
        Assert.True(IsPhaseRequired(context, 14));
        Assert.False(IsPhaseRequired(context, 15));
        Assert.True(IsPhaseRequired(context, 16));
        Assert.False(IsPhaseRequired(context, 17));
        Assert.True(IsPhaseRequired(context, 18));
        Assert.False(IsPhaseRequired(context, 19));
    }

    [Fact]
    public void PhaseGating_MeteorShortAndLong_RunsBothNarrationPhases()
    {
        var context = CreateContext("MeteorShower", ["ShortVideo", "LongVideo"]);

        Assert.True(IsPhaseRequired(context, 13));
        Assert.True(IsPhaseRequired(context, 14));
        Assert.True(IsPhaseRequired(context, 15));
        Assert.True(IsPhaseRequired(context, 16));
        Assert.True(IsPhaseRequired(context, 17));
        Assert.True(IsPhaseRequired(context, 18));
        Assert.True(IsPhaseRequired(context, 19));
    }

    [Fact]
    public void PhaseGating_ThumbnailOnly_RunsSceneAudioSyncButSkipsVideoPhasesNotRequested()
    {
        var context = CreateContext("FutureDomain", ["Thumbnail"]);

        Assert.False(IsPhaseRequired(context, 11));
        Assert.True(IsPhaseRequired(context, 12));
        Assert.True(IsPhaseRequired(context, 13));
        Assert.True(IsPhaseRequired(context, 14));
        Assert.False(IsPhaseRequired(context, 15));
        Assert.False(IsPhaseRequired(context, 16));
        Assert.False(IsPhaseRequired(context, 17));
        Assert.False(IsPhaseRequired(context, 18));
        Assert.False(IsPhaseRequired(context, 19));
        Assert.True(IsPhaseRequired(context, 20));
    }


    [Fact]
    public void Phase14NarrationExtraction_ReadsSectionsFromRootScenesArray()
    {
        var path = Path.Combine(Path.GetTempPath(), "astro-phase14-narration", Guid.NewGuid().ToString("N"), "question-driven-narration-v2.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            section = "WrongRootSection",
            narration = new { section = "WrongNarrationSection" },
            scenes = new[]
            {
                new { sceneNumber = 1, section = "Hook", narrationText = "Look up tonight." },
                new { sceneNumber = 2, section = "ViewingAdvice", narrationText = "Face west after sunset." },
                new { sceneNumber = 3, section = "Explanation", narrationText = "The alignment is easy to see." },
                new { sceneNumber = 4, section = "Reward", narrationText = "You will spot a bright pairing." },
                new { sceneNumber = 5, section = "Curiosity", narrationText = "The planets only appear close." },
                new { sceneNumber = 6, section = "CTA", narrationText = "Save this reminder." }
            }
        }));

        var method = typeof(ProductionPipelineExecutionService).GetMethod("ExtractNarrationBeats", BindingFlags.NonPublic | BindingFlags.Static);
        var beats = Assert.IsAssignableFrom<System.Collections.IEnumerable>(method!.Invoke(null, [path]));
        var sections = beats.Cast<object>()
            .Select(beat => beat.GetType().GetProperty("Section")!.GetValue(beat)?.ToString())
            .ToArray();

        Assert.Equal(["Hook", "ViewingAdvice", "Explanation", "Reward", "Curiosity", "CTA"], sections);
        Assert.DoesNotContain("WrongRootSection", sections);
        Assert.DoesNotContain("WrongNarrationSection", sections);
    }

    [Fact]
    public void Phase14DocumentaryNarration_PlanetConjunctionUsesDocumentaryStoryArcAndPerspective()
    {
        var context = CreateContext("PlanetConjunction", ["ShortVideo", "LongVideo"], "Venus Jupiter Conjunction");
        var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase14DocumentaryNarration", BindingFlags.NonPublic | BindingFlags.Static);

        var narration = method!.Invoke(null, [context])!;
        var narrationType = narration.GetType();
        var shortItems = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(narrationType.GetProperty("ShortItems")!.GetValue(narration));
        var longItems = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(narrationType.GetProperty("LongItems")!.GetValue(narration));
        var diagnostics = narrationType.GetProperty("Diagnostics")!.GetValue(narration)!;
        var diagnosticsType = diagnostics.GetType();
        var allText = string.Join(" ", shortItems.Values.Concat(longItems.Values));

        Assert.Equal(["001-hook", "002-what-is-it", "003-cause", "004-viewing-tip", "005-final-reminder"], shortItems.Keys.ToArray());
        Assert.Equal(9, longItems.Count);
        Assert.StartsWith("Before dawn, Jupiter takes the lead", shortItems["001-hook"]);
        Assert.Contains("Venus slips in beside it", shortItems["001-hook"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quiet sky story", shortItems["001-hook"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jupiter", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Venus", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vast solar-system distances", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("separated|distances|space|line-of-sight|perspective", allText);
        Assert.DoesNotContain("low in the evening sky", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("start with", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you will see", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fellow stargazers", allText, StringComparison.OrdinalIgnoreCase);
        Assert.True((int)diagnosticsType.GetProperty("DocumentaryScore")!.GetValue(diagnostics)! >= 90);
        Assert.True((int)diagnosticsType.GetProperty("WonderScore")!.GetValue(diagnostics)! >= 90);
        Assert.True((int)diagnosticsType.GetProperty("ScientificAccuracyScore")!.GetValue(diagnostics)! >= 95);
    }


    [Theory]
    [InlineData("Geminids Meteor Shower", "en", "MeteorShower")]
    [InlineData("Geminids Meteor Shower", "hi", "MeteorShower")]
    [InlineData("Jupiter Venus Conjunction", "en", "PlanetConjunction")]
    [InlineData("Jupiter Venus Conjunction", "hi", "PlanetConjunction")]
    [InlineData("Named Full Moon", "en", "NamedFullMoon")]
    [InlineData("Solar Eclipse", "hi", "SolarEclipse")]
    public void Phase14V31Adapter_ExpandsNormalizedScenesIntoUniquePurposeSpecificNarration(string title, string language, string family)
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hook"] = $"Hook for {title}.",
            ["InterestingFact"] = $"Interesting fact for {title}.",
            ["BestTime"] = language == "hi" ? $"{title} देखने का सबसे अच्छा समय स्थानीय शाम की खिड़की है।" : $"The best viewing window for {title} is the local evening window.",
            ["FinalReminder"] = $"Final reminder for {title}."
        };
        var shortItems = CreateSceneAudioSyncItems("short", ["001-hook", "002-cause", "003-accurate-sky-guide", "004-viewing-tip", "005-final-reminder"]);
        var longItems = CreateSceneAudioSyncItems("long", ["001-hook", "002-what-is-it", "003-cause", "004-interesting-fact", "005-best-time", "006-accurate-sky-guide", "007-what-you-will-see", "008-viewing-tips", "009-final-reminder"]);
        var adaptMethod = typeof(ProductionPipelineExecutionService).GetMethod("AdaptNarrationGenerationScenes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var validateMethod = typeof(ProductionPipelineExecutionService).GetMethod("ValidateAdaptedV31ProductionNarration", BindingFlags.NonPublic | BindingFlags.Static)!;

        var shortTexts = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(adaptMethod.Invoke(null, [shortItems, source, family, language, false]));
        var longTexts = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(adaptMethod.Invoke(null, [longItems, source, family, language, true]));

        validateMethod.Invoke(null, [shortTexts, longTexts, 5, 9]);
        Assert.Equal(["001-hook", "002-cause", "003-accurate-sky-guide", "004-viewing-tip", "005-final-reminder"], shortTexts.Keys.ToArray());
        Assert.Equal(["001-hook", "002-what-is-it", "003-cause", "004-interesting-fact", "005-best-time", "006-accurate-sky-guide", "007-what-you-will-see", "008-viewing-tips", "009-final-reminder"], longTexts.Keys.ToArray());
        Assert.Equal(5, shortTexts.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(9, longTexts.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(source["BestTime"], longTexts["005-best-time"]);
        Assert.DoesNotContain(source["BestTime"], longTexts["006-accurate-sky-guide"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Use the timing and direction cue", string.Join(" ", shortTexts.Values.Concat(longTexts.Values)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("viewing window cue", string.Join(" ", shortTexts.Values.Concat(longTexts.Values)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source scene", string.Join(" ", shortTexts.Values.Concat(longTexts.Values)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("metadata", string.Join(" ", shortTexts.Values.Concat(longTexts.Values)), StringComparison.OrdinalIgnoreCase);
    }

    private static Array CreateSceneAudioSyncItems(string format, IReadOnlyList<string> sceneIds)
    {
        var itemType = typeof(ProductionPipelineExecutionService).GetNestedType("SceneAudioSyncItem", BindingFlags.NonPublic)!;
        var items = Array.CreateInstance(itemType, sceneIds.Count);
        for (var i = 0; i < sceneIds.Count; i++)
        {
            var item = Activator.CreateInstance(itemType, [format, i + 1, sceneIds[i], $"{sceneIds[i]}.png", string.Empty, string.Empty, string.Empty, string.Empty, 5, string.Empty, string.Empty, string.Empty, string.Empty])!;
            items.SetValue(item, i);
        }
        return items;
    }

    [Fact]
    public void Phase14EventConsistencyGuard_FailsMeteorNarrationWithPlanetConjunctionLeakage()
    {
        var context = CreateContext("MeteorShower", ["ShortVideo", "LongVideo"], "Geminids Meteor Shower");
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase14EventConsistency", BindingFlags.NonPublic | BindingFlags.Static);
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "Jupiter and Venus form a conjunction tonight."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "These two planets appear close together, not like a meteor shower."
        };
        var firstSentenceByScene = shortTexts
            .Select(kv => new KeyValuePair<string, string>($"short:{kv.Key}", kv.Value))
            .Concat(longTexts.Select(kv => new KeyValuePair<string, string>($"long:{kv.Key}", kv.Value)))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [context, "Meteor", shortTexts, longTexts, firstSentenceByScene]));

        var inner = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Phase 14 event-consistency validation failed before TTS", inner.Message);
        Assert.Contains("forbidden narration leakage detected", inner.Message);
        Assert.Contains("Jupiter", inner.Message);
        Assert.Contains("Venus", inner.Message);
        Assert.Contains("conjunction", inner.Message);
        Assert.Contains("two planets", inner.Message);
    }



    [Fact]
    public void Phase14HindiCueDuplicateRewrite_UsesShortMeteorAlternateWithinWrapLimit()
    {
        var sceneAudioSyncType = typeof(ProductionPipelineExecutionService).GetNestedType("SceneAudioSyncItem", BindingFlags.NonPublic)!;
        var sceneDurationType = typeof(ProductionPipelineExecutionService).GetNestedType("SceneDurationPlanItem", BindingFlags.NonPublic)!;
        var method = GetPrivateStaticMethod("BuildHindiUniqueSubtitleCueText", typeof(string), typeof(string), sceneAudioSyncType, sceneDurationType, typeof(HashSet<string>), typeof(string), typeof(string), typeof(int), typeof(int), typeof(SubtitleTtsOptions), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(), typeof(bool).MakeByRefType(), typeof(string));
        var originalCueText = "आज रात उल्का वर्षा देखने के लिए अंधेरे आसमान में रेडिएंट के पास ध्यान रखें।";
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "आज रात उल्का वर्षा देखने के लिए अंधेरे आसमान में रेडिएंट के पास ध्यान रखें"
        };

        var invokeArguments = new object?[] { originalCueText, "007-what-you-will-see", null, null, occupied, "test", "long", 7, 2, new SubtitleTtsOptions(), 0, 0, false, originalCueText };
        var rewritten = (string)method.Invoke(null, invokeArguments)!;

        Assert.NotEqual(originalCueText, rewritten);
        Assert.True(rewritten.Length < originalCueText.Length);
        Assert.Contains("रेडिएंट", rewritten);

        var canWrap = GetPrivateStaticMethod("CanWrapSubtitleChunk", typeof(string));
        Assert.True((bool)canWrap.Invoke(null, [rewritten])!);
    }

    [Fact]
    public void Phase14HindiTranslation_TranslatesMeteorNarrationWithoutConjunctionLeakage()
    {
        var method = GetPrivateStaticMethod("ApplyPhase14NarrationTranslationIfNeeded", typeof(string), typeof(string), typeof(IDictionary<string, string>), typeof(IDictionary<string, string>));
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "Tonight the Geminids meteor shower is strongest under a dark sky. Watch near the radiant and avoid moonlight."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["006-radiant-guide"] = "The Geminids radiant helps you orient, but meteors can streak across the whole dark sky."
        };

        var diagnostics = method!.Invoke(null, ["hi", "Meteor", shortTexts, longTexts])!;
        var translated = string.Join(" ", shortTexts.Values.Concat(longTexts.Values));

        Assert.Contains("जेमिनिड्स", translated);
        Assert.Contains("उल्का वर्षा", translated);
        Assert.Contains("रेडिएंट", translated);
        Assert.Contains("अंधेरे आसमान", translated);
        Assert.DoesNotContain("बृहस्पति", translated);
        Assert.DoesNotContain("शुक्र", translated);
        Assert.DoesNotContain("युति", translated);
        Assert.False((bool)diagnostics.GetType().GetProperty("HardcodedTemplateUsed")!.GetValue(diagnostics)!);
        Assert.False((bool)diagnostics.GetType().GetProperty("ForbiddenNarrationLeakageDetected")!.GetValue(diagnostics)!);
        Assert.Equal("deterministic-english-to-hindi-text-translation", diagnostics.GetType().GetProperty("TranslationMode")!.GetValue(diagnostics));
    }


    [Fact]
    public void Phase14HindiTranslation_RejectsHinglishFragmentsAndUsesSentenceTranslation()
    {
        var method = GetPrivateStaticMethod("ApplyPhase14NarrationTranslationIfNeeded", typeof(string), typeof(string), typeof(IDictionary<string, string>), typeof(IDictionary<string, string>));
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "Geminids can turn quiet dark sky into sudden streaks of light."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["002-cause"] = "Meteor showers happen when Earth passes through trail of comet debris."
        };

        var diagnostics = method!.Invoke(null, ["hi", "Meteor", shortTexts, longTexts])!;
        var translated = string.Join(" ", shortTexts.Values.Concat(longTexts.Values));

        Assert.Contains("जेमिनिड्स", translated);
        Assert.Contains("शांत अंधेरे आसमान", translated);
        Assert.Contains("धूमकेतु", translated);
        Assert.DoesNotContain("can turn", translated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("happen when", translated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passes through", translated, StringComparison.OrdinalIgnoreCase);
        Assert.True((bool)diagnostics.GetType().GetProperty("FullSentenceTranslationApplied")!.GetValue(diagnostics)!);
        Assert.True((double)diagnostics.GetType().GetProperty("HindiCharacterRatio")!.GetValue(diagnostics)! >= 0.85);
        Assert.False((bool)diagnostics.GetType().GetProperty("EnglishFragmentDetected")!.GetValue(diagnostics)!);
        Assert.Empty((IReadOnlyList<string>)diagnostics.GetType().GetProperty("DetectedEnglishFragments")!.GetValue(diagnostics)!);
    }

    [Fact]
    public void Phase14HindiTranslation_TranslatesConjunctionNarrationWithoutMeteorLeakage()
    {
        var method = GetPrivateStaticMethod("ApplyPhase14NarrationTranslationIfNeeded", typeof(string), typeof(string), typeof(IDictionary<string, string>), typeof(IDictionary<string, string>));
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "Tonight Jupiter and Venus form a bright conjunction after sunset."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["002-cause"] = "The planets look close in the sky, so watch the western horizon after sunset."
        };

        var diagnostics = method!.Invoke(null, ["hi", "PlanetConjunction", shortTexts, longTexts])!;
        var translated = string.Join(" ", shortTexts.Values.Concat(longTexts.Values));

        Assert.Contains("बृहस्पति", translated);
        Assert.Contains("शुक्र", translated);
        Assert.Contains("युति", translated);
        Assert.DoesNotContain("उल्का", translated);
        Assert.DoesNotContain("रेडिएंट", translated);
        Assert.DoesNotContain("जेमिनिड्स", translated);
        Assert.False((bool)diagnostics.GetType().GetProperty("HardcodedTemplateUsed")!.GetValue(diagnostics)!);
        Assert.False((bool)diagnostics.GetType().GetProperty("ForbiddenNarrationLeakageDetected")!.GetValue(diagnostics)!);
    }



    [Fact]
    public void Phase14HindiTranslation_PreservesPlanetConjunctionScenePurposeDistinctly()
    {
        var method = GetPrivateStaticMethod("ApplyPhase14NarrationTranslationIfNeeded", typeof(string), typeof(string), typeof(IDictionary<string, string>), typeof(IDictionary<string, string>));
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "Tonight Jupiter and Venus form a bright conjunction that makes you wonder why two planets can look so close.",
            ["002-cause"] = "Jupiter and Venus only appear close because perspective and orbital positions place them along a similar line of sight from Earth.",
            ["003-accurate-sky-guide"] = "Look toward the western horizon after sunset to find Jupiter and Venus.",
            ["004-viewing-tip"] = "Use your eyes first, then binoculars, and avoid trees or buildings near the horizon.",
            ["005-final-reminder"] = "Remember this conjunction is brief, so use the next clear evening as your takeaway."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var diagnostics = method!.Invoke(null, ["hi", "PlanetConjunction", shortTexts, longTexts])!;
        var translated = shortTexts.Values.ToArray();
        var scenePurposeDiagnostics = (IReadOnlyList<object>)diagnostics.GetType().GetProperty("ScenePurposeTranslationDiagnostics")!.GetValue(diagnostics)!;

        Assert.Equal(translated.Length, translated.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("जिज्ञासा", translated[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("दृष्टि-रेखा", translated[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("कक्षीय", translated[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("पश्चिमी क्षितिज", translated[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("दूरबीन", translated[3], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("याद", translated[4], StringComparison.OrdinalIgnoreCase);
        Assert.False((bool)diagnostics.GetType().GetProperty("DuplicateAcrossScenesRemaining")!.GetValue(diagnostics)!);
        Assert.Equal(0, (int)diagnostics.GetType().GetProperty("DuplicateSubtitleBlockCount")!.GetValue(diagnostics)!);
        Assert.Equal(shortTexts.Count, scenePurposeDiagnostics.Count);
        Assert.All(scenePurposeDiagnostics, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string)item.GetType().GetProperty("scenePurpose")!.GetValue(item)!));
            Assert.False(string.IsNullOrWhiteSpace((string)item.GetType().GetProperty("sourceEnglishText")!.GetValue(item)!));
            Assert.False(string.IsNullOrWhiteSpace((string)item.GetType().GetProperty("translatedHindiText")!.GetValue(item)!));
            Assert.False(string.IsNullOrWhiteSpace((string)item.GetType().GetProperty("semanticFingerprint")!.GetValue(item)!));
            Assert.True((double)item.GetType().GetProperty("similarityScore")!.GetValue(item)! >= 0);
            Assert.True((double)item.GetType().GetProperty("semanticSimilarityScore")!.GetValue(item)! > 0);
        });
    }


    [Theory]
    [InlineData("Meteor")]
    [InlineData("Moon")]
    [InlineData("Eclipse")]
    public void Phase14HindiTranslation_PreservesScenePurposeDistinctlyAcrossFamilies(string family)
    {
        var method = GetPrivateStaticMethod("ApplyPhase14NarrationTranslationIfNeeded", typeof(string), typeof(string), typeof(IDictionary<string, string>), typeof(IDictionary<string, string>));
        var shortTexts = BuildScenePurposePreservationSourceScenes(family);
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var diagnostics = method!.Invoke(null, ["hi", family, shortTexts, longTexts])!;
        var translated = shortTexts.Values.ToArray();
        var scenePurposeDiagnostics = (IReadOnlyList<object>)diagnostics.GetType().GetProperty("ScenePurposeTranslationDiagnostics")!.GetValue(diagnostics)!;

        Assert.Equal(translated.Length, translated.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(translated, text => text.Contains("जिज्ञासा", StringComparison.OrdinalIgnoreCase) || text.Contains("शुरुआत", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(translated, text => text.Contains("कारण", StringComparison.OrdinalIgnoreCase) || text.Contains("प्रक्रिया", StringComparison.OrdinalIgnoreCase) || text.Contains("वजह", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(translated, text => text.Contains("कहाँ", StringComparison.OrdinalIgnoreCase) || text.Contains("दिशा", StringComparison.OrdinalIgnoreCase) || text.Contains("क्षितिज", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(translated, text => text.Contains("अवलोकन", StringComparison.OrdinalIgnoreCase) || text.Contains("सलाह", StringComparison.OrdinalIgnoreCase) || text.Contains("दूरबीन", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(translated, text => text.Contains("याद", StringComparison.OrdinalIgnoreCase) || text.Contains("अंतिम", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, (int)diagnostics.GetType().GetProperty("DuplicateSubtitleBlockCount")!.GetValue(diagnostics)!);
        Assert.Equal(shortTexts.Count, scenePurposeDiagnostics.Count);
        Assert.All(scenePurposeDiagnostics, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string)item.GetType().GetProperty("scenePurpose")!.GetValue(item)!));
            Assert.False(string.IsNullOrWhiteSpace((string)item.GetType().GetProperty("semanticFingerprint")!.GetValue(item)!));
            Assert.True((double)item.GetType().GetProperty("similarityScore")!.GetValue(item)! >= 0);
            Assert.True((double)item.GetType().GetProperty("similarityScoreToOtherScenes")!.GetValue(item)! >= 0);
        });
    }

    private static Dictionary<string, string> BuildScenePurposePreservationSourceScenes(string family)
        => family switch
        {
            "Meteor" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["001-hook"] = "Tonight the Geminids meteor shower can make a dark sky feel surprising.",
                ["002-cause"] = "Meteor showers happen when Earth passes through comet debris.",
                ["003-accurate-sky-guide"] = "Look toward the radiant but keep the whole dark sky in view.",
                ["004-viewing-tip"] = "Use your eyes, avoid bright lights, and give the sky patient attention.",
                ["005-final-reminder"] = "Remember that patience under a dark sky is the takeaway."
            },
            "Moon" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["001-hook"] = "Tonight the full moon makes the winter sky worth a first look.",
                ["002-cause"] = "The full moon appears when the illuminated lunar side faces Earth.",
                ["003-accurate-sky-guide"] = "Look near the eastern horizon around moonrise.",
                ["004-viewing-tip"] = "Use your eyes first, then binoculars to study the lunar edge.",
                ["005-final-reminder"] = "Remember the quiet moonlight is the main takeaway."
            },
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["001-hook"] = "A solar eclipse makes you wonder how the Sun can change so quickly.",
                ["002-cause"] = "An eclipse happens when the Moon blocks the Sun along the shadow path.",
                ["003-accurate-sky-guide"] = "Use the eclipse path and Sun direction to choose a safe open view.",
                ["004-viewing-tip"] = "Use certified eclipse glasses and never remove eye protection during partial phases.",
                ["005-final-reminder"] = "Remember that safe viewing is the final takeaway."
            }
        };

    [Fact]
    public void Phase14HindiTranslation_RewritesGenericDuplicateConjunctionScenesFromSourceText()
    {
        var method = GetPrivateStaticMethod("ApplyPhase14NarrationTranslationIfNeeded", typeof(string), typeof(string), typeof(IDictionary<string, string>), typeof(IDictionary<string, string>));
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-close-spacing"] = "Planets appear close together tonight, with the small separation making the conjunction easy to notice.",
            ["002-close-spacing"] = "Planets appear close together tonight, with the small separation making the conjunction easy to notice.",
            ["003-gear"] = "Bring binoculars only after you find Jupiter and Venus with your eyes first. Bring a steady view and avoid buildings on the horizon."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var diagnostics = method!.Invoke(null, ["hi", "PlanetConjunction", shortTexts, longTexts])!;
        var translated = shortTexts.Values.ToArray();
        var duplicateAcrossScenes = (IReadOnlyList<string>)diagnostics.GetType().GetProperty("DuplicateAcrossScenesDetected")!.GetValue(diagnostics)!;
        var finalUniqueSceneText = (IReadOnlyDictionary<string, string>)diagnostics.GetType().GetProperty("FinalUniqueSceneText")!.GetValue(diagnostics)!;
        var rewrittenSceneIds = (IReadOnlyList<string>)diagnostics.GetType().GetProperty("RewrittenSceneIds")!.GetValue(diagnostics)!;
        var rewrittenUniqueText = (IReadOnlyDictionary<string, string>)diagnostics.GetType().GetProperty("RewrittenUniqueText")!.GetValue(diagnostics)!;
        var duplicateAcrossScenesRemaining = (bool)diagnostics.GetType().GetProperty("DuplicateAcrossScenesRemaining")!.GetValue(diagnostics)!;

        Assert.All(translated, text => Assert.Contains("।", text));
        Assert.Equal(translated.Length, translated.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("सूर्यास्त के बाद पश्चिमी आसमान में चमकीले ग्रहों को पास-पास देखने का अच्छा अवसर मिलेगा।", translated.Skip(1));
        Assert.All(translated, text =>
        {
            Assert.DoesNotContain("दृश्य १", text);
            Assert.DoesNotContain("दृश्य २", text);
            Assert.DoesNotContain("दृश्य ३", text);
            Assert.DoesNotContain("लंबे संस्करण में", text);
            Assert.DoesNotContain("इस दृश्य में", text);
            Assert.DoesNotContain("अपना अलग आकाशीय संदर्भ", text);
        });
        Assert.NotEmpty(duplicateAcrossScenes);
        Assert.Contains("short:002-close-spacing", finalUniqueSceneText.Keys);
        Assert.Contains("short:002-close-spacing", rewrittenSceneIds);
        Assert.Equal(shortTexts["002-close-spacing"], finalUniqueSceneText["short:002-close-spacing"]);
        Assert.Equal(shortTexts["002-close-spacing"], rewrittenUniqueText["short:002-close-spacing"]);
        Assert.False(duplicateAcrossScenesRemaining);
    }


    [Fact]
    public void Phase14HindiTranslation_UsesMoonSpecificNarrationAndCleanupForNamedFullMoon()
    {
        var method = GetPrivateStaticMethod("ApplyPhase14NarrationTranslationIfNeeded", typeof(string), typeof(string), typeof(IDictionary<string, string>), typeof(IDictionary<string, string>), typeof(string), typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>));
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "The Wolf Moon full moon rises with bright illumination over the winter horizon.",
            ["002-what-is-it"] = "The Wolf Moon is a named full moon from seasonal tradition.",
            ["003-cause"] = "A full moon happens when the Moon appears fully illuminated from Earth.",
            ["004-viewing-tip"] = "Watch moonrise from an open eastern horizon.",
            ["005-final-reminder"] = "Watch moonset later and remember the quiet full moon glow."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["002-what-is-it"] = "The Wolf Moon is a named full moon from seasonal tradition.",
            ["003-cause"] = "A full moon happens when the Moon appears fully illuminated from Earth.",
            ["004-interesting-fact"] = "Moon names carry culture and tradition.",
            ["005-best-time"] = "Moonrise is the best time to watch the full moon near the horizon.",
            ["006-accurate-sky-guide"] = "Use the eastern horizon for moonrise.",
            ["007-what-you-will-see"] = "You will see full moon brightness, color, and illumination.",
            ["008-viewing-tips"] = "Use binoculars after you find the Moon with your eyes.",
            ["009-final-reminder"] = "Remember the quiet full moon glow."
        };

        var diagnostics = method!.Invoke(null, ["hi", "Moon", shortTexts, longTexts, "NamedFullMoon", new[] { "Moon" }, Array.Empty<string>()])!;
        var translated = string.Join(" ", shortTexts.Values.Concat(longTexts.Values));
        var duplicateAcrossScenesRemaining = (bool)diagnostics.GetType().GetProperty("DuplicateAcrossScenesRemaining")!.GetValue(diagnostics)!;
        var englishTermsRemaining = (IReadOnlyList<string>)diagnostics.GetType().GetProperty("EnglishTermsRemaining")!.GetValue(diagnostics)!;

        Assert.Contains("पूर्णिमा", translated);
        Assert.Contains("वुल्फ मून", translated);
        Assert.Contains("चंद्र उदय", translated);
        Assert.Contains("चंद्र प्रकाश", translated);
        Assert.DoesNotContain("illumination", translated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("moonrise", translated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("moonset", translated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("full moon", translated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("इस दृश्य में", translated);
        Assert.DoesNotContain("इस दृश्य को", translated);
        Assert.DoesNotContain("समय, दिशा और दृश्यता को अलग शब्दों", translated);
        Assert.Equal(shortTexts.Values.Concat(longTexts.Values).Count(), shortTexts.Values.Concat(longTexts.Values).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.False(duplicateAcrossScenesRemaining);
        Assert.True((bool)diagnostics.GetType().GetProperty("MoonSpecificRewriteApplied")!.GetValue(diagnostics)!);
        Assert.True((int)diagnostics.GetType().GetProperty("MoonSpecificRewriteCount")!.GetValue(diagnostics)! > 0);
        Assert.Empty(englishTermsRemaining);
        Assert.False((bool)diagnostics.GetType().GetProperty("GenericFallbackPhraseDetected")!.GetValue(diagnostics)!);
        Assert.True((bool)diagnostics.GetType().GetProperty("DuplicateCleanupMoonMode")!.GetValue(diagnostics)!);
    }

    [Fact]
    public void Phase14HindiTranslation_UsesEclipseSpecificNarrationWithoutMoonLeakage()
    {
        var method = GetPrivateStaticMethod("ApplyPhase14NarrationTranslationIfNeeded", typeof(string), typeof(string), typeof(IDictionary<string, string>), typeof(IDictionary<string, string>), typeof(string), typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>));
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "A total solar eclipse begins as the Moon's shadow reaches the Sun.",
            ["002-cause"] = "A solar eclipse happens when the Moon passes between Earth and the Sun, briefly blocking part or all of the Sun's disk.",
            ["003-accurate-sky-guide"] = "Stand in the eclipse path and use certified solar filters for safe viewing.",
            ["004-viewing-tip"] = "Use certified eclipse glasses before and after totality.",
            ["005-final-reminder"] = "Remember eye safety during every partial phase."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "A total solar eclipse begins as the Moon's shadow reaches the Sun.",
            ["002-what-is-it"] = "A solar eclipse shows the Sun being covered by the Moon's shadow.",
            ["003-cause"] = "A solar eclipse happens when the Moon passes between Earth and the Sun, briefly blocking part or all of the Sun's disk.",
            ["004-interesting-fact"] = "During totality, the Sun's corona can appear around the dark disk.",
            ["005-best-time"] = "Prepare before the partial phase begins.",
            ["006-accurate-sky-guide"] = "Stand in the eclipse path and use certified solar filters for safe viewing.",
            ["007-what-you-will-see"] = "You will see the Sun being covered until the moment of totality.",
            ["008-viewing-tips"] = "Use certified eclipse glasses before and after totality.",
            ["009-final-reminder"] = "Remember eye safety during every partial phase."
        };

        var diagnostics = method!.Invoke(null, ["hi", "Eclipse", shortTexts, longTexts, "Total Solar Eclipse", new[] { "Sun", "Moon" }, Array.Empty<string>()])!;
        var translated = string.Join(" ", shortTexts.Values.Concat(longTexts.Values));
        var duplicateAcrossScenesRemaining = (bool)diagnostics.GetType().GetProperty("DuplicateAcrossScenesRemaining")!.GetValue(diagnostics)!;

        Assert.Contains("सूर्य ग्रहण", translated);
        Assert.Contains("पूर्ण सूर्य ग्रहण", translated);
        Assert.Contains("चंद्रमा की छाया", translated);
        Assert.Contains("सूर्य का ढकना", translated);
        Assert.Contains("प्रमाणित सोलर फिल्टर", translated);
        Assert.Contains("आँखों की सुरक्षा", translated);
        Assert.DoesNotContain("पूर्णिमा", translated);
        Assert.DoesNotContain("वुल्फ मून", translated);
        Assert.DoesNotContain("चंद्र चमक", translated);
        Assert.DoesNotContain("सर्दियों का आकाश", translated);
        Assert.DoesNotContain("चंद्रमा की कलाएँ", translated);
        Assert.DoesNotContain("आकाशीय अवलोकन में समय, दिशा और दृश्यता", translated);
        Assert.Equal(shortTexts.Values.Concat(longTexts.Values).Count(), shortTexts.Values.Concat(longTexts.Values).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.False(duplicateAcrossScenesRemaining);
        Assert.False((bool)diagnostics.GetType().GetProperty("ForbiddenNarrationLeakageDetected")!.GetValue(diagnostics)!);
        Assert.False((bool)diagnostics.GetType().GetProperty("GenericFallbackPhraseDetected")!.GetValue(diagnostics)!);
    }

    [Fact]
    public void RequestedOutputCompletion_ReportsSkippedForUnrequestedLongVideo()
    {
        var context = CreateContext("PlanetPairing", ["ShortVideo", "Thumbnail"]);
        var now = DateTimeOffset.UtcNow;
        ProductionPhaseResult[] phaseResults =
        [
            new(12, "Generate Thumbnails", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(13, "Generate Gallery", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(14, "Scene Audio Sync V1", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(15, "Generate Long Narration", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested"),
            new(16, "Generate Short TTS", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(17, "Motion Layer V1", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(18, "Assemble Short Video", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(19, "Assemble Long Video", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested")
        ];

        var completion = BuildRequestedOutputCompletion(context, phaseResults);

        Assert.Contains(completion, item => item.OutputType == "ShortVideo" && item.Requested && item.Status == "Succeeded");
        Assert.Contains(completion, item => item.OutputType == "LongVideo" && !item.Requested && item.Status == "Skipped");
        Assert.Contains(completion, item => item.OutputType == "Thumbnail" && item.Requested && item.Status == "Succeeded");
    }


    [Fact]
    public void RequestedOutputCompletion_PartialPhase12Only_MarksUnexecutedOutputsOutOfScope()
    {
        var context = CreateContext("PlanetPairing", ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"]);
        var pipelineRequest = context.PipelineRequest with { RequestedStartPhaseNo = 12, RequestedEndPhaseNo = 12 };
        context = context with { PipelineRequest = pipelineRequest, StartPhaseNo = 12, EndPhaseNo = 12 };
        var now = DateTimeOffset.UtcNow;
        ProductionPhaseResult[] phaseResults =
        [
            new(11, "Generate Hero Asset", ProductionPhaseStatus.Failed, now, now, 0, [], [], null, [], ["Hero was not executed in this partial request."], false),
            new(12, "Generate Thumbnails", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(15, "Generate Long Narration", ProductionPhaseStatus.Failed, now, now, 0, [], [], null, [], ["Long narration was not executed in this partial request."], false),
            new(16, "Generate Short TTS", ProductionPhaseStatus.Failed, now, now, 0, [], [], null, [], ["Short TTS was not executed in this partial request."], false),
            new(18, "Assemble Short Video", ProductionPhaseStatus.Failed, now, now, 0, [], [], null, [], ["Short video was not executed in this partial request."], false),
            new(19, "Assemble Long Video", ProductionPhaseStatus.Failed, now, now, 0, [], [], null, [], ["Long video was not executed in this partial request."], false)
        ];

        var completion = BuildRequestedOutputCompletion(context, phaseResults);

        Assert.Contains(completion, item => item.OutputType == "Thumbnail" && item.Requested && item.Status == "Succeeded");
        Assert.Contains(completion, item => item.OutputType == "HeroAsset" && item.Requested && item.Status == "OutOfScope");
        Assert.Contains(completion, item => item.OutputType == "ShortVideo" && item.Requested && item.Status == "OutOfScope");
        Assert.Contains(completion, item => item.OutputType == "LongVideo" && item.Requested && item.Status == "OutOfScope");
    }

    [Fact]
    public void Phase10SceneAssetDiagnostics_CountsV2SceneAssetFinalPngs()
    {
        var root = Path.Combine(Path.GetTempPath(), "astro-phase10-scene-assets", Guid.NewGuid().ToString("N"), "scene-approval-v3");
        WritePhase10SceneAssets(root, "short", 6);
        WritePhase10SceneAssets(root, "long", 6);

        var method = typeof(ProductionPipelineExecutionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "BuildPhase10SceneAssetDiagnostics"
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(string));
        var diagnostics = method!.Invoke(null, [root])!;
        var diagnosticsType = diagnostics.GetType();

        Assert.Equal(6, diagnosticsType.GetProperty("ShortSceneCount")!.GetValue(diagnostics));
        Assert.Equal(6, diagnosticsType.GetProperty("LongSceneCount")!.GetValue(diagnostics));
        Assert.Equal(6, diagnosticsType.GetProperty("ShortPngCount")!.GetValue(diagnostics));
        Assert.Equal(6, diagnosticsType.GetProperty("LongPngCount")!.GetValue(diagnostics));
        Assert.Equal(false, diagnosticsType.GetProperty("LegacyArtifactCheckUsed")!.GetValue(diagnostics));
        Assert.Equal(true, diagnosticsType.GetProperty("V2ArtifactCheckUsed")!.GetValue(diagnostics));

        var validatedShortFinalPaths = Assert.IsAssignableFrom<IReadOnlyList<string>>(diagnosticsType.GetProperty("ValidatedShortFinalPaths")!.GetValue(diagnostics));
        var validatedLongFinalPaths = Assert.IsAssignableFrom<IReadOnlyList<string>>(diagnosticsType.GetProperty("ValidatedLongFinalPaths")!.GetValue(diagnostics));
        var missingFinalPaths = Assert.IsAssignableFrom<IReadOnlyList<string>>(diagnosticsType.GetProperty("MissingFinalPaths")!.GetValue(diagnostics));
        Assert.Equal(6, validatedShortFinalPaths.Count);
        Assert.Equal(6, validatedLongFinalPaths.Count);
        Assert.Empty(missingFinalPaths);
        Assert.Contains("scene-assets/short/scene-001/scene-001-final.png", validatedShortFinalPaths[0]);
        Assert.Contains("scene-assets/long/scene-001/scene-001-final.png", validatedLongFinalPaths[0]);
    }

    [Fact]
    public void Phase10SceneAssetValidation_RequiresFinalPngInEachV2SceneDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "astro-phase10-scene-assets", Guid.NewGuid().ToString("N"), "scene-approval-v3");
        WritePhase10SceneAssets(root, "short", 6);
        WritePhase10SceneAssets(root, "long", 6, skipFinalSceneNumber: 3);

        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase10SceneAssetCoverage", BindingFlags.NonPublic | BindingFlags.Static);
        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [root]));

        var inner = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerException);
        Assert.Contains("long scene asset validation expected 6 final PNGs but found 5", inner.Message);
        Assert.Contains("scene-003-final.png", inner.Message);
    }

    [Fact]
    public void Phase10SceneAssetValidation_PassesWithV2FinalPngsAndNoLegacyFlatArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "astro-phase10-scene-assets", Guid.NewGuid().ToString("N"), "scene-approval-v3");
        WritePhase10SceneAssets(root, "short", 6);
        WritePhase10SceneAssets(root, "long", 6);

        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase10SceneAssetCoverage", BindingFlags.NonPublic | BindingFlags.Static);
        var exception = Record.Exception(() => method!.Invoke(null, [root]));

        Assert.Null(exception);
        Assert.False(File.Exists(Path.Combine(root, "short", "scene-001-final.png")));
        Assert.False(File.Exists(Path.Combine(root, "long", "scene-001-final.png")));
    }


    [Theory]
    [InlineData("MeteorShower", "Perseids Tonight", "MeteorShower")]
    [InlineData("PlanetPairing", "Venus Jupiter Pairing", "PlanetPairing")]
    [InlineData("Comet", "Comet Tonight", "Comet")]
    [InlineData("Eclipse", "Eclipse Tonight", "Eclipse")]
    public void BuildDurationTargetedShortNarration_UsesDynamicFacts_AndTargetsProfileRange(string eventType, string shortTitle, string expectedEventType)
    {
        var context = CreateContext(eventType, ["ShortVideo"], shortTitle);
        var buildMethod = typeof(ProductionPipelineExecutionService).GetMethod("BuildDurationTargetedShortNarration", BindingFlags.NonPublic | BindingFlags.Static);
        var estimateMethod = typeof(ProductionPipelineExecutionService).GetMethod("EstimateShortNarrationSeconds", BindingFlags.NonPublic | BindingFlags.Static);

        var narration = (string)buildMethod!.Invoke(null, [context])!;
        var estimatedSeconds = (double)estimateMethod!.Invoke(null, [narration])!;

        Assert.Contains(expectedEventType, narration);
        Assert.Contains(shortTitle, narration);
        Assert.Contains("western sky", narration);
        Assert.Contains("9 PM", narration);
        Assert.Contains("check clouds", narration, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(estimatedSeconds, 30.0, 40.0);
    }

    [Fact]
    public void TrimLowestPriorityShortNarrationSentences_SelfCorrectsOneWordAndHalfSecondOverflow()
    {
        var context = CreateContext("MeteorShower", ["ShortVideo"], "Perseids Tonight");
        var trimMethod = typeof(ProductionPipelineExecutionService).GetMethod("TrimLowestPriorityShortNarrationSentences", BindingFlags.NonPublic | BindingFlags.Static);
        var countMethod = typeof(ProductionPipelineExecutionService).GetMethod("CountSpokenWords", BindingFlags.NonPublic | BindingFlags.Static);
        var estimateMethod = typeof(ProductionPipelineExecutionService).GetMethod("EstimateShortNarrationSeconds", BindingFlags.NonPublic | BindingFlags.Static);
        var narration = string.Join(" ", new[]
        {
            "Current MeteorShower Event makes Perseids Tonight worth planning for tonight with family nearby.",
            "Watch near western sky with peak timing around 9 PM and the best viewing window at 9 PM to midnight.",
            "Use a chair, dim your phone, and let your eyes adapt before scanning slowly.",
            "This extra context adds atmosphere, expectation, wonder, patience, comfort, curiosity, and perspective for viewers tonight.",
            "Check clouds, choose a safe open spot, save this viewing window, share it nearby, and step outside safely."
        });

        var preTrimWordCount = (int)countMethod!.Invoke(null, [narration])!;
        var preTrimDuration = (double)estimateMethod!.Invoke(null, [narration])!;
        var trimmed = (string)trimMethod!.Invoke(null, [narration, context])!;
        var postTrimWordCount = (int)countMethod.Invoke(null, [trimmed])!;
        var postTrimDuration = (double)estimateMethod.Invoke(null, [trimmed])!;

        Assert.Equal(80, preTrimWordCount);
        Assert.True(preTrimDuration > 45.0);
        Assert.True(postTrimWordCount <= 79);
        Assert.True(postTrimDuration <= 45.0);
        Assert.DoesNotContain("This extra context adds atmosphere", trimmed);
        Assert.Contains("Perseids Tonight", trimmed);
        Assert.Contains("9 PM to midnight", trimmed);
        Assert.Contains("Check clouds", trimmed);
    }


    [Fact]
    public void BuildPhase6SceneVisualVariants_ReturnsPlanningOnlyMetadataWithoutRendering()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase6SceneVisualVariants", BindingFlags.NonPublic | BindingFlags.Static);
        var scene = new EnrichedQuestionSceneDto(
            2,
            "How",
            "ExplainObject",
            "How do I find Mars?",
            "Look west after sunset.",
            "CasualSkyWatcher",
            "Beginner",
            "Mars is low in the west.",
            "Explain where Mars appears.",
            "Show Mars above the western horizon.",
            "Mars over a dim western horizon.",
            "Mars • western horizon",
            "Mars label near the horizon.",
            true);

        var variants = (IReadOnlyList<SceneVisualVariantDto>)method!.Invoke(null, [scene])!;

        Assert.InRange(variants.Count, 3, 5);
        Assert.Equal(["wide_context", "object_focus", "educational_overlay", "cinematic_detail", "transition_or_closing"], variants.Select(v => v.VariantType).ToArray());
        Assert.Equal(Enumerable.Range(1, variants.Count), variants.Select(v => v.VariantNo));
        Assert.Equal(variants.Count, variants.Select(v => v.CompositionHint).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(variants, variant => variant.VariantType == "wide_context" && variant.CompositionHint.Contains("WIDE FRAMING", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, variant => variant.VariantType == "object_focus" && variant.CompositionHint.Contains("ZOOMED FRAMING", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, variant => variant.VariantType == "educational_overlay" && variant.CompositionHint.Contains("INFOGRAPHIC LAYOUT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, variant => variant.VariantType == "cinematic_detail" && variant.CompositionHint.Contains("CLOSE-UP CINEMATIC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, variant => variant.VariantType == "transition_or_closing" && variant.CompositionHint.Contains("CTA COMPOSITION", StringComparison.OrdinalIgnoreCase));
        Assert.All(variants, variant =>
        {
            Assert.False(string.IsNullOrWhiteSpace(variant.Purpose));
            Assert.True(variant.RecommendedDurationSeconds > 0);
            Assert.False(string.IsNullOrWhiteSpace(variant.CameraStyle));
            Assert.False(string.IsNullOrWhiteSpace(variant.CompositionHint));
            Assert.False(string.IsNullOrWhiteSpace(variant.MotionHint));
            Assert.False(string.IsNullOrWhiteSpace(variant.OverlayHint));
            Assert.Contains("do not render", variant.RendererHint, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("scene-02-", variant.OutputFileNameSuggestion, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void EnrichedSceneJson_OmitsVisualVariants_WhenSceneVariantsAreDisabled()
    {
        var scene = new EnrichedQuestionSceneDto(
            1,
            "What",
            "OpeningOverview",
            "What is happening?",
            "The Moon is full.",
            "CasualSkyWatcher",
            "Beginner",
            "The full Moon is visible tonight.",
            "Explain the full Moon timing.",
            "Show the Moon over the horizon.",
            "Full Moon above trees.",
            "Full Moon",
            "Moon label centered.",
            true);

        var json = JsonSerializer.Serialize(scene, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("visualVariants", json);
    }

    [Fact]
    public async Task Phase6SceneVisualVariants_AreWrittenIntoEnrichedScenePlan_WhenEnabled()
    {
        var context = CreateContext("NamedFullMoon", ["ShortVideo"], enableSceneVariants: true);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "The Moon is full tonight.",
            narrationIntent: "Explain when the full Moon rises.",
            visualIntent: "Show the Moon above the horizon.",
            imagePromptIntent: "Full Moon over a clean horizon.",
            overlayIntent: "Moon • eastern horizon",
            accessibilityIntent: "Full Moon label near the horizon.");
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        var method = typeof(ProductionPipelineExecutionService).GetMethod("AddPhase6SceneVisualVariantsAsync", BindingFlags.NonPublic | BindingFlags.Static);

        var generatedVariants = await (Task<int>)method!.Invoke(null, [path, CancellationToken.None])!;

        var json = await File.ReadAllTextAsync(path);
        var plan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(generatedVariants >= 3);
        Assert.Contains("visualVariants", json);
        Assert.All(plan.Scenes, scene => Assert.True(scene.VisualVariants?.Count >= 3));
    }

    [Fact]
    public async Task ValidatePhase6EnrichedScenePlanContract_Fails_WhenSceneVariantsEnabledAndAnySceneHasFewerThanThreeVariants()
    {
        var context = CreateContext("NamedFullMoon", ["ShortVideo"], enableSceneVariants: true);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "The Moon is full tonight.",
            narrationIntent: "Explain when the full Moon rises.",
            visualIntent: "Show the Moon above the horizon.",
            imagePromptIntent: "Full Moon over a clean horizon.",
            overlayIntent: "Moon • eastern horizon",
            accessibilityIntent: "Full Moon label near the horizon.");
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase6EnrichedScenePlanContractAsync", BindingFlags.NonPublic | BindingFlags.Static);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await (Task)method!.Invoke(null, [context, path, CancellationToken.None])!);

        Assert.Contains("at least 3 visual variants", exception.Message);
    }

    [Fact]
    public async Task Phase6PlanetGroupingDiagnostics_DetectsInjectedIntentPhrasesAcrossAllIntentFields()
    {
        var context = CreateContext("PLANET_GROUPING", ["ShortVideo"]);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Draw a grouping arc connecting the visible planets.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Guided scan path: begin at western horizon and move upward.");

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);

        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
        Assert.Equal(6, GetIntDiagnostic(diagnostics, "EnrichedSceneIntentCount"));
    }

    [Fact]
    public async Task Phase6PlanetGroupingDiagnostics_DoesNotApplyInjectedPhraseDetectionToOtherEventTypes()
    {
        var context = CreateContext("PlanetPairing", ["ShortVideo"]);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Multi-planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Use a scan path from west to east.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Guided scan path: begin at western horizon and move upward.");

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);

        Assert.False(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
    }

    [Fact]
    public async Task Phase6PlanetGroupingContract_UsesInjectedIntentDiagnosticsInsteadOfLegacyObjectPresence()
    {
        var context = CreateContext("PLANET_GROUPING", ["ShortVideo"]);
        context = context with
        {
            ProductionEventIntelligence = context.ProductionEventIntelligence with
            {
                RequiredVisualObjects = ["planet grouping", "guided scan path"]
            }
        };
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Draw a grouping arc connecting the visible planets.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Begin at the western horizon and move upward.");

        await ValidatePhase6EnrichedScenePlanContractAsync(context);

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
    }


    [Fact]
    public void Phase7LegacyNarrationValidationHelper_IsRemoved()
    {
        Assert.Null(typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase7NarrationFilesGenerated", BindingFlags.NonPublic | BindingFlags.Static));
    }


    private static bool IsPhaseRequired(ProductionPhaseContext context, int phaseNo)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("IsPhaseRequiredForRequestedOutputs", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method!.Invoke(null, [context, phaseNo])!;
    }

    private static object BuildPhase6SceneEnrichmentDiagnostics(ProductionPhaseContext context)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase6SceneEnrichmentDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
        return method!.Invoke(null, [context])!;
    }

    private static async Task ValidatePhase6EnrichedScenePlanContractAsync(ProductionPhaseContext context)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase6EnrichedScenePlanContractAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        var task = (Task)method!.Invoke(null, [context, path, CancellationToken.None])!;
        await task;
    }

    private static bool GetBooleanDiagnostic(object diagnostics, string propertyName)
        => (bool)diagnostics.GetType().GetProperty(propertyName)!.GetValue(diagnostics)!;

    private static int GetIntDiagnostic(object diagnostics, string propertyName)
        => (int)diagnostics.GetType().GetProperty(propertyName)!.GetValue(diagnostics)!;


    private static QuestionDrivenNarrationResponse BuildValidNarrationResponse(IReadOnlyList<string> generatedFiles)
    {
        var narration = new QuestionDrivenNarrationDto("event-id", "us", "en", [], 0, DateTimeOffset.UtcNow);
        var review = new QuestionDrivenNarrationReviewDto("event-id", "us", "en", true, 0, 0, [], [], DateTimeOffset.UtcNow);
        return new QuestionDrivenNarrationResponse("event-id", 0, 0, true, narration, review, generatedFiles, []);
    }

    private static async Task WriteEnrichedScenePlanAsync(
        ProductionPhaseContext context,
        string viewerTakeaway,
        string narrationIntent,
        string visualIntent,
        string imagePromptIntent,
        string overlayIntent,
        string accessibilityIntent)
    {
        Directory.CreateDirectory(context.ExecutionContext.QuestionRoot!);
        var plan = new EnrichedQuestionScenePlanDto(
            "event-id",
            context.Request.RegionId,
            context.Request.Language,
            "CasualSkyWatcher",
            "Beginner",
            [
                new EnrichedQuestionSceneDto(
                    1,
                    "What",
                    "OpeningOverview",
                    "What should I look for?",
                    "Look for the planets near the horizon.",
                    "CasualSkyWatcher",
                    "Beginner",
                    viewerTakeaway,
                    narrationIntent,
                    visualIntent,
                    imagePromptIntent,
                    overlayIntent,
                    accessibilityIntent,
                    true)
            ],
            true,
            DateTimeOffset.UtcNow,
            new QuestionSceneEnrichmentDiagnostics(
                context.ProductionEventIntelligence.EventType,
                context.ProductionEventIntelligence.RequiredVisualObjects,
                [],
                [],
                [],
                "Test"));
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static IReadOnlyList<RequestedOutputCompletion> BuildRequestedOutputCompletion(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults)
    {
        var method = typeof(ProductionPipelineExecutionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "BuildRequestedOutputCompletion" && m.GetParameters().Length == 2);
        return (IReadOnlyList<RequestedOutputCompletion>)method.Invoke(null, [context, phaseResults])!;
    }

    private static bool InvokePhase18DiagnosticsValidator(string methodName, JsonNode? diagnostics)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [diagnostics])!;
    }

    private static string InvokePhase18MotionV2StrengthResolver(string? requestMotionV2Strength, string? planMotionV2Strength)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase18MotionV2Strength", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [requestMotionV2Strength, planMotionV2Strength])!;
    }

    private static bool InvokePhase18MotionV2StrengthMismatch(string? requestMotionV2Strength, string? motionV2StrengthUsed)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("HasMotionV2StrengthMismatch", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [requestMotionV2Strength, motionV2StrengthUsed])!;
    }

    private static bool InvokePhase18MotionV2StrengthOverrideWarning(string? requestMotionV2Strength, string? planMotionV2Strength)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ShouldWarnMotionV2StrengthRequestOverride", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [requestMotionV2Strength, planMotionV2Strength])!;
    }

    private static void WritePhase10SceneAssets(string root, string profile, int count, int? skipFinalSceneNumber = null)
    {
        for (var i = 1; i <= count; i++)
        {
            var sceneId = $"scene-{i:000}";
            var sceneDirectory = Path.Combine(root, "scene-assets", profile, sceneId);
            Directory.CreateDirectory(sceneDirectory);
            if (skipFinalSceneNumber == i) continue;
            File.WriteAllBytes(Path.Combine(sceneDirectory, $"{sceneId}-final.png"), [1, 2, 3]);
        }
    }

    [Fact]
    public void OverwriteCleanup_Phase13Only_PreservesEarlierValidationAndOtherOutputRoots()
    {
        var baseContext = CreateContext("MeteorShower", ["Gallery"]);
        var deleted = new List<string>();
        var context = baseContext with
        {
            StartPhaseNo = 13,
            EndPhaseNo = 13,
            OverwriteExisting = true,
            DeletedFilesDueToOverwrite = deleted,
            PipelineRequest = baseContext.PipelineRequest with { StartPhaseNo = 13, EndPhaseNo = 13, OverwriteExisting = true }
        };

        Directory.CreateDirectory(context.ExecutionContext.ValidationRoot!);
        Directory.CreateDirectory(Path.Combine(context.OutputRoot, "gallery"));
        Directory.CreateDirectory(context.ExecutionContext.HeroRoot!);
        Directory.CreateDirectory(context.ExecutionContext.ThumbnailRoot!);
        Directory.CreateDirectory(context.ExecutionContext.QuestionRoot!);
        Directory.CreateDirectory(context.ExecutionContext.SceneRoot!);
        Directory.CreateDirectory(context.ExecutionContext.NarrationRoot!);
        Directory.CreateDirectory(context.ExecutionContext.TtsRoot!);
        Directory.CreateDirectory(context.ExecutionContext.VideoAssemblyRoot!);

        for (var phaseNo = 1; phaseNo <= 13; phaseNo++)
            File.WriteAllText(Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json"), "{}");
        File.WriteAllText(Path.Combine(context.OutputRoot, "gallery", "gallery-01.png"), "gallery");
        File.WriteAllText(Path.Combine(context.ExecutionContext.HeroRoot!, "hero-final.png"), "hero");
        File.WriteAllText(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail.png"), "thumbnail");
        File.WriteAllText(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-answer-set.json"), "questions");
        File.WriteAllText(Path.Combine(context.ExecutionContext.SceneRoot!, "scene.png"), "scene");
        File.WriteAllText(Path.Combine(context.ExecutionContext.NarrationRoot!, "narration.txt"), "narration");
        File.WriteAllText(Path.Combine(context.ExecutionContext.TtsRoot!, "narration.mp3"), "tts");
        File.WriteAllText(Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "final-video-short.mp4"), "video");

        var method = GetPrivateInstanceMethod("ClearPhaseRangeOutputsForOverwrite", typeof(ProductionPhaseContext), typeof(int?));
        var service = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ProductionPipelineExecutionService));
        method.Invoke(service, [context, null]);

        for (var phaseNo = 1; phaseNo <= 12; phaseNo++)
            Assert.True(File.Exists(Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json")), $"phase {phaseNo} validation should be preserved");

        Assert.False(File.Exists(Path.Combine(context.ExecutionContext.ValidationRoot!, "phase-13-validation.json")));
        Assert.False(Directory.Exists(Path.Combine(context.OutputRoot, "gallery")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.HeroRoot!, "hero-final.png")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail.png")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-answer-set.json")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.SceneRoot!, "scene.png")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.NarrationRoot!, "narration.txt")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.TtsRoot!, "narration.mp3")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "final-video-short.mp4")));
        Assert.DoesNotContain(deleted, path => path.Contains("phase-12-validation.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deleted, path => path.Contains("phase-13-validation.json", StringComparison.OrdinalIgnoreCase));
    }



    [Fact]
    public void OverwriteCleanup_Phase15Only_PreservesNarrationSubtitlesAndDeletesOnlyTts()
    {
        var baseContext = CreateContext("MeteorShower", ["TtsTimeline"]);
        var deleted = new List<string>();
        var deletedDirectories = new List<string>();
        var skippedDirectories = new List<string>();
        var context = baseContext with
        {
            StartPhaseNo = 15,
            EndPhaseNo = 15,
            OverwriteExisting = true,
            DeletedFilesDueToOverwrite = deleted,
            DeletedDirectoriesDueToOverwrite = deletedDirectories,
            SkippedDirectoriesDueToOverwrite = skippedDirectories,
            PipelineRequest = baseContext.PipelineRequest with { StartPhaseNo = 15, EndPhaseNo = 15, OverwriteExisting = true }
        };

        var subtitleRoot = Path.Combine(context.ExecutionContext.NarrationRoot!, "subtitles", "en");
        var ttsEnRoot = Path.Combine(context.ExecutionContext.TtsRoot!, "en");
        var ttsHiRoot = Path.Combine(context.ExecutionContext.TtsRoot!, "hi");
        Directory.CreateDirectory(subtitleRoot);
        Directory.CreateDirectory(ttsEnRoot);
        Directory.CreateDirectory(ttsHiRoot);
        File.WriteAllText(Path.Combine(subtitleRoot, "short.srt"), "1");
        File.WriteAllText(Path.Combine(subtitleRoot, "long.srt"), "1");
        File.WriteAllText(Path.Combine(context.ExecutionContext.NarrationRoot!, "narration.txt"), "phase 14 narration");
        File.WriteAllText(Path.Combine(ttsEnRoot, "short.mp3"), "tts");
        File.WriteAllText(Path.Combine(ttsHiRoot, "short.mp3"), "tts");

        var method = GetPrivateInstanceMethod("ClearPhaseRangeOutputsForOverwrite", typeof(ProductionPhaseContext), typeof(int?));
        var service = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ProductionPipelineExecutionService));
        method.Invoke(service, [context, null]);

        Assert.True(File.Exists(Path.Combine(subtitleRoot, "short.srt")));
        Assert.True(File.Exists(Path.Combine(subtitleRoot, "long.srt")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.NarrationRoot!, "narration.txt")));
        Assert.False(Directory.Exists(context.ExecutionContext.TtsRoot!));
        Assert.DoesNotContain(deletedDirectories, path => path.Contains(Path.Combine("narration", "subtitles"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deletedDirectories, path => path.EndsWith("tts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(skippedDirectories, path => path.EndsWith("narration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(skippedDirectories, path => path.Contains(Path.Combine("narration", "subtitles"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanetConjunctionNarrationV22_HumanizesBestTimeAndSkyGuideFragments()
    {
        var naturalTime = GetPrivateStaticMethod("NaturalViewingWindow", typeof(string), typeof(bool));
        var naturalDirection = GetPrivateStaticMethod("NaturalSkyDirection", typeof(string), typeof(bool));

        var time = (string)naturalTime!.Invoke(null, ["Jun 9, 2026 7:23 PM", false])!;
        var direction = (string)naturalDirection!.Invoke(null, ["the western sky after sunset horizon", false])!;

        Assert.Equal("June ninth", time);
        Assert.Equal("the western horizon", direction);
        Assert.DoesNotContain("7:23", time);
        Assert.DoesNotContain("after sunset horizon", direction);
    }

    [Fact]
    public void PlanetConjunctionNarrationV22_DiagnosticsCatchCauseSkyGuideAndBestTimeQuality()
    {
        var causeMethod = typeof(ProductionPipelineExecutionService).GetMethod("DetectCauseDuplication", BindingFlags.NonPublic | BindingFlags.Static);
        var skyGuideMethod = typeof(ProductionPipelineExecutionService).GetMethod("SkyGuideGrammarPassed", BindingFlags.NonPublic | BindingFlags.Static);
        var bestTimeMethod = typeof(ProductionPipelineExecutionService).GetMethod("BestTimeHumanizationPassed", BindingFlags.NonPublic | BindingFlags.Static);

        var causeDuplicationDetected = (bool)causeMethod!.Invoke(null, [new[] { "Although the planets appear close together, they are separated by distance. Their apparent closeness is because they appear close from perspective. This repeats the alignment perspective again." }])!;
        var skyGuideGrammarPassed = (bool)skyGuideMethod!.Invoke(null, [new[] { "About thirty minutes after sunset, turn your attention toward the western horizon. There you'll find two bright planets appearing unusually close together above the skyline." }])!;
        var bestTimeHumanizationPassed = (bool)bestTimeMethod!.Invoke(null, [new[] { "The conjunction reaches its finest appearance during the evenings surrounding June ninth. Arriving a little before sunset gives your eyes time to adjust as the sky slowly darkens." }])!;

        Assert.True(causeDuplicationDetected);
        Assert.True(skyGuideGrammarPassed);
        Assert.True(bestTimeHumanizationPassed);
    }

    [Fact]
    public void PlanetConjunctionHookGreetingGuard_PrefixesOnlyUngreetedHookNarration()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ApplyPlanetConjunctionHookGreetingGuard", BindingFlags.NonPublic | BindingFlags.Static);
        var shortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "Jupiter and Venus make the western sky feel cinematic tonight.",
            ["002-cause"] = "The planets appear close because their orbits line up along our view from Earth."
        };
        var longTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001-hook"] = "Hello, fellow stargazers. Jupiter and Venus already have a host greeting.",
            ["002-what-is-it"] = "A conjunction is an apparent close pairing in the sky."
        };

        var diagnostics = method!.Invoke(null, ["PlanetConjunction", shortTexts, longTexts])!;

        Assert.StartsWith("Welcome to Drashyam. Jupiter and Venus", shortTexts["001-hook"]);
        Assert.StartsWith("Hello, fellow stargazers. Jupiter and Venus", longTexts["001-hook"]);
        Assert.Equal("The planets appear close because their orbits line up along our view from Earth.", shortTexts["002-cause"]);
        Assert.Equal("A conjunction is an apparent close pairing in the sky.", longTexts["002-what-is-it"]);
        Assert.True((bool)diagnostics.GetType().GetProperty("HookGreetingRequired")!.GetValue(diagnostics)!);
        Assert.True((bool)diagnostics.GetType().GetProperty("HookGreetingApplied")!.GetValue(diagnostics)!);
        Assert.Equal("Welcome to Drashyam", diagnostics.GetType().GetProperty("HookGreetingText")!.GetValue(diagnostics));
        Assert.Equal("Jupiter and Venus make the western sky feel cinematic tonight.", diagnostics.GetType().GetProperty("HookBeforePrefixFirst120Chars")!.GetValue(diagnostics));
        Assert.StartsWith("Welcome to Drashyam. Jupiter and Venus", (string)diagnostics.GetType().GetProperty("HookAfterPrefixFirst120Chars")!.GetValue(diagnostics)!);
    }

    private static ProductionPhaseContext CreateContext(string eventType, IReadOnlyList<string> requestedOutputs, string? shortTitleOverride = null, bool enableSceneVariants = false)
    {
        var planId = Guid.NewGuid();
        var outputRoot = Path.Combine(Path.GetTempPath(), "astro-pulse-phase-gating-tests", planId.ToString("N"));
        var request = new ContentPlanProductionPipelineRequest(
            planId,
            "AstronomyEvent",
            $"Current {eventType} Event",
            shortTitleOverride ?? $"{eventType} Tonight",
            eventType,
            "us",
            "en",
            [eventType == "PlanetPairing" ? "Venus" : "Moon"],
            eventType == "PlanetPairing" ? ["Jupiter"] : [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow,
            null,
            string.Join("+", requestedOutputs),
            requestedOutputs,
            null,
            null,
            null,
            null,
            "Verified",
            "Test",
            "Current event strategy",
            "9 PM",
            "western sky",
            "United States",
            null,
            "9 PM to midnight",
            null,
            null,
            null,
            requestedOutputs,
            [],
            []);
        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            eventType,
            request.Title,
            request.ShortTitle,
            request.StartUtc,
            request.PeakUtc,
            request.LocalPeakTime,
            request.BestViewingWindowLocal,
            request.SkyDirectionHint,
            request.VisibilityRegion,
            request.PrimaryObjects,
            request.SecondaryObjects,
            null,
            request.MoonInterference,
            request.MoonIlluminationPercent,
            null,
            [],
            [],
            [],
            [],
            []);
        var executionContext = new ProductionPipelineExecutionContext(
            true,
            planId,
            Guid.NewGuid(),
            null,
            true,
            true,
            "Approved",
            "Approved",
            true,
            true,
            "Verified",
            request.ContentStrategy,
            request.RegionId,
            request.Language,
            request.RequestedOutputs,
            request.Category,
            request.PlannedFormat,
            DateTimeOffset.UtcNow.Year,
            request.EventType,
            Path.Combine(outputRoot, "plan-input"),
            Path.Combine(outputRoot, "question-engine"),
            Path.Combine(outputRoot, "scene-approval-v3"),
            Path.Combine(outputRoot, "hero"),
            Path.Combine(outputRoot, "thumbnails"),
            Path.Combine(outputRoot, "narration"),
            Path.Combine(outputRoot, "tts"),
            Path.Combine(outputRoot, "video-assembly"),
            Path.Combine(outputRoot, "validation"),
            intelligence,
            new GenericAstronomyEventStrategy(),
            EnableSubtitles: false);
        var pipelineRequest = new ProductionPipelineRequest(request, Guid.NewGuid(), outputRoot, false, ExecutionContext: executionContext, EnableSceneVariants: enableSceneVariants);
        return new ProductionPhaseContext(pipelineRequest, request, Guid.NewGuid(), Guid.NewGuid().ToString("D"), outputRoot, executionContext, intelligence, new GenericAstronomyEventStrategy(), false, false, 1, 20, false);
    }
}
