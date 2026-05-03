using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.AstroData.Services;

public sealed class ObservationWindow
{
    public string LocationName { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string Timezone { get; init; } = "UTC";
    public DateOnly TargetDate { get; init; }
    public DateTimeOffset SunsetLocal { get; init; }
    public DateTimeOffset SunriseLocal { get; init; }
    public DateTimeOffset NightWindowStartUtc { get; init; }
    public DateTimeOffset NightWindowEndUtc { get; init; }
    public string CalculationSource { get; init; } = "fallback";
}

public interface IObservationWindowService
{
    Task<ObservationWindow> BuildNightWindowAsync(ObservationOptions options, DateOnly targetDate, CancellationToken cancellationToken);
}

public sealed class ObservationWindowService : IObservationWindowService
{
    private readonly ISkyfieldSidecarClient _sidecarClient;
    private readonly ILogger<ObservationWindowService> _logger;
    public ObservationWindowService(ISkyfieldSidecarClient sidecarClient, ILogger<ObservationWindowService> logger)
    {
        _sidecarClient = sidecarClient;
        _logger = logger;
    }

    public async Task<ObservationWindow> BuildNightWindowAsync(ObservationOptions options, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(options.Timezone);
        var fallbackSunsetLocal = targetDate.ToDateTime(new TimeOnly(18, 30), DateTimeKind.Unspecified);
        var fallbackSunriseLocal = targetDate.AddDays(1).ToDateTime(new TimeOnly(6, 0), DateTimeKind.Unspecified);

        var request = new SkyfieldNightPlanRequest
        {
            Date = targetDate.ToString("yyyy-MM-dd"),
            LocationName = options.LocationName,
            Latitude = options.Latitude,
            Longitude = options.Longitude,
            Timezone = options.Timezone,
            MinimumAltitudeDegrees = options.MinimumObjectAltitudeDegrees,
            StepMinutes = options.VisibilitySearchStepMinutes,
            Candidates = []
        };

        var source = "fallback";
        var sunsetLocal = fallbackSunsetLocal;
        var sunriseLocal = fallbackSunriseLocal;
        var response = await _sidecarClient.GetNightVisibilityPlanAsync(request, cancellationToken);
        if (response is not null && DateTimeOffset.TryParse(response.SunsetLocal, out var parsedSunset) && DateTimeOffset.TryParse(response.SunriseLocal, out var parsedSunrise))
        {
            sunsetLocal = parsedSunset.DateTime;
            sunriseLocal = parsedSunrise.DateTime;
            source = "skyfield-night-plan";
        }

        var sunsetOffset = new DateTimeOffset(sunsetLocal, tz.GetUtcOffset(sunsetLocal));
        var sunriseOffset = new DateTimeOffset(sunriseLocal, tz.GetUtcOffset(sunriseLocal));
        var window = new ObservationWindow
        {
            LocationName = options.LocationName,
            Latitude = options.Latitude,
            Longitude = options.Longitude,
            Timezone = options.Timezone,
            TargetDate = targetDate,
            SunsetLocal = sunsetOffset,
            SunriseLocal = sunriseOffset,
            NightWindowStartUtc = sunsetOffset.ToUniversalTime(),
            NightWindowEndUtc = sunriseOffset.ToUniversalTime(),
            CalculationSource = source
        };
        _logger.LogInformation("Observation window {Date} {Tz} sunset {Sunset} sunrise {Sunrise} utc [{Start}->{End}] source={Source}",
            targetDate, options.Timezone, window.SunsetLocal, window.SunriseLocal, window.NightWindowStartUtc, window.NightWindowEndUtc, source);
        return window;
    }
}
