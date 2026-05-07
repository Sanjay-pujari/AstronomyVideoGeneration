using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Mvc;

namespace Astronomy.MediaFactory.Api.Controllers;

[ApiController]
[Route("api/metaoauth")]
public sealed class MetaOAuthController : ControllerBase
{
    private readonly IMetaOAuthService _metaOAuthService;

    public MetaOAuthController(IMetaOAuthService metaOAuthService)
    {
        _metaOAuthService = metaOAuthService;
    }

    [HttpGet("start")]
    [ProducesResponseType(typeof(MetaOAuthStartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Start([FromQuery] bool redirect = false)
    {
        try
        {
            var authorizationUrl = _metaOAuthService.BuildAuthorizationUrl();
            if (redirect)
            {
                return Redirect(authorizationUrl);
            }

            return Ok(new MetaOAuthStartResponse(
                Success: true,
                AuthorizationUrl: authorizationUrl,
                Message: "Open authorizationUrl in a browser to grant Meta publishing access."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("callback")]
    [ProducesResponseType(typeof(MetaOAuthSetupResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest(new { success = false, message = $"Meta OAuth returned error: {error}" });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { success = false, message = "OAuth authorization code is required." });
        }

        try
        {
            return Ok(await _metaOAuthService.CompleteSetupAsync(code, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
