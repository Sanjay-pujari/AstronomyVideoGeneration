using Astronomy.MediaFactory.Rendering;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SsmlBuilderTests
{
    private readonly SsmlBuilder _sut = new();

    [Fact]
    public void BuildSsml_HasValidRootAndVoice()
    {
        var ssml = _sut.BuildSsml("Hello Moon.", "en-US-AriaNeural");
        Assert.Contains("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\">", ssml);
        Assert.Contains("<voice name=\"en-US-AriaNeural\">", ssml);
    }

    [Fact]
    public void BuildSsml_EscapesXmlCharacters()
    {
        var ssml = _sut.BuildSsml("A & B < C > D \"quote\" 'single'", "en-US-AriaNeural");
        Assert.Contains("&amp;", ssml);
        Assert.Contains("&lt;", ssml);
        Assert.Contains("&gt;", ssml);
        Assert.Contains("&quot;", ssml);
        Assert.Contains("&#39;", ssml);
    }

    [Fact]
    public void BuildSsml_InsertsConfiguredPauses()
    {
        var ssml = _sut.BuildSsml("Moon, bright.\n\nJupiter rises:", "en-US-AriaNeural");
        Assert.Contains("<break time=\"300ms\"/>", ssml);
        Assert.Contains("<break time=\"450ms\"/>", ssml);
        Assert.Contains("<break time=\"900ms\"/>", ssml);
    }

    [Fact]
    public void BuildSsml_AddsAstronomyEmphasis_WithoutDoubleWrapping()
    {
        var ssml = _sut.BuildSsml("Look up at the Moon tonight. Jupiter is next to a nebula.", "en-US-AriaNeural");
        Assert.Contains("<emphasis level=\"strong\">Look up</emphasis>", ssml);
        Assert.Contains("<emphasis level=\"strong\">Moon</emphasis>", ssml);
        Assert.Contains("<emphasis level=\"moderate\">Jupiter</emphasis>", ssml);
        Assert.Contains("<emphasis level=\"moderate\">tonight</emphasis>", ssml);
        Assert.Contains("<emphasis level=\"moderate\">nebula</emphasis>", ssml);
        Assert.Equal(1, CountOccurrences(ssml, "<emphasis level=\"moderate\">Jupiter</emphasis>"));
    }

    [Fact]
    public void BuildSsml_ShortsProfile_UsesShorterPauses()
    {
        var ssml = _sut.BuildSsml("Mars, visible.\n\nOrion!", "en-US-AriaNeural", SsmlNarrationProfile.Shorts);
        Assert.Contains("prosody rate=\"92%\" pitch=\"+3%\"", ssml);
        Assert.Contains("<break time=\"300ms\"/>", ssml);
        Assert.Contains("<break time=\"450ms\"/>", ssml);
        Assert.Contains("<break time=\"600ms\"/>", ssml);
    }

    private static int CountOccurrences(string source, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
