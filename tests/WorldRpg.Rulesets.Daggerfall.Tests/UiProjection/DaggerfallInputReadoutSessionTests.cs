using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The developer's input readout over a running session: off until the developer turns it on, then the HUD carries the
/// player's state read from its owners and the latest input refused or dropped, with the reason each owner reports.
/// </summary>
public sealed class DaggerfallInputReadoutSessionTests : IDisposable
{
    private readonly DaggerfallSession _session;
    private EngineContextFake _engine = null!;
    private ulong _step = 1;

    public DaggerfallInputReadoutSessionTests()
    {
        (_session, _, _) = VisibleEnemySession([], distance: 5d, engineCreated: engine => _engine = engine);
        _session.State.WeaponDrawn = true;
        Run([]);
    }

    public void Dispose() => _session.Dispose();

    private void Run(ProductInputEvent[] input) => _session.Update(new ProductUpdate(OuterUpdate(_step++), input));

    private Dictionary<string, object?>? Readout() =>
        (Dictionary<string, object?>?)Assert.IsType<Dictionary<string, object?>>(_engine.Published())["readout"];

    private string State() => Assert.IsType<string>(Readout()!["state"]);

    private string? Refusal() => (string?)Readout()!["refusal"];

    private void TurnOn()
    {
        IDeveloperReadoutCommands commands = _session.InputReadout;
        Assert.Equal("{\"on\":false}", commands.Read().Message);
        Assert.True(commands.ShowReadout().Succeeded);
        Run([]);
    }

    [Fact]
    public void The_readout_is_off_until_turned_on_and_then_follows_a_swing_through_its_phases()
    {
        Assert.Null(Readout());
        TurnOn();
        Assert.Equal("weapon drawn · attack ready · guard down · no spell ready · arms: classic sprite", State());
        Assert.Null(Refusal());

        Run(AttackTap);
        List<string> phases = [State()];
        for (int update = 0; update < 120 && _session.PlayerAttack.Clock.Current is not null; update++)
        {
            Run([]);
            phases.Add(State());
        }
        string[] seen = [.. new[] { "fastA wind-up", "fastA swing", "fastA recovery", "attack ready" }.Where(phase => phases.Any(state => state.Contains(phase)))];
        Assert.Equal(["fastA wind-up", "fastA swing", "fastA recovery", "attack ready"], seen);
        Assert.Contains(phases, state => state.Contains("chain 1") && state.Contains("recovery "));

        Assert.True(((IDeveloperReadoutCommands)_session.InputReadout).HideReadout().Succeeded);
        Run([]);
        Assert.Null(Readout());
    }

    [Fact]
    public void A_press_made_before_the_swing_frees_the_clock_shows_as_waiting_and_then_starts()
    {
        TurnOn();
        Run(AttackTap);
        Run(AttackTap);
        Assert.Contains("fastA wind-up", State());
        Assert.Contains("press waiting: attack", State());
        Assert.Contains("blow pending", State());
        for (int update = 0; update < 120 && !State().Contains("fastB"); update++) Run([]);
        Assert.DoesNotContain("press waiting", State());
        Assert.Null(Refusal());
    }

    [Fact]
    public void A_sheathed_weapon_refuses_the_attack_and_a_held_block()
    {
        TurnOn();
        _session.State.WeaponDrawn = false;
        Run(AttackTap);
        Assert.Equal("Attack refused: weapon sheathed", Refusal());
        Assert.StartsWith("weapon sheathed · attack ready", State());

        Run([Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "block")]);
        Assert.Equal("Block refused: weapon sheathed", Refusal());
    }

    [Fact]
    public void A_press_the_held_world_drops_names_what_holds_it()
    {
        TurnOn();
        Run([Ui("""{"action":"menu","open":true}""")]);
        Run(AttackTap);
        Assert.Equal("Attack dropped: menu open", Refusal());
        Run([Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "interact")]);
        Assert.Equal("Use dropped: menu open", Refusal());
    }

    [Fact]
    public void A_press_the_ui_kept_from_play_is_reported_with_the_surface_that_held_the_cursor()
    {
        TurnOn();
        Run([Ui("""{"action":"input-claimed","item":"attack","key":"developer tools"}""")]);
        Assert.Equal("Attack dropped: the developer tools has the cursor", Refusal());
        Run([Ui("""{"action":"input-claimed","item":"recast","key":"menu"}""")]);
        Assert.Equal("Cast dropped: the menu has the cursor", Refusal());
    }

    [Fact]
    public void A_cast_with_nothing_ready_or_too_little_magicka_is_refused()
    {
        TurnOn();
        Run([Ui("""{"action":"spell-cast"}""")]);
        Run([]);
        Assert.Equal("Cast refused: no spell ready", Refusal());

        // The first spell the player can learn and ready with the magicka they have.
        string spell = TestPayload.Definitions.Magic.Spells.Values.Where(candidate => !candidate.IsCustom && !candidate.Name.StartsWith('!'))
            .Select(candidate => candidate.Key).First(key =>
            {
                _session.State.Character.LearnSpell(key);
                return _session.SpellRelease.ReadyPlayerSpell(key).Outcome == DaggerfallCastOutcome.Ready;
            });
        Run([]);
        Assert.Contains($"spell ready: {TestPayload.Definitions.Magic.Spells[spell].Name}", State());
        _session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).SetCurrent(0);
        Run([Ui("""{"action":"spell-cast"}""")]);
        Run([]);
        Assert.Equal("Cast refused: not enough magicka", Refusal());
    }
}
