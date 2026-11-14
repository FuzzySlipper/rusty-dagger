using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallSpellbookPanel"/> built without the session over <see cref="MagicCastFixture"/>'s casting and its
/// one bought spell, with no one at the counter.
/// </summary>
public sealed class DaggerfallSpellbookPanelTests : IDisposable
{
    private readonly MagicCastFixture _fixture = new();
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly DaggerfallSpellbookPanel _spellbook;

    public DaggerfallSpellbookPanelTests()
    {
        DaggerfallDefinitions definitions = _fixture.Items.Definitions;
        DaggerfallSpellRelease release = new(_fixture.Casting, _fixture.Targets, definitions, _fixture.Actors, _fixture.Items.Character,
            _fixture.Items.Inventory, _presentation, 1.6f, () => _fixture.Positions, _ => throw new InvalidOperationException("No touch spell here."),
            null!, () => default, (_, _) => false, (_, _, _) => { });
        DaggerfallServiceCounter counter = new(() => null, () => null, null!, () => default, new DaggerfallActivationPresentation());
        DaggerfallMakerPanels makers = new(_fixture.Items.ItemMaker(), _fixture.Items.PotionMaker(), definitions, counter, _presentation);
        DaggerfallQuestInstances quests = new(definitions, RandomMinimum.Create());
        DaggerfallDaedricSummoning summoning = new(definitions, _fixture.Items.Npcs, _fixture.Items.GuildServices, _fixture.Items.Services,
            _fixture.Items.Social, RandomMinimum.Create(), DaggerfallDisabledQuestSelection.None, () => _fixture.Items.Calendar,
            () => (false, false), () => default, _ => throw new InvalidOperationException("No quest is prepared here."));
        _spellbook = new(_fixture.Items.Character, _fixture.Actors, _fixture.Casting, release, definitions,
            _fixture.Items.SpellSales(_ => null), _fixture.Items.SpellMaker(_fixture.Effects.Catalog), counter, makers,
            new DaggerfallSummoningPanel(summoning, quests, null, false, counter, () => { }, _presentation), () => null, _presentation);
    }

    [Fact]
    public void The_book_lists_the_known_spell_and_its_changes_are_worded_for_the_player()
    {
        DaggerfallSpellbookView book = _spellbook.Read();
        Assert.Equal(_fixture.SpellKey, Assert.Single(book.Available).Key);
        Assert.Null(book.Sale);
        Assert.Null(book.Summoning);

        _spellbook.ChangeSpellbook(new DaggerfallPlayerUiAction("spell-swap", Key: _fixture.SpellKey, Item: "spell.unknown"));
        Assert.Equal("Those spells cannot be swapped.", _presentation.LastOutcome);
        _spellbook.ChangeSpellbook(new DaggerfallPlayerUiAction("spell-delete", Key: _fixture.SpellKey));
        Assert.Equal("Confirm this spellbook change.", _presentation.LastOutcome);
        _spellbook.ChangeSpellbook(new DaggerfallPlayerUiAction("spell-buy", "1", Key: _fixture.SpellKey, Amount: 1, Confirm: true));
        Assert.Equal("That spell seller is no longer available.", _presentation.LastOutcome);
        Assert.Equal("That spell seller is no longer available.", _spellbook.Read().Result);
    }

    [Fact]
    public void Unreadying_cancels_the_cast_and_the_spellmaker_needs_its_maker()
    {
        _spellbook.ChangeSpell(new DaggerfallPlayerUiAction("spell-unready"));
        Assert.Equal("No spell ready.", _presentation.LastOutcome);
        _spellbook.ChangeSpellMaker(new DaggerfallPlayerUiAction("spellmaker-draft", "1", Text: "{}"));
        Assert.Equal("This spellmaker is unavailable to you.", _presentation.LastOutcome);
    }

    public void Dispose() => _fixture.Dispose();
}
