using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class ViewerCuriosityArtifactProjectorTests
{
    private readonly ViewerCuriosityArtifactProjector projector = new();

    [Fact]
    public void ViewerCuriosityArtifactProjector_projects_question_answer_set()
    {
        var result = Project();
        Assert.Equal(2, result.ViewerQuestionBank.Questions.Count);
        Assert.Equal("en", result.ViewerQuestionBank.Metadata.Language);
        Assert.All(result.ViewerQuestionBank.Questions, q => Assert.Equal([EventId.ToString("D")], q.KnowledgeReferences));
    }

    [Fact]
    public void ViewerCuriosityArtifactProjector_preserves_source_order() =>
        Assert.Equal([1, 2], Project().ViewerQuestionBank.Questions.Select(x => x.Order));

    [Fact]
    public void ViewerCuriosityArtifactProjector_generates_stable_ids()
    {
        var first = Project().ViewerQuestionBank.Questions.Select(x => x.QuestionId);
        var second = Project().ViewerQuestionBank.Questions.Select(x => x.QuestionId);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ViewerCuriosityArtifactProjector_maps_knowledge_references() =>
        Assert.All(Project().ViewerQuestionBank.Questions, x => Assert.Contains(EventId.ToString("D"), x.KnowledgeReferences));

    [Fact]
    public void ViewerCuriosityArtifactProjector_derives_learning_objectives()
    {
        var result = Project();
        Assert.Equal(["Recognize the event", "Locate the event"], result.LearningObjectives.Objectives.Select(x => x.Text));
        Assert.All(result.LearningObjectives.Objectives, x => Assert.Single(x.ViewerQuestionIds));
    }

    [Fact]
    public void ViewerCuriosityArtifactProjector_deduplicates_normalized_questions()
    {
        var source = Source() with { Answers = [Source().Answers[0], Source().Answers[0] with { QuestionText = "  WHAT   is visible ", DisplayOrder = 3 }] };
        var result = projector.Project(source, Intelligence(), ExecutionId, "Gold", CreatedUtc);
        Assert.Single(result.ViewerQuestionBank.Questions);
        Assert.Single(result.QuestionPlan.ProjectionWarnings);
    }

    [Fact]
    public void ViewerCuriosityArtifactProjector_does_not_call_external_provider()
    {
        // The projector accepts materialized DTOs and has no provider dependency.
        Assert.Empty(typeof(ViewerCuriosityArtifactProjector).GetConstructors().Single().GetParameters());
        Assert.NotEmpty(Project().ViewerQuestionBank.Questions);
    }

    [Fact]
    public void ViewerQuestionBankValidator_rejects_duplicate_ids()
    {
        var value = Project();
        var duplicate = value.ViewerQuestionBank.Questions[1] with { QuestionId = value.ViewerQuestionBank.Questions[0].QuestionId };
        value = Rechecksum(value with { ViewerQuestionBank = value.ViewerQuestionBank with { Questions = [value.ViewerQuestionBank.Questions[0], duplicate] } });
        Assert.Contains(ViewerCuriosityArtifactValidator.Validate(value, ExecutionId, "en", "Gold"), x => x.Contains("duplicate question IDs"));
    }

    [Fact]
    public void ViewerQuestionBankValidator_rejects_empty_question_text()
    {
        var value = Project();
        value = Rechecksum(value with { ViewerQuestionBank = value.ViewerQuestionBank with { Questions = [value.ViewerQuestionBank.Questions[0] with { QuestionText = "" }] } });
        Assert.Contains(ViewerCuriosityArtifactValidator.Validate(value, ExecutionId, "en", "Gold"), x => x.Contains("empty question text"));
    }

    [Fact]
    public void ViewerQuestionBankValidator_rejects_invalid_metadata()
    {
        var value = Project();
        value = value with { ViewerQuestionBank = value.ViewerQuestionBank with { Metadata = value.ViewerQuestionBank.Metadata with { Language = "hi" } } };
        Assert.Contains(ViewerCuriosityArtifactValidator.Validate(value, ExecutionId, "en", "Gold"), x => x.Contains("metadata"));
    }

    [Fact]
    public void ViewerQuestionBankValidator_rejects_checksum_mismatch()
    {
        var value = Project();
        value = value with { ViewerQuestionBank = value.ViewerQuestionBank with { Metadata = value.ViewerQuestionBank.Metadata with { Checksum = new string('0', 64) } } };
        Assert.Contains(ViewerCuriosityArtifactValidator.Validate(value, ExecutionId, "en", "Gold"), x => x.Contains("checksum mismatch"));
    }

    [Fact]
    public void LearningObjectivesValidator_rejects_unknown_question_reference()
    {
        var value = Project();
        value = value with { LearningObjectives = value.LearningObjectives with { Objectives = [value.LearningObjectives.Objectives[0] with { ViewerQuestionIds = ["unknown"] }] } };
        value = value with { LearningObjectives = value.LearningObjectives with { Metadata = value.LearningObjectives.Metadata with { Checksum = ViewerCuriosityChecksum.For(value.LearningObjectives.Objectives) } } };
        Assert.Contains(ViewerCuriosityArtifactValidator.Validate(value, ExecutionId, "en", "Gold"), x => x.Contains("unknown Viewer Question"));
    }

    [Fact]
    public void QuestionPlan_counts_match_projected_questions()
    {
        var value = Project();
        Assert.Empty(ViewerCuriosityArtifactValidator.Validate(value, ExecutionId, "en", "Gold"));
        Assert.Equal(value.ViewerQuestionBank.Questions.Count, value.QuestionPlan.CategoryCoverage.Values.Sum());
    }

    private ViewerCuriosityProjection Project() => projector.Project(Source(), Intelligence(), ExecutionId, "Gold", CreatedUtc);
    private static ViewerCuriosityProjection Rechecksum(ViewerCuriosityProjection value) => value with
    {
        ViewerQuestionBank = value.ViewerQuestionBank with { Metadata = value.ViewerQuestionBank.Metadata with { Checksum = ViewerCuriosityChecksum.For(value.ViewerQuestionBank.Questions) } }
    };
    private static readonly Guid EventId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string ExecutionId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private static readonly DateTimeOffset CreatedUtc = DateTimeOffset.Parse("2026-07-29T00:00:00Z");
    private static QuestionAnswerSetDto Source() => new(null, EventId, "EVENT", "Event", "Conjunction", "US", "en", "v1", "Generated", CreatedUtc,
        [new(null, "What", "What is visible?", "Recognize the event", "Two objects are visible.", 1), new(null, "Where", "Where should viewers look?", "Locate the event", "Look west.", 2)]);
    private static ProductionEventIntelligence Intelligence() => new("Astronomy", "Conjunction", "Event", "Event", null, null, null, null, "west", "US", ["Object A"], ["Object B"], null, null, null, "Scientific context", ["Look west"], [], [], [], []);
}
