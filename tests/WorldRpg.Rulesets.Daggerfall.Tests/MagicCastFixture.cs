using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Magic's owners' inputs without the session: the guild maker fixture's player and pack, the Kit actors (the player and
/// one monster), the effect lifecycle over the restoration family, a custom caster-only spell the player bought from the
/// spellmaker, and the casting owner over it.
/// </summary>
internal sealed class MagicCastFixture : IDisposable
{
    internal const long Monster = 2;
    internal GuildMakerFixture Items { get; } = new();
    internal ActorsState Actors { get; } = new();
    internal DaggerfallActorDefinition MonsterDefinition { get; }
    internal DaggerfallEffectLifecycle Effects { get; }
    internal DaggerfallSpellTargets Targets { get; }
    internal DaggerfallCasting Casting { get; }
    internal string SpellKey { get; }
    internal Dictionary<long, WorldPoint> Positions { get; } = [];

    internal MagicCastFixture()
    {
        DaggerfallDefinitions definitions = Items.Definitions;
        MonsterDefinition = definitions.Actors.Values.First(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId is not null);
        Actors.CreatePlayer(1, new EntityTypeId("player"), Items.Stats, "health", DaggerActorFactory.PlayerCapabilities);
        DaggerfallActorDefinition player = definitions.RequireActor(new("player"));
        Actors.CreateActor(Monster, new EntityTypeId("monster"), new DaggerfallMechanicsState().CreateStats(player,
                DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00"))),
            new ActorPose(new WorldPoint(3f, 0f, 0f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
        // The actor factory attaches spell readiness to every actor it makes.
        Actors.Player.Actor.Add(new DaggerfallSpellReadiness());
        Actors.Get(Monster).Actor.Add(new DaggerfallSpellReadiness());
        foreach (StatsComponent stats in new[] { Actors.Player.Stats, Actors.Get(Monster).Stats })
        {
            Track magicka = stats.GetTrack(TrackId.Parse("magicka"));
            magicka.Maximum.BaseValue = 10_000;
            magicka.SetCurrent(10_000);
        }
        Effects = new(Actors, new DaggerfallEffectCatalog(DaggerfallRestorationEffects.Definitions(RandomMinimum.Create(), _ => 1,
            new DaggerfallVitalityConsequences(new WorldRpg.Kit.Combat.CombatResolution()))));

        DaggerfallSpellMaker maker = Items.SpellMaker(Effects.Catalog);
        DaggerfallServiceProvider provider = Items.Provider("make-spells", 40, 64, 0);
        DaggerfallSpellMakerEffect option = maker.Effects.Single(effect => effect.Key == "free-action");
        maker.SetDraft(new("Freedom", 4, 0, 1, [new("free-action", option.Type, option.SubType, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1)]));
        Items.Carry("MiscItems", 132);
        Items.GrantGold(100_000);
        DaggerfallSpellConstructionQuote quote = maker.Quote(provider)!;
        if (!maker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Accepted) throw new InvalidOperationException("The fixture's spell was not bought.");
        SpellKey = Items.Character.KnownSpells.Single();

        Positions[1] = new WorldPoint(0f, 0f, 0f);
        Positions[Monster] = new WorldPoint(3f, 0f, 0f);
        Targets = new(definitions, Actors, Items.Character, Effects, null!, _ => true,
            id => id == Monster ? MonsterDefinition : null, () => Positions, _ => throw new InvalidOperationException("No Engine query here."));
        Casting = new(definitions.Magic, Effects, Targets.CastActor, Targets.MagicProfile, _ => true, _ => { }, _ => { },
            RandomMinimum.Create(), 1, playerKnowsSpell: Items.Character.KnownSpells.Contains, casterLevel: _ => 1);
    }

    public void Dispose() { Actors.Dispose(); Items.Dispose(); }
}
