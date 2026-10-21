using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

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
            INarrationPerformer performer = options.IsOllama
                ? sp.GetRequiredService<OllamaNarrationPerformer>()
                : sp.GetRequiredService<AzureOpenAiContentGenerationService>();
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("RC2.NarrationRouting").LogInformation(
                "RC2_AI_PROVIDER_SELECTED phase={PhaseNumber} provider={Provider} model={Model} digest={ModelDigest} invocationType={InvocationType} correlationId={CorrelationId} configurationKey={ConfigurationKey} selectionReason={SelectionReason}",
                7, performer.ProviderName, performer.ModelOrDeployment, "NotObserved", "PerformerResolution",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "None", "NarrationAI:Provider",
                configuration["NarrationAI:Provider"] is null ? "DefaultAzureProvider_NoExplicitSelection" : "ExplicitConfiguration");
            return performer;
        });
        return services;
    }
}
