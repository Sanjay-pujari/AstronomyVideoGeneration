namespace Astronomy.MediaFactory.Core;

public sealed record QuestionAnswerGenerationRequest(
    string RegionId,
    IReadOnlyList<string>? EventIds = null,
    IReadOnlyList<string>? PlanIds = null,
    int MaxEvents = 10,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record QuestionAnswerValidationRequest(
    string RegionId,
    string EventId,
    string Language = "en");

public sealed record QuestionAnswerGenerationResponse(
    int EventCount,
    int QuestionSetCount,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<QuestionAnswerSetDto> QuestionSets,
    IReadOnlyList<string> Warnings);

public sealed record QuestionAnswerSetDto(
    Guid? Id,
    Guid AstronomyEventIntelligenceId,
    string EventCode,
    string EventTitle,
    string EventType,
    string RegionId,
    string Language,
    string Version,
    string Status,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<QuestionAnswerDto> Answers);

public sealed record QuestionAnswerDto(
    Guid? Id,
    string QuestionType,
    string QuestionText,
    string Title,
    string AnswerText,
    int DisplayOrder);

public sealed record QuestionAnswerValidationResponse(
    string EventId,
    bool IsApproved,
    int Score,
    IReadOnlyList<QuestionAnswerValidationCheckDto> Checks,
    IReadOnlyList<string> Warnings);

public sealed record QuestionAnswerValidationCheckDto(
    string QuestionType,
    bool Approved,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Recommendations);

public interface IQuestionEngine
{
    Task<QuestionAnswerGenerationResponse> GenerateQuestionAnswersAsync(QuestionAnswerGenerationRequest request, CancellationToken cancellationToken);
    Task<QuestionAnswerValidationResponse> ValidateQuestionAnswerSetAsync(QuestionAnswerValidationRequest request, CancellationToken cancellationToken);
}
