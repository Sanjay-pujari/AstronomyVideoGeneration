using System.Text.Json;
using Astronomy.MediaFactory.Core.Certification;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintProjectionCertificationCorrectionTests
{
    [Fact]
    public void Canonical_aggregate_json_embeds_each_variant_and_blueprint_once()
    {
        var aggregate = Aggregate();

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(aggregate, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var names = json.RootElement.EnumerateObject().Select(x => x.Name).ToArray();

        names.Should().ContainSingle(x => x == "longVariant");
        names.Should().ContainSingle(x => x == "shortVariant");
        names.Should().NotContain("longBlueprint");
        names.Should().NotContain("shortBlueprint");
        names.Should().NotContain("longProjectionChecksum");
        names.Should().NotContain("shortProjectionChecksum");
        json.RootElement.GetProperty("longVariant").EnumerateObject().Should().ContainSingle(x => x.Name == "blueprint");
        json.RootElement.GetProperty("shortVariant").EnumerateObject().Should().ContainSingle(x => x.Name == "blueprint");
        DocumentaryBlueprintProjectionChecksum.CalculateAggregate(aggregate)
            .Should().Be(DocumentaryBlueprintProjectionChecksum.CalculateAggregate(aggregate with
            {
                DeterministicChecksum = "a checksum value that is deliberately ignored"
            }));
    }

    [Fact]
    public void Explicit_scene_order_normalizes_different_source_insertion_order_for_projection_and_validation()
    {
        var first = Opportunity("op-1", 1, "Question one");
        var second = Opportunity("op-2", 2, "Question two");
        var source = Variant([second, first]);
        var ordered = source.SceneOpportunities.OrderBy(x => x.Order).ThenBy(x => x.OpportunityId, StringComparer.Ordinal).ToArray();
        var inputs = ordered.Select(Input).ToArray();
        var blueprint = Blueprint("ordered", inputs.Select(Scene).ToArray());
        var trace = ordered.Zip(blueprint.Scenes).Select(x => Trace(x.First, x.Second)).ToArray();

        var result = new DocumentaryBlueprintVariantProjectionValidator().Validate(source, inputs, blueprint, trace);

        result.Success.Should().BeTrue();
        blueprint.Scenes.Select(x => x.Title).Should().Equal("Question one", "Question two");
    }

    [Fact]
    public void Canonical_adapter_projects_the_owning_certification_registry_without_parallel_catalog()
    {
        var owner = new ConstellationCertificationProfile();
        var registry = new RecordingRegistry(owner);
        var adapter = new CanonicalDocumentaryBlueprintProfileAdapter(registry);

        var projected = adapter.ProjectOrionGold();

        registry.ResolvedKeys.Should().Equal("CONSTELLATION");
        projected.FamilyCode.Should().Be(owner.FamilyId);
        typeof(CanonicalDocumentaryBlueprintProfileAdapter).GetFields(System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Should().ContainSingle().Which.FieldType
            .Should().Be(typeof(IFamilyCertificationProfileRegistry));
    }

    [Fact]
    public void Production_di_resolves_orion_gold_from_canonical_source_with_twelve_and_four_scenes()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var provider = new ServiceCollection().AddMediaFactory(configuration).BuildServiceProvider();

        var owner = provider.GetRequiredService<IFamilyCertificationProfileRegistry>().Resolve("CONSTELLATION");
        var profile = provider.GetRequiredService<DocumentaryBlueprintProfile>();

        owner.FamilyId.Should().Be("CONSTELLATION");
        profile.ProfileId.Should().Be(CanonicalDocumentaryBlueprintProfileAdapter.OrionGoldProfileId);
        profile.FamilyCode.Should().Be(owner.FamilyId);
        profile.LongProfile.ExpectedSceneCount.Should().Be(12);
        profile.ShortProfile.ExpectedSceneCount.Should().Be(4);
    }

    private static DocumentaryBlueprintAggregate Aggregate()
    {
        var longArtifact = Artifact("Long", Blueprint("long", [Scene(Input(Opportunity("long-op", 1, "Long question")))]));
        var shortArtifact = Artifact("Short", Blueprint("short", [Scene(Input(Opportunity("short-op", 1, "Short question")))]));
        return new("1", "1", "1", "aggregate", "execution", "plan", "event", "en", "orion-gold", "1",
            "intent", "intent-checksum", Lineage(), longArtifact, shortArtifact,
            new([], [], [], []), new(1, 1, 2), [], "aggregate-checksum");
    }

    private static DocumentaryBlueprintVariantArtifact Artifact(string variant, global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint blueprint) =>
        new("1", "1", "1", variant.ToLowerInvariant(), "execution", "plan", "event", "en", "orion-gold", "1", variant,
            "intent", $"{variant}-intent", "intent-checksum", "variant-checksum", Lineage(), blueprint, [],
            new([], [], [], [], [], []), new([], [], [], [], [], []), [], [], 1, 1, 1, 1, $"{variant}-checksum");

    private static DocumentaryVariantIntent Variant(IReadOnlyList<DocumentarySceneOpportunity> scenes) =>
        new("Long", "long-intent", "orion-gold", "1", 2, 2, scenes,
            new([], [], [], [], [], []), new([], [], [], [], [], []), [], [], 2, "checksum");

    private static DocumentarySceneOpportunity Opportunity(string id, int order, string question) =>
        new(id, "Long", order, $"slot-{order}", "Wonder", "OpeningHook", "purpose", $"q-{order}", question, [],
            QuestionEvidenceStatus.ResolvedGrounded, $"objective-{order}", $"Objective {order}", "outcome", "Outcome", [],
            [new($"q-{order}", "Long", "Primary", id, $"q-{order}", "reason", "permission")], [], [],
            order == 2 ? "Close" : "Continue", 1, 1, 1, "Visual", "checksum");

    private static DocumentarySceneBlueprintInput Input(DocumentarySceneOpportunity value) =>
        new($"scene-{value.Order}", value.Order, value.PrimaryViewerQuestionText, DocumentaryNarrativeStage.Wonder,
            DocumentarySceneRole.OpeningHook, new(value.PrimaryViewerQuestionText),
            new(value.LearningObjectiveText, value.LearningObjectiveText, value.ObjectiveCuriosityGoal, value.ObjectiveEmotionalGoal),
            new(value.EditorialOutcome, value.EditorialOutcomeCode, false, false, false, false, false), value.EditorialPriority,
            [], [new(value.VisualOpportunityIntent, value.VisualOpportunityType, null, null, value.VisualIsScientificallyRequired)],
            new(value.TransitionIntent, value.TransitionNextQuestionSeed, value.TransitionEditorialDirection), 1);

    private static DocumentarySceneBlueprint Scene(DocumentarySceneBlueprintInput value) =>
        new(value.SceneId, value.SceneNumber, value.Title, value.NarrativeStage, value.SceneRole, value.ViewerQuestion,
            value.SceneObjective, value.EditorialOutcome, value.EditorialPriority, value.KnowledgeReferences,
            value.VisualOpportunities, value.Transition, value.EstimatedDurationSeconds);

    private static global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint Blueprint(
        string id, IReadOnlyList<DocumentarySceneBlueprint> scenes) => new(id, "knowledge", "event", "Goal",
            BlueprintPublicationFormat.LongDocumentary, "en", "1",
            new(DateTimeOffset.UnixEpoch, "test", "test", "source", "1", "correlation"), scenes);

    private static DocumentarySceneBlueprintTraceability Trace(DocumentarySceneOpportunity value, DocumentarySceneBlueprint scene) =>
        new(scene.SceneId, value.OpportunityId, value.DeterministicChecksum, value.PrimaryViewerQuestionId,
            value.SupportingViewerQuestionIds, value.LearningObjectiveId, value.QuestionEvidenceStatus, value.ProfileSlotId,
            value.MinimumDurationSeconds, value.MaximumDurationSeconds, value.EditorialConstraints, value.MustNotClaim,
            value.SelectedKnowledgeReferences);

    private static DocumentarySourceLineage Lineage() =>
        new("execution", "plan", "phase2", "phase2-checksum", null, "knowledge", "knowledge-checksum",
            "questions", "questions-checksum", "objectives", "objectives-checksum", "question-plan", "plan-checksum", "en", "orion-gold", "1");

    private sealed class RecordingRegistry(IFamilyCertificationProfile owner) : IFamilyCertificationProfileRegistry
    {
        public List<string> ResolvedKeys { get; } = [];
        public IFamilyCertificationProfile Resolve(string eventType) { ResolvedKeys.Add(eventType); return owner; }
        public bool TryResolve(string eventType, out IFamilyCertificationProfile? profile) { profile = owner; return true; }
    }
}
