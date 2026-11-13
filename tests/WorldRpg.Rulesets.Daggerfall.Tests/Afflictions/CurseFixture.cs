using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Lycanthropy and vampirism built without the session: the player, the racial override over an effect lifecycle,
/// the healing a new curse brings, a clock, a keyed draw that answers <c>roll</c>, recorded quests and recorded time.
/// </summary>
internal sealed class CurseFixture : IDisposable
{
    internal CurseFixture(long roll = 1, long startMinute = 1440L * 365)
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);
        Character = new DaggerfallCharacterState(definitions, stats, player);
        Random = KeyedRandomFake.Create(roll);
        Time = new DaggerfallWorldTime(DaggerfallCalendar.FromAbsoluteSeconds(startMinute * 60), 0d, 12d);
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;
        Effects = new(Actors, new DaggerfallEffectCatalog([DaggerfallRacialOverrides.Definition(Character, tuning.Lycanthropy, tuning.Vampirism)]));
        Overrides = new DaggerfallRacialOverrides(Effects, Character, definitions, previous => Vampirism!.Ended(previous));
        Character.UseRaceOverride(Overrides.ApplyToBirthRace);
        CombatResolution combat = new();
        DaggerfallDiseases diseases = new(Effects, Actors, Random.Service, Time, Character, combat, Presentation, definitions);
        DaggerfallPoisoning poisoning = new(Effects, Random.Service, Actors, Character, new DaggerfallItemInstances(),
            new Dictionary<long, DaggerfallActorDefinition>(), definitions, combat, 0);
        DaggerfallTransformationHealing healing = new(diseases, poisoning, Effects, Actors, Character);
        Lycanthropy = new DaggerfallLycanthropy(Overrides, Time, tuning.Lycanthropy, () => RingWorn, () => HandsEmptied++,
            Actors, Random.Service, Presentation, Quests, healing, Elapsed.Add, QuestTime.Add);
        Vampirism = new DaggerfallVampirism(Overrides, Time, tuning.Vampirism, definitions.Factions, Random.Service,
            Presentation, Quests, healing, Elapsed.Add, QuestTime.Add);
    }

    internal ActorsState Actors { get; } = new();
    internal DaggerfallCharacterState Character { get; }
    internal KeyedRandomFake Random { get; }
    internal DaggerfallWorldTime Time { get; }
    internal DaggerfallEffectLifecycle Effects { get; }
    internal DaggerfallRacialOverrides Overrides { get; }
    internal PresentationState Presentation { get; } = new("Ready", 5d);
    internal RecordedQuests Quests { get; } = new();
    internal DaggerfallLycanthropy Lycanthropy { get; }
    internal DaggerfallVampirism Vampirism { get; }
    internal bool RingWorn { get; set; }
    internal int HandsEmptied { get; private set; }

    /// <summary>The elapsed and quest-owned seconds the curses asked to pass.</summary>
    internal List<long> Elapsed { get; } = [];
    internal List<long> QuestTime { get; } = [];

    internal Track Health => Actors.Player.Stats.GetTrack(TrackId.Parse("health"));

    internal static DaggerfallInfectionTransition Transition(DaggerfallInfectionKind kind, int region = 17) =>
        new("infection", kind, 0, region);

    /// <summary>Moves the clock on, as the session's elapsed list would.</summary>
    internal void Pass(long seconds) => _ = Time.AdvanceInterval(seconds, []);

    public void Dispose() => Actors.Dispose();

    /// <summary>The quests the curses asked about, offered and failed.</summary>
    internal sealed class RecordedQuests : IDaggerfallAfflictionQuests
    {
        internal HashSet<string> ActiveSources { get; } = new(StringComparer.Ordinal);
        internal List<string> Work { get; } = [];
        internal List<(string Instance, string Source, int Faction)> Offered { get; } = [];
        internal List<(string Prefix, string Reason)> Failed { get; } = [];
        internal bool Refuse { get; set; }

        public bool Active(string sourceFile) => ActiveSources.Contains(sourceFile);
        public bool Started(string instanceId) => Offered.Any(offer => offer.Instance == instanceId);
        public IReadOnlyList<string> ClanWork(int clan) => Work;

        public bool Offer(string instanceId, string sourceFile, int factionId)
        {
            if (Refuse) return false;
            Offered.Add((instanceId, sourceFile, factionId));
            return true;
        }

        public void FailActive(string definitionPrefix, string reason) => Failed.Add((definitionPrefix, reason));
    }
}
