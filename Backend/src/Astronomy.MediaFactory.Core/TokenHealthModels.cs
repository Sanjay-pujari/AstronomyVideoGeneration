namespace Astronomy.MediaFactory.Core;

public sealed class TokenHealthResult
{
    public string Platform { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public bool IsValid { get; set; }
    public bool CanRefresh { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
