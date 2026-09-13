using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>The published message line and the admitted time that ages it.</summary>
public sealed class PresentationStateTests
{
    [Fact]
    public void A_message_clears_once_its_admitted_lifetime_runs_out()
    {
        PresentationState presentation = new("Ready.");

        presentation.SetOutcome("Hit the rat for 4 damage");
        presentation.Advance(PresentationState.LifetimeSeconds - 1d);
        Assert.Equal("Hit the rat for 4 damage", presentation.LastOutcome);

        presentation.Advance(2d);
        Assert.Equal(string.Empty, presentation.LastOutcome);

        // A cleared line stays cleared: nothing ages a message that is not there.
        presentation.Advance(100d);
        Assert.Equal(string.Empty, presentation.LastOutcome);
    }

    [Fact]
    public void An_initial_message_is_transient_like_any_other()
    {
        // A session starts with a greeting, and a greeting that never leaves the screen stops
        // reporting what just happened on the very first update.
        PresentationState presentation = new("Ready.");
        presentation.Advance(PresentationState.LifetimeSeconds + 1d);
        Assert.Equal(string.Empty, presentation.LastOutcome);

        // An empty start stays empty, and the first real message still gets its full span.
        PresentationState quiet = new(string.Empty);
        quiet.Advance(100d);
        Assert.Equal(string.Empty, quiet.LastOutcome);
        quiet.SetOutcome("Hit the rat for 4 damage");
        quiet.Advance(PresentationState.LifetimeSeconds + 1d);
        Assert.Equal(string.Empty, quiet.LastOutcome);
    }

    [Fact]
    public void Repeating_a_message_restarts_its_lifetime_and_appending_never_starts_with_a_separator()
    {
        PresentationState presentation = new(string.Empty);

        presentation.SetOutcome("Loot changed. Choose the item again.");
        presentation.Advance(PresentationState.LifetimeSeconds / 2d);
        // The modal republishes the same line every frame, so the message must keep living.
        presentation.SetOutcome("Loot changed. Choose the item again.");
        presentation.Advance(PresentationState.LifetimeSeconds / 2d);
        Assert.Equal("Loot changed. Choose the item again.", presentation.LastOutcome);

        presentation.AppendOutcome("looted 1 gold-piece");
        Assert.Equal("Loot changed. Choose the item again.; looted 1 gold-piece", presentation.LastOutcome);

        // An empty line takes the clause alone rather than a leading separator.
        presentation.SetOutcome(string.Empty);
        presentation.AppendOutcome("Corpse is empty");
        Assert.Equal("Corpse is empty", presentation.LastOutcome);
    }
}
