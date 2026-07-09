namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed record PromptQualityContract(
    string Version,
    int OverallPromptScore,
    int SectionCompletenessScore,
    int EditorialConsistencyScore,
    int ScientificCoverageScore,
    int WritingConsistencyScore,
    int EngineeringLeakageScore,
    int ReadabilityScore,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool ReadyForGeneration);
