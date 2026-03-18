using Astronomy.MediaFactory.Contracts;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Astronomy.MediaFactory.Infrastructure.Configuration;

public static class SecureConfigurationExtensions
{
    public static IConfigurationBuilder AddMediaFactorySecureConfiguration(this IConfigurationBuilder builder, IHostEnvironment environment)
    {
        builder.AddEnvironmentVariables();

        var bootstrapConfiguration = builder.Build();
        var keyVault = bootstrapConfiguration.GetSection(KeyVaultOptions.SectionName).Get<KeyVaultOptions>() ?? new KeyVaultOptions();
        var keyVaultUri = keyVault.VaultUri
                          ?? Environment.GetEnvironmentVariable("KeyVault__VaultUri")
                          ?? Environment.GetEnvironmentVariable("KEYVAULT__VAULTURI");

        if (!string.IsNullOrWhiteSpace(keyVaultUri) && Uri.TryCreate(keyVaultUri, UriKind.Absolute, out var vaultUri))
        {
            var credential = CreateCredential(keyVault.ManagedIdentityClientId);
            builder.AddAzureKeyVault(vaultUri, credential);
        }

        return builder;
    }

    private static DefaultAzureCredential CreateCredential(string? managedIdentityClientId)
        => new(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(managedIdentityClientId) ? null : managedIdentityClientId.Trim()
        });
}
