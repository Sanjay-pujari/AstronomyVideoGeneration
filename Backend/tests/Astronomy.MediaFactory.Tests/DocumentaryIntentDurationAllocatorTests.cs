using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class DocumentaryIntentDurationAllocatorTests
{
    [Fact]
    public void duration_allocator_is_weight_proportional()
    {
        var slots = Slots(1, 3);
        var result = DocumentaryIntentPlanner.AllocateDurations(Profile(slots, 40, 5, 30), slots);
        result["slot-2"].Should().BeGreaterThan(result["slot-1"]);
        result.Values.Sum().Should().Be(40);
    }

    [Fact]
    public void duration_allocator_exactly_reconciles_budget()
    {
        var slots = Slots(1, 2, 3, 4);
        DocumentaryIntentPlanner.AllocateDurations(Profile(slots, 137, 7, 80), slots).Values.Sum().Should().Be(137);
    }

    [Fact]
    public void duration_allocator_uses_slot_order_then_id_for_fractional_ties()
    {
        var slots = Slots(1, 1, 1);
        var result = DocumentaryIntentPlanner.AllocateDurations(Profile(slots, 4, 1, 4), slots);
        result["slot-1"].Should().Be(2);
        result["slot-2"].Should().Be(1);
    }

    [Fact]
    public void duration_allocator_enforces_minimum_and_maximum_with_redistribution()
    {
        var slots = Slots(100, 1, 1);
        var result = DocumentaryIntentPlanner.AllocateDurations(Profile(slots, 20, 5, 8), slots);
        result.Values.Should().OnlyContain(x => x is >= 5 and <= 8);
        result.Values.Sum().Should().Be(20);
    }

    [Fact]
    public void duration_allocator_handles_large_budget_without_per_second_iteration()
    {
        var slots = Slots(5, 3, 2);
        var result = DocumentaryIntentPlanner.AllocateDurations(Profile(slots, 2_000_000_000, 1, 1_000_000_000), slots);
        result.Values.Sum(x => (long)x).Should().Be(2_000_000_000L);
    }

    private static DocumentaryNarrativeSlot[] Slots(params int[] weights) => weights.Select((weight, index) =>
        new DocumentaryNarrativeSlot($"slot-{index + 1}", index + 1, "stage", "role", "purpose", [], [], [], false,
            "objective", "outcome-code", "transition", weight, true, true, index == weights.Length - 1 ? "Terminal" : "Continue")
        { VisualOpportunityIntent = "ProfileVisual", EditorialOutcome = "Profile outcome" }).ToArray();

    private static DocumentaryVariantProfile Profile(DocumentaryNarrativeSlot[] slots, int budget, int minimum, int maximum) =>
        new("Long", true, slots.Length, slots.Length, slots.Length, budget, minimum, maximum, slots, [], [], "Sequential");
}
