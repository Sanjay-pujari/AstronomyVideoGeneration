namespace Astronomy.MediaFactory.Contracts;

public sealed class ObservationOptions
{
    public const string SectionName = "Observation";
    public string DefaultLocationName { get; set; } = "Udaipur, India";
    public double Latitude { get; set; } = 24.5854;
    public double Longitude { get; set; } = 73.7125;
    public string Timezone { get; set; } = "Asia/Kolkata";
    public int SkyOverviewMinutesAfterSunset { get; set; } = 90;
    public string DeepSkyPreferredLocalTime { get; set; } = "23:30";
}
