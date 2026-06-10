using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class SkyfieldSidecarAccuracyProvider(
    HttpClient httpClient,
    IOptions<SkyfieldSidecarOptions> options,
    ILogger<SkyfieldSidecarAccuracyProvider> logger) : ISkyfieldAccuracyProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SkyfieldSidecarOptions _options = options.Value;

    public async Task<SkyfieldAccuracyResult> ComputeYearlyAccuracyAsync(int year, RegionScheduleOptions region, IReadOnlyList<AstronomyEventPreviewItem> events, CancellationToken cancellationToken)
    {
        var result = new SkyfieldAccuracyResult();
        if (!_options.Enabled)
        {
            result.Warnings.Add("Skyfield sidecar is disabled; yearly event verification remains approximate/manual-review only.");
            return result;
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out _))
        {
            result.Warnings.Add("SkyfieldSidecar:BaseUrl is invalid; yearly event verification remains approximate/manual-review only.");
            return result;
        }

        try
        {
            var request = new YearlyAccuracySidecarRequest
            {
                Year = year,
                Latitude = region.Latitude,
                Longitude = region.Longitude,
                Timezone = string.IsNullOrWhiteSpace(region.Timezone) ? "UTC" : region.Timezone,
                Modes = ["moonPhases", "planetPairings", "meteorMoonlight"],
                MeteorPeaks = events
                    .Where(e => e.EventType.Contains("Meteor", StringComparison.OrdinalIgnoreCase))
                    .Select(e => new YearlyAccuracyMeteorPeakRequest { EventId = e.EventId, PeakUtc = ToIsoUtc(e.PeakUtc) })
                    .ToList()
            };

            using var response = await httpClient.PostAsJsonAsync("/events/yearly-accuracy", request, JsonOptions, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AddFailureWarning(result, $"Skyfield sidecar yearly accuracy returned HTTP {(int)response.StatusCode} ({response.StatusCode}); keeping approximate/manual statuses where applicable. {TrimWarning(responseBody)}");
                return result;
            }

            var sidecar = JsonSerializer.Deserialize<YearlyAccuracySidecarResponse>(responseBody, JsonOptions);
            if (sidecar is null)
            {
                AddFailureWarning(result, "Skyfield sidecar yearly accuracy returned no usable JSON; keeping approximate/manual statuses where applicable.");
                return result;
            }

            result.MoonPhases.AddRange(sidecar.MoonPhases.Select(m => new SkyfieldMoonPhase
            {
                Phase = m.PhaseType,
                PeakUtc = m.PeakUtc,
                LocalPeakTime = m.LocalPeakTime
            }));
            result.PlanetPairings.AddRange(sidecar.PlanetPairings.Where(p => p.PrimaryObjects.Count >= 2).Select(p => new SkyfieldPlanetPairing
            {
                PrimaryObject = p.PrimaryObjects[0],
                SecondaryObject = p.PrimaryObjects[1],
                PeakUtc = p.PeakUtc,
                AngularSeparationDegrees = p.AngularSeparationDegrees,
                ObjectAltitudesDegrees = new Dictionary<string, double>(p.ObjectAltitudesDegrees, StringComparer.OrdinalIgnoreCase),
                SunAltitudeDegrees = p.SunAltitudeDegrees,
                BestViewingLocalTime = string.IsNullOrWhiteSpace(p.BestViewingLocalTime) ? p.LocalPeakTime : p.BestViewingLocalTime,
                SkyDirectionHint = p.SkyDirectionHint,
                Quality = p.Quality,
                InvolvesBrightPlanet = p.PrimaryObjects.Any(o => o.Equals("Venus", StringComparison.OrdinalIgnoreCase) || o.Equals("Jupiter", StringComparison.OrdinalIgnoreCase))
            }));
            result.MeteorMoonlight.AddRange(sidecar.MeteorMoonlight.Select(m => new SkyfieldMeteorMoonlight
            {
                EventId = m.EventId,
                MoonIlluminationPercent = m.MoonIlluminationPercent,
                MoonInterference = m.MoonInterference,
                VisibilityScoreAdjustment = m.MoonInterference switch
                {
                    "Low" => 5,
                    "Medium" => -7,
                    "High" => -15,
                    _ => 0
                },
                BestViewingWindowLocal = m.BestViewingWindowLocal,
                RadiantVisibilityNote = "Moonlight estimate computed by Skyfield at the provided meteor peak instant."
            }));
            result.Warnings.AddRange(sidecar.Warnings);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Skyfield sidecar yearly accuracy computation unavailable.");
            AddFailureWarning(result, $"Skyfield sidecar yearly accuracy unavailable; keeping approximate/manual statuses where applicable. {ex.Message}");
            return result;
        }
    }

    private void AddFailureWarning(SkyfieldAccuracyResult result, string warning)
    {
        result.Warnings.Add(_options.FallbackOnFailure
            ? warning
            : $"{warning} FallbackOnFailure=false, but verification endpoint will not promote events without Skyfield results.");
    }

    private static string ToIsoUtc(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private static string TrimWarning(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "No response body details were provided.";
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 240 ? text : text[..240] + "…";
    }

    private sealed class YearlyAccuracySidecarRequest
    {
        public int Year { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Timezone { get; set; } = "UTC";
        public List<string> Modes { get; set; } = [];
        public List<YearlyAccuracyMeteorPeakRequest> MeteorPeaks { get; set; } = [];
    }

    private sealed class YearlyAccuracyMeteorPeakRequest
    {
        public string EventId { get; set; } = string.Empty;
        public string PeakUtc { get; set; } = string.Empty;
    }

    private sealed class YearlyAccuracySidecarResponse
    {
        public int Year { get; set; }
        public List<YearlyMoonPhaseResponse> MoonPhases { get; set; } = [];
        public List<YearlyPlanetPairingResponse> PlanetPairings { get; set; } = [];
        public List<YearlyMeteorMoonlightResponse> MeteorMoonlight { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }

    private sealed class YearlyMoonPhaseResponse
    {
        public string PhaseType { get; set; } = string.Empty;
        public DateTimeOffset PeakUtc { get; set; }
        public string LocalPeakTime { get; set; } = string.Empty;
        public double IlluminationPercent { get; set; }
    }

    private sealed class YearlyPlanetPairingResponse
    {
        public string EventType { get; set; } = string.Empty;
        public List<string> PrimaryObjects { get; set; } = [];
        public DateTimeOffset PeakUtc { get; set; }
        public string LocalPeakTime { get; set; } = string.Empty;
        public string BestViewingLocalTime { get; set; } = string.Empty;
        public double AngularSeparationDegrees { get; set; }
        public Dictionary<string, double> ObjectAltitudesDegrees { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public double SunAltitudeDegrees { get; set; }
        public string SkyDirectionHint { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
    }

    private sealed class YearlyMeteorMoonlightResponse
    {
        public string EventId { get; set; } = string.Empty;
        public double MoonIlluminationPercent { get; set; }
        public string MoonInterference { get; set; } = string.Empty;
        public string BestViewingWindowLocal { get; set; } = string.Empty;
    }
}
