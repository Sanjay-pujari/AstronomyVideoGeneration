using System.Net.Mime;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Microsoft.AspNetCore.Mvc;

namespace Astronomy.MediaFactory.Api.Controllers;

[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("api/content-planning/rc2")]
public sealed class ContentPlanningRc2Controller(
    Rc2ContentPlanningBatchOrchestrator orchestrator,
    ILogger<ContentPlanningRc2Controller> logger) : ControllerBase
{
    [HttpPost("batch-generate-from-plans")]
    [ProducesResponseType(typeof(BatchGenerateFromPlansResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BatchGenerateFromPlans([FromBody] BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "RC2 content plan batch generation requested for {RegionId}/{Language}/{Year}. DryRun={DryRun}; MaxPlans={MaxPlans}; OnlyHighPriority={OnlyHighPriority}; UseProductionPipeline={UseProductionPipeline}; RequestedTitles={RequestedTitleCount}",
            request.RegionId,
            request.Language,
            request.Year,
            request.DryRun,
            request.MaxPlans,
            request.OnlyHighPriority,
            request.UseProductionPipeline,
            request.PlanTitles?.Count ?? 0);

        try
        {
            var response = await orchestrator.GenerateFromPlansAsync(request, cancellationToken);
            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
