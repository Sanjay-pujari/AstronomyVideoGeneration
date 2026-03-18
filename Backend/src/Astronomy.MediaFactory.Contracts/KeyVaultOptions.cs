namespace Astronomy.MediaFactory.Contracts;

public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    public string? VaultUri { get; set; }
    public string? ManagedIdentityClientId { get; set; }
}
