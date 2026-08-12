using System.Net.Mime;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Mvc;

namespace Astronomy.MediaFactory.Api.Controllers;

[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("api/rc2/publishing")]
public sealed class Rc2PublishingController(IRc2PublishingControlService service, ILogger<Rc2PublishingController> logger) : ControllerBase
{
    [HttpPost("package")]
    public async Task<IActionResult> Package([FromBody] Rc2CreatePublishingPackageRequest request, CancellationToken ct)
        => await Invoke(() => service.CreateOrRefreshPackageAsync(request.PlanId, request.OverwriteExisting, ct));

    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] Rc2SetPublishingApprovalRequest request, CancellationToken ct)
        => await Invoke(() => service.SetApprovalAsync(request.PlanId, request.Decision, ct));

    [HttpGet("status/{planId:guid}")]
    public async Task<IActionResult> Status(Guid planId, CancellationToken ct)
        => await Invoke(() => service.GetStatusAsync(planId, ct));

    private async Task<IActionResult> Invoke<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException ex) { return BadRequest(new { code = "RC2_PUBLISH_INVALID_REQUEST", message = ex.Message }); }
        catch (Rc2PublishingControlException ex)
        {
            var status = ex.Code switch
            {
                "RC2_PUBLISH_PLAN_NOT_FOUND" => StatusCodes.Status404NotFound,
                "RC2_PUBLISH_AUTHORITY_CHANGED" => StatusCodes.Status409Conflict,
                "RC2_PUBLISH_PHASE20_INVALID" or "RC2_PUBLISH_PACKAGE_NOT_AVAILABLE" or "RC2_PUBLISH_OUTPUT_ROOT_NOT_AVAILABLE" or "RC2_PUBLISH_PACKAGE_GOVERNANCE_FAILED" => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status500InternalServerError
            };
            return StatusCode(status, new { code = ex.Code, message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RC2 publishing persistence operation failed.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { code = "RC2_PUBLISH_PERSISTENCE_FAILED", message = "The publishing approval could not be persisted." });
        }
    }
}
