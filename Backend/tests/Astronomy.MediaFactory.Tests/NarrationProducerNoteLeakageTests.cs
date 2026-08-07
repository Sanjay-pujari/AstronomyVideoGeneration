using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationProducerNoteLeakageTests
{
    [Fact]
    public void Anchor_is_not_producer_note_leakage()
    {
        var leaks = NarrationGeneratorV5.DetectProducerNotesLeakage(EmptyContract,
            "Bright Betelgeuse and Rigel anchor the figure, making Orion easy to recognize.");

        Assert.Empty(leaks);
    }

    [Fact]
    public void Explicit_producer_note_label_is_leakage()
    {
        var leaks = NarrationGeneratorV5.DetectProducerNotesLeakage(EmptyContract,
            "Producer notes: anchor the scene around Orion's Belt.");

        Assert.Contains("producer notes", leaks, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copied_producer_note_sentence_is_leakage()
    {
        const string copied = "Introduce Orion through its three unmistakable aligned Belt stars";
        var contract = new ProducerNotesContract("v1", "en",
            [new("scene-1", 1, copied + ".", "Explain recognition", "Build confidence", [], "Look south", "Curious", "Continue naturally", "long", false)]);

        var leaks = NarrationGeneratorV5.DetectProducerNotesLeakage(contract, copied + ".");

        Assert.Contains(copied, leaks, StringComparer.OrdinalIgnoreCase);
    }

    private static ProducerNotesContract EmptyContract { get; } = new("v1", "en", []);
}
