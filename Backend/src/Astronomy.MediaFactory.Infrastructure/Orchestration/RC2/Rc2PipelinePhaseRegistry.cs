using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class Rc2PipelinePhaseRegistry
{
    public const string OrchestrationVersion = "RC2";
    public const int PracticalPhaseCount = 21;

    public IReadOnlyList<Rc2PipelinePhaseDefinition> PhaseDefinitions { get; } =
    [
        new(1, "Load Plan"),
        new(2, "Build ProductionEventIntelligence"),
        new(3, "Generate QuestionAnswerSet"),
        new(4, "Validate Questions"),
        new(5, "Generate Scene Plan"),
        new(6, "Editorial Intelligence Foundation")
    ];

    public IReadOnlyList<int> ResolveRequestedPhaseNumbers(BatchGenerateFromPlansRequest request)
    {
        var startPhaseNo = request.StartPhaseNo ?? 1;
        var endPhaseNo = request.EndPhaseNo ?? PracticalPhaseCount;

        if (startPhaseNo > endPhaseNo)
            return Array.Empty<int>();

        return Enumerable.Range(startPhaseNo, endPhaseNo - startPhaseNo + 1).ToArray();
    }
}

public sealed record Rc2PipelinePhaseDefinition(int PhaseNo, string PhaseName);
