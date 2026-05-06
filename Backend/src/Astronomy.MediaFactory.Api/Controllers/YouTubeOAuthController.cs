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
    public IActionResult Start()
    {
        try
        {
            return Redirect(_youTubeOAuthService.BuildAuthorizationUrl());
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
