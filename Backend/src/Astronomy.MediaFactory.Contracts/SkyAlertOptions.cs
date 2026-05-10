namespace Astronomy.MediaFactory.Contracts;

public sealed class AlertsOptions
{
    public const string SectionName = "Alerts";
    public bool Enabled { get; set; } = true;
    public int GenerateEveryMinutes { get; set; } = 60;
    public int SendEveryMinutes { get; set; } = 15;
    public double DefaultMinimumEventScore { get; set; } = 0.65;
    public int MaxAlertsPerSubscriberPerDay { get; set; } = 3;
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public bool Enabled { get; set; } = false;
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "AstroPulse";
}
