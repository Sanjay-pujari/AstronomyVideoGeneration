using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed record Rc2PipelineExecutionContext(
    BatchGenerateFromPlansRequest Request,
    string OrchestrationVersion,
    DateTimeOffset StartedUtc)
{
    public static Rc2PipelineExecutionContext Create(BatchGenerateFromPlansRequest request) =>
        new(request, Rc2PipelinePhaseRegistry.OrchestrationVersion, DateTimeOffset.UtcNow);
}
