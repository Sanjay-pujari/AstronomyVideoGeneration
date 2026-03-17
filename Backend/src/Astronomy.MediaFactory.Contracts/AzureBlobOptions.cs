namespace Astronomy.MediaFactory.Contracts;

public sealed class AzureBlobOptions
{
    public const string SectionName = "AzureBlob";
    public string ConnectionString { get; set; } = "";
    public string ContainerName { get; set; } = "astronomy-videos";
}
