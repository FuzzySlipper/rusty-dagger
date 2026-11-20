using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallUiDispatch"/> built without the session, over <see cref="SocietyStage"/>'s character, racial
/// overrides and law: the Session lists it calls are recorded, and the panels these paths never reach are left out.
/// The session's UI action tests keep the end-to-end dispatch and its order.
/// </summary>
public sealed class DaggerfallUiDispatchTests : IDisposable
{
    private readonly SocietyStage _stage = new();
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly List<string> _calls = [];
    private readonly PlayerInputSystem _input;
    private readonly DaggerfallInputRefusals _refusals = new();
    private readonly DaggerfallUiDispatch _dispatch;

    public DaggerfallUiDispatchTests()
    {
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;
        _input = new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput);
        _dispatch = new(_stage.Character, quickEntry: false, _stage.Overrides, _stage.Law, null!, null!, null!, null!, null!, null!, null, null!,
            null!, null!, null!, _input, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            new DaggerfallUiDispatch.SessionCalls(
                ChooseLegal: (revision, choice) => _calls.Add($"legal {revision} {choice}"),
                AnswerQuestOffer: (quest, accept) => _calls.Add($"offer {quest} {accept}"),
                ToggleWeapon: () => _calls.Add("weapon"),
                RequestPanel: panel => _calls.Add($"panel {panel}"),
                RequestSaveSlot: request => _calls.Add($"slot {request.Operation}"),
                ChooseBundle: bundle => _calls.Add($"bundle {bundle}"),
                SetMenuOpen: open => _calls.Add($"menu {open}"),
                HoldsWorldOpen: () => false),
            _presentation, _refusals);
    }

    [Fact]
    public void An_action_is_admitted_by_its_phase_and_routed_to_its_list()
    {
        Admit(null, DaggerfallUiPhases.Playing);
        Assert.Equal(DaggerfallUiAction.UnrecognizedRefusal, _presentation.LastOutcome);
        _presentation.SetOutcome("Ready");
        Admit(null, DaggerfallUiPhases.Dead);
        Assert.Equal("Ready", _presentation.LastOutcome);

        Admit(new DaggerfallPlayerUiAction("save-game"), DaggerfallUiPhases.Held);
        Assert.Empty(_calls);
        Admit(new DaggerfallPlayerUiAction("save-game"), DaggerfallUiPhases.Playing);
        Admit(new DaggerfallPlayerUiAction("weapon-toggle"), DaggerfallUiPhases.Held);
        Admit(new DaggerfallPlayerUiAction("legal-choice", "3", Key: "plead"), DaggerfallUiPhases.Modal);
        Admit(new DaggerfallPlayerUiAction("menu", Open: true), DaggerfallUiPhases.Playing);
        Assert.Equal(["slot QuickSave", "weapon", "legal 3 plead", "menu True"], _calls);
    }

    [Fact]
    public void A_world_act_the_dispatch_drops_says_why()
    {
        Admit(new DaggerfallPlayerUiAction("attack"), DaggerfallUiPhases.Modal);
        Assert.Equal("Attack dropped: a modal holds the world", _refusals.Latest.ToString());
        Admit(new DaggerfallPlayerUiAction("weapon-toggle"), DaggerfallUiPhases.Dead);
        Assert.Equal("Weapon toggle dropped: dead", _refusals.Latest.ToString());
        Admit(new DaggerfallPlayerUiAction("attack"), DaggerfallUiPhases.Playing, opensInteraction: true);
        Assert.Equal("Attack dropped: an interaction opened in the same update", _refusals.Latest.ToString());

        // A world act's binding pressed while a DOM surface held the cursor; a binding that is no world act, or a
        // surface named in anything but words, is not a claim.
        DaggerfallUiInput claims = _dispatch.Take([Ui("""{"action":"input-claimed","item":"kick","key":"menu"}"""),
            Ui("""{"action":"input-claimed","item":"inventory","key":"menu"}"""), Ui("""{"action":"input-claimed","item":"kick","key":"<b>"}""")]);
        Assert.Null(claims.ActionAt(1));
        Assert.Null(claims.ActionAt(2));
        Admit(claims.ActionAt(0), DaggerfallUiPhases.Held);
        Assert.Equal("Kick dropped: the menu has the cursor", _refusals.Latest.ToString());

        // A panel key that opens a panel between steps takes the attack queued with it.
        _input.Request(DaggerfallInput.Attack);
        _input.Request(DaggerfallInput.Inventory);
        _dispatch.ApplyIdleInterfaceActions();
        Assert.Equal("Attack dropped: a panel key took the same update", _refusals.Latest.ToString());
        Assert.False(_input.HasPendingActions);
    }

    [Fact]
    public void A_press_the_ui_kept_from_play_is_reported_while_the_court_holds_the_player()
    {
        _stage.Crime.RecordIncident(_stage.Reported("theft:1"));
        _stage.Law.Arrest(_stage.Law.Current!);
        Assert.True(_stage.Law.ModalOpen);

        DaggerfallUiInput claim = _dispatch.Take([Ui("""{"action":"input-claimed","item":"kick","key":"modal screen"}""")]);
        Admit(claim.ActionAt(0), DaggerfallUiPhases.Modal);
        Assert.Equal("Kick dropped: the modal screen has the cursor", _refusals.Latest.ToString());
        _refusals.Age(4.9);
        Assert.Equal("Kick dropped: the modal screen has the cursor", _refusals.Latest.ToString());
        // The court still admits nothing else.
        Admit(new DaggerfallPlayerUiAction("weapon-toggle"), DaggerfallUiPhases.Modal);
        Assert.Equal("Weapon toggle dropped: the court waits for an answer", _refusals.Latest.ToString());
        Assert.Empty(_calls);
    }

    [Fact]
    public void A_wire_no_rule_names_is_reported_as_unrecognized()
    {
        DaggerfallUiInput input = _dispatch.Take([Ui("""{"action":"spellinfo","key":"spell:1"}""")]);
        Assert.True(input.IsUiAction(0));
        Admit(input.ActionAt(0), DaggerfallUiPhases.Playing);
        Assert.Equal(DaggerfallUiAction.UnrecognizedRefusal, _presentation.LastOutcome);
    }

    [Fact]
    public void A_form_that_suppresses_the_inventory_refuses_its_actions()
    {
        _stage.Overrides.Select(DaggerfallRacialKind.Werewolf, "curse", 0);
        _stage.Overrides.SetBeastForm(true, 0);
        Admit(new DaggerfallPlayerUiAction("bank-open", "1"), DaggerfallUiPhases.Playing);
        Assert.Equal(DaggerfallRacialOverrideView.InventoryRefusal, _presentation.LastOutcome);
    }

    [Fact]
    public void Begin_waits_for_open_character_choices_and_quick_start_begins_at_once()
    {
        Assert.False(_dispatch.RequestsBegin([]));
        Assert.True(_dispatch.RequestsBegin([Ui("""{"action":"begin"}""")]));
        new DaggerfallCharacterCreation(_stage.Character, _stage.Definitions, RandomMinimum.Create()).Begin();
        Assert.False(_dispatch.RequestsBegin([Ui("""{"action":"begin"}""")]));
        Assert.Equal("Commit or cancel character choices before beginning play.", _presentation.LastOutcome);
        Assert.True(_dispatch.RequestsBegin([Ui("""{"action":"character-quick-start","name":"","race":"breton","gender":"male","faceIndex":0,"reflexes":2,"career":"class00"}""")]));
    }

    [Fact]
    public void Shortcut_and_panel_keys_ask_for_their_panel_or_save()
    {
        _dispatch.ApplyShortcut("quick-save");
        _dispatch.ApplyShortcut("rest");
        _input.Request(DaggerfallInput.Inventory);
        _dispatch.ApplyIdleInterfaceActions();
        Assert.Equal(["slot QuickSave", "panel rest", $"panel {DaggerfallPanel.Inventory}"], _calls);
    }

    private void Admit(DaggerfallPlayerUiAction? action, DaggerfallUiPhases phase, bool opensInteraction = false)
    {
        bool elapsed = false;
        _dispatch.Admit(action, phase, new ProductUpdateState(1f / 60f), opensInteraction, ref elapsed);
    }

    public void Dispose() => _stage.Dispose();
}
