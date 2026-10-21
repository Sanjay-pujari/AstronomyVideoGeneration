using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Configuration;

internal sealed class AzureOpenAiOptionsValidator(IOptions<NarrationAIOptions> narration) : IValidateOptions<AzureOpenAiOptions>
{
    public ValidateOptionsResult Validate(string? name, AzureOpenAiOptions options)
    {
        if (narration.Value.IsOllama)
            return ValidateOptionsResult.Success;

        var failures = AzureConfigurationValidation.ValidateOpenAi(options, requireConfiguration: false).ToArray();
        return failures.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
