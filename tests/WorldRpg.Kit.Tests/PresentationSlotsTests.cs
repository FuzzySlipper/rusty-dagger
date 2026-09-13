using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>The status rows owners publish, and the teardown paths that retire them.</summary>
public sealed class PresentationSlotsTests
{
    [Fact]
    public void One_owner_cannot_disturb_another_and_the_same_id_replaces()
    {
        PresentationSlots slots = new();
        slots.Publish(new PresentationSlot("effect.poison", "tick", "Poisoned", "3 damage", 10));
        slots.Publish(new PresentationSlot("quest.main", "objective", "Find the letter", "", 20));

        // A second publish of the same owner and id is the same row changing, not a new row.
        slots.Publish(new PresentationSlot("effect.poison", "tick", "Poisoned", "1 damage", 10));
        Assert.Equal(2, slots.Read().Count);
        Assert.Equal("1 damage", slots.Read().Single(slot => slot.Owner == "effect.poison").Detail);

        Assert.Equal(10, slots.Read()[0].Order);
        Assert.Equal(20, slots.Read()[1].Order);
    }

    [Fact]
    public void Retiring_one_row_and_retiring_an_owner_both_report_what_went_away()
    {
        PresentationSlots slots = new();
        slots.Publish(new PresentationSlot("escort.knight", "following", "Knight follows", "", 5));
        slots.Publish(new PresentationSlot("escort.knight", "paid", "Paid 40 gold", "", 6));
        slots.Publish(new PresentationSlot("effect.poison", "tick", "Poisoned", "", 10));

        Assert.True(slots.Retire("escort.knight", "following"));
        Assert.False(slots.Retire("escort.knight", "following"));

        Assert.Equal(1, slots.RetireOwner("escort.knight"));
        Assert.Equal(0, slots.RetireOwner("escort.knight"));

        // Only the effect's row is left, so a source going away cannot strand its rows.
        Assert.Equal("effect.poison", Assert.Single(slots.Read()).Owner);
    }
}
