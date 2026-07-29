using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public sealed record ViewerCuriosityArtifactMetadata(
    string ExecutionId,
    string Language,
    string Profile,
    string Version,
    string Checksum,
    DateTimeOffset CreatedUtc);

public sealed record ViewerQuestion(
    string QuestionId,
    string QuestionText,
    string Priority,
    string Category,
    IReadOnlyList<string> KnowledgeReferences,
    string ExpectedLearningOutcome,
    IReadOnlyList<string> ApplicableVariants,
    string Language,
    int Order);

public sealed record ViewerQuestionBank(ViewerCuriosityArtifactMetadata Metadata, IReadOnlyList<ViewerQuestion> Questions);
public sealed record LearningObjective(string ObjectiveId, string Text, IReadOnlyList<string> ViewerQuestionIds);
public sealed record ViewerLearningObjectives(ViewerCuriosityArtifactMetadata Metadata, IReadOnlyList<LearningObjective> Objectives);
public sealed record ViewerQuestionPlan(
    ViewerCuriosityArtifactMetadata Metadata,
    int TotalGeneratedQuestions,
    int AcceptedQuestions,
    IReadOnlyDictionary<string, int> CategoryCoverage,
    IReadOnlyDictionary<string, int> PriorityDistribution,
    IReadOnlyList<string> QuestionsRequiringEditorialAttention,
    IReadOnlyList<string> ProjectionWarnings);

public sealed record ViewerCuriosityProjection(
    ViewerQuestionBank ViewerQuestionBank,
    ViewerLearningObjectives LearningObjectives,
    ViewerQuestionPlan QuestionPlan);

public interface IViewerCuriosityArtifactProjector
{
    ViewerCuriosityProjection Project(
        QuestionAnswerSetDto source,
        ProductionEventIntelligence intelligence,
        string executionId,
        string profile,
        DateTimeOffset createdUtc);
}

public sealed class ViewerCuriosityArtifactProjector : IViewerCuriosityArtifactProjector
{
    private const string Version = "1.0";

    public ViewerCuriosityProjection Project(QuestionAnswerSetDto source, ProductionEventIntelligence intelligence, string executionId, string profile, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(intelligence);

        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var questions = new List<ViewerQuestion>();
        foreach (var answer in source.Answers.OrderBy(x => x.DisplayOrder))
        {
            var normalized = Normalize(answer.QuestionText);
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                warnings.Add($"Question at source order {answer.DisplayOrder} was omitted because it was empty or duplicated after normalization.");
                continue;
            }

            var category = MapCategory(answer.QuestionType);
            var knowledge = new[] { source.AstronomyEventIntelligenceId.ToString("D") };
            var id = "vq-" + Hash($"{profile}|{source.Language}|{normalized}|{knowledge[0]}|{category}|{answer.DisplayOrder}")[..16];
            var outcome = string.IsNullOrWhiteSpace(answer.Title) ? answer.AnswerText.Trim() : answer.Title.Trim();
            questions.Add(new ViewerQuestion(id, answer.QuestionText.Trim(), Priority(answer.DisplayOrder), category, knowledge,
                outcome, ResolveVariants(profile), source.Language, answer.DisplayOrder));
        }

        var bank = new ViewerQuestionBank(Metadata(executionId, source.Language, profile, createdUtc), questions);
        bank = bank with { Metadata = bank.Metadata with { Checksum = ViewerCuriosityChecksum.For(bank.Questions) } };
        var objectives = questions.Select((q, index) => new LearningObjective(
            "lo-" + Hash($"{q.QuestionId}|{q.ExpectedLearningOutcome}")[..16],
            q.ExpectedLearningOutcome,
            [q.QuestionId])).ToArray();
        var learning = new ViewerLearningObjectives(Metadata(executionId, source.Language, profile, createdUtc), objectives);
        learning = learning with { Metadata = learning.Metadata with { Checksum = ViewerCuriosityChecksum.For(learning.Objectives) } };
        var plan = new ViewerQuestionPlan(Metadata(executionId, source.Language, profile, createdUtc), source.Answers.Count, questions.Count,
            Count(questions, q => q.Category), Count(questions, q => q.Priority),
            questions.Where(q => q.KnowledgeReferences.Count == 0).Select(q => q.QuestionId).ToArray(), warnings);
        plan = plan with { Metadata = plan.Metadata with { Checksum = ViewerCuriosityChecksum.ForPlan(plan) } };
        return new ViewerCuriosityProjection(bank, learning, plan);
    }

    private static ViewerCuriosityArtifactMetadata Metadata(string executionId, string language, string profile, DateTimeOffset createdUtc) =>
        new(executionId, language, profile, Version, string.Empty, createdUtc);
    private static IReadOnlyDictionary<string, int> Count(IEnumerable<ViewerQuestion> questions, Func<ViewerQuestion, string> key) =>
        questions.GroupBy(key, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
    private static string Normalize(string value) => string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('?', '.', '!');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Priority(int order) => order <= 2 ? "High" : order <= 4 ? "Medium" : "Normal";
    private static string MapCategory(string type) => type.Trim().ToLowerInvariant() switch
    {
        "what" => "Recognition", "why" => "ScientificExplanation", "where" or "when" => "ObservationGuidance",
        "action" => "PracticalViewingAdvice", "how" => "EventSignificance", _ => "ViewerComparison"
    };
    private static IReadOnlyList<string> ResolveVariants(string profile) => profile.Contains("short", StringComparison.OrdinalIgnoreCase)
        ? ["Short"] : profile.Contains("long", StringComparison.OrdinalIgnoreCase) ? ["Long"] : ["Long", "Short"];
}

public static class ViewerCuriosityChecksum
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string For<T>(IReadOnlyList<T> payload) => Hash(JsonSerializer.Serialize(payload, Options));
    public static string ForPlan(ViewerQuestionPlan plan) => Hash(JsonSerializer.Serialize(new
    {
        plan.TotalGeneratedQuestions, plan.AcceptedQuestions, plan.CategoryCoverage, plan.PriorityDistribution,
        plan.QuestionsRequiringEditorialAttention, plan.ProjectionWarnings
    }, Options));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class ViewerCuriosityArtifactValidator
{
    public static IReadOnlyList<string> Validate(ViewerCuriosityProjection projection, string executionId, string language, string profile)
    {
        var errors = new List<string>();
        ValidateMetadata(projection.ViewerQuestionBank.Metadata, executionId, language, profile, ViewerCuriosityChecksum.For(projection.ViewerQuestionBank.Questions), "Viewer Question Bank", errors);
        if (projection.ViewerQuestionBank.Questions.Count == 0) errors.Add("Viewer Question Bank must contain at least one usable question.");
        if (projection.ViewerQuestionBank.Questions.Any(x => string.IsNullOrWhiteSpace(x.QuestionText))) errors.Add("Viewer Question Bank contains empty question text.");
        var validPriorities = new HashSet<string>(["High", "Medium", "Normal"], StringComparer.Ordinal);
        var validCategories = new HashSet<string>(["Recognition", "ScientificExplanation", "ObservationGuidance", "PracticalViewingAdvice", "EventSignificance", "ViewerComparison"], StringComparer.Ordinal);
        if (projection.ViewerQuestionBank.Questions.Any(x => !validPriorities.Contains(x.Priority))) errors.Add("Viewer Question Bank contains an invalid priority.");
        if (projection.ViewerQuestionBank.Questions.Any(x => !validCategories.Contains(x.Category))) errors.Add("Viewer Question Bank contains an invalid category.");
        if (projection.ViewerQuestionBank.Questions.Select(x => x.QuestionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != projection.ViewerQuestionBank.Questions.Count) errors.Add("Viewer Question Bank contains duplicate question IDs.");
        var normalized = projection.ViewerQuestionBank.Questions.Select(x => string.Join(' ', x.QuestionText.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('?', '.', '!')).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length) errors.Add("Viewer Question Bank contains duplicate normalized questions.");
        if (projection.ViewerQuestionBank.Questions.Any(x => x.Language != language)) errors.Add("Viewer Question Bank contains unsupported language mixing.");
        ValidateMetadata(projection.LearningObjectives.Metadata, executionId, language, profile, ViewerCuriosityChecksum.For(projection.LearningObjectives.Objectives), "Learning Objectives", errors);
        var ids = projection.ViewerQuestionBank.Questions.Select(x => x.QuestionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (projection.LearningObjectives.Objectives.Any(x => string.IsNullOrWhiteSpace(x.Text) || x.ViewerQuestionIds.Count == 0 || x.ViewerQuestionIds.Any(id => !ids.Contains(id)))) errors.Add("Learning Objectives contain empty text or unknown Viewer Question references.");
        if (projection.LearningObjectives.Objectives.Select(x => x.ObjectiveId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != projection.LearningObjectives.Objectives.Count) errors.Add("Learning Objectives contain duplicate objective IDs.");
        ValidateMetadata(projection.QuestionPlan.Metadata, executionId, language, profile, ViewerCuriosityChecksum.ForPlan(projection.QuestionPlan), "Question Plan", errors);
        if (projection.QuestionPlan.AcceptedQuestions != projection.ViewerQuestionBank.Questions.Count || projection.QuestionPlan.CategoryCoverage.Values.Sum() != projection.QuestionPlan.AcceptedQuestions || projection.QuestionPlan.PriorityDistribution.Values.Sum() != projection.QuestionPlan.AcceptedQuestions) errors.Add("Question Plan counts do not reconcile with projected questions.");
        if (projection.QuestionPlan.QuestionsRequiringEditorialAttention.Any(id => !ids.Contains(id))) errors.Add("Question Plan contains an unknown Viewer Question reference.");
        return errors;
    }

    private static void ValidateMetadata(ViewerCuriosityArtifactMetadata metadata, string executionId, string language, string profile, string checksum, string artifact, List<string> errors)
    {
        if (metadata.Version != "1.0" || metadata.ExecutionId != executionId || metadata.Language != language || metadata.Profile != profile || metadata.CreatedUtc == default) errors.Add($"{artifact} metadata does not match the current execution.");
        if (!string.Equals(metadata.Checksum, checksum, StringComparison.Ordinal)) errors.Add($"{artifact} checksum mismatch.");
    }
}
