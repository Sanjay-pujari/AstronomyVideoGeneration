namespace Astronomy.MediaFactory.Contracts;

public sealed class TokenHealthOptions
{
    public const string SectionName = "TokenHealth";

    public bool Enabled { get; set; } = true;
    public bool CheckOnStartup { get; set; } = true;
    public bool CheckBeforePublish { get; set; } = true;
    public int RefreshBeforeExpiryDays { get; set; } = 7;
    public bool WriteHealthReport { get; set; } = true;
}
