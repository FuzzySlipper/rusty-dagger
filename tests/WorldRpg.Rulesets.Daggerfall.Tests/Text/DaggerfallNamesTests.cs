using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallNamesTests
{
    [Fact]
    public void NpcName_takes_faction_name_only_for_individual_type_4_and_seeds_commoners_and_daedra_flats()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallNames names = new(definitions, RandomMinimum.Create());
        const int daggerfallRegion = 17;

        // Type-4 Individual faction keeps its canonical faction name (matches DFU StaticNPC.GetDisplayName).
        DaggerfallNpcAppearance noble = new("breton", "Male", 334, 0, NameSeed: 42, FactionId: 364);
        Assert.Equal("King Gothryd", names.NpcName(noble, daggerfallRegion));

        // Ordinary commoner (no Individual faction) rolls a deterministic regional name from its seed.
        DaggerfallNpcAppearance commoner = noble with { FactionId = 0 };
        string seededName = names.NpcName(commoner, daggerfallRegion);
        Assert.False(string.IsNullOrWhiteSpace(seededName));
        Assert.NotEqual("King Gothryd", seededName);
        Assert.Equal(seededName, names.NpcName(commoner, daggerfallRegion));

        // Type-0 Daedra faction on a placed flat (a source typo on nine Sentinel/Wayrest commoners)
        // rolls the same seeded regional name rather than the Daedra Prince's name.
        DaggerfallNpcAppearance daedraTypoFlat = noble with { FactionId = 3 };
        Assert.Equal(seededName, names.NpcName(daedraTypoFlat, daggerfallRegion));
        Assert.NotEqual("Molag Bal", names.NpcName(daedraTypoFlat, daggerfallRegion));
    }

    [Fact]
    public void Castle_daggerfall_royals_show_their_individual_faction_names_in_talk_and_across_save()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord castle = definitions.Locations.Records.Single(record =>
            record.DungeonBlocks.Any(block => string.Equals(block.SourceKey, "S0000160.RDB", StringComparison.OrdinalIgnoreCase)));
        DaggerfallSiteProfile inputs = Assembled(DaggerfallWorldProfileIds.Dungeon(castle.Id));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, inputs, DaggerfallTuning.Defaults);

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallNpc gothryd = Assert.Single(session.State.Npcs.All, npc => npc.Appearance.FactionId == 364);
        DaggerfallNpc aubki = Assert.Single(session.State.Npcs.All, npc => npc.Appearance.FactionId == 365);
        Assert.Equal("King Gothryd", gothryd.DisplayName);
        Assert.Equal("Queen Aubk-i", aubki.DisplayName);

        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(), value => value.Identity.Value == (ulong)gothryd.DurableId);
        Assert.Equal("King Gothryd", target.Label);
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        Assert.Equal("King Gothryd", Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue).TargetLabel);

        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, session.CaptureSave());
        Assert.Equal("King Gothryd", restored.State.Npcs.Require(gothryd.DurableId).DisplayName);
        Assert.Equal("Queen Aubk-i", restored.State.Npcs.Require(aubki.DurableId).DisplayName);
    }
}
