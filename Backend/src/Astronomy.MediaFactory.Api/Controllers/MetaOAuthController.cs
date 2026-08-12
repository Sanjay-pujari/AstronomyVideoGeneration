using System.Net.Mime;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;

namespace Astronomy.MediaFactory.Api.Controllers;

[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("api/metaoauth")]
public sealed class MetaOAuthController : ControllerBase
{
    private const string StateCookie = "meta_oauth_state";
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
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            Response.Cookies.Append(StateCookie, state, new CookieOptions { HttpOnly = true, Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), IsEssential = true });
            var authorizationUrl = AppendState(_metaOAuthService.BuildAuthorizationUrl(), state);
            if (redirect && IsTopLevelNavigationRequest())
            {
                return Redirect(authorizationUrl);
            }

            var message = redirect
                ? "Use authorizationUrl as a top-level browser navigation. Token files are generated after Meta redirects back to the callback."
                : "Open authorizationUrl in a browser to grant Meta publishing access.";

            return Ok(new MetaOAuthStartResponse(
                Success: true,
                AuthorizationUrl: authorizationUrl,
                Message: message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    private bool IsTopLevelNavigationRequest()
    {
        if (Request.Headers.ContainsKey("Origin"))
        {
            return false;
        }

        var fetchMode = Request.Headers["Sec-Fetch-Mode"].ToString();
        return string.IsNullOrWhiteSpace(fetchMode) || string.Equals(fetchMode, "navigate", StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet("callback")]
    [ProducesResponseType(typeof(MetaOAuthSetupResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, [FromQuery] string? state, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest(new { success = false, message = $"Meta OAuth returned error: {error}" });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { success = false, message = "OAuth authorization code is required." });
        }
        if (!Request.Cookies.TryGetValue(StateCookie, out var expectedState) || !StateEquals(expectedState, state))
            return BadRequest(new { success = false, message = "OAuth state validation failed." });
        Response.Cookies.Delete(StateCookie);

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

    private static string AppendState(string url, string state) => $"{url}{(url.Contains('?') ? '&' : '?')}state={Uri.EscapeDataString(state)}";
    private static bool StateEquals(string expected, string? actual) => actual is not null &&
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(actual));
}
