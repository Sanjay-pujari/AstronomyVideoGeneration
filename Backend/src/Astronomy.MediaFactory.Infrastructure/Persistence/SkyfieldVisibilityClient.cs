using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class SkyfieldVisibilityClient(HttpClient httpClient, IOptions<AstronomyOptions> options, ILogger<SkyfieldVisibilityClient> logger) : ISkyfieldVisibilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SkyfieldVisibilityResponse> CalculateAsync(SkyfieldVisibilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cfg = options.Value;
        if (!cfg.UseSkyfield)
        {
            return new SkyfieldVisibilityResponse(false, null, null, null, null, [], ["Skyfield disabled by configuration; using fallback visibility approximation."], "Skyfield disabled by configuration.");
        }

        if (!Uri.TryCreate(cfg.SkyfieldSidecarBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Astronomy:SkyfieldSidecarBaseUrl must be an absolute URI when Astronomy:UseSkyfield=true.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, cfg.SkyfieldTimeoutSeconds)));

        try
        {
            using var response = await httpClient.PostAsJsonAsync("/visibility/night-plan", request, JsonOptions, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], $"Skyfield sidecar returned status {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<SkyfieldVisibilityResponse>(JsonOptions, timeoutCts.Token);
            if (payload is null)
                return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], "Skyfield sidecar returned empty payload.");

            if (!payload.Success || payload.SunsetUtc is null || payload.SunriseUtc is null)
                return payload with { Success = false, ErrorMessage = payload.ErrorMessage ?? "Skyfield sidecar payload was invalid." };

            return payload;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], "Skyfield sidecar timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skyfield visibility request failed.");
            return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], ex.Message);
        }
    }
}
