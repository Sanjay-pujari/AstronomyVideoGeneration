using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Mvc;

namespace Astronomy.MediaFactory.Api.Controllers;

[ApiController]
[Route("api/youtubeoauth")]
public sealed class YouTubeOAuthController : ControllerBase
{
    private readonly IYouTubeOAuthService _youTubeOAuthService;

    public YouTubeOAuthController(IYouTubeOAuthService youTubeOAuthService)
    {
        _youTubeOAuthService = youTubeOAuthService;
    }

    [HttpGet("start")]
    [ProducesResponseType(typeof(YouTubeOAuthStartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Start([FromQuery] bool redirect = false)
    {
        try
        {
            var authorizationUrl = _youTubeOAuthService.BuildAuthorizationUrl();
            if (redirect)
            {
                return Redirect(authorizationUrl);
            }

            return Ok(new YouTubeOAuthStartResponse(
                Success: true,
                AuthorizationUrl: authorizationUrl,
                Message: "Open authorizationUrl in a browser to grant YouTube upload access."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest(new { success = false, message = $"Google OAuth returned error: {error}" });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { success = false, message = "OAuth authorization code is required." });
        }

        try
        {
            var result = await _youTubeOAuthService.CompleteSetupAsync(code, cancellationToken);
            return Ok(result);
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
