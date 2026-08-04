using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7CanonicalFieldPathDiagnosticTests
{
    [Fact]
    public void RejectionDiagnosticPreservesExactRawPathAndContext()
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

        var entries = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(JsonDocument.Parse).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal("before", entries[0].RootElement.GetProperty("outcome").GetString());
        Assert.Equal("rejected", entries[1].RootElement.GetProperty("outcome").GetString());
        Assert.All(entries, entry =>
        {
            Assert.Equal("cultureAndMythology.indian (Hindu).summary", entry.RootElement.GetProperty("rawPath").GetString());
            Assert.Equal("cultureAndMythology.indian (Hindu).summary", entry.RootElement.GetProperty("normalizedPath").GetString());
            Assert.Equal("test-caller", entry.RootElement.GetProperty("caller").GetString());
            Assert.Equal("production-orion", entry.RootElement.GetProperty("payloadSource").GetString());
            Assert.Equal("indian (Hindu)", entry.RootElement.GetProperty("traditionName").GetString());
            Assert.Equal("summary", entry.RootElement.GetProperty("fieldName").GetString());
        });
        foreach (var entry in entries) entry.Dispose();
    }
}
