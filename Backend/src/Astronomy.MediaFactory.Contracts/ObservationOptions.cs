namespace Astronomy.MediaFactory.Contracts;

public sealed class ObservationOptions
{
    public const string SectionName = "Observation";
    public double Latitude { get; set; } = 24.5854;
    public double Longitude { get; set; } = 73.7125;
    public string Timezone { get; set; } = "Asia/Kolkata";
    public string LocationName { get; set; } = "Udaipur, India";
    public bool AutoCalculateSunTimes { get; set; } = true;
    public int SkyOverviewMinutesAfterSunset { get; set; } = 90;
    public int DefaultObservationHour { get; set; } = 22;
    public double MinimumObjectAltitudeDegrees { get; set; } = 10;
    public double PreferredObjectAltitudeDegrees { get; set; } = 20;
    public int VisibilitySearchStepMinutes { get; set; } = 15;
    public bool PreferHighestAltitude { get; set; } = true;
}
