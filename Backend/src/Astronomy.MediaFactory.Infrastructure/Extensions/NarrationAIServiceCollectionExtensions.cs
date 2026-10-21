using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Extensions;

public static class NarrationAIServiceCollectionExtensions
{
    public static IServiceCollection AddNarrationAI(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NarrationAIOptions>()
            .Bind(configuration.GetSection(NarrationAIOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<NarrationAIOptions>, NarrationAIOptionsValidator>();
        services.AddOptions<AzureOpenAiOptions>()
            .Bind(configuration.GetSection(AzureOpenAiOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AzureOpenAiOptions>, AzureOpenAiOptionsValidator>();

        services.AddHttpClient<AzureOpenAiContentGenerationService>();
        services.AddSingleton<OllamaNarrationConcurrencyLimiter>();
        services.AddHttpClient<OllamaNarrationPerformer>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<INarrationPerformer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NarrationAIOptions>>().Value;
            return options.IsOllama
                ? sp.GetRequiredService<OllamaNarrationPerformer>()
                : sp.GetRequiredService<AzureOpenAiContentGenerationService>();
        });
        return services;
    }
}
