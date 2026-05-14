using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Scheduling;

public sealed class OrchestratorPipelineRunExecutor : IPipelineRunExecutor
{
    private readonly PipelineOrchestrator _orchestrator;

    public OrchestratorPipelineRunExecutor(PipelineOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<PipelineRun> ExecuteAsync(RunPipelineRequest request, Guid? pipelineRunId, CancellationToken cancellationToken)
        => _orchestrator.RunAsync(request, cancellationToken, pipelineRunId);
}
