using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallTextActions"/> built without the session: Text's dungeon text and notebook, with the riddle's
/// continuation and the held building entry recorded as plain inputs.
/// </summary>
public sealed class DaggerfallTextActionsTests
{
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly DaggerfallDungeonText _text = new(new DaggerfallTextResolver(TestPayload.Definitions.Text));
    private readonly DaggerfallBookNotebook _notebook = new(TestPayload.Definitions, new DaggerfallTextResolver(TestPayload.Definitions.Text));
    private readonly List<string> _continued = [];
    private int _entries;

    private DaggerfallTextActions Actions() => new(_text, _notebook, _continued.Add, _presentation, () => _entries++);

    [Fact]
    public void Dungeon_text_input_names_its_prompt_and_a_greeting_lets_the_entry_go_ahead()
    {
        DaggerfallTextActions actions = Actions();
        actions.ApplyDungeonText(new DaggerfallPlayerUiAction("dungeon-text-close", Revision: "x", Item: "greeting"));
        Assert.Equal("Dungeon text input has no current action identity or revision.", _presentation.LastOutcome);
        actions.ApplyDungeonText(new DaggerfallPlayerUiAction("dungeon-text-close", Revision: "4", Item: "greeting"));
        Assert.Equal("Dungeon text input belongs to an older prompt.", _presentation.LastOutcome);

        _text.Greet(new DaggerfallDungeonTextProjection("greeting", DaggerfallDungeonTextActionKind.BuildingGreeting, 0, "Welcome.", [], 4, IsHud: false, ClickAnywhereToClose: true));
        actions.ApplyDungeonText(new DaggerfallPlayerUiAction("dungeon-text-close", Revision: "4", Item: "greeting"));
        Assert.Equal(1, _entries);
        Assert.Empty(_continued);
    }

    [Fact]
    public void A_notebook_action_asks_the_notebook_against_its_revision()
    {
        DaggerfallTextActions actions = Actions();
        actions.ApplyNotebook(new DaggerfallPlayerUiAction("notebook-add", Revision: "stale", Text: "Ask the smith."));
        Assert.Equal("Notebook changed. Choose the page or note again.", _presentation.LastOutcome);
        actions.ApplyNotebook(new DaggerfallPlayerUiAction("notebook-add", Revision: _notebook.Read().Revision, Text: "Ask the smith."));
        Assert.Contains(_notebook.Read().Notes, note => note.Text == "Ask the smith.");
    }
}
