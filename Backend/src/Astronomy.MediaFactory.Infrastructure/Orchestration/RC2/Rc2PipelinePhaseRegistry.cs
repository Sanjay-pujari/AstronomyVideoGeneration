using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class Rc2PipelinePhaseRegistry
{
    public const string OrchestrationVersion = "RC2";
    public const int PracticalPhaseCount = 21;

    public IReadOnlyList<Rc2PipelinePhaseDefinition> PhaseDefinitions { get; } =
    [
        new(1, "Run Setup / Plan Selection"),
        new(2, "Domain Intelligence"),
        new(3, "Question / Story Planning"),
        new(4, "Story Intelligence"),
        new(5, "Editorial Intelligence"),
        new(6, "Story Frames Authority"),
        new(7, "Narration Studio V5"),
        new(8, "Format-Aware Scene Asset Generation")
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
