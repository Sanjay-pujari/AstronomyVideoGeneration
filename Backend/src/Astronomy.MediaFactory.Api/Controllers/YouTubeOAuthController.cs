using System.Net.Mime;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;

namespace Astronomy.MediaFactory.Api.Controllers;

[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("api/youtubeoauth")]
public sealed class YouTubeOAuthController : ControllerBase
{
    private const string StateCookie = "youtube_oauth_state";
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
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10), IsEssential = true
            });
            var authorizationUrl = AppendState(_youTubeOAuthService.BuildAuthorizationUrl(), state);
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
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, [FromQuery] string? state, CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(StateCookie, out var expectedState) || !StateEquals(expectedState, state))
            return BadRequest(new { success = false, message = "OAuth state validation failed." });
        Response.Cookies.Delete(StateCookie);

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

    private static string AppendState(string url, string state) => $"{url}{(url.Contains('?') ? '&' : '?')}state={Uri.EscapeDataString(state)}";
    private static bool StateEquals(string expected, string? actual) => actual is not null &&
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(actual));
}
