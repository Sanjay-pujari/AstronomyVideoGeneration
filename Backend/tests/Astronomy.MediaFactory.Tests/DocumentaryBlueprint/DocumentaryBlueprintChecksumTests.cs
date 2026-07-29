using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintChecksumTests
{
    [Fact] public void Checksum_is_stable_for_identical_artifact() => Assert.Equal(Checksum(Artifact()), Checksum(Artifact()));
    [Fact] public void Checksum_ignores_CreatedUtc()
    {
        var value = Artifact();
        Assert.Equal(Checksum(value), Checksum(value with { Metadata = value.Metadata with { CreatedUtc = value.Metadata.CreatedUtc.AddDays(1) } }));
    }
    [Fact] public void Checksum_changes_when_variant_changes()
    {
        var value = Artifact();
        Assert.NotEqual(Checksum(value), Checksum(value with { Metadata = value.Metadata with { Variant = "Short" } }));
    }
    [Fact] public void Checksum_changes_when_scene_title_changes()
    {
        var value = Artifact();
        var scene = value.Blueprint.Scenes[0];
        var changed = Copy(scene, title: "A semantic title change");
        Assert.NotEqual(Checksum(value), Checksum(value with { Blueprint = Blueprint(changed) }));
    }
    [Fact] public void Checksum_changes_when_scene_objective_changes()
    {
        var value = Artifact(); var scene = value.Blueprint.Scenes[0];
        var changed = Copy(scene, objective: new("Changed summary", "Changed goal", "Changed curiosity", "Changed emotion"));
        Assert.NotEqual(Checksum(value), Checksum(value with { Blueprint = Blueprint(changed) }));
    }
    [Fact] public void Checksum_changes_when_viewer_question_coverage_changes()
    {
        var value = Artifact(); var changed = value.Coverage with { CoveredViewerQuestionIds = ["question.changed"] };
        Assert.NotEqual(Checksum(value), Checksum(value with { Coverage = changed }));
    }
    [Fact] public void Checksum_changes_when_learning_objective_coverage_changes()
    {
        var value = Artifact(); var changed = value.Coverage with { CoveredLearningObjectiveIds = ["objective.changed"] };
        Assert.NotEqual(Checksum(value), Checksum(value with { Coverage = changed }));
    }
    [Fact] public void Checksum_is_stable_when_dictionary_insertion_order_changes()
    {
        var value = Artifact(twoScenes: true);
        var reversed = value.Coverage with
        {
            SectionQuestionMap = value.Coverage.SectionQuestionMap.Reverse().ToDictionary(x => x.Key, x => x.Value),
            SectionKnowledgeMap = value.Coverage.SectionKnowledgeMap.Reverse().ToDictionary(x => x.Key, x => x.Value)
        };
        Assert.Equal(Checksum(value), Checksum(value with { Coverage = reversed }));
    }

    private static string Checksum(DocumentaryBlueprintArtifact artifact) => DocumentaryBlueprintChecksum.Calculate(artifact);
    private static DocumentaryBlueprintArtifact Artifact(bool twoScenes = false)
    {
        var scenes = twoScenes ? new[] { OrionDocumentaryBlueprintFixture.Scene(), OrionDocumentaryBlueprintFixture.DistanceScene() } : [OrionDocumentaryBlueprintFixture.Scene()];
        var blueprint = OrionDocumentaryBlueprintFixture.Create(scenes);
        var questions = scenes.ToDictionary(x => x.SceneId, x => (IReadOnlyList<string>)["question." + x.SceneNumber]);
        var knowledge = scenes.ToDictionary(x => x.SceneId, x => (IReadOnlyList<global::Astronomy.MediaFactory.Core.ViewerKnowledgeReference>)x.KnowledgeReferences.Select(k => new global::Astronomy.MediaFactory.Core.ViewerKnowledgeReference(k.KnowledgeEntryId, k.Section, "test", "Resolved")).ToArray());
        var coverage = new BlueprintCoverage(questions.Values.SelectMany(x => x).ToArray(), [], [], ["objective.1"], [], questions, knowledge, new Dictionary<string, string>());
        return new(new("execution", "orion", "en-US", "LongVideo", "Master", "1", "ignored", DateTimeOffset.UnixEpoch, "phase3", "intel"), blueprint, coverage, []);
    }
    private static global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint Blueprint(DocumentarySceneBlueprint scene) => OrionDocumentaryBlueprintFixture.Create([scene]);
    private static DocumentarySceneBlueprint Copy(DocumentarySceneBlueprint s, string? title = null, SceneObjective? objective = null) =>
        new(s.SceneId, s.SceneNumber, title ?? s.Title, s.NarrativeStage, s.SceneRole, s.ViewerQuestion, objective ?? s.SceneObjective, s.EditorialOutcome, s.EditorialPriority, s.KnowledgeReferences, s.VisualOpportunities, s.Transition, s.EstimatedDurationSeconds);
}
