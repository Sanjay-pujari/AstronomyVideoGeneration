using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintArtifactValidatorTests
{
    [Fact] public void Validate_accepts_valid_master_blueprint() => Assert.True(Validate(Artifact()).IsValid);
    [Fact] public void Validate_rejects_wrong_execution_id() => AssertField(Validate(Artifact(), execution: "wrong"), "Metadata.ExecutionId");
    [Fact] public void Validate_rejects_wrong_event_id() => AssertField(Validate(Artifact(), eventId: "wrong"), "Metadata.EventId");
    [Fact] public void Validate_rejects_wrong_language() => AssertField(Validate(Artifact(), language: "fr-FR"), "Metadata.Language");
    [Fact] public void Validate_rejects_wrong_profile() => AssertField(Validate(Artifact(), profile: "ShortVideo"), "Metadata.Profile");
    [Fact] public void Validate_rejects_wrong_variant() => AssertField(Validate(Artifact(), variant: "Long"), "Metadata.Variant");
    [Fact] public void Validate_rejects_checksum_mismatch()
    {
        var artifact = Artifact() with { Metadata = Artifact().Metadata with { Checksum = "corrupt" } };
        AssertField(Validate(artifact), "Metadata.Checksum");
    }
    [Fact] public void Validate_rejects_unknown_viewer_question_reference()
    {
        var artifact = Artifact();
        AssertField(DocumentaryBlueprintArtifactValidator.Validate(artifact, Context(questions: new HashSet<string>())), "Coverage.ViewerQuestionId");
    }
    [Fact] public void Validate_rejects_uncovered_high_priority_question()
    {
        var artifact = Artifact();
        AssertField(DocumentaryBlueprintArtifactValidator.Validate(artifact, Context(high: new HashSet<string> { "mandatory" })), "Coverage.HighPriorityViewerQuestionId");
    }

    private static void AssertField(DocumentaryBlueprintArtifactValidationResult result, string field) => Assert.Contains(result.Errors, x => x.Field == field && x.Artifact.Length > 0 && x.Expected is not null);
    private static DocumentaryBlueprintArtifactValidationResult Validate(DocumentaryBlueprintArtifact artifact, string execution = "execution", string eventId = "orion", string language = "en-US", string profile = "LongVideo", string variant = "Master") =>
        DocumentaryBlueprintArtifactValidator.Validate(artifact, Context(execution, eventId, language, profile, variant));
    private static DocumentaryBlueprintValidationContext Context(string execution = "execution", string eventId = "orion", string language = "en-US", string profile = "LongVideo", string variant = "Master", IReadOnlySet<string>? questions = null, IReadOnlySet<string>? high = null) =>
        new(execution, eventId, language, profile, variant, "phase3", questions ?? new HashSet<string> { "question.1" }, high ?? new HashSet<string> { "question.1" }, new HashSet<string> { "objective.1" }, new HashSet<string> { "orion.recognition.belt", "orion.fact.belt-distance" }, new HashSet<string> { "Long", "Short" });
    private static DocumentaryBlueprintArtifact Artifact()
    {
        var blueprint = OrionDocumentaryBlueprintFixture.Create();
        var coverage = new BlueprintCoverage(["question.1"], [], [], ["objective.1"], [],
            new Dictionary<string, IReadOnlyList<string>> { [blueprint.Scenes[0].SceneId] = ["question.1"] },
            new Dictionary<string, IReadOnlyList<global::Astronomy.MediaFactory.Core.ViewerKnowledgeReference>> { [blueprint.Scenes[0].SceneId] = blueprint.Scenes[0].KnowledgeReferences.Select(k => new global::Astronomy.MediaFactory.Core.ViewerKnowledgeReference(k.KnowledgeEntryId, k.Section, "test", "Resolved")).ToArray() }, new Dictionary<string, string>());
        var metadata = new BlueprintArtifactMetadata("execution", "orion", "en-US", "LongVideo", "Master", "1", "", DateTimeOffset.UnixEpoch, "phase3", "intel");
        var artifact = new DocumentaryBlueprintArtifact(metadata, blueprint, coverage, []);
        return artifact with { Metadata = metadata with { Checksum = DocumentaryBlueprintChecksum.Calculate(artifact) } };
    }
}
