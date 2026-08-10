using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

/// <summary>
/// Typed adapter between current semantic authority and the retained AstroPulse Gallery planner.
/// It intentionally has no dependency on the abandoned square-gallery role taxonomy.
/// </summary>
internal static class MatureGalleryCandidateGenerator
{
    internal sealed record MatureGallerySemanticContext(
        string EventType, string EventFamily, string Title, string ShortTitle, string Language,
        IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects,
        string? Direction, string? LocalPeakTime, string? BestViewingWindowLocal,
        IReadOnlyList<Phase13GallerySemanticHydrator.GallerySemanticItem> PublicSemantics,
        IReadOnlyList<Phase13GallerySemanticHydrator.GallerySemanticItem> VisualPlanningSemantics);

    internal static IReadOnlyList<Phase13GalleryAuthority.MatureGalleryTopicPlan> BuildPlans(
        Phase13GallerySemanticHydrator.HydrationResult hydration)
    {
        var intelligence = hydration.EventAuthority.Intelligence;
        var semantic = new MatureGallerySemanticContext(
            hydration.EventAuthority.EventIdentity.EventType,
            hydration.EventAuthority.EventIdentity.EventFamily,
            hydration.EventAuthority.EventIdentity.Title,
            intelligence.ShortTitle,
            hydration.EventAuthority.Metadata.Language,
            hydration.Context.PrimaryObjects,
            hydration.Context.SecondaryObjects,
            intelligence.SkyDirectionHint,
            intelligence.LocalPeakTime,
            intelligence.BestViewingWindowLocal,
            hydration.Context.AllItems.Where(x => x.IsPublicationEligible).ToArray(),
            hydration.Context.AllItems.Where(x => x.IsVisualPlanningEligible).ToArray());

        var objects = semantic.PrimaryObjects.Concat(semantic.SecondaryObjects)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var galleryContext = new AstroPulseGalleryService.GalleryContext(
            semantic.EventType, semantic.Title, intelligence.StoryTheme ?? string.Empty,
            intelligence.VisualTheme ?? string.Empty, intelligence.EventDate?.ToString("O") ?? string.Empty,
            semantic.LocalPeakTime ?? string.Empty, semantic.Direction ?? string.Empty,
            semantic.Language, semantic.Language, "UTC",
            EventObjectContextBuilder.FromJsonValues(semantic.EventType, semantic.Title, objects,
                semantic.PrimaryObjects, semantic.SecondaryObjects, intelligence.RequiredVisualObjects ?? []),
            intelligence.ForbiddenTerms, semantic.Title, semantic.EventFamily,
            LocalizedEventTitle: semantic.Title, TitleSource: Phase13GallerySemanticHydrator.Phase2Authority);

        // GalleryContentResolver + BuildTopics remain the actual six-page role planner.
        var contract = AstroPulseGalleryService.GalleryContentResolver.Resolve(galleryContext);
        var topics = AstroPulseGalleryService.BuildTopics(contract);
        if (topics.Count != 6) return [];

        return topics.Select(topic => CreatePlan(topic, semantic)).ToArray();
    }

    private static Phase13GalleryAuthority.MatureGalleryTopicPlan CreatePlan(
        AstroPulseGalleryService.GalleryTopic topic, MatureGallerySemanticContext context)
    {
        var primary = context.PrimaryObjects.FirstOrDefault() ?? context.Title;
        var publicFacts = context.PublicSemantics.Select(x => x.Text)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var shortTitle = string.IsNullOrWhiteSpace(context.ShortTitle) ? context.Title : context.ShortTitle;
        var (treatment, reason, detail, facts) = topic.Purpose switch
        {
            "Opening view" => ("Event identity", (string?)null, shortTitle, new[] { primary }),
            "What happens" => ("Event overview", (string?)null, publicFacts.FirstOrDefault() ?? shortTitle,
                publicFacts.Take(2).ToArray()),
            "Where to look" when !string.IsNullOrWhiteSpace(context.Direction) => ("Where to look", (string?)null,
                context.Direction!, new[] { primary }),
            "Where to look" => ("How to recognize " + primary,
                "No certified direction is available; use event identity and object recognition.", shortTitle,
                context.PrimaryObjects.Concat(context.SecondaryObjects).Take(3).ToArray()),
            "When to observe" when !string.IsNullOrWhiteSpace(context.BestViewingWindowLocal) => ("When to observe", (string?)null,
                context.BestViewingWindowLocal!, Array.Empty<string>()),
            "When to observe" when !string.IsNullOrWhiteSpace(context.LocalPeakTime) => ("When to observe", (string?)null,
                context.LocalPeakTime!, Array.Empty<string>()),
            "When to observe" => ("Evergreen observing context",
                "No certified local time or viewing window is available; avoid inventing timing.", shortTitle,
                new[] { primary }),
            "Key objects" => ("Key objects", (string?)null,
                string.Join(", ", context.SecondaryObjects.DefaultIfEmpty(primary)),
                context.SecondaryObjects.DefaultIfEmpty(primary).Take(5).ToArray()),
            _ => ("Viewing checklist", (string?)null,
                publicFacts.LastOrDefault() ?? shortTitle, context.PrimaryObjects.Take(2).ToArray())
        };

        var authorities = AuthoritiesFor(detail, facts, context).ToArray();
        var visualReferences = context.VisualPlanningSemantics.Take(3).Select(Reference).ToArray();
        var visualTreatment = topic.Purpose switch
        {
            "Where to look" when string.IsNullOrWhiteSpace(context.Direction) => "clear constellation recognition composition",
            "When to observe" when string.IsNullOrWhiteSpace(context.LocalPeakTime) && string.IsNullOrWhiteSpace(context.BestViewingWindowLocal)
                => "timeless dark-sky observing composition",
            "Opening view" => "cinematic event identity composition",
            "What happens" => "clear educational astronomy composition",
            "Key objects" => "recognizable celestial object portrait composition",
            "Viewing checklist" => "practical dark-sky observing composition",
            _ => "role-appropriate astronomy composition"
        };
        var prompt = Phase13GalleryAuthority.BuildMatureGalleryPrompt(new(
            context.Title, context.PrimaryObjects.Concat(context.SecondaryObjects).ToArray(), facts,
            visualTreatment, topic.Purpose, context.EventFamily,
            "Large role-specific subject, coherent dark-sky lighting, lower-third negative space"));
        return new(topic.Number, topic.Purpose, treatment, reason, topic.LocalizedEducationalRole,
            context.Title, detail, facts, context.PrimaryObjects.Concat(context.SecondaryObjects).ToArray(),
            visualTreatment, authorities, visualReferences, prompt);
    }

    private static IEnumerable<Phase13GalleryAuthority.GalleryAuthorityReference> AuthoritiesFor(string detail,
        IReadOnlyList<string> facts, MatureGallerySemanticContext context)
    {
        var values = new[] { detail }.Concat(facts).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var item = context.PublicSemantics.FirstOrDefault(x => x.Text.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (item is not null) { yield return Reference(item); continue; }
            var pointer = value == context.Title ? "/eventIdentity/title"
                : value == context.ShortTitle ? "/intelligence/shortTitle"
                : context.SecondaryObjects.Contains(value, StringComparer.OrdinalIgnoreCase) ? "/intelligence/secondaryObjects"
                : "/eventIdentity/primaryObjects";
            yield return new(Phase13GallerySemanticHydrator.Phase2Authority, pointer, value, "verified-event-intelligence");
        }
    }

    private static Phase13GalleryAuthority.GalleryAuthorityReference Reference(
        Phase13GallerySemanticHydrator.GallerySemanticItem item) =>
        new(item.AuthoritySource, item.AuthorityPath, item.Text, item.Usage.ToString());
}
