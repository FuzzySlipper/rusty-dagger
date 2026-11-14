using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallSummoningPanel"/> built without the session over a summoning owner on <see cref="GuildMakerFixture"/>'s
/// registry and purse, no conversation and no cinematics; the summoned hostiles' list recorded as a plain input.
/// </summary>
public sealed class DaggerfallSummoningPanelTests : IDisposable
{
    private readonly GuildMakerFixture _fixture = new();
    private readonly PresentationState _presentation = new("Ready", 5d);
    private int _reconciled;

    [Fact]
    public void With_no_summoner_and_no_offer_the_pane_is_closed_and_actions_say_why()
    {
        DaggerfallQuestInstances quests = new(_fixture.Definitions, RandomMinimum.Create());
        DaggerfallDaedricSummoning summoning = new(_fixture.Definitions, _fixture.Npcs, _fixture.GuildServices, _fixture.Services,
            _fixture.Social, RandomMinimum.Create(), DaggerfallDisabledQuestSelection.None, () => _fixture.Calendar, () => (false, false),
            () => default, _ => throw new InvalidOperationException("No quest is prepared here."));
        DaggerfallServiceCounter counter = new(() => null, () => null, null!, () => default, new DaggerfallActivationPresentation());
        DaggerfallSummoningPanel panel = new(summoning, quests, cinematics: null, videosEnabled: false, counter, () => _reconciled++, _presentation);

        Assert.Null(panel.Read());
        panel.Change(new DaggerfallPlayerUiAction("daedra-summon", "1", Key: "prince", Amount: 1));
        Assert.Equal("Your standing does not permit this service.", _presentation.LastOutcome);
        panel.Change(new DaggerfallPlayerUiAction("daedra-answer", "not-a-sequence", Confirm: true));
        Assert.Equal("That offer is no longer open.", _presentation.LastOutcome);
        Assert.Equal(2, _reconciled);
    }

    public void Dispose() => _fixture.Dispose();
}
