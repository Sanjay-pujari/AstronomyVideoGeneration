using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.NarrationCertification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class GroundedOllamaCertificationTests
{
    private const string Model = "qwen2.5:7b-instruct-q4_K_M";
    private const string Digest = "845dbda0ea48ed749caafd9e6037047aa19acfcfd82e704d7ca97d631a0b697e";
    private const string English = "Orion is a constellation, both an official sky region and a recognizable pattern. Its stars appear near one another in our view, but they are not physically adjacent at one distance. The Orion Nebula, known as M42, is an emission nebula, not a galaxy. These distinctions help us describe what the sky pattern actually represents without assuming that its stars form one nearby group.";
    private const string Hindi = "ओरायन एक तारामंडल है, जो आकाश का आधिकारिक क्षेत्र और पहचानने योग्य आकृति भी है। इसके तारे हमें पास दिखाई देते हैं, लेकिन वे भौतिक रूप से एक ही दूरी पर पास-पास स्थित नहीं होते। ओरायन नीहारिका को M42 कहा जाता है। यह एक उत्सर्जन नीहारिका है, आकाशगंगा नहीं। इन भेदों को ध्यान में रखने से हम दिखने वाली आकृति और तारों के वास्तविक संबंधों को अलग समझ सकते हैं।";

    [Theory]
    [InlineData("en", English)]
    [InlineData("hi", Hindi)]
    public async Task ProductionComposerAndActualDotNetProvider_ForwardGroundedEvidenceThroughMockedTransport(string language, string prose)
    {
        var fixture = GroundedNarrationCases.Create(language);
        var request = GroundedNarrationCases.BuildRequest(fixture);
        var settings = new Dictionary<string, string?>
        {
            ["NarrationAI:Provider"] = "Ollama", ["NarrationAI:Ollama:Model"] = Model,
            ["NarrationAI:Ollama:ExpectedModelDigest"] = Digest, ["NarrationAI:Ollama:MaxRetries"] = "0"
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNarrationAI(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        var handler = new EvidenceHandler(fixture, prose);
        var counters = new TransportCounters();
        services.AddHttpClient<OllamaNarrationPerformer>().ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddHttpMessageHandler(() => new LocalOnlyTransportGuard(new Uri("http://127.0.0.1:11434"), counters));
        services.AddTransient<AzureOpenAiContentGenerationService>(_ => throw new InvalidOperationException("Azure must not initialize"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var performer = scope.ServiceProvider.GetRequiredService<INarrationPerformer>();
        Assert.IsType<OllamaNarrationPerformer>(performer);
        var response = await performer.InvokeAsync(request, CancellationToken.None);
        var validation = GroundedNarrationCases.Validate(fixture, response.Response);
        Assert.True(validation.AutomatedGatesPassed, string.Join("; ", validation.Errors));
        Assert.True(validation.RequiresScientificReview);
        Assert.Equal(1, counters.ChatAttempts);
        Assert.Equal(0, counters.AzureResolutionAttempts);
        Assert.Equal(0, counters.BlockedRequests);
        Assert.Equal(2, handler.InventoryCalls);
    }

    [Fact]
    public void HindiFixture_HasIndependentPlanAndLanguageAuthority()
    {
        var en = GroundedNarrationCases.Create("en");
        var hi = GroundedNarrationCases.Create("hi");
        Assert.NotEqual(en.PlanId, hi.PlanId);
        Assert.NotEqual(en.EvidenceSha256, hi.EvidenceSha256);
        Assert.Equal("hi", hi.AuthorityLanguage);
        Assert.Contains("ConstellationFamily.cs", hi.Source);
        Assert.Throws<InvalidOperationException>(() => GroundedNarrationCases.BuildRequest(hi with { AuthorityLanguage = "en" }));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void MissingFacts_BlockBeforeProviderInvocation(string language)
    {
        var fixture = GroundedNarrationCases.Create(language);
        Assert.Throws<InvalidOperationException>(() => GroundedNarrationCases.BuildRequest(fixture with { Facts = [] }));
        Assert.Throws<InvalidOperationException>(() => GroundedNarrationCases.BuildRequest(fixture with { Source = "" }));
    }

    [Theory]
    [InlineData("en", English, " It is 1000 light years away.")]
    [InlineData("hi", Hindi, " यह १००० प्रकाशवर्ष दूर है।")]
    public void UnsupportedDistance_IsRejected(string language, string valid, string invented)
    {
        var result = GroundedNarrationCases.Validate(GroundedNarrationCases.Create(language), Response(valid + invented));
        Assert.False(result.AutomatedGatesPassed);
        Assert.Contains(result.Errors, e => e.Contains("quantitative"));
    }

    [Fact]
    public void HindiConstellationGalaxyConfusion_IsRejected()
    {
        var result = GroundedNarrationCases.Validate(GroundedNarrationCases.Create("hi"), Response(Hindi.Replace("तारामंडल", "आकाशगंगा")));
        Assert.False(result.AutomatedGatesPassed);
        Assert.Contains(result.Errors, e => e.Contains("classification"));
    }

    [Fact]
    public void MissingNebulaFact_IsNotDeclaredCoveredByModelOutput()
    {
        var result = GroundedNarrationCases.Validate(GroundedNarrationCases.Create("en"), Response(English.Replace("M42", "the cloud")));
        Assert.False(result.AutomatedGatesPassed);
        Assert.Contains(result.Errors, e => e.Contains("M42"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not JSON")]
    [InlineData("{\"variant\":\"Long\",\"scenes\":[]}")]
    public void InvalidContract_IsRejected(string response)
        => Assert.False(GroundedNarrationCases.Validate(GroundedNarrationCases.Create("en"), response).AutomatedGatesPassed);

    [Fact]
    public void TruncatedSentence_IsRejected()
        => Assert.Contains(GroundedNarrationCases.Validate(GroundedNarrationCases.Create("en"), Response(English.TrimEnd('.'))).Errors, e => e.Contains("Incomplete"));

    [Theory]
    [InlineData("en", Hindi)]
    [InlineData("hi", English)]
    [InlineData("en", "")]
    public void WrongLanguageOrEmptyNarration_IsRejected(string language, string text)
        => Assert.False(LanguageOutputValidator.Validate(text, LanguageProfileResolver.Resolve(language)).Passed);

    [Fact]
    public void HindiTerminologyPolicy_DistinguishesObjectCategories()
    {
        var profile = LanguageProfileResolver.Resolve("hi");
        Assert.Equal("तारामंडल", profile.Terminology["constellation"]);
        Assert.Equal("आकाशगंगा", profile.Terminology["galaxy"]);
        Assert.Equal("नीहारिका", profile.Terminology["nebula"]);
    }

    private static string Response(string text) => JsonSerializer.Serialize(new { variant = "Short", scenes = new[] { new { sceneNumber = 1, narrationText = text } } });

    private sealed class EvidenceHandler(GroundedNarrationCase fixture, string prose) : HttpMessageHandler
    {
        public int InventoryCalls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get)
            {
                InventoryCalls++;
                return Ok(new { models = new[] { new { name = Model, digest = Digest } } });
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            foreach (var fact in fixture.Facts) Assert.Contains(fact.Text, prompt);
            Assert.Contains(fixture.Language == "hi" ? "hi-IN" : "en-US", prompt);
            Assert.Contains("do not invent distances", prompt);
            Assert.Equal(768, body.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
            return Ok(new { model = Model, done = true, done_reason = "stop", message = new { role = "assistant", content = Response(prose) } });
        }
        private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
