using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class MenuHoldSessionTests
{
    [Fact]
    public void Reporting_menu_open_holds_calendar_fatigue_and_enemies_in_the_same_admitted_batch()
    {
        using var session = FreshSession();
        var before = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        double fatigue = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("fatigue")).Current;
        session.Update(new ProductUpdate(OuterUpdate(1) with { FixedDeltaSeconds = 1, AdmittedStepCount = 60 },
            [Ui("""{"action":"menu","open":true}""")]));
        Assert.Equal(before, DaggerfallSavePayload.Read(session.CaptureSave()).Calendar);
        Assert.Equal(fatigue, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("fatigue")).Current);
        Assert.Empty(session.LastEnemyBehavior);
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Modal);
        session.Update(new ProductUpdate(OuterUpdate(61) with { FixedDeltaSeconds = 1, AdmittedStepCount = 60 }, []));
        Assert.Equal(before, DaggerfallSavePayload.Read(session.CaptureSave()).Calendar);
        Assert.Equal(fatigue, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("fatigue")).Current);
        session.Update(new ProductUpdate(OuterUpdate(121), [Ui("""{"action":"menu","open":false}""")]));
        Assert.Equal(ProductMode.Playing, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Playing);
        session.Update(new ProductUpdate(OuterUpdate(122) with { FixedDeltaSeconds = 1 }, []));
        Assert.NotEqual(before, DaggerfallSavePayload.Read(session.CaptureSave()).Calendar);
    }

    [Fact]
    public void Opening_and_closing_the_menu_keeps_the_recent_messages_and_adds_none_of_its_own()
    {
        using var session = FreshSession();
        session.Presentation.SetOutcome("Quest choice recorded.");
        session.ApplyProductMode(ProductMode.Modal);
        Assert.Equal("Quest choice recorded.", session.Presentation.LastOutcome);
        session.ApplyProductMode(ProductMode.Playing);
        Assert.Equal("Quest choice recorded.", session.Presentation.LastOutcome);
    }

    [Fact]
    public void The_radial_readies_and_sheathes_the_weapon_as_it_closes_on_a_held_world()
    {
        using var session = FreshSession();
        bool before = session.State.WeaponDrawn;
        for (ulong update = 1; update <= 4; update += 2)
        {
            // The radial holds the world as the menu does; the update that closes it admits no world step.
            session.Update(new ProductUpdate(OuterUpdate(update), [Ui("""{"action":"menu","open":true}""")]));
            session.ApplyProductMode(ProductMode.Modal);
            session.Update(new ProductUpdate(OuterUpdate(update + 1) with { AdmittedStepCount = 0 },
                [Ui("""{"action":"menu","open":false}"""), Ui("""{"action":"weapon-toggle"}""")]));
            session.ApplyProductMode(ProductMode.Playing);
            Assert.Equal(update == 1 ? !before : before, session.State.WeaponDrawn);
        }
    }
}
