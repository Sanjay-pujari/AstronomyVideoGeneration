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

        var keyVaultUri = Environment.GetEnvironmentVariable("KeyVault__VaultUri")
                          ?? Environment.GetEnvironmentVariable("KEYVAULT__VAULTURI");

        if (!string.IsNullOrWhiteSpace(keyVaultUri) && Uri.TryCreate(keyVaultUri, UriKind.Absolute, out var vaultUri))
        {
            builder.AddAzureKeyVault(vaultUri, new DefaultAzureCredential());
        }

        return builder;
    }
}
