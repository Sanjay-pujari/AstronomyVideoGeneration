using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7CanonicalFieldPathDiagnosticTests
{
    [Fact]
    public void CanonicalizationIsSilentAndRejectionRemainsGoverned()
    {
        var prior = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            var exception = Assert.Throws<ArgumentException>(() =>
                Phase7CanonicalFieldPathDiagnostics.Canonicalize("cultureAndMythology.indian (Hindu).summary",
                    "test-caller", "production-orion", "phase7.culture.v1", "indian (Hindu)", "summary"));

            Assert.Equal("value", exception.ParamName);
        }
        finally
        {
            Console.SetError(prior);
        }

        Assert.Equal(string.Empty, output.ToString());
    }
}
