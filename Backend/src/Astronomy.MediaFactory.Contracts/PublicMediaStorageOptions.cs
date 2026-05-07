namespace Astronomy.MediaFactory.Contracts;

public sealed class PublicMediaStorageOptions
{
    public const string SectionName = "PublicMediaStorage";

    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "AzureBlob";
    public string ConnectionString { get; set; } = "";
    public string ContainerName { get; set; } = "meta-media";
    public string BlobPrefix { get; set; } = "astronomy/reels";
    public bool UseSasUrl { get; set; } = true;
    public int SasExpiryHours { get; set; } = 24;
    public string PublicBaseUrl { get; set; } = "";
}
