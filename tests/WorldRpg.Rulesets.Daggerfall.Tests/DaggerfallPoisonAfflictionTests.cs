using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The course of one poison: counting down, ticking once a minute, and ending.</summary>
public sealed class DaggerfallPoisonAfflictionTests
{
    private static DaggerfallPoisonArchetype Archetype(int variant) =>
        DaggerfallPoisonArchetypes.All.Single(archetype => archetype.Variant == variant);

    [Fact]
    public void A_poison_waits_out_its_onset_and_then_ticks_in_the_minute_it_starts()
    {
        // The donor counts the onset down first and ticks in the same minute the count reaches zero, so a
        // four-minute onset gives its first arm on the fourth minute rather than the fifth.
        DaggerfallPoisonAffliction nux = new(Archetype(128), minutesToStart: 4, minutesRemaining: 3);

        Assert.Equal(DaggerfallPoisonPhase.Waiting, nux.Phase);
        Assert.Empty(nux.AdvanceMinute());
        Assert.Empty(nux.AdvanceMinute());
        Assert.Empty(nux.AdvanceMinute());
        Assert.Equal(DaggerfallPoisonPhase.Waiting, nux.Phase);

        Assert.Equal(Archetype(128).Effects, nux.AdvanceMinute());
        Assert.Equal(DaggerfallPoisonPhase.Active, nux.Phase);
        Assert.Equal(2, nux.MinutesRemaining);
    }

    [Fact]
    public void A_poison_with_no_onset_acts_at_once()
    {
        // Moonseed and the other immediates roll an onset of zero, which is acting rather than waiting.
        DaggerfallPoisonAffliction moonseed = new(Archetype(130), minutesToStart: 0, minutesRemaining: 2);

        Assert.Equal(DaggerfallPoisonPhase.Active, moonseed.Phase);
        Assert.Equal(Archetype(130).Effects, moonseed.AdvanceMinute());
    }

    [Fact]
    public void A_poison_ticks_once_a_minute_until_its_minutes_are_spent()
    {
        DaggerfallPoisonAffliction arsenic = new(Archetype(129), minutesToStart: 0, minutesRemaining: 2);

        Assert.NotEmpty(arsenic.AdvanceMinute());
        Assert.Equal(1, arsenic.MinutesRemaining);
        Assert.Equal(DaggerfallPoisonPhase.Active, arsenic.Phase);

        Assert.NotEmpty(arsenic.AdvanceMinute());
        Assert.Equal(DaggerfallPoisonPhase.Complete, arsenic.Phase);
        Assert.Empty(arsenic.AdvanceMinute());
        Assert.Empty(arsenic.AdvanceMinute());
    }

    [Fact]
    public void A_cure_ends_the_poison_where_it_stands()
    {
        DaggerfallPoisonAffliction thyrwort = new(Archetype(135), minutesToStart: 2, minutesRemaining: 20);
        Assert.Equal(DaggerfallPoisonPhase.Waiting, thyrwort.Phase);

        thyrwort.Cure();

        Assert.Equal(DaggerfallPoisonPhase.Complete, thyrwort.Phase);
        Assert.Empty(thyrwort.AdvanceMinute());
    }

    [Fact]
    public void The_drugs_that_help_their_victim_say_so()
    {
        // Exactly the four arms the donor has to take back when the poison ends: luck, strength, fatigue
        // and magicka.
        Assert.True(new DaggerfallPoisonAffliction(Archetype(136), 0, 2).HasPositiveArm);
        Assert.True(new DaggerfallPoisonAffliction(Archetype(137), 0, 2).HasPositiveArm);
        Assert.True(new DaggerfallPoisonAffliction(Archetype(138), 0, 2).HasPositiveArm);
        Assert.True(new DaggerfallPoisonAffliction(Archetype(139), 0, 2).HasPositiveArm);
        Assert.False(new DaggerfallPoisonAffliction(Archetype(128), 0, 2).HasPositiveArm);
        Assert.False(new DaggerfallPoisonAffliction(Archetype(129), 0, 2).HasPositiveArm);
        Assert.Equal(4, DaggerfallPoisonArchetypes.All.Count(archetype =>
            new DaggerfallPoisonAffliction(archetype, 0, 1).HasPositiveArm));
    }

    [Fact]
    public void An_affliction_that_cannot_run_is_refused_rather_than_sitting_idle()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallPoisonAffliction(Archetype(128), -1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallPoisonAffliction(Archetype(128), 0, 0));
        Assert.Throws<ArgumentNullException>(() => new DaggerfallPoisonAffliction(null!, 0, 1));
    }
}
