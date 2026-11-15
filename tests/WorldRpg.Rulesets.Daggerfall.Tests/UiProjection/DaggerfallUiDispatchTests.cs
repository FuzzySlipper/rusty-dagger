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
            _presentation);
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

    private void Admit(DaggerfallPlayerUiAction? action, DaggerfallUiPhases phase)
    {
        bool elapsed = false;
        _dispatch.Admit(action, phase, new ProductUpdateState(1f / 60f), opensInteraction: false, ref elapsed);
    }

    public void Dispose() => _stage.Dispose();
}
