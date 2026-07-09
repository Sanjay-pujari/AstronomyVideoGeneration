using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Directors;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class DocumentaryStyleDirectorTests
{
    [Fact]
    public async Task BuildContract_creates_scene_styles_and_rhythm()
    {
        var contract = await CreateDirector().BuildAsync(Editorial(), Storyboard(), Briefs(), CancellationToken.None);

        Assert.Equal(DocumentaryStyleDirector.Version, contract.Version);
        Assert.Equal("Observe", contract.DocumentaryRhythm.Observe);
        Assert.Equal(2, contract.SceneStyles.Count);
    }

    [Fact]
    public void FactTransformation_converts_reusable_fact_shapes()
    {
        var transformer = new DocumentaryFactTransformer();

        Assert.Contains("On the evening of June 9", transformer.Transform(new NarrationFactV5("EventDate", "2026-06-09")));
        Assert.Contains("one and a half degrees", transformer.Transform(new NarrationFactV5("RelativePositions", "1.63")));
        Assert.Contains("ordinary viewing language", transformer.Transform(new NarrationFactV5("ViewingWindow", "shortly after sunset")));
    }

    [Fact]
    public void TransitionSelection_uses_semantic_templates()
    {
        var transition = new DocumentaryTransitionLibrary().Select("Science", "Observation");

        Assert.Contains("step outside", transition);
    }

    [Fact]
    public void VocabularySelection_returns_preferred_and_forbidden_terms()
    {
        var vocabulary = new DocumentaryVocabulary();

        Assert.Contains("As twilight deepens...", vocabulary.SelectPreferred("Observation"));
        Assert.Contains("Prompt", vocabulary.ForbiddenExpressions);
    }

    [Fact]
    public async Task NoPromptLeakage_forbids_prompt_terms_in_scene_styles()
    {
        var contract = await CreateDirector().BuildAsync(Editorial(), Storyboard(), Briefs(), CancellationToken.None);

        Assert.All(contract.SceneStyles, style => Assert.Contains("Prompt", style.ForbiddenVocabulary));
        Assert.DoesNotContain(contract.SceneStyles, style => style.EditorialObjective.Contains("Prompt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoAstronomyLeakage_uses_only_contract_facts()
    {
        var contract = await CreateDirector().BuildAsync(Editorial(), Storyboard(), Briefs(), CancellationToken.None);

        Assert.DoesNotContain(contract.SceneStyles.SelectMany(s => s.FactTransformations), value => value.Contains("ProductionEventIntelligence", StringComparison.OrdinalIgnoreCase));
    }

    private static DocumentaryStyleDirector CreateDirector() => new(new DocumentaryVocabulary(), new DocumentaryTransitionLibrary(), new DocumentaryFactTransformer(), NullLogger<DocumentaryStyleDirector>.Instance);

    private static NarrationBriefsV5 Briefs() => new("AstroPulse-NarrationBriefs-v1", "test", "en", [
        new NarrationBriefV5("scene-001", "Hook", 1, "Introduce the sky event.", "The viewer should know why it matters.", [new NarrationFactV5("EventDate", "2026-06-09")], [], [], "Discovery", "calm", "natural", "short", false, "Do not include ending."),
        new NarrationBriefV5("scene-002", "Observation", 2, "Give practical viewing guidance.", "The viewer should know where to look.", [new NarrationFactV5("ViewingWindow", "shortly after sunset"), new NarrationFactV5("Direction", "western horizon")], [], [], "Close", "calm", "measured", "short", true, "Include ending.")
    ]);

    private static CreativeStoryboard Storyboard() => new("AstroPulse-CreativeStoryboard-v1", "test", "conjunction", "Test Event", "en", "US", "clarity", "Hook → Observation", "calm", [], []);

    private static EditorialContract Editorial() => new("AstroPulse-EditorialContract-v1", "test", "style", "CalmDocumentary", "conjunction", "Test Event", "en", "US", EmptyEventFacts(), EmptyObservationFacts(), new StoryGraphSummary("StoryGraph-v1", "Hook → Observation", 2, []), [], [], [], [], [], [], new EditorialChannelIdentity("Astro Pulse", "Until next time, keep looking up."), []);

    private static EditorialContractEventFacts EmptyEventFacts() => new(Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact());
    private static EditorialContractObservationFacts EmptyObservationFacts() => new(Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact(), Fact());
    private static EditorialContractFact Fact() => new(null, true, null);
}
