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
    public OverviewOptions Overview { get; set; } = new();
}

public sealed class OverviewOptions
{
    public string Mode { get; set; } = "Hybrid";
    public string DefaultHookStrategy { get; set; } = "AttractiveObject";
    public bool EnablePolarisOrientation { get; set; } = true;
    public int PolarisOrientationDurationSeconds { get; set; } = 5;
    public int AttractiveOverviewDurationSeconds { get; set; } = 6;
}
