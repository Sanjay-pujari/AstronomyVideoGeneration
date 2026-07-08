using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Configuration;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Confidence;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Services;
using Astronomy.MediaFactory.Core.EditorialIntelligence.StyleGuide;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class EditorialIntelligenceTests
{
    [Fact]
    public void DisabledEditorialIntelligenceReturnsNoContract()
    {
        var service = CreateService(new EditorialIntelligenceOptions { Enabled = false });
        using var doc = JsonDocument.Parse("{}");
        Assert.Null(service.CreateContract("evt-1", "Event", "Conjunction", doc.RootElement));
    }

    [Fact]
    public void MissingMetadataProducesCautiousFallbackAndChannelEnding()
    {
        var service = CreateService();
        using var doc = JsonDocument.Parse("{}");
        var contract = service.CreateContract("evt-1", "Event", "Conjunction", doc.RootElement)!;
        Assert.Equal(ObservationConsistencyEngine.MissingMetadataFallback, contract.ObservationGuidance.Text);
        Assert.Contains("Until next time, keep looking up.", contract.ChannelEnding);
    }

    [Fact]
    public void VenusJupiterMetadataProducesWestEveningBrightnessGuidance()
    {
        var service = CreateService();
        using var doc = JsonDocument.Parse("""{"bestViewingTime":"About thirty minutes after sunset","direction":"the western horizon","brightness":"Venus will be the brighter object","relativePositions":"Jupiter appears nearby with a steadier golden glow","nakedEyeVisible":true}""");
        var contract = service.CreateContract("evt-1", "Venus Jupiter conjunction", "PlanetConjunction", doc.RootElement)!;
        Assert.Contains("after sunset", contract.ObservationGuidance.Text);
        Assert.Contains("western horizon", contract.ObservationGuidance.Text);
        Assert.Contains("brighter object", contract.ObservationGuidance.Text);
        Assert.NotEmpty(contract.ConfidenceCues);
    }

    [Fact]
    public void ProhibitedPhraseListExists()
    {
        Assert.Contains("mind-blowing", AstroPulseStyleGuide.VocabularyRules.ProhibitedPhrases);
    }

    [Fact]
    public async Task NarrationPromptReceivesContractWhenServiceIsProvided()
    {
        var request = new NarrationPreviewRequest(null, "PlanetConjunction", "Venus Jupiter conjunction", null, "en", "US-CA", null, JsonDocument.Parse("""{"bestViewingTime":"After sunset","direction":"western horizon","brightness":"Venus is the brighter object"}""").RootElement, true);
        var narration = new NarrationGenerationService(null!, CreateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<NarrationGenerationService>.Instance);
        var response = await narration.GeneratePreviewAsync(request, CancellationToken.None);
        Assert.NotNull(response.EditorialIntelligenceContract);
        Assert.Contains("Observation instructions must come from the EditorialIntelligenceContract", response.NarrationPromptEditorialGuidance);
    }

    private static EditorialIntelligenceService CreateService(EditorialIntelligenceOptions? options = null)
        => new(Options.Create(options ?? new EditorialIntelligenceOptions()), new ObservationConsistencyEngine(), new ObservationConfidenceEngine());
}
