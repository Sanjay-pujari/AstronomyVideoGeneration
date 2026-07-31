using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.All(result.ViewerQuestionBank.Questions, q => Assert.All(q.KnowledgeReferences, r => Assert.Equal("Resolved", r.ResolutionStatus)));
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
    public void ViewerCuriosityArtifactProjector_keeps_question_id_when_source_order_changes()
    {
        var first = Project().ViewerQuestionBank.Questions.Single(x => x.QuestionText.StartsWith("What", StringComparison.Ordinal)).QuestionId;
        var reordered = Source() with { Answers = Source().Answers.Select(x => x with { DisplayOrder = 10 - x.DisplayOrder }).ToArray() };
        var second = projector.Project(reordered, Intelligence(), ExecutionId, "Gold", ["Long", "Short"], CreatedUtc).ViewerQuestionBank.Questions.Single(x => x.QuestionText.StartsWith("What", StringComparison.Ordinal)).QuestionId;
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("What", "Recognition")]
    [InlineData("Why", "ScientificExplanation")]
    [InlineData("Where", "LocationGuidance")]
    [InlineData("When", "TimingGuidance")]
    [InlineData("How", "ObservationGuidance")]
    [InlineData("Action", "PracticalViewingAdvice")]
    public void ViewerCuriosityArtifactProjector_maps_known_question_types(string type, string category)
    {
        var source = Source() with { Answers = [Source().Answers[0] with { QuestionType = type }] };
        Assert.Equal(category, projector.Project(source, Intelligence(), ExecutionId, "Gold", ["Long"], CreatedUtc).ViewerQuestionBank.Questions.Single().Category);
    }

    [Fact]
    public void ViewerCuriosityChecksum_is_stable_after_round_trip()
    {
        var questions = Project().ViewerQuestionBank.Questions;
        var json = System.Text.Json.JsonSerializer.Serialize(questions, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<ViewerQuestion[]>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(ViewerCuriosityChecksum.For(questions), ViewerCuriosityChecksum.For(roundTrip));
    }

    [Fact]
    public void ViewerCuriosityChecksum_is_stable_when_dictionary_insertion_order_changes()
    {
        var plan = Project().QuestionPlan;
        var reversed = plan with { CategoryCoverage = plan.CategoryCoverage.Reverse().ToDictionary(x => x.Key, x => x.Value) };
        Assert.Equal(ViewerCuriosityChecksum.ForPlan(plan), ViewerCuriosityChecksum.ForPlan(reversed));
    }

    [Fact]
    public void ViewerCuriosityChecksum_changes_when_semantic_payload_changes() =>
        Assert.NotEqual(ViewerCuriosityChecksum.For(Project().ViewerQuestionBank.Questions), ViewerCuriosityChecksum.For(Project().ViewerQuestionBank.Questions.Select(x => x with { QuestionText = x.QuestionText + " changed" }).ToArray()));

    [Fact]
    public void ViewerCuriosityChecksum_ignores_created_utc()
    {
        var first = Project();
        var second = projector.Project(Source(), Intelligence(), ExecutionId, "Gold", ["Long", "Short"], CreatedUtc.AddDays(1));
        Assert.Equal(first.ViewerQuestionBank.Metadata.Checksum, second.ViewerQuestionBank.Metadata.Checksum);
    }

    [Fact]
    public void AddMediaFactory_registers_mandatory_viewer_curiosity_projector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=postgres.example;Database=astronomy_tests;Username=test;Password=test"
            })
            .Build();

        services.AddMediaFactory(configuration);
        Assert.Contains(services, x => x.ServiceType == typeof(IViewerCuriosityArtifactProjector) && x.ImplementationType == typeof(ViewerCuriosityArtifactProjector) && x.Lifetime == ServiceLifetime.Singleton);
        Assert.False(typeof(ProductionPipelineExecutionService).GetConstructors().Single().GetParameters().Single(x => x.ParameterType == typeof(IViewerCuriosityArtifactProjector)).HasDefaultValue);
    }

    [Fact]
    public void ViewerCuriosityArtifactProjector_maps_knowledge_references() =>
        Assert.All(Project().ViewerQuestionBank.Questions, x => Assert.All(x.KnowledgeReferences, r => Assert.Equal("02-intelligence/production-event-intelligence.json", r.SourceArtifact)));

    [Theory]
    [InlineData("When", "TimingGuidance", "Best viewing is 5:30 PM India Standard Time")]
    [InlineData("Where", "LocationGuidance", "Look toward the clearest open horizon")]
    [InlineData("How", "ObservationGuidance", "Use binoculars")]
    [InlineData("Action", "PracticalViewingAdvice", "Bring special equipment")]
    public void Provider_specific_answer_is_not_certified_without_Phase2_support(string type, string category, string providerAnswer)
    {
        var source = Source() with { Answers = [new(null, type, "What should the viewer do?", "Viewer guidance", providerAnswer, 1)] };
        var intelligence = Intelligence() with { LocalPeakTime = null, BestViewingWindowLocal = null, SkyDirectionHint = null, ViewerInstructions = [] };

        var question = Assert.Single(projector.Project(source, intelligence, ExecutionId, "Gold", ["Long"], CreatedUtc).ViewerQuestionBank.Questions);

        Assert.Equal(category, question.Category);
        Assert.Equal(providerAnswer, question.SourceAnswer);
        Assert.DoesNotContain(providerAnswer, question.CertifiedAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Unresolved", question.AnswerResolutionStatus);
        Assert.Equal("EditorialOnly", question.AnswerUsability);
        Assert.True(question.RequiresEditorialAttention);
        Assert.NotEmpty(question.GroundingWarnings);
    }

    [Theory]
    [InlineData("ShortVideo", "Short")]
    [InlineData("short", "Short")]
    [InlineData("LongVideo", "Long")]
    [InlineData("long", "Long")]
    public void ViewerCuriosityArtifactProjector_uses_only_explicit_requested_variant(string requested, string expected)
    {
        var result = projector.Project(Source(), Intelligence(), ExecutionId, "short-and-long-profile-name", [requested], CreatedUtc);
        Assert.All(result.ViewerQuestionBank.Questions, question => Assert.Equal([expected], question.ApplicableVariants));
    }

    [Theory]
    [InlineData("ShortVideo")]
    [InlineData("LongVideo")]
    public void phase3_when_execution_includes_phase4_projects_long_and_short_variants(string requestedOutput)
    {
        var scope = ViewerCuriosityVariantScope.Resolve(4, [requestedOutput, "Thumbnail"]);
        var result = projector.Project(Source(), Intelligence(), ExecutionId, requestedOutput, scope.ExpectedVariants, CreatedUtc);

        Assert.Equal(["Long", "Short"], scope.ExpectedVariants);
        Assert.All(result.ViewerQuestionBank.Questions, question => Assert.Equal(["Long", "Short"], question.ApplicableVariants));
        Assert.Equal(result.ViewerQuestionBank.Questions.Count, result.QuestionPlan.VariantCoverage["Long"]);
        Assert.Equal(result.ViewerQuestionBank.Questions.Count, result.QuestionPlan.VariantCoverage["Short"]);
    }

    [Fact]
    public void phase3_when_execution_stops_before_phase4_retains_requested_output_scope()
    {
        Assert.Equal(["Short"], ViewerCuriosityVariantScope.Resolve(3, ["ShortVideo", "Thumbnail"]).ExpectedVariants);
        Assert.Equal(["Long"], ViewerCuriosityVariantScope.Resolve(3, ["LongVideo", "Thumbnail"]).ExpectedVariants);
    }

    [Fact]
    public void ViewerQuestionBankValidator_rejects_nonexistent_phase2_field()
    {
        var value = Project();
        var question = value.ViewerQuestionBank.Questions[0] with { KnowledgeReferences = [new("production-event-intelligence#/doesNotExist", "ProductionIntelligenceField", "02-intelligence/production-event-intelligence.json", "Resolved")] };
        value = Rechecksum(value with { ViewerQuestionBank = value.ViewerQuestionBank with { Questions = [question, value.ViewerQuestionBank.Questions[1]] } });
        Assert.Contains(ViewerCuriosityArtifactValidator.Validate(value, ExecutionId, "en", "Gold", intelligence: Intelligence()), error => error.Contains("invalid or unresolved"));
    }

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
        var result = projector.Project(source, Intelligence(), ExecutionId, "Gold", ["Long", "Short"], CreatedUtc);
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

    private ViewerCuriosityProjection Project() => projector.Project(Source(), Intelligence(), ExecutionId, "Gold", ["Long", "Short"], CreatedUtc);
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
