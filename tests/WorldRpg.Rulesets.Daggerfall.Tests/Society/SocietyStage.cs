using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Society's owners' inputs without the session: the player with the classic stats, the NPC registry and its
/// civilians at an exterior site the player stands at, the crime and social state, a keyed draw that answers
/// <c>roll</c>, a perception that sees no one, the crime owner with its gold grants and provocations recorded, and
/// the law over them with its sentence time, purse and hostility recorded (no watch arrives or converts here).
/// </summary>
internal sealed class SocietyStage : IDisposable
{
    internal SocietyStage(long roll = 1)
    {
        Definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = Definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = _mechanics.CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, Definitions.Catalogs.RequireCareer("class00")));
        Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);
        Character = new DaggerfallCharacterState(Definitions, stats, player);
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;
        Effects = new(Actors, new DaggerfallEffectCatalog([DaggerfallRacialOverrides.Definition(Character, tuning.Lycanthropy, tuning.Vampirism)]));
        Overrides = new DaggerfallRacialOverrides(Effects, Character, Definitions);
        SkillUses = new DaggerfallSkillUseReactions(Actors.Player.Progression, stats, Definitions, () => Character.Career);
        Site = Definitions.Locations.Records.First(site => site.Exterior is not null);
        Sites = new DaggerfallSiteContext(Definitions.Locations);
        Sites.Enter(Site.Id);
        Npcs.Identities = new DurableIdentityAllocator(DurableIdentityKind.Actor, 100);
        Social = new DaggerfallSocialState(Definitions.Factions);
        Random = KeyedRandomFake.Create(roll);
        Crimes = new DaggerfallCrimes(Crime, Actors, Npcs, Player, Sites, ActorDefinitions, Definitions, ItemInstances, SkillUses,
            Overrides, Random.Service, Perception.Service, () => new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            () => Calendar, Granted.Add, Provoked.Add);
        _ = CombatCollaborators.Targeting(Actors, ActorDefinitions, out _spatial, out _);
        Placement = new DaggerfallActorPlacement(Actors, Player, Npcs.IsGameplayActive, ActorDefinitions, _ => null, _ => false, _ => true,
            _spatial, Perception.Service, Random.Service, () => default, allyDetectionDistance: 10d, fieldOfViewYDegrees: 60f);
        Active = DaggerfallWorldProfileIds.Exterior(Site.Id);
        Law = new DaggerfallLaw(Crime, Social, Definitions, DaggerfallTuning.Defaults.Law, Crimes, Actors, Npcs, Player, Sites, Placement,
            SkillUses, Overrides,
            activeProfile: () => Active,
            calendar: () => Calendar,
            passTime: seconds =>
            {
                Passed.Add(seconds);
                long applied = Math.Min(seconds, PassLimit);
                return new(Calendar, applied, seconds - applied, DaggerfallCalendar.NoConsequence, default);
            },
            gold: () => Gold,
            locationLoaded: () => true,
            activeInputs: () => throw new InvalidOperationException("No profile inputs in this stage."),
            profiles: () => null,
            returnProfile: () => Active,
            spawn: (_, _) => throw new InvalidOperationException("No watch arrives in this stage."),
            makeHostile: Hostile.Add,
            retireCivilian: _ => throw new InvalidOperationException("No guard converts in this stage."));
        Crime.IncidentRecorded += Law.Admit;
    }

    internal DaggerfallDefinitions Definitions { get; }
    internal ActorsState Actors { get; } = new();
    internal DaggerfallCharacterState Character { get; }
    internal DaggerfallEffectLifecycle Effects { get; }
    internal DaggerfallRacialOverrides Overrides { get; }
    internal DaggerfallSkillUseReactions SkillUses { get; }
    internal DaggerfallSiteRecord Site { get; }
    internal DaggerfallSiteContext Sites { get; }
    internal DaggerfallNpcRegistry Npcs { get; } = new();
    internal DaggerfallSocialState Social { get; }
    internal DaggerfallCrimeState Crime { get; } = new(null, () => false);
    internal DaggerfallItemInstances ItemInstances { get; } = new();
    internal PlayerControlState Player { get; } = new(null, 0f, 0f);
    internal Dictionary<long, DaggerfallActorDefinition> ActorDefinitions { get; } = [];
    internal KeyedRandomFake Random { get; }
    internal PerceptionFake Perception { get; } = PerceptionFake.Create();
    internal DaggerfallCalendar Calendar { get; set; } = DaggerfallCalendar.Start;
    internal DaggerfallCrimes Crimes { get; }
    internal DaggerfallActorPlacement Placement { get; }
    internal DaggerfallLaw Law { get; }
    internal DaggerfallWorldProfileKey Active { get; set; }
    internal ulong Gold { get; set; }
    /// <summary>The most a sentence's pass-time applies before a consequence would stop it.</summary>
    internal long PassLimit { get; set; } = long.MaxValue;
    internal List<long> Passed { get; } = [];
    internal List<long> Hostile { get; } = [];
    internal List<InventoryAtomicGrant> Granted { get; } = [];
    internal List<long> Provoked { get; } = [];
    internal DaggerfallNpcSite NpcSite => new(Site.Id.Region, Site.Name, string.Empty);

    private readonly DaggerfallMechanicsState _mechanics = new();
    private readonly InventoryStore _inventories = new();
    private long _nextWatch = 9000;
    private readonly SpatialMovementSystem _spatial;

    /// <summary>A civilian of the site the player stands at, offering these services, live as an actor at the origin.</summary>
    internal long Civilian(string role = "civilian", params string[] services)
    {
        long id = Npcs.RegisterCivilian(NpcSite, new DaggerfallNpcAppearance("breton", "Male", 210, 4, 7, 0), role, services);
        DaggerActorFactory.CreateCivilianActor(_mechanics, Actors, _inventories, Npcs.Require(id), default);
        return id;
    }

    /// <summary>A live city watchman the roster's definitions name as mobile 146.</summary>
    internal long CityWatch()
    {
        long id = _nextWatch++;
        DaggerfallActorDefinition watch = Definitions.Actors.Values.Single(definition => definition.MobileId == 146);
        DaggerActorFactory.CreateNonPlayerActor(Actors, id, watch, _mechanics.CreateStats(watch, DaggerActorFactory.InitialVitals(Random.Service, watch, id)), default);
        ActorDefinitions[id] = watch;
        return id;
    }

    /// <summary>Takes the actor's health to its minimum, so it counts as defeated.</summary>
    internal void Defeat(long id)
    {
        Track health = Actors.Get(id).Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(health.Minimum);
    }

    /// <summary>A reported crime of this kind by the player in the site's region, now.</summary>
    internal DaggerfallCrimeIncidentSave Reported(string operation, DaggerfallCrimeKind kind = DaggerfallCrimeKind.Theft) =>
        new(operation, kind, DaggerfallCrimeStage.Completed, DaggerfallActorIdentity.PlayerEntityId, null, Site.Id.Region,
            Calendar.MinuteNumber, DaggerfallCrimeTargetKind.Unknown, DaggerfallCrimeWitnessEvidence.NotQueried, DaggerfallCrimeGuildCredit.None, Reported: true);

    public void Dispose() { _spatial.Dispose(); Actors.Dispose(); }
}
