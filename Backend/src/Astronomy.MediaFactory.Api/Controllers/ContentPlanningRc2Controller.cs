using System.Net.Mime;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Microsoft.AspNetCore.Mvc;

namespace Astronomy.MediaFactory.Api.Controllers;

[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("api/content-planning/rc2")]
public sealed class ContentPlanningRc2Controller(
    IRc2ContentPlanningBatchOrchestrator orchestrator,
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

        if (request.UseProductionPipeline && request.StartPhaseNo == 11 && request.EndPhaseNo == 11
            && request.RequestedOutputsOverride?.Any(output =>
                string.Equals(output, "HeroAsset", StringComparison.OrdinalIgnoreCase)) != true)
        {
            logger.LogWarning(
                "RC2 Phase 11 manual execution did not receive HeroAsset in requestedOutputsOverride. " +
                "Phase 11 will remain inapplicable (P11_HERO_ASSET_NOT_REQUESTED). " +
                "Setup warning: RC2_HERO_CERTIFICATION_OVERRIDE_MISSING");
        }

        try
        {
            var response = await orchestrator.GenerateFromPlansAsync(request, cancellationToken);
            // Echo the value directly from ASP.NET model binding.  Do not infer this from
            // persisted plan outputs or from downstream pipeline state.
            response = response with
            {
                ManualRequestedOutputsOverrideReceived = request.RequestedOutputsOverride ?? []
            };
            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
