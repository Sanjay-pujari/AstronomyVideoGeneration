using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticFactValueRealizerRegressionTests
{
    private static readonly LanguageProfile English = LanguageProfileResolver.Resolve("en");

    [Fact]
    public void AstronomicalObjectArraysRealizeAsStableObjectNames()
    {
        var objects = ImmutableArray.Create(
            new AstronomicalObjectValue("Jupiter", "Planet", "Primary", null, []),
            new AstronomicalObjectValue("Venus", "Planet", "Secondary", null, []));

        var text = SemanticFactValueRealizer.Instance.RealizeCandidateValue(objects, capability: "AstronomicalObjects");

        Assert.Equal("Jupiter and Venus", text);
        Assert.DoesNotContain("ImmutableArray", text);
        Assert.DoesNotContain("Astronomy.MediaFactory.Infrastructure", text);
    }

    [Fact]
    public void StructuredScienceRealizesWithoutAnonymousObjectText()
    {
        var fact = new ResolvedSemanticFact("ApparentPairingScience", "ApparentPairingScience", new DomainScientificKnowledgeValue(null, "They appear close because of line-of-sight geometry from Earth.", null, null), null, "DomainScientificKnowledge", "test", "science", null, SemanticVerificationStatus.Verified, 1m, SemanticFactRequiredness.Required, null, null, "en", true);

        var realized = SemanticFactValueRealizer.Instance.Realize(fact, English);

        Assert.True(realized.Succeeded);
        Assert.Contains("line-of-sight", realized.SpeakableValue);
        Assert.DoesNotContain("{", realized.SpeakableValue);
        Assert.DoesNotContain("=", realized.SpeakableValue);
    }

    [Fact]
    public void DisplayLocationSuppressesInternalRegionCodeFallback()
    {
        var location = new ObservationLocationValue("Udaipur, Rajasthan", 24.58m, 73.68m, null, "Asia/Kolkata");

        var text = SemanticFactValueRealizer.Instance.RealizeCandidateValue(location, capability: "ObservationLocation");

        Assert.Equal("Udaipur, Rajasthan", text);
        Assert.DoesNotContain("india-udaipur", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredUnsupportedStructuredValueBlocksWithDiagnostic()
    {
        var fact = new ResolvedSemanticFact("UnsupportedRequired", "UnsupportedRequired", new { Internal = "value" }, null, "Unsupported", "test", "field", null, SemanticVerificationStatus.Verified, 1m, SemanticFactRequiredness.Required, null, null, "en", true);

        var realized = SemanticFactValueRealizer.Instance.Realize(fact, English);

        Assert.False(realized.Succeeded);
        Assert.True(realized.BlocksNarration);
        Assert.Equal("UnsupportedStructuredSemanticValue", realized.DiagnosticCode);
        Assert.Null(realized.SpeakableValue);
    }

    [Fact]
    public void OptionalUnsupportedStructuredValueOmitsSafely()
    {
        var fact = new ResolvedSemanticFact("UnsupportedOptional", "UnsupportedOptional", new { Internal = "value" }, null, "Unsupported", "test", "field", null, SemanticVerificationStatus.Verified, 1m, SemanticFactRequiredness.Optional, null, null, "en", true);

        var realized = SemanticFactValueRealizer.Instance.Realize(fact, English);

        Assert.False(realized.Succeeded);
        Assert.False(realized.BlocksNarration);
        Assert.Null(realized.SpeakableValue);
    }
}
