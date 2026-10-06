using System.Numerics;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Every retained spell list reaches a castable compiled bundle, and each delivery path keeps the donor's bundle identity.</summary>
public sealed class MagicConsumerReconciliationTests
{
    [Fact]
    public void Every_classic_spell_and_enemy_list_entry_resolves_to_a_castable_bundle()
    {
        // The donor reads a classic record without element or target filtering: Buoyancy is a cold
        // water-walking spell and Holy Touch a touch dispel, which the spellmaker alone would refuse.
        using ConditionSessionFixture fixture = new();
        DaggerfallDefinitions definitions = fixture.Definitions;
        string[] classic = [.. definitions.Magic.Spells.Values
            .Where(spell => !spell.IsCustom && !spell.Name.StartsWith('!') && spell.Effects.Count > 0).Select(spell => spell.Key)];
        Assert.NotEmpty(classic);
        Assert.Empty(classic.Where(key => !fixture.Session.Casting.CanCastQuestSpell(key)));
        string[] enemy = [.. definitions.EnemySpells.MobileLists.Values.SelectMany(list => list)
            .Concat(definitions.EnemySpells.ClassTiers.SelectMany(tier => tier)).Distinct()];
        Assert.Empty(enemy.Where(key => !fixture.Session.Casting.CanCastQuestSpell(key)));
        Assert.Contains(definitions.Magic.Spells.Values, spell => spell.Name == "Buoyancy" && classic.Contains(spell.Key));
    }

    [Fact]
    public void A_potion_and_a_spell_of_the_same_effect_run_as_separate_bundles()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        long player = session.State.Actors.Player.DurableId;
        Track magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 10_000; magicka.SetCurrent(10_000);
        string levitate = fixture.Definitions.Magic.Spells.Values.Single(spell => !spell.IsCustom && spell.Name == "Levitate").Key;
        session.State.Character.LearnSpell(levitate);
        Assert.Equal(DaggerfallCastOutcome.Ready, session.ReadyPlayerSpell(levitate).Outcome);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, session.ReleaseReadySpell(1, Vector3.UnitZ).Outcome);
        int recipe = fixture.Definitions.Magic.PotionRecipes.Values.Single(value => value.Name == "Levitation").Key;
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, session.Casting.DrinkPotion(player, recipe).Outcome);

        // The donor's incumbent search stays within one bundle type: the potion neither refreshes
        // nor replaces the spell's levitation, and each keeps its own duration.
        DaggerfallActiveEffectSave[] levitation = [.. session.State.Effects.Capture().Where(effect => effect.EffectKey == "levitate")];
        Assert.Equal(2, levitation.Length);
        Assert.Equal([DaggerfallEffectBundleKind.Spell, DaggerfallEffectBundleKind.Potion],
            levitation.Select(effect => effect.BundleKind).Order());
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(2, restored.State.Effects.Capture().Count(effect => effect.EffectKey == "levitate"));
    }

    [Fact]
    public void Classic_records_carry_the_donor_free_action_patch_and_the_resist_magic_cost()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        // SPELLS.STD names Cure Paralyzation in Free Action's first slot; the donor patches it on read.
        DaggerfallSpellDefinition freeAction = definitions.Magic.Spells.Values.Single(spell => !spell.IsCustom && spell.Identity == 10);
        Assert.Equal((26, -1), (freeAction.Effects[0].Type, freeAction.Effects[0].SubType));
        // Resist Magic is the fifth elemental resistance and is priced for constructed spells.
        Assert.True(definitions.Magic.EffectCosts.ContainsKey((8, 4)));
    }
}
