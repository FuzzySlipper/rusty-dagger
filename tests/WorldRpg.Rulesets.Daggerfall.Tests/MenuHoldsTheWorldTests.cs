using Rusty.Engine;
using WorldRpg.Kit;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class MenuHoldsTheWorldTests
{
    /// <summary>
    /// The game menu holds the world: while the DOM reports it open the session asks for Modal, and an
    /// update in that mode advances no game time. Closing it asks for play again, and a client that
    /// attaches afresh starts with the menu closed.
    /// </summary>
    [Fact]
    public void An_open_game_menu_holds_the_world_until_it_closes_or_a_client_attaches()
    {
        static ProductInputEvent Menu(bool open) => Ui(open ? """{"action":"menu","open":true}""" : """{"action":"menu","open":false}""");

        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        Assert.Null(session.PendingModeRequest);

        session.Update(new ProductUpdate(OuterUpdate(1), [Menu(true)]));
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Modal);
        DaggerfallCalendarSave held = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        for (ulong step = 2; step < 32; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Equal(held, DaggerfallSavePayload.Read(session.CaptureSave()).Calendar);

        session.Update(new ProductUpdate(OuterUpdate(32), [Menu(false)]));
        Assert.Equal(ProductMode.Playing, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Playing);
        session.Update(new ProductUpdate(OuterUpdate(33), []));
        Assert.NotEqual(held, DaggerfallSavePayload.Read(session.CaptureSave()).Calendar);

        // A page that went away with its menu open cannot hold the world for the next client.
        session.Update(new ProductUpdate(OuterUpdate(34), [Menu(true)]));
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);
        session.PublishInitial();
        Assert.Null(session.PendingModeRequest);
    }
}
