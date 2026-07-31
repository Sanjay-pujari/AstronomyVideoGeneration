using System.Reflection;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase4DownstreamAuthorityArchitectureTests
{
    [Fact]
    public void execution_context_carries_the_published_aggregate()
    {
        var property = typeof(ProductionPipelineExecutionContext)
            .GetProperty(nameof(ProductionPipelineExecutionContext.PublishedDocumentaryBlueprintAggregate));

        Assert.NotNull(property);
        Assert.Equal(typeof(DocumentaryBlueprintAggregate), property!.PropertyType);
    }

    [Fact]
    public void pipeline_has_no_legacy_phase4_builder_dependencies()
    {
        var constructors = typeof(ProductionPipelineExecutionService).GetConstructors();
        var dependencies = constructors.SelectMany(x => x.GetParameters()).Select(x => x.ParameterType).ToArray();

        Assert.DoesNotContain(dependencies, x => x.Name is "DocumentaryBlueprintBuilder" or "StoryGraphBuilder");
        Assert.Contains(typeof(IDocumentaryBlueprintPhase4IntegrationService), dependencies);
    }

    [Fact]
    public void publication_service_is_the_phase4_publication_boundary()
    {
        Assert.True(typeof(IPhase4DocumentaryBlueprintPublicationService)
            .IsAssignableFrom(typeof(Phase4DocumentaryBlueprintPublicationService)));
        Assert.DoesNotContain(typeof(ProductionPipelineExecutionService).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            method => method.Name.Contains("DocumentaryBlueprintBuilder", StringComparison.Ordinal));
    }

    [Fact]
    public void production_pipeline_phase5_uses_typed_aggregate_adapter()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));
        Assert.Contains("IDocumentaryBlueprintPhase5CompatibilityAdapter", source);
        Assert.Contains("PublishedDocumentaryBlueprintAggregate", source);
    }

    [Fact]
    public void production_pipeline_does_not_cross_deserialize_aggregate_as_legacy_artifact()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));
        Assert.DoesNotContain("Deserialize<DocumentaryBlueprintArtifact>", source);
        Assert.DoesNotContain("ExistingBlueprintArtifactsAreValid", source);
        Assert.DoesNotContain("Phase4ManifestIsValid", source);
    }

    [Fact]
    public void phase4_resume_and_rc2_status_use_committed_state_evaluator()
    {
        var pipeline = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));
        var status = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Orchestration", "RC2", "Rc2CertifiedExecutionStatusReader.cs"));
        Assert.Contains("phase4CommittedAuthorityEvaluator.EvaluateAsync", pipeline);
        Assert.Contains("IPhase4CommittedAuthorityEvaluator", status);
        Assert.DoesNotContain("story-graph.json\"));\n        var committed", status);
    }

    [Fact]
    public void phase5_adapter_does_not_read_files_derive_short_or_publish_master()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("DocumentaryBlueprint", "DocumentaryBlueprintPhase5CompatibilityAdapter.cs"));
        Assert.DoesNotContain("File.", source);
        Assert.DoesNotContain("ShortVariant =", source);
        Assert.DoesNotContain("PublishAsync", source);
        Assert.Contains("aggregate.ShortVariant", source);
    }

    [Fact]
    public void rc2_phase_range_is_never_expanded_past_the_explicit_end_phase()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Orchestration", "RC2", "Rc2ContentPlanningBatchOrchestrator.cs"));
        Assert.DoesNotContain("ExpandProductionRangeForRc2PhaseContract", source);
        Assert.Contains("GenerateFromPlansAsync(request, cancellationToken)", source);
    }

    [Fact]
    public void phase5_adapter_preserves_scene_knowledge_lineage()
    {
        var source = AdapterSource();
        Assert.Contains("trace.KnowledgeSelections.Select", source);
        Assert.Contains("selection.KnowledgeReferenceId", source);
        Assert.Contains("selection.SourceArtifact", source);
        Assert.DoesNotContain("(IReadOnlyList<ViewerKnowledgeReference>)[]", source);
    }

    [Fact]
    public void phase5_adapter_preserves_scene_traceability()
    {
        var source = AdapterSource();
        Assert.Contains("variant.SceneTraceability.ToDictionary", source);
        Assert.Contains("traceability.TryGetValue(x.SceneId", source);
    }

    [Fact]
    public void phase5_adapter_preserves_learning_objectives()
    {
        var source = AdapterSource();
        Assert.Contains("variant.SceneTraceability.Select(x => x.LearningObjectiveId)", source);
        Assert.Contains("CoveredObjectives(variant)", source);
    }

    private static string AdapterSource() => File.ReadAllText(
        RepositoryTestPaths.InfrastructureSource("DocumentaryBlueprint", "DocumentaryBlueprintPhase5CompatibilityAdapter.cs"));
}
