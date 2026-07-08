using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class Rc2PipelinePhaseRegistry
{
    public const string OrchestrationVersion = "RC2";

    public IReadOnlyList<int> ResolveRequestedPhaseNumbers(BatchGenerateFromPlansRequest request)
    {
        var startPhaseNo = request.StartPhaseNo ?? 1;
        var endPhaseNo = request.EndPhaseNo ?? 20;

        if (startPhaseNo > endPhaseNo)
            return Array.Empty<int>();

        return Enumerable.Range(startPhaseNo, endPhaseNo - startPhaseNo + 1).ToArray();
    }
}
