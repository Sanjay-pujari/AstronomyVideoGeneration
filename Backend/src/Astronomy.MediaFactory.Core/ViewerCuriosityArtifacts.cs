using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public sealed record ViewerCuriosityArtifactMetadata(string ExecutionId, string Language, string Profile, string Version, string Checksum, DateTimeOffset CreatedUtc);
public sealed record ViewerKnowledgeReference(string ReferenceId, string ReferenceType, string SourceArtifact, string ResolutionStatus);
public sealed record ViewerQuestion(string QuestionId, string QuestionText, string Priority, string Category,
    IReadOnlyList<ViewerKnowledgeReference> KnowledgeReferences, string ExpectedLearningOutcome,
    IReadOnlyList<string> ApplicableVariants, string Language, int Order, int SourceDisplayOrder)
{
    public string SourceAnswer { get; init; } = "";
    public string CertifiedAnswer { get; init; } = "";
    public string AnswerResolutionStatus { get; init; } = "Unresolved";
    public string AnswerUsability { get; init; } = "EditorialOnly";
    public bool RequiresEditorialAttention { get; init; } = true;
    public IReadOnlyList<string> GroundingWarnings { get; init; } = [];
}
public sealed record ViewerQuestionBank(ViewerCuriosityArtifactMetadata Metadata, IReadOnlyList<ViewerQuestion> Questions);
public sealed record LearningObjective(string ObjectiveId, string Text, IReadOnlyList<string> ViewerQuestionIds);
public sealed record ViewerLearningObjectives(ViewerCuriosityArtifactMetadata Metadata, IReadOnlyList<LearningObjective> Objectives);
public sealed record ViewerQuestionPlan(ViewerCuriosityArtifactMetadata Metadata, int TotalGeneratedQuestions, int AcceptedQuestions,
    IReadOnlyDictionary<string, int> CategoryCoverage, IReadOnlyDictionary<string, int> PriorityDistribution,
    IReadOnlyDictionary<string, int> VariantCoverage, IReadOnlyDictionary<string, int> KnowledgeCoverage,
    IReadOnlyList<string> QuestionsRequiringEditorialAttention, IReadOnlyList<string> ProjectionWarnings);
public sealed record ViewerCuriosityProjection(ViewerQuestionBank ViewerQuestionBank, ViewerLearningObjectives LearningObjectives, ViewerQuestionPlan QuestionPlan);

public interface IViewerCuriosityArtifactProjector
{
    ViewerCuriosityProjection Project(QuestionAnswerSetDto source, ProductionEventIntelligence intelligence, string executionId,
        string profile, IReadOnlyList<string> applicableVariants, DateTimeOffset createdUtc);
}

public sealed class ViewerCuriosityArtifactProjector : IViewerCuriosityArtifactProjector
{
    public const string Version = "1.2";
    private static readonly HashSet<string> SupportedTypes = new(["what", "why", "where", "when", "action", "how"], StringComparer.OrdinalIgnoreCase);

    public ViewerCuriosityProjection Project(QuestionAnswerSetDto source, ProductionEventIntelligence intelligence, string executionId,
        string profile, IReadOnlyList<string> applicableVariants, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(intelligence); ArgumentNullException.ThrowIfNull(applicableVariants);
        if (!Guid.TryParse(executionId, out var executionGuid) || executionGuid == Guid.Empty) throw new ArgumentException("Phase 3 executionId must be a non-empty GUID.", nameof(executionId));
        if (string.IsNullOrWhiteSpace(profile)) throw new ArgumentException("Phase 3 profile must be non-empty.", nameof(profile));
        if (string.IsNullOrWhiteSpace(source.Language)) throw new ArgumentException("Phase 3 source language must be non-empty.", nameof(source));
        if (source.AstronomyEventIntelligenceId == Guid.Empty) throw new ArgumentException("Phase 3 AstronomyEventIntelligenceId must not be Guid.Empty.", nameof(source));
        if (source.Answers is null) throw new ArgumentException("Phase 3 source Answers collection must not be null.", nameof(source));
        var variants = applicableVariants.Select(NormalizeVariant).Order(StringComparer.Ordinal).ToArray();
        if (variants.Length == 0 || variants.Any(string.IsNullOrEmpty) || variants.Distinct(StringComparer.Ordinal).Count() != variants.Length)
            throw new ArgumentException("Phase 3 applicableVariants must contain unique supported values Long and/or Short.", nameof(applicableVariants));

        var warnings = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal); var projected = new List<ViewerQuestion>();
        foreach (var answer in source.Answers.OrderBy(x => x?.DisplayOrder ?? int.MaxValue).ThenBy(x => Normalize(x?.QuestionText), StringComparer.Ordinal))
        {
            if (answer is null) throw new ArgumentException("Phase 3 source Answers contains a null question.", nameof(source));
            var normalized = Normalize(answer.QuestionText);
            if (normalized.Length == 0) throw new ArgumentException($"Phase 3 question at source order {answer.DisplayOrder} has empty QuestionText.", nameof(source));
            if (answer.DisplayOrder <= 0) throw new ArgumentException($"Phase 3 question '{answer.QuestionText}' has invalid DisplayOrder={answer.DisplayOrder}; expected a positive value.", nameof(source));
            if (string.IsNullOrWhiteSpace(answer.Title) && string.IsNullOrWhiteSpace(answer.AnswerText))
                throw new ArgumentException($"Phase 3 question at source order {answer.DisplayOrder} has no usable Title or AnswerText learning outcome.", nameof(source));
            if (!seen.Add(normalized)) { warnings.Add($"Question at source order {answer.DisplayOrder} was omitted because it duplicated an earlier normalized question."); continue; }
            var recognized = SupportedTypes.Contains(answer.QuestionType ?? string.Empty);
            var category = MapCategory(answer.QuestionType);
            if (!recognized) warnings.Add($"Question at source order {answer.DisplayOrder} has unsupported QuestionType '{answer.QuestionType ?? "<null>"}' and requires editorial attention.");
            var references = ResolveReferences(category, intelligence);
            var outcome = string.IsNullOrWhiteSpace(answer.Title) ? answer.AnswerText.Trim() : answer.Title.Trim();
            var identityRefs = string.Join(',', references.OrderBy(x => x.ReferenceType).ThenBy(x => x.ReferenceId).Select(x => $"{x.ReferenceType}:{x.ReferenceId}"));
            var id = "vq-" + Hash($"{Normalize(profile)}|{Normalize(source.Language)}|{normalized}|{category}|{identityRefs}|{string.Join(',', variants.Order())}")[..16];
            var resolved = references.Count > 0;
            var groundingWarning = resolved ? Array.Empty<string>() : new[] { $"{category} answer is not supported by certified Phase 2 event intelligence and requires editorial completion." };
            projected.Add(new(id, answer.QuestionText.Trim(), Priority(category, answer.DisplayOrder), category, references, outcome, variants, source.Language, 0, answer.DisplayOrder)
            {
                SourceAnswer = answer.AnswerText?.Trim() ?? "",
                CertifiedAnswer = resolved ? answer.AnswerText?.Trim() ?? outcome : $"{category} guidance is not available in the certified event intelligence and requires editorial completion.",
                AnswerResolutionStatus = resolved ? "Resolved" : "Unresolved",
                AnswerUsability = resolved ? "Certified" : "EditorialOnly",
                RequiresEditorialAttention = !resolved,
                GroundingWarnings = groundingWarning
            });
        }
        var questions = projected.Select((q, i) => q with { Order = i + 1 }).ToArray();
        var metadata = new ViewerCuriosityArtifactMetadata(executionId, source.Language, profile, Version, "", createdUtc);
        var bank = new ViewerQuestionBank(metadata, questions); bank = bank with { Metadata = metadata with { Checksum = ViewerCuriosityChecksum.For(bank.Questions) } };
        var learning = new ViewerLearningObjectives(metadata, questions.Select(q => new LearningObjective("lo-" + Hash($"{q.QuestionId}|{q.ExpectedLearningOutcome}")[..16], q.ExpectedLearningOutcome, [q.QuestionId])).ToArray());
        learning = learning with { Metadata = metadata with { Checksum = ViewerCuriosityChecksum.For(learning.Objectives) } };
        var attention = questions.Where(q => q.RequiresEditorialAttention).Select(q => q.QuestionId).ToArray();
        foreach (var q in questions.Where(q => q.KnowledgeReferences.Count == 0)) warnings.Add($"Question '{q.QuestionId}' has no resolvable Phase 2 knowledge field and requires editorial attention.");
        var plan = new ViewerQuestionPlan(metadata, source.Answers.Count, questions.Length, Count(questions, q => q.Category), Count(questions, q => q.Priority),
            questions.SelectMany(q => q.ApplicableVariants).GroupBy(x => x).OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Count()),
            questions.SelectMany(q => q.KnowledgeReferences).GroupBy(x => x.ReferenceType).OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Count()), attention, warnings);
        plan = plan with { Metadata = metadata with { Checksum = ViewerCuriosityChecksum.ForPlan(plan) } };
        return new(bank, learning, plan);
    }

    private static IReadOnlyList<ViewerKnowledgeReference> ResolveReferences(string category, ProductionEventIntelligence i)
    {
        string[] fields = category switch
        {
            "Recognition" => i.PrimaryObjects.Count > 0 ? ["primaryObjects"] : ["title"],
            "ScientificExplanation" or "EventSignificance" => !string.IsNullOrWhiteSpace(i.ScientificContext) ? ["scientificContext"] : Array.Empty<string>(),
            "TimingGuidance" => !string.IsNullOrWhiteSpace(i.BestViewingWindowLocal) ? ["bestViewingWindowLocal"] : !string.IsNullOrWhiteSpace(i.LocalPeakTime) ? ["localPeakTime"] : Array.Empty<string>(),
            "LocationGuidance" => !string.IsNullOrWhiteSpace(i.SkyDirectionHint) ? ["skyDirectionHint"] : Array.Empty<string>(),
            "ObservationGuidance" => i.ViewerInstructions.Count > 0 ? ["viewerInstructions"] : Array.Empty<string>(),
            "PracticalViewingAdvice" => i.ViewerInstructions.Count > 0 ? ["viewerInstructions"] : Array.Empty<string>(),
            _ => Array.Empty<string>()
        };
        return fields.Select(field => new ViewerKnowledgeReference($"production-event-intelligence#/{field}", "ProductionIntelligenceField", "02-intelligence/production-event-intelligence.json", "Resolved")).ToArray();
    }
    private static string MapCategory(string? type) => (type ?? "").Trim().ToLowerInvariant() switch { "what" => "Recognition", "why" => "ScientificExplanation", "where" => "LocationGuidance", "when" => "TimingGuidance", "action" => "PracticalViewingAdvice", "how" => "ObservationGuidance", _ => "Other" };
    private static string Priority(string category, int order) => category is "ScientificExplanation" or "Recognition" ? "High" : order <= 4 ? "Medium" : "Normal";
    private static string NormalizeVariant(string value) => value?.Trim().ToLowerInvariant() switch { "long" or "longvideo" => "Long", "short" or "shortvideo" => "Short", _ => "" };
    private static IReadOnlyDictionary<string, int> Count(IEnumerable<ViewerQuestion> q, Func<ViewerQuestion, string> key) => q.GroupBy(key, StringComparer.Ordinal).OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Count());
    private static string Normalize(string? value) => string.Join(' ', (value ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('?', '.', '!');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class ViewerCuriosityChecksum
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string For<T>(IReadOnlyList<T> payload) => Hash(JsonSerializer.Serialize(payload, Options));
    public static string ForPlan(ViewerQuestionPlan p) => Hash(JsonSerializer.Serialize(new { p.TotalGeneratedQuestions, p.AcceptedQuestions,
        CategoryCoverage = Sorted(p.CategoryCoverage), PriorityDistribution = Sorted(p.PriorityDistribution), VariantCoverage = Sorted(p.VariantCoverage), KnowledgeCoverage = Sorted(p.KnowledgeCoverage),
        Editorial = p.QuestionsRequiringEditorialAttention.Order(StringComparer.Ordinal), Warnings = p.ProjectionWarnings }, Options));
    private static SortedDictionary<string, int> Sorted(IReadOnlyDictionary<string, int> source)
    {
        var sorted = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in source) sorted[entry.Key] = entry.Value;
        return sorted;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class ViewerCuriosityArtifactValidator
{
    private static readonly HashSet<string> Priorities = new(["High", "Medium", "Normal"], StringComparer.Ordinal);
    private static readonly HashSet<string> Categories = new(["Recognition", "ScientificExplanation", "ObservationGuidance", "PracticalViewingAdvice", "EventSignificance", "ViewerComparison", "CulturalHistoricalContext", "Safety", "EquipmentGuidance", "TimingGuidance", "LocationGuidance", "Other"], StringComparer.Ordinal);
    public static IReadOnlyList<string> Validate(ViewerCuriosityProjection p, string executionId, string language, string profile, IReadOnlyList<string>? expectedVariants = null, ProductionEventIntelligence? intelligence = null)
    {
        var e = new List<string>(); ValidateMetadata(p.ViewerQuestionBank.Metadata, executionId, language, profile, ViewerCuriosityChecksum.For(p.ViewerQuestionBank.Questions), "Viewer Question Bank", e);
        var q = p.ViewerQuestionBank.Questions;
        if (q.Count == 0) e.Add("Viewer Question Bank must contain at least one usable question.");
        if (q.Select(x => x.QuestionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != q.Count) e.Add("Viewer Question Bank contains duplicate question IDs.");
        if (!q.Select(x => x.Order).SequenceEqual(Enumerable.Range(1, q.Count))) e.Add("Viewer Question Bank Order must be canonical sequential values 1..N.");
        var normalized = q.Select(x => Normalize(x.QuestionText)).ToArray(); if (normalized.Any(string.IsNullOrEmpty)) e.Add("Viewer Question Bank contains empty question text.");
        if (normalized.Distinct().Count() != normalized.Length) e.Add("Viewer Question Bank contains duplicate normalized questions.");
        foreach (var x in q)
        {
            if (string.IsNullOrWhiteSpace(x.ExpectedLearningOutcome)) e.Add($"Viewer Question Bank question '{x.QuestionId}' field ExpectedLearningOutcome is empty.");
            if (x.RequiresEditorialAttention && (x.AnswerResolutionStatus != "Unresolved" || x.AnswerUsability != "EditorialOnly")) e.Add($"Viewer Question Bank question '{x.QuestionId}' has inconsistent editorial grounding status.");
            if (!x.RequiresEditorialAttention && (x.AnswerResolutionStatus != "Resolved" || x.AnswerUsability != "Certified" || x.KnowledgeReferences.Count == 0)) e.Add($"Viewer Question Bank question '{x.QuestionId}' claims certification without resolved grounding.");
            if (!Priorities.Contains(x.Priority)) e.Add($"Viewer Question Bank question '{x.QuestionId}' field Priority has unsupported value '{x.Priority}'.");
            if (!Categories.Contains(x.Category)) e.Add($"Viewer Question Bank question '{x.QuestionId}' field Category has unsupported value '{x.Category}'.");
            if (!string.Equals(x.Language, language, StringComparison.OrdinalIgnoreCase)) e.Add($"Viewer Question Bank question '{x.QuestionId}' language expected '{language}', actual '{x.Language}'.");
            if (x.ApplicableVariants.Count == 0 || x.ApplicableVariants.Any(v => v is not ("Long" or "Short")) || x.ApplicableVariants.Distinct().Count() != x.ApplicableVariants.Count) e.Add($"Viewer Question Bank question '{x.QuestionId}' has invalid ApplicableVariants.");
            if (expectedVariants is not null && !x.ApplicableVariants.Order().SequenceEqual(expectedVariants.Order(), StringComparer.OrdinalIgnoreCase)) e.Add($"Viewer Question Bank question '{x.QuestionId}' variant scope does not match execution scope.");
            foreach (var r in x.KnowledgeReferences)
                if (string.IsNullOrWhiteSpace(r.ReferenceId) || r.ReferenceId.EndsWith(Guid.Empty.ToString("D"), StringComparison.OrdinalIgnoreCase) || r.ReferenceType != "ProductionIntelligenceField" || r.SourceArtifact != "02-intelligence/production-event-intelligence.json" || r.ResolutionStatus != "Resolved" || (intelligence is not null && !ReferenceResolves(r.ReferenceId, intelligence))) e.Add($"Viewer Question Bank question '{x.QuestionId}' knowledge reference '{r.ReferenceId}' is invalid or unresolved.");
            if (x.Category is "ScientificExplanation" or "CulturalHistoricalContext" && x.KnowledgeReferences.Count == 0) e.Add($"Viewer Question Bank question '{x.QuestionId}' category '{x.Category}' requires a resolved certified knowledge reference.");
        }
        ValidateMetadata(p.LearningObjectives.Metadata, executionId, language, profile, ViewerCuriosityChecksum.For(p.LearningObjectives.Objectives), "Learning Objectives", e);
        var ids = q.Select(x => x.QuestionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (p.LearningObjectives.Objectives.Select(x => x.ObjectiveId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.LearningObjectives.Objectives.Count) e.Add("Learning Objectives contain duplicate objective IDs.");
        foreach (var o in p.LearningObjectives.Objectives) if (string.IsNullOrWhiteSpace(o.Text) || o.ViewerQuestionIds.Count == 0 || o.ViewerQuestionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != o.ViewerQuestionIds.Count || o.ViewerQuestionIds.Any(id => !ids.Contains(id))) e.Add($"Learning Objective '{o.ObjectiveId}' contains empty text, duplicate, or unknown Viewer Question references.");
        ValidateMetadata(p.QuestionPlan.Metadata, executionId, language, profile, ViewerCuriosityChecksum.ForPlan(p.QuestionPlan), "Question Plan", e);
        if (p.QuestionPlan.AcceptedQuestions != q.Count || p.QuestionPlan.CategoryCoverage.Values.Sum() != q.Count || p.QuestionPlan.PriorityDistribution.Values.Sum() != q.Count) e.Add("Question Plan counts do not reconcile with projected questions.");
        if (!DictionaryEqual(p.QuestionPlan.CategoryCoverage, q.GroupBy(x => x.Category).ToDictionary(x => x.Key, x => x.Count())) || !DictionaryEqual(p.QuestionPlan.PriorityDistribution, q.GroupBy(x => x.Priority).ToDictionary(x => x.Key, x => x.Count()))) e.Add("Question Plan coverage does not reconcile with projected questions.");
        if (p.QuestionPlan.QuestionsRequiringEditorialAttention.Any(id => !ids.Contains(id))) e.Add("Question Plan contains an unknown Viewer Question reference.");
        return e;
    }
    private static bool DictionaryEqual(IReadOnlyDictionary<string,int> a, IReadOnlyDictionary<string,int> b) => a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var v) && v == x.Value);
    private static bool ReferenceResolves(string referenceId, ProductionEventIntelligence intelligence)
    {
        var field = referenceId.Split("#/", StringSplitOptions.None).LastOrDefault();
        return field switch
        {
            "primaryObjects" => intelligence.PrimaryObjects.Count > 0,
            "title" => !string.IsNullOrWhiteSpace(intelligence.Title),
            "scientificContext" => !string.IsNullOrWhiteSpace(intelligence.ScientificContext),
            "bestViewingWindowLocal" => !string.IsNullOrWhiteSpace(intelligence.BestViewingWindowLocal),
            "localPeakTime" => !string.IsNullOrWhiteSpace(intelligence.LocalPeakTime),
            "skyDirectionHint" => !string.IsNullOrWhiteSpace(intelligence.SkyDirectionHint),
            "viewerInstructions" => intelligence.ViewerInstructions.Count > 0,
            _ => false
        };
    }
    private static string Normalize(string? s) => string.Join(' ', (s ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('?', '.', '!');
    private static void ValidateMetadata(ViewerCuriosityArtifactMetadata m, string id, string language, string profile, string checksum, string artifact, List<string> e)
    { if (m.Version != ViewerCuriosityArtifactProjector.Version || m.ExecutionId != id || !string.Equals(m.Language, language, StringComparison.OrdinalIgnoreCase) || m.Profile != profile || m.CreatedUtc == default) e.Add($"{artifact} metadata does not match the current execution."); if (m.Checksum != checksum) e.Add($"{artifact} checksum mismatch."); }
}
