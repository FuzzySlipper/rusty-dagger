using System.Numerics;
using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Magic's owners built alone over their inputs: identify, the item souls, the detectors and the player's cast. Their
/// spells, saves and composition stay in the session tests.
/// </summary>
public sealed class DaggerfallMagicOwnersTests
{
    [Fact]
    public void Identify_offers_each_unidentified_item_and_all_spends_magicka_per_attempt_and_lapses_with_its_source()
    {
        using MagicCastFixture f = new();
        var (_, id) = f.Items.MagicItem("magic-item.0004");
        f.Items.Instances.ReplaceUnique(id, f.Items.Instances.RequireUnique(id) with { Identified = false });
        PresentationState presentation = new("", 5d);
        FactBuffer<IProductFact> facts = new();
        DaggerfallIdentify identify = Identify(f, presentation, facts, new("cast.3", 100, 5));

        DaggerfallIdentifyView view = identify.View!;
        Assert.Equal(5, view.Cost);
        Assert.Equal([id.ToString(System.Globalization.CultureInfo.InvariantCulture), "all"], view.Options.Select(option => option.Id));
        Track magicka = f.Items.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.SetCurrent(0);
        identify.Choose("cast.3", "all");
        Assert.Equal("Not enough magicka to identify.", presentation.LastOutcome);
        magicka.SetCurrent(20);
        identify.Choose("cast.3", "all");
        Assert.Equal("Identified 1 of 1 items.", presentation.LastOutcome);
        Assert.Equal(15, magicka.Current);
        Assert.True(f.Items.Instances.RequireUnique(id).Identified);
        List<IProductFact> appended = [];
        facts.Deliver(appended.Add);
        Assert.Equal(new MagicItemIdentifiedFact(id, true), Assert.Single(appended));
        Assert.Equal(1, identify.Pending!.Attempts);

        DaggerfallIdentify lapsed = Identify(f, presentation, facts, new("cast.4", 100, 5, SourceItem: 999_999));
        Assert.True(lapsed.Open);
        Assert.Null(lapsed.Pending);
        Assert.Null(lapsed.View);
    }

    [Fact]
    public void A_worn_Azuras_Star_takes_the_soul_of_what_the_player_kills_and_releases_it_on_use()
    {
        using MagicCastFixture f = new();
        var (star, starId) = f.Items.MagicItem("magic-item.0009");
        using DaggerfallHeldEnchantments held = new(f.Items.Equipment, f.Items.Instances, f.Items.Definitions.Magic, f.Items.Stats,
            f.Items.Entities, f.Items.PlayerEntity, () => DaggerfallCalendar.Start, () => null, () => []);
        f.Items.Equipment.Equip(star, [new("amulet0")]);
        held.Refresh();
        FactBuffer<IProductFact> facts = new();
        PresentationState presentation = new("", 5d);
        DaggerfallItemSouls souls = new(new(f.Items.Inventory, f.Items.Instances, f.Items.Definitions.Magic, f.Items.UniqueItems),
            f.Items.Definitions, f.Items.Instances, held, f.Effects, facts, presentation,
            id => id == MagicCastFixture.Monster ? f.MonsterDefinition : null, _ => null, _ => false);

        Assert.Equal(new DaggerfallInventoryUseResult(false, "Azura's Star has no soul to release."), souls.UseAzurasStar(starId));
        souls.CaptureHeldSoul(new ActorDiedFact(MagicCastFixture.Monster, 3, DaggerfallDamageCause.PhysicalAttack, 1, 1, 0, 0));
        Assert.Null(f.Items.Instances.RequireUnique(starId).CapturedSoulMobileId);
        souls.CaptureHeldSoul(new ActorDiedFact(MagicCastFixture.Monster, 1, DaggerfallDamageCause.PhysicalAttack, 1, 1, 0, 0));
        Assert.Equal(f.MonsterDefinition.MobileId, f.Items.Instances.RequireUnique(starId).CapturedSoulMobileId);
        List<IProductFact> appended = [];
        facts.Deliver(appended.Add);
        Assert.Equal(new AzurasStarCaptureFact(MagicCastFixture.Monster, starId, DaggerfallStarCaptureOutcome.Captured), Assert.Single(appended));

        Assert.True(souls.UseAzurasStar(starId).Applied);
        Assert.Null(f.Items.Instances.RequireUnique(starId).CapturedSoulMobileId);
    }

    [Fact]
    public void Detect_Enemy_senses_monsters_within_its_distance_nearest_first_and_nothing_without_a_player()
    {
        using MagicCastFixture f = new();
        DaggerfallEffectLifecycle effects = new(f.Actors, new DaggerfallEffectCatalog(DaggerfallDetectionEffects.Definitions()));
        WorldPoint? player = new WorldPoint(0f, 0f, 0f);
        DaggerfallDetectors detectors = new(effects, f.Actors, f.Items.Containers, 20d, () => player, _ => true,
            id => id == MagicCastFixture.Monster ? f.MonsterDefinition : null, () => [],
            (origin, positions, radius) => positions.Where(pair => Vector3.Distance(pair.Value.ToVector(), origin.ToVector()) <= radius)
                .ToDictionary(pair => pair.Key, pair => (double)Vector3.Distance(pair.Value.ToVector(), origin.ToVector())));
        Assert.Empty(detectors.Read());

        DaggerfallSpellEffectDefinition setting = new("detect", 39, 1, 10, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, effects.Start(new("detect.1", "detect-enemy", "test.detect", 1, 1, "detect", "Magic", null, 1, 10,
            JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(setting, 1, 0, 100), DaggerfallMagicJson.Default.DaggerfallCastEffectState))));
        DaggerfallDetectorView view = Assert.Single(detectors.Read());
        Assert.Equal("enemy", view.Kind);
        DaggerfallDetectionFact contact = Assert.Single(view.Contacts);
        Assert.Equal(("actor", "2", 3d), (contact.Kind, contact.Id, contact.Distance));
        Assert.Equal(DaggerfallIdLabel.For(f.MonsterDefinition.Id.Value), contact.Label);

        player = null;
        Assert.Empty(detectors.Read());
    }

    [Fact]
    public void The_players_cast_keeps_its_chosen_spell_ready_and_a_spellbook_recalls_the_last_spell_cast()
    {
        using MagicCastFixture f = new();
        PresentationState presentation = new("", 5d);
        DaggerfallSpellRelease release = new(f.Casting, f.Targets, f.Items.Definitions, f.Actors, f.Items.Character, f.Items.Inventory,
            presentation, 1.6f, () => f.Positions, _ => throw new InvalidOperationException("No touch spell here."), null!,
            () => default, (_, _) => false, (_, _, _) => { });
        List<DaggerfallCastOutcome> told = [];

        Assert.Equal(DaggerfallCastOutcome.UnknownSpell, release.ReadyPlayerSpell("spell.unknown").Outcome);
        Assert.False(release.Ready(told.Add));
        Assert.Equal("You have not cast a spell yet.", presentation.LastOutcome);
        Assert.Equal(DaggerfallCastOutcome.Unready, release.ReleaseReadySpell(1, Vector3.UnitZ).Outcome);

        Assert.Equal(DaggerfallCastOutcome.Ready, release.ReadyPlayerSpell(f.SpellKey).Outcome);
        Assert.True(release.Ready(told.Add));
        release.Release(Vector3.UnitZ, harmsCivilians: false, told.Add);
        Assert.NotEqual(DaggerfallCastOutcome.Unready, Assert.Single(told));
        Assert.Equal(f.SpellKey, f.Casting.ReadyFor(1)!.SpellKey);

        f.Casting.Cancel(1);
        Assert.True(release.Ready(told.Add));
        Assert.Equal(f.SpellKey, f.Casting.ReadyFor(1)!.SpellKey);
    }

    private static DaggerfallIdentify Identify(MagicCastFixture f, PresentationState presentation, FactBuffer<IProductFact> facts,
        DaggerfallIdentifyRequest restored)
    {
        DaggerfallEquipmentMoves moves = new(f.Items.Inventory, f.Items.Equipment, f.Items.Definitions, itemInstances: f.Items.Instances);
        return new(f.Items.Inventory, f.Items.Instances, new DaggerfallItemConditionService(f.Items.Definitions, f.Items.Instances, moves),
            f.Items.Definitions.Magic, f.Items.Stats, f.Actors.Player.Actor, new DaggerfallVitalityConsequences(new WorldRpg.Kit.Combat.CombatResolution()),
            RandomMinimum.Create(), facts, presentation, _ => "Magic item", restored);
    }
}
