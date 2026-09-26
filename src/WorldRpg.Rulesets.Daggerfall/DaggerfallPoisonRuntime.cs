using System.Text.Json;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rusty.Engine;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The draw identity a poison's arms come from. The key carries the poison's own ordinal rather than a wall
/// clock, so the same sequence of minutes draws the same values when a session is replayed; the ordinal
/// travels in the poison's effect state, so a reloaded poison continues its own sequence.
/// </summary>
internal static class DaggerfallPoisonRandomKey
{
    internal const ulong Seed = 0;
    internal const string Scope = "dagger.poison.v1";
    internal const string MinuteScope = "dagger.poison.minute.v1";

    internal static string For(long entityId, long ordinal, string arm) =>
        $"entity:{entityId}:draw:{ordinal}:arm:{arm}";
}

/// <summary>
/// The poisons an actor carries, started, read and cured through the effect lifecycle.
/// </summary>
/// <remarks>
/// A poison <b>is</b> an effect here rather than a mechanism beside one. The donor's poison owns a course and
/// a set of stat modifications, which is exactly what an active effect owns: an instance identity, a target,
/// durable state, contributions cleaned when it ends, and one save entry. Holding poisons anywhere else
/// produced stat sources no cleanup owner could account for, and the save refuses those by design — there is
/// no exemption here, only the effect the sources belong to.
/// </remarks>
internal sealed class DaggerfallPoisonRuntime
{
    /// <summary>The source key every poison effect is started under, and the key a cure selects them by.</summary>
    internal const string SourceKey = "poison";

    /// <summary>The settings string a poison effect carries; the archetype is named by the effect key.</summary>
    internal const string Settings = "classic";

    private readonly DaggerfallEffectLifecycle _effects;
    private readonly Func<int, int, int> _roll;
    private long _draws;

    /// <param name="effects">The lifecycle that owns every poison's instance, contributions and save entry.</param>
    /// <param name="roll">Draws an inclusive lower and upper bound, the way an archetype window is written.</param>
    internal DaggerfallPoisonRuntime(DaggerfallEffectLifecycle effects, Func<int, int, int> roll)
    {
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _roll = roll ?? throw new ArgumentNullException(nameof(roll));
    }

    /// <summary>How many poisons are held, an actor's ongoing one and the residue of its completed ones together.</summary>
    internal int Count => Active.Count;

    /// <summary>Whether an actor carries a poison or the damage one left behind.</summary>
    internal bool IsAfflicted(Actor actor) => Active.Any(effect => SameActor(effect.Target, actor));

    /// <summary>
    /// The affliction an actor is running, or the residue it carries when nothing is running: a poison that
    /// has completed stays held only for the damage it left, so that is what a reader is told about.
    /// </summary>
    internal DaggerfallPoisonAffliction? Affliction(Actor actor)
    {
        DaggerfallActiveEffect? effect = Active.FirstOrDefault(candidate => SameActor(candidate.Target, actor)
            && DaggerfallPoisonArms.State(candidate).Affliction.Phase != DaggerfallPoisonPhase.Complete)
            ?? Active.FirstOrDefault(candidate => SameActor(candidate.Target, actor));
        return effect is null ? null : DaggerfallPoisonArms.State(effect).Affliction;
    }

    /// <summary>Whether an actor carries attribute damage a poison left on it.</summary>
    internal bool HasPersistingDamage(Actor actor) => Active.Any(effect => SameActor(effect.Target, actor)
        && DaggerfallPoisonArms.State(effect).Totals.Count > 0);

    /// <summary>
    /// Afflicts an actor with one classic variant. An actor already carrying a running poison keeps the worse
    /// of the two rather than being poisoned twice: the donor's incumbent rule lets the longer affliction win,
    /// which is what stops a second scratch from shortening a poison already running.
    /// </summary>
    internal bool Afflict(Actor actor, int variant)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!DaggerfallPoisonArchetypes.TryResolve(variant, out DaggerfallPoisonArchetype archetype)) return false;
        DaggerfallPoisonAffliction applying = new(
            archetype,
            _roll(archetype.MinimumOnsetMinutes, archetype.MaximumOnsetMinutes),
            _roll(archetype.MinimumDurationMinutes, archetype.MaximumDurationMinutes));
        DaggerfallActiveEffect? running = Active.FirstOrDefault(effect => SameActor(effect.Target, actor)
            && DaggerfallPoisonArms.State(effect).Affliction.Phase != DaggerfallPoisonPhase.Complete);
        if (running is not null)
        {
            DaggerfallPoisonState incumbent = DaggerfallPoisonArms.State(running);
            if (!incumbent.Affliction.SupersededBy(applying)) return false;
            // The older poison stops where it stands and whatever it helped with comes back off, but the
            // damage it already did stays on the actor: taking a second poison must not heal the first.
            DaggerfallPoisonArms.CompleteCourse(running, incumbent);
        }

        DaggerfallPoisonState state = new(
            applying,
            Admitted: false,
            Draw: ++_draws,
            new Dictionary<string, int>(StringComparer.Ordinal));
        DaggerfallEffectAdmissionOutcome started = _effects.Start(new DaggerfallEffectRequest(
            InstanceFor(archetype),
            archetype.Key,
            SourceKey,
            CasterId: null,
            checked((long)actor.Entity.Value),
            Settings,
            Element: null,
            ItemId: null,
            Stacks: 1,
            // The poison's own state owns its course: the lifecycle must not expire an effect whose arms are
            // still holding damage, which is why the course is minutes in state rather than remaining rounds.
            RemainingRounds: null,
            DaggerfallPoisonState.Write(state)));
        return started is DaggerfallEffectAdmissionOutcome.Started or DaggerfallEffectAdmissionOutcome.Replaced;
    }

    /// <summary>Cures an actor: its poisons end and everything they still hold on it is removed.</summary>
    internal bool Cure(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        bool cured = false;
        foreach (DaggerfallActiveEffect effect in Active.Where(candidate => SameActor(candidate.Target, actor)).ToArray())
            cured |= _effects.Cure(effect.Lifecycle.Context.Instance);
        return cured;
    }

    /// <summary>The live poison effects, in the lifecycle's own stable instance order.</summary>
    private IReadOnlyList<DaggerfallActiveEffect> Active => _effects.Active
        .Where(effect => effect.Lifecycle.Context.Source.Key == SourceKey)
        .ToArray();

    /// <summary>
    /// Whether a live effect is on the actor the caller means. The actor a caller holds and the one an
    /// effect resolved are facades over the same entity rather than the same object, so the entity is what
    /// answers the question.
    /// </summary>
    private static bool SameActor(Actor live, Actor asked) => live.Entity == asked.Entity;

    /// <summary>A poison instance name nothing active already carries, so a reload cannot collide with a dose.</summary>
    private string InstanceFor(DaggerfallPoisonArchetype archetype)
    {
        string prefix = $"{SourceKey}.{archetype.Variant}.";
        long next = 1;
        foreach (DaggerfallActiveEffect effect in Active)
        {
            string instance = effect.Lifecycle.Context.Instance.Value;
            if (instance.StartsWith(prefix, StringComparison.Ordinal)
                && long.TryParse(instance[prefix.Length..], out long ordinal))
            {
                next = Math.Max(next, checked(ordinal + 1));
            }
        }

        return $"{prefix}{next}";
    }
}

/// <summary>
/// One poison's durable state: where it is in its course, whether the round it was handed over in has been
/// consumed, the ordinal its next arm draw uses, and what each attribute arm has made of its attribute.
/// </summary>
/// <remarks>
/// The attribute contributions themselves are deliberately absent. They live on the actor as stat sources
/// keyed by the effect instance, and the stats save already rebuilds them, so carrying the totals here as well
/// would give the same number two owners; what a reload needs is the course and the draw ordinal, and the
/// totals are verified against the sources the stats save restored.
/// </remarks>
internal sealed record DaggerfallPoisonState(
    DaggerfallPoisonAffliction Affliction,
    bool Admitted,
    long Draw,
    IReadOnlyDictionary<string, int> Totals)
{
    internal static JsonElement Write(DaggerfallPoisonState state)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("minutesToStart", state.Affliction.MinutesToStart);
            writer.WriteNumber("minutesRemaining", state.Affliction.MinutesRemaining);
            writer.WriteBoolean("admitted", state.Admitted);
            writer.WriteNumber("draw", state.Draw);
            writer.WritePropertyName("totals");
            writer.WriteStartObject();
            foreach ((string stat, int total) in state.Totals.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                writer.WriteNumber(stat, total);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    internal static DaggerfallPoisonState Read(JsonElement state, DaggerfallPoisonArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        if (state.ValueKind != JsonValueKind.Object
            || !state.TryGetProperty("minutesToStart", out JsonElement onset)
            || !onset.TryGetInt32(out int minutesToStart) || minutesToStart < 0
            || !state.TryGetProperty("minutesRemaining", out JsonElement remaining)
            || !remaining.TryGetInt32(out int minutesRemaining) || minutesRemaining < 0
            || !state.TryGetProperty("admitted", out JsonElement admitted)
            || admitted.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !state.TryGetProperty("draw", out JsonElement draw)
            || !draw.TryGetInt64(out long ordinal) || ordinal < 0
            || !state.TryGetProperty("totals", out JsonElement totals)
            || totals.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Poison effect state is malformed.", nameof(state));
        }

        Dictionary<string, int> carried = new(StringComparer.Ordinal);
        foreach (JsonProperty property in totals.EnumerateObject())
        {
            if (!DaggerfallPoisonArms.IsAttribute(property.Name)
                || !property.Value.TryGetInt32(out int total)
                || total == 0
                || !carried.TryAdd(property.Name, total))
            {
                throw new ArgumentException("Poison attribute state is malformed.", nameof(state));
            }
        }

        DaggerfallPoisonAffliction affliction = minutesRemaining < 1 && minutesToStart < 1
            ? DaggerfallPoisonAffliction.AlreadyComplete(archetype)
            : new DaggerfallPoisonAffliction(archetype, minutesToStart, minutesRemaining);
        return new DaggerfallPoisonState(affliction, admitted.GetBoolean(), ordinal, carried);
    }
}

/// <summary>
/// The arms a poison applies each minute, and the sources a completed one has to take back.
/// </summary>
/// <remarks>
/// The donor draws a sharp line through a poison's end, and this follows it. A drug's arms that help the
/// victim are <b>taken back</b> when the poison completes, while the attribute damage a poison does
/// <b>persists</b> until the victim is cured or heals the attribute back — so a completed affliction whose
/// arms are still on the actor stays active, holding the sources that will remove them, and leaves the
/// lifecycle only when nothing of it remains. Health, fatigue and magicka arms are one-off applications: the
/// donor changes them as they tick and has nothing to take back.
/// </remarks>
internal static class DaggerfallPoisonArms
{
    /// <summary>The source definition a poison's attribute arms carry. The identity is one per poison instance.</summary>
    internal const string AttributeSource = "daggerfall.poison.attributes";

    /// <summary>Whether a stat name is one of the eight attributes a poison can touch.</summary>
    internal static bool IsAttribute(string stat) => Attributes.ContainsKey(stat);

    /// <summary>One live poison's durable state, read through the archetype its effect key names.</summary>
    internal static DaggerfallPoisonState State(DaggerfallActiveEffect effect) =>
        DaggerfallPoisonState.Read(effect.State, Archetype(effect));

    /// <summary>
    /// Passes one minute for one live poison and writes its new state back. The lifecycle calls this for the
    /// assignment round as well, which is the round the poison was handed over in: that round is consumed
    /// without spending a minute, so a poison's onset starts counting on the following minute.
    /// </summary>
    internal static void AdvanceMinute(
        DaggerfallActiveEffect effect,
        DaggerfallPoisonArchetype archetype,
        IRandomService random,
        DaggerfallVitalityConsequences vitality)
    {
        DaggerfallPoisonState state = DaggerfallPoisonState.Read(effect.State, archetype);
        if (!state.Admitted)
        {
            Write(effect, state with { Admitted = true });
            return;
        }

        if (state.Affliction.Phase == DaggerfallPoisonPhase.Complete)
        {
            CompleteCourse(effect, state);
            return;
        }

        IReadOnlyList<DaggerfallPoisonEffect> arms = state.Affliction.AdvanceMinute();
        Dictionary<string, int> totals = new(state.Totals, StringComparer.Ordinal);
        long draw = state.Draw;
        foreach (DaggerfallPoisonEffect arm in arms)
        {
            int amount = Draw(random, effect, ++draw, arm);
            if (amount != 0) Apply(effect, arm, amount, totals, vitality);
        }

        DaggerfallPoisonState advanced = new(state.Affliction, Admitted: true, draw, totals);
        Write(effect, advanced);
        if (state.Affliction.Phase == DaggerfallPoisonPhase.Complete) CompleteCourse(effect, advanced);
    }

    /// <summary>
    /// A poison whose course has ended: a drug's helping arms come back off, and attribute damage stays where
    /// the donor leaves it. Once nothing of the poison is left on the actor, the effect that owned it goes
    /// too, which is what releases a residue a cure has emptied.
    /// </summary>
    internal static void CompleteCourse(DaggerfallActiveEffect effect, DaggerfallPoisonState state)
    {
        state.Affliction.Cure();
        Dictionary<string, int> remaining = new(state.Totals, StringComparer.Ordinal);
        if (state.Affliction.Archetype.Kind == DaggerfallPoisonKind.Drug)
        {
            foreach (DaggerfallPoisonEffect arm in state.Affliction.Archetype.Effects.Where(
                arm => arm.IsPositive && arm.Target == DaggerfallPoisonTarget.Attribute))
            {
                DaggerfallStatId attribute = AttributeId(arm.Attribute!);
                if (!remaining.Remove(attribute.Value)) continue;
                Withdraw(effect, attribute);
            }
        }

        Write(effect, state with { Totals = remaining });
        if (remaining.Count == 0)
        {
            // The lifecycle ends the effect after this round, which runs its cleanup exactly once.
            effect.ExpireAfterCurrentRound = true;
        }
    }

    /// <summary>Rebuilds one poison's stat sources from the state a save carried.</summary>
    internal static void Resume(DaggerfallActiveEffect effect, DaggerfallPoisonState state)
    {
        StatsComponent stats = effect.Target.Get<StatsComponent>();
        EffectSourceIdentity identity = IdentityFor(effect);
        foreach ((string stat, int total) in state.Totals)
        {
            int applied = stats.GetStat(StatId.Parse(stat)).Sources
                .Where(source => source.Identity == identity)
                .Sum(source => source.Contributions.Count == 1 && source.Contributions[0].Contribution is StatContribution.Add add
                    ? checked((int)add.Amount)
                    : 0);
            if (applied != total)
            {
                throw new ArgumentException($"Restored poison '{effect.Context.Instance.Value}' has attribute contributions that do not match its durable poison state.");
            }
        }
    }

    /// <summary>Removes every arm source one poison instance owns, which is what a cure or its end does.</summary>
    internal static void RemoveArms(DaggerfallActiveEffect effect)
    {
        DaggerfallPoisonState state = State(effect);
        StatsComponent stats = effect.Target.Get<StatsComponent>();
        EffectSourceIdentity identity = IdentityFor(effect);
        // Only the attributes this poison actually touched are visited: an actor that never carried one of
        // the eight has no such stat, and asking for it would fail a cleanup that has nothing to do there.
        foreach (string attribute in state.Totals.Keys)
            _ = stats.GetStat(StatId.Parse(attribute)).RemoveSource(identity);
    }

    /// <summary>
    /// Adds one attribute arm to what the poison has already made of that attribute and writes the running
    /// total back as the source's contribution. Accumulating rather than keeping a modifier per minute keeps
    /// the actor's modifier list as long as the poison, not as long as its course.
    /// </summary>
    private static void Accumulate(DaggerfallActiveEffect effect, DaggerfallStatId attribute, int amount, Dictionary<string, int> totals)
    {
        int total = checked(totals.GetValueOrDefault(attribute.Value) + amount);
        totals[attribute.Value] = total;
        StatsComponent stats = effect.Target.Get<StatsComponent>();
        EffectSourceIdentity identity = IdentityFor(effect);
        Stat stat = stats.GetStat(StatId.Parse(attribute.Value));
        List<StatSource> sources = [.. stat.Sources.Where(source => source.Identity != identity)];
        sources.Add(new StatSource(
            identity,
            SourceDefinitionId.Parse(AttributeSource),
            priority: 0,
            [new StatContributionDefinition(
                StatId.Parse(attribute.Value),
                StackingGroupId.Parse($"daggerfall.poison.{attribute.Value}"),
                MechanicsStackingPolicy.Sum,
                new StatContribution.Add(total))]));
        stat.SetSources(StatId.Parse(attribute.Value), sources);
    }

    /// <summary>Takes one attribute arm back off the actor, which is also removing the source it owns.</summary>
    private static void Withdraw(DaggerfallActiveEffect effect, DaggerfallStatId attribute)
    {
        StatsComponent stats = effect.Target.Get<StatsComponent>();
        _ = stats.GetStat(StatId.Parse(attribute.Value)).RemoveSource(IdentityFor(effect));
    }

    private static void Apply(DaggerfallActiveEffect effect, DaggerfallPoisonEffect arm, int amount, Dictionary<string, int> totals, DaggerfallVitalityConsequences vitality)
    {
        // Two encodings meet here and the archetype table is explicit about both: a vital arm records how
        // much it moves and carries its direction in the positive flag, while an attribute arm records the
        // signed change itself, because a poison drains some attributes and a drug raises others.
        switch (arm.Target)
        {
            case DaggerfallPoisonTarget.Health when !arm.IsPositive && amount > 0:
                _ = vitality.ResolvePoisonDamage(effect.Target, amount);
                return;
            case DaggerfallPoisonTarget.Fatigue:
                Adjust(effect.Target, DaggerfallMechanicsIds.Stamina, arm.IsPositive ? amount : -amount);
                return;
            case DaggerfallPoisonTarget.Magicka:
                Adjust(effect.Target, DaggerfallMechanicsIds.Magicka, arm.IsPositive ? amount : -amount);
                return;
            case DaggerfallPoisonTarget.Attribute:
                Accumulate(effect, AttributeId(arm.Attribute!), amount, totals);
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// Moves a vital track by the arm's own sign, which is how the donor's draining and restoring arms read:
    /// a negative arm takes what is there and no more, a positive one does not pass the maximum.
    /// </summary>
    private static void Adjust(Actor actor, DaggerfallTrackId track, int amount)
    {
        Track value = actor.Get<StatsComponent>().GetTrack(TrackId.Parse(track.Value));
        value.SetCurrent(value.Current + amount, clamp: true);
    }

    private static int Draw(IRandomService random, DaggerfallActiveEffect effect, long ordinal, DaggerfallPoisonEffect arm)
    {
        string key = DaggerfallPoisonRandomKey.For(
            checked((long)effect.Target.Entity.Value),
            ordinal,
            arm.Target == DaggerfallPoisonTarget.Attribute
                ? $"{arm.Attribute}:{(arm.IsPositive ? "help" : "harm")}"
                : arm.Target.ToString());
        return checked((int)random.DrawKeyed(new KeyedRngRequest(
            DaggerfallPoisonRandomKey.Seed,
            DaggerfallPoisonRandomKey.MinuteScope,
            key,
            arm.Minimum,
            arm.Maximum)).Value);
    }

    /// <summary>
    /// The source one poison instance's arms own. Its effect instance is the identity, so the save can
    /// attribute every arm to the effect that has to clean it up.
    /// </summary>
    private static EffectSourceIdentity IdentityFor(DaggerfallActiveEffect effect) => new(
        effect.Target.Entity,
        effect.Context.Instance,
        1,
        SourceDefinitionId.Parse(AttributeSource));

    /// <summary>The archetype a live poison's effect key names.</summary>
    internal static DaggerfallPoisonArchetype Archetype(DaggerfallActiveEffect effect) =>
        DaggerfallPoisonArchetypes.All.SingleOrDefault(archetype => StringComparer.Ordinal.Equals(archetype.Key, effect.Definition.Key))
        ?? throw new InvalidOperationException($"Effect '{effect.Definition.Key}' is not a classic poison archetype.");

    /// <summary>The attribute an archetype's arm names, in the product's own vocabulary.</summary>
    internal static DaggerfallStatId AttributeId(string attribute) => Attributes.TryGetValue(attribute, out DaggerfallStatId id)
        ? id
        : throw new InvalidOperationException($"Poison archetype names attribute '{attribute}', which the product does not carry.");

    private static readonly IReadOnlyDictionary<string, DaggerfallStatId> Attributes = new Dictionary<string, DaggerfallStatId>(StringComparer.Ordinal)
    {
        ["strength"] = DaggerfallMechanicsIds.Strength,
        ["intelligence"] = DaggerfallMechanicsIds.Intelligence,
        ["willpower"] = DaggerfallMechanicsIds.Willpower,
        ["agility"] = DaggerfallMechanicsIds.Agility,
        ["endurance"] = DaggerfallMechanicsIds.Endurance,
        ["personality"] = DaggerfallMechanicsIds.Personality,
        ["speed"] = DaggerfallMechanicsIds.Speed,
        ["luck"] = DaggerfallMechanicsIds.Luck,
    };

    /// <summary>Writes an effect's new state back, which is the only way a poison's course moves.</summary>
    private static void Write(DaggerfallActiveEffect effect, DaggerfallPoisonState state) => effect.State = DaggerfallPoisonState.Write(state);
}
