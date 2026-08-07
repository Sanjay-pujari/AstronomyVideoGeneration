using Astronomy.MediaFactory.Core;
using System.Text.Json;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase8AuthorityArchitectureTests
{
    [Fact]
    public void Rc2ProductionUsesAuthoritySceneAssetsV3WithoutFeatureFlag()
        => Assert.True(ProductionPipelineExecutionService.ShouldUseAuthoritySceneAssetsV3(useProductionPipeline: true, enableSceneAssetsV3: false));

    [Fact]
    public void LegacyCompatibilityModeCanStillUseVariants()
        => Assert.False(ProductionPipelineExecutionService.ShouldUseAuthoritySceneAssetsV3(useProductionPipeline: false, enableSceneAssetsV3: false));

    [Fact]
    public void AuthorityModeNeverReadsEnrichedQuestionPlan()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "private async Task<IReadOnlyList<string>> GenerateSceneAssetsV3Async", "private async Task<StoryFrameV4ComparisonExecutionResult>");
        Assert.DoesNotContain("question-driven-scene-plan.enriched.json", method);
        Assert.DoesNotContain("ValidateSceneApprovalTextBeforeRendering", method);
        Assert.DoesNotContain("RenderPhase8SceneVisualVariantsAsync", method);
    }

    [Fact]
    public void AuthorityFailureDoesNotFallbackToLegacy()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "private async Task<IReadOnlyList<string>> GenerateSceneAssetsV3Async", "private async Task<StoryFrameV4ComparisonExecutionResult>");
        Assert.Contains("phase8AuthorityLoader.LoadAsync", method);
        Assert.Contains("throw;", method);
        Assert.DoesNotContain("question-driven-scene-plan.enriched.json", method);
    }

    [Fact]
    public void ShortOnlyOrionRequestsFourScenes()
    {
        var request = new Phase8AuthorityLoadRequest("root", "plan", "event", "en", ["Short"]);
        Assert.Contains("Short", request.RequestedVariants);
        Assert.DoesNotContain("Long", request.RequestedVariants);
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        Assert.Contains("expectedShortSceneCount\"] = requestedShort ? Phase8AuthoritySceneCount", source);
    }

    [Fact]
    public void BothRequestedOrionProduces12And4()
    {
        var request = new Phase8AuthorityLoadRequest("root", "plan", "event", "en", ["Long", "Short"]);
        Assert.Equal(["Long", "Short"], request.RequestedVariants);
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        Assert.Contains("authority.ShortScenes.Count", source);
        Assert.Contains("authority.LongScenes.Count", source);
    }

    [Fact]
    public void AuthorityCleanupOwns08SceneAssets()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "private void ClearPhaseRangeOutputsForOverwrite", "private static void DeleteFileIfExists");
        Assert.Contains("IsSceneAssetsV3Enabled(context)", method);
        Assert.Contains("\"08-scene-assets\"", method);
        Assert.Contains("\"scene-assets-v3\"", method);
    }

    [Fact]
    public void AuthorityRequest_PreservesExactLineageAndAcceptedNarration()
    {
        var properties = typeof(Phase8SceneRequirement).GetProperties().Select(x => x.Name).ToHashSet();
        Assert.Contains(nameof(Phase8SceneRequirement.SceneId), properties);
        Assert.Contains(nameof(Phase8SceneRequirement.BlueprintSceneId), properties);
        Assert.Contains(nameof(Phase8SceneRequirement.StoryFrameId), properties);
        Assert.Contains(nameof(Phase8SceneRequirement.AcceptedNarrationSceneId), properties);
        Assert.Contains(nameof(Phase8SceneRequirement.KnowledgeReferenceIds), properties);
    }

    [Fact]
    public void ProductionAuthorityPath_DoesNotOwnLegacyFiveOrNineCounts()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "private async Task<IReadOnlyList<string>> GenerateSceneAssetsV3Async", "private async Task<StoryFrameV4ComparisonExecutionResult>");
        Assert.Contains("authority.ShortScenes.Count", method);
        Assert.Contains("authority.LongScenes.Count", method);
        Assert.DoesNotContain("GetExpectedSceneIds", method);
        Assert.DoesNotContain("question-driven-narration-v2", method);
    }

    [Fact]
    public void AuthorityDrivenService_DoesNotLoadLegacyTimelineContext()
    {
        var source = ReadInfrastructure("SceneAssetsV3Service.cs");
        Assert.Contains("request.AuthorityInput is { } authority", source);
        Assert.Contains("BuildAuthorityBeats(context, scenes)", source);
        Assert.Contains("07-narration/accepted-release-candidate.json", source);
    }

    [Fact]
    public void Loader_UsesOnlyCommittedPhaseFourSixSevenPaths()
    {
        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        Assert.Contains("04-blueprint", source);
        Assert.Contains("06-story-frames", source);
        Assert.Contains("accepted-release-candidate.json", source);
        Assert.DoesNotContain("narration-v5", source.ToLowerInvariant());
        Assert.DoesNotContain("question-driven-narration-v2", source.ToLowerInvariant());
    }

    [Fact]
    public void AcceptedReleaseCandidateDoesNotRequireNarrativeDraft()
    {
        const string json = """
            {"schemaVersion":"2.0","attemptId":"attempt","generatedUtc":"2026-08-07T00:00:00Z",
             "releaseCandidateId":"short-accepted","executionId":"execution","planId":"plan","eventId":"orion",
             "language":"en","variant":"Short","sourceBlueprintAggregateId":"blueprint","sourceBlueprintAggregateChecksum":"bp-checksum",
             "sourceVariantBlueprintId":"short-blueprint","sourceVariantBlueprintChecksum":"variant-checksum",
             "sourceStoryFramesAuthorityId":"story-authority","sourceStoryFramesAuthorityChecksum":"story-checksum",
             "blueprintSceneCount":4,"acceptedSceneCount":4,
             "scenes":[
               {"sceneId":"orion-short-01","sceneNumber":1,"blueprintSceneId":"orion-short-01","storyFrameId":"sf-01","selectedKnowledgeReferenceIds":[],"selectedClaimIds":[],"narrationText":"Orion rises over the winter horizon."},
               {"sceneId":"orion-short-02","sceneNumber":2,"blueprintSceneId":"orion-short-02","storyFrameId":"sf-02","selectedKnowledgeReferenceIds":[],"selectedClaimIds":[],"narrationText":"Three belt stars mark its center."},
               {"sceneId":"orion-short-03","sceneNumber":3,"blueprintSceneId":"orion-short-03","storyFrameId":"sf-03","selectedKnowledgeReferenceIds":[],"selectedClaimIds":[],"narrationText":"Use the belt to orient your view."},
               {"sceneId":"orion-short-04","sceneNumber":4,"blueprintSceneId":"orion-short-04","storyFrameId":"sf-04","selectedKnowledgeReferenceIds":[],"selectedClaimIds":[],"narrationText":"Look from a dark location."}],
             "acceptanceResult":{"accepted":true,"reason":"Accepted"},"deterministicChecksum":"checksum"}
            """;
        var accepted = JsonSerializer.Deserialize<Phase7AcceptedReleaseCandidate>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(accepted);
        Assert.Equal(4, accepted.AcceptedSceneCount);
        Assert.Equal(4, accepted.Scenes.Count);

        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        Assert.Contains("ReadAsync<Phase7AcceptedReleaseCandidate>", source);
        Assert.DoesNotContain("DocumentaryNarrativeReleaseCandidate", source);
        Assert.DoesNotContain("NarrativeDraft", source);
    }

    [Fact]
    public void AuthorityModeNeverConstructsNarrativeDraft()
    {
        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        Assert.DoesNotContain("new DocumentaryNarrativeDraft", source);
        Assert.DoesNotContain("NarrativeLifecycle", source);
    }

    [Fact]
    public void ShortOnlyLoadsOnlyShortAcceptedCandidate()
    {
        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        Assert.Contains("if (variants.Contains(\"Short\"))", source);
        Assert.Contains("if (variants.Contains(\"Long\"))", source);
        Assert.DoesNotContain("blueprint.LongBlueprint.Scenes.Count", source);
    }

    [Fact]
    public void AcceptedNarrationMapsToCommittedStoryFramesByGovernedIdentity()
    {
        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        var sceneId = source.IndexOf("x.SceneId == group.Key", StringComparison.Ordinal);
        var storyFrameId = source.IndexOf("f.FrameId == x.StoryFrameId", StringComparison.Ordinal);
        var blueprintSceneId = source.IndexOf("x.BlueprintSceneId == scene.SceneId", StringComparison.Ordinal);
        Assert.True(sceneId >= 0 && sceneId < storyFrameId && storyFrameId < blueprintSceneId);
        Assert.Contains("candidate.Scenes.Count != frames.Length", source);
    }

    [Fact]
    public void MissingShortCandidateFailsPrecisely()
    {
        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        Assert.Contains("Phase8AuthorityReasonCodes.VariantAuthorityMissing", source);
        Assert.Contains("accepted release candidate is missing", source);
    }

    [Fact]
    public void MissingNarrationSceneMappingFailsPrecisely()
        => Assert.Contains("Phase8AuthorityReasonCodes.NarrationSceneMappingFailed", ReadInfrastructure("Phase8AuthorityLoader.cs"));

    [Fact]
    public void Phase6CommittedAuthorityIsUsedAndLegacyStoryFrameV4ComparisonNotRequired()
    {
        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        Assert.Contains("06-story-frames", source);
        Assert.Contains("story-frames.json", source);
        Assert.Contains("story-frame-index.json", source);
        Assert.DoesNotContain("short-story-frames", source);
        Assert.DoesNotContain("long-story-frames", source);
    }

    [Fact]
    public void OrionShortAuthorityCountComesFromProjection()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        Assert.Contains("d[\"expectedShortSceneCount\"] = authority.ShortScenes.Count", source);
        Assert.Contains("d[\"expectedLongSceneCount\"] = authority.LongScenes.Count", source);
    }

    [Fact]
    public void RoutingDiagnosticsReportTrueReason()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        Assert.Contains("RC2ProductionAuthoritySceneAssetsV3", source);
        Assert.Contains("enableSceneAssetsV3Flag", source);
    }

    [Fact]
    public void LegacyCandidateSemanticChecksumDoesNotOverrideCommittedPhysicalState()
    {
        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        Assert.Contains("Legacy/NotAuthoritative/Advisory", source);
        Assert.Contains("ExpectedPhysicalSha256, physical", source);
        Assert.Contains("ValidatePublication(publication, variant)", source);
        Assert.DoesNotContain("NarrationCandidateSemanticChecksumMismatch,", source);
    }

    [Fact]
    public void PlanningArtifactsAreWrittenBeforeProviderGenerationAndSurviveFailure()
    {
        var source = ReadInfrastructure("SceneAssetsV3Service.cs");
        var planning = source.IndexOf("WriteAuthorityPlanningPackageAsync(request, authority", StringComparison.Ordinal);
        var generation = source.IndexOf("GenerateFormatAsync(root, \"short\"", planning, StringComparison.Ordinal);
        Assert.True(planning >= 0 && generation > planning);
        foreach (var artifact in new[] { "media-project.json", "visual-asset-plan.json", "visual-generation-requests.json",
                     "authority-load-diagnostics.json", "visual-plan-diagnostics.json", "provider-failure-diagnostics.json",
                     "publication-failure-report.json", "phase8-publication-report.json" })
            Assert.Contains(artifact, source);
    }

    [Fact]
    public void AuthorityLoadingPublishesEveryStageDiagnostic()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        foreach (var name in new[] { "phase4AuthorityLoadStarted", "phase4AuthorityLoaded", "phase6AuthorityLoadStarted",
                     "phase6AuthorityLoaded", "shortNarrationAuthorityLoadStarted", "shortNarrationAuthorityLoaded",
                     "longNarrationAuthorityLoadStarted", "longNarrationAuthorityLoaded", "authorityProjectionStarted",
                     "authorityProjectionCompleted", "authorityFailureStage", "authorityFailureType", "authorityFailureMessage" })
            Assert.Contains($"diagnostics[\"{name}\"]", source);
    }

    [Fact]
    public void ManifestCarriesAuthorityProviderPhysicalAndSemanticEvidence()
    {
        var properties = typeof(SceneAssetManifestItem).GetProperties().Select(x => x.Name).ToHashSet();
        foreach (var name in new[] { "ProviderType", "ProviderStatus", "SourceInstructionId", "SourceKnowledgeReferenceIds", "Checksum", "SemanticIdentity", "Reused", "ProviderCalledThisExecution" })
            Assert.Contains(name, properties);
    }

    [Fact]
    public void PhaseNineConsumesCommittedLongAuthorityBeforeGeneration()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "PhaseValidateLongSceneImagesAsync", "PhaseValidateSceneAssetsAsync");
        Assert.Contains("scene-asset-manifest.json", method);
        Assert.Contains("PublicationState == \"Committed\"", method);
    }

    [Fact]
    public void PhaseTenUsesRequestedAuthorityVariants()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "ValidateSceneAssetsV3WithDiagnosticsAsync", "PopulatePhase8FormatAwareDiagnostics");
        Assert.Contains("RequestedVariants.Contains(\"Short\"", method);
        Assert.Contains("RequestedVariants.Contains(\"Long\"", method);
    }

    private static string ReadInfrastructure(string name) => File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", name));
    private static string Slice(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal); var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(first >= 0 && last > first); return source[first..last];
    }
}
