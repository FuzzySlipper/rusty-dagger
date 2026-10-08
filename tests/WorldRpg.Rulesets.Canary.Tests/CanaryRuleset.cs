using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Canary;

/// <summary>
/// A deliberately unlike second ruleset. It shares no vocabulary with the reference ruleset and
/// composes the Kit's actor, combat, effect and equipment owners with its own rules: a roll must
/// reach its chance to hit, nothing records where a blow lands, the defeat track is vitality, an
/// effect is a timed rally rather than a spell, and the warden carries no currency.
/// </summary>
public sealed class CanaryRuleset : IGameRuleset
{
    public static readonly RulesetId Identity = new("canary");
    public static readonly GameBundleId Bundle = new("canary.single-room");

    public RulesetId Id => Identity;
    internal CanarySession? CreatedSession { get; private set; }

    public IGameSession CreateSession(GameSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Composition.Ruleset != Identity) throw new InvalidOperationException("Canary received a different ruleset composition.");
        _ = context.Composition.RequireContentPack(new ContentPackId("canary.single-room"));
        CanarySession session = new(CanaryScenario.SingleRoom, CanaryTuning.Parse(context.Composition.Tuning.Payload.Span));
        CreatedSession = session;
        return session;
    }
}

/// <summary>The canary's adjustable values, read from its tuning payload.</summary>
internal sealed record CanaryTuning(string Label, int HitChance, double StrikeCooldownSeconds, uint RallyRounds, int RallyMight, double MessageSeconds)
{
    internal static CanaryTuning Parse(ReadOnlySpan<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        JsonElement root = document.RootElement;
        CanaryTuning tuning = new(
            root.GetProperty("label").GetString() ?? throw new InvalidDataException("Canary tuning needs a label."),
            root.GetProperty("hitChance").GetInt32(),
            root.GetProperty("strikeCooldownSeconds").GetDouble(),
            root.GetProperty("rallyRounds").GetUInt32(),
            root.GetProperty("rallyMight").GetInt32(),
            root.GetProperty("messageSeconds").GetDouble());
        if (tuning.Label.Length == 0 || tuning.HitChance is < 1 or > 100 || !(tuning.StrikeCooldownSeconds > 0d)
            || tuning.RallyRounds == 0 || tuning.RallyMight <= 0 || !(tuning.MessageSeconds > 0d))
            throw new InvalidDataException($"Canary tuning '{tuning.Label}' has a value outside its range.");
        return tuning;
    }
}

internal abstract record CanaryFact : IWorldRpgFact;
internal sealed record CanaryStruckFact(long Attacker, long Target, int Roll, int Chance, int Damage, double VitalityLost, bool Defeated) : CanaryFact;
internal sealed record CanaryMissedFact(long Attacker, long Target, int Roll, int Chance) : CanaryFact;
internal sealed record CanaryRefusedFact(AttackRefusal Reason) : CanaryFact;
internal sealed record CanaryRallyEndedFact(string Instance) : CanaryFact;

/// <summary>The canary's own accepted strike: the roll it made rides here, never in Kit's outcome.</summary>
internal sealed record CanaryPreparedStrike(double CooldownSeconds, AttackOutcome Outcome, int Roll, int Chance)
    : PreparedAttack(CooldownSeconds, Outcome);

internal sealed class CanarySession : IGameSession, IModeAwareGameSession, IEntryScreenSession
{
    internal const string ActionContract = "canary.action.v1";
    internal const long WardenId = 1;
    internal const long IntruderId = 2;
    private const string StrikeCause = "strike";
    private const string RallySource = "rally";
    private static readonly InventoryItemId Spear = new("spear");

    private readonly ActiveEffectLifecycle _rallies;
    private readonly FactBuffer<CanaryFact> _facts = new();
    private readonly List<CanaryFact> _delivered = [];
    private ulong _nextRally;
    private bool _disposed;

    internal CanarySession(CanaryScenario scenario, CanaryTuning tuning)
    {
        Scenario = scenario;
        Tuning = tuning;
        Presentation = new PresentationState(scenario.Room, tuning.MessageSeconds);
        Actors = new ActorsState();
        // Only attack state is opted in: the canary has no current-target memory and no levels.
        Warden = Actors.CreatePlayer(WardenId, new EntityTypeId(scenario.Actor), Stats(10, 20, 30), Vitality, ActorCapabilities.Attacks);
        Actors.CreateActor(IntruderId, new EntityTypeId("intruder"), Stats(6, 4, 12),
            new ActorPose(new WorldPoint(0f, 0f, -1f), 0f), Vitality, ActorCapabilities.Attacks);
        Combat = new CombatResolution();
        Attacks = new AttackExecution<CanaryFact>(Actors, new CanaryStrikeRules(this));
        _rallies = new ActiveEffectLifecycle(Warden.Effects);
        Equipment = ComposeEquipment(Warden.Actor);

        UiValueBuilder values = new();
        uint vitality = values.String(scenario.Resources[0]);
        uint focus = values.String(scenario.Resources[1]);
        uint resources = values.Array(vitality, focus);
        Hud = values.Build(values.Object(("resources", resources)));
    }

    internal CanaryScenario Scenario { get; }
    internal CanaryTuning Tuning { get; }
    internal PresentationState Presentation { get; }
    internal ActorsState Actors { get; }
    internal PlayerActorState Warden { get; }
    internal ActorState Intruder => Actors.Get(IntruderId);
    internal CombatResolution Combat { get; }
    internal AttackExecution<CanaryFact> Attacks { get; }
    internal MechanicsEquipmentCoordinator Equipment { get; }
    internal IReadOnlyList<ActiveEffectState> Rallies => _rallies.Active;
    internal IReadOnlyList<CanaryFact> Delivered => _delivered;
    internal UiValue Hud { get; }
    internal ProductMode Mode { get; private set; } = ProductMode.Playing;
    internal int InitialPublishCount { get; private set; }
    internal uint AppliedStepCount { get; private set; }
    internal bool IsDisposed => _disposed;
    private string Vitality => Scenario.Resources[0];
    private string Might => Scenario.Statistics[0];
    private string Finesse => Scenario.Statistics[1];

    public ProductMode? PendingModeRequest => null;
    public bool PendingModeRequestClosesModal => false;

    public void ApplyProductMode(ProductMode mode) => Mode = mode;

    public bool RequestsBegin(ReadOnlySpan<ProductInputEvent> input)
    {
        foreach (ProductInputEvent value in input)
            if (Action(value) == "begin") return true;
        return false;
    }

    public void PublishInitial()
    {
        ThrowIfDisposed();
        InitialPublishCount++;
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        ThrowIfDisposed();
        // The entry screen, a modal and death hold the world still; only ordinary play steps it.
        if (Mode != ProductMode.Playing
            || update.Facts.LifecycleState != ProductLifecycleState.Running
            || update.Facts.AdmittedStepCount == 0)
            return ProductUpdateResult.None;

        foreach (ProductInputEvent value in update.Input)
        {
            if (Action(value) == "strike")
                Attacks.Start(new AttackRequest(WardenId, IntruderId, update.Facts.Generation, update.Facts.SimulationStep,
                    update.Facts.FixedDeltaSeconds, Delayed: false), _facts);
        }

        AppliedStepCount = checked(AppliedStepCount + update.Facts.AdmittedStepCount);
        foreach (ActiveEffectLifecycleReceipt ended in _rallies.AdvanceRounds(update.Facts.AdmittedStepCount, RestoreFocus))
            foreach (ActiveEffectState removed in ended.Removed)
                _facts.Append(new CanaryRallyEndedFact(removed.Context.Instance.Value));
        Presentation.Advance(update.Facts.AdmittedStepCount * update.Facts.FixedDeltaSeconds);
        _facts.Deliver(Report);
        return ProductUpdateResult.None;
    }

    /// <summary>
    /// A rally is a timed, non-magical effect: it raises the warden's might for its rounds and
    /// restores one focus at each round, then its contribution leaves through the Kit lifecycle.
    /// </summary>
    internal ActiveEffectState Rally()
    {
        ThrowIfDisposed();
        string instance = $"rally-{++_nextRally}";
        ActiveEffectContext context = new(EffectInstanceId.Parse(instance), new ActiveEffectSource(RallySource), null,
            ActorsState.Identity(WardenId), null);
        EffectDefinition definition = new(EffectDefinitionId.Parse("canary.rally"), StackingGroupId.Parse("canary.rally"),
            EffectStackingPolicy.IndependentByProvenance, maximumInstances: 4, maximumStacks: 1, [SourceDefinitionId.Parse("canary.rally")]);
        MechanicsSourceIdentity provenance = new EffectSourceIdentity(null, context.Instance, 1, SourceDefinitionId.Parse("canary.rally"));
        Stat might = Warden.Stats.GetStat(StatId.Parse(Might));
        StatModifierHandle handle = might.AddModifier(Tuning.RallyMight);
        DelegateActiveEffectContribution contribution = new(() => might.RemoveModifier(handle));
        try
        {
            return _rallies.Admit(definition, ActiveEffectAdmissionKind.Apply, context, provenance, 1, Tuning.RallyRounds,
                [contribution]).Current!;
        }
        catch
        {
            contribution.Remove();
            throw;
        }
    }

    /// <summary>Gives the warden a spear in the canary's single weapon slot; the spear lends its reach to strikes.</summary>
    internal KitUniqueInventoryItem EquipSpear(ulong durableItem, int reach)
    {
        KitUniqueInventoryItem spear = Equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, durableItem), Spear);
        Actors.Store.Add(new EntityId(spear.EntityId), new CombatContributions { Rules = { new ReachContribution(reach) } });
        Equipment.Equip(spear, [new KitEquipmentSlotId(Scenario.WeaponSlot)]);
        return spear;
    }

    internal Track Track(Actor actor, string id) => actor.Get<StatsComponent>().GetTrack(TrackId.Parse(id));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rallies.Dispose();
        Actors.Dispose();
    }

    private void RestoreFocus(ActiveEffectState rally)
    {
        Track focus = Track(Warden.Actor, Scenario.Resources[1]);
        focus.SetCurrent(focus.Current + 1d, clamp: true);
    }

    private void Report(CanaryFact fact)
    {
        _delivered.Add(fact);
        Presentation.SetOutcome(fact switch
        {
            CanaryStruckFact struck => struck.Defeated ? "The intruder falls." : $"Struck for {struck.Damage}.",
            CanaryMissedFact => "The strike goes wide.",
            CanaryRefusedFact refused => $"Not now ({refused.Reason}).",
            CanaryRallyEndedFact => "The rally fades.",
            _ => string.Empty,
        });
    }

    private StatsComponent Stats(double might, double finesse, double vitality)
    {
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(Might), new Stat(might));
        stats.AddStat(StatId.Parse(Finesse), new Stat(finesse));
        Stat vitalityMaximum = new(vitality);
        stats.AddStat(StatId.Parse($"{Vitality}-maximum"), vitalityMaximum);
        stats.AddTrack(TrackId.Parse(Vitality), new Track(vitalityMaximum, vitality));
        Stat focusMaximum = new(5);
        stats.AddStat(StatId.Parse($"{Scenario.Resources[1]}-maximum"), focusMaximum);
        stats.AddTrack(TrackId.Parse(Scenario.Resources[1]), new Track(focusMaximum, 0));
        return stats;
    }

    private MechanicsEquipmentCoordinator ComposeEquipment(Actor warden)
    {
        InventoryStore store = new();
        store.RegisterInventory(new InventoryState(warden.Entity));
        store.RegisterEquipment(new EquipmentState(warden.Entity));
        InventoryComponent inventory = new(store, warden.Entity);
        EquipmentComponent equipment = new(store, warden.Entity);
        warden.Add(inventory);
        warden.Add(equipment);
        ItemClassificationId style = ItemClassificationId.Parse(Scenario.CombatStyle);
        // The canary defines one weapon and no currency item at all.
        Dictionary<InventoryItemId, ItemDefinition> items = new()
        {
            [Spear] = new(ItemDefinitionId.Parse(Spear.Value), ItemKind.Unique, 1, classifications: [style], equipment: new ItemEquipmentPolicy(1)),
        };
        Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition> slots = new()
        {
            [new KitEquipmentSlotId(Scenario.WeaponSlot)] = new(Rusty.Engine.Mechanics.EquipmentSlotId.Parse(Scenario.WeaponSlot), [style]),
        };
        return new MechanicsEquipmentCoordinator(inventory, equipment, Actors.Entities, items, slots);
    }

    private static string? Action(ProductInputEvent value)
    {
        if (!value.PayloadContract.Span.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(ActionContract))) return null;
        using JsonDocument document = JsonDocument.Parse(value.PayloadData);
        return document.RootElement.TryGetProperty("action", out JsonElement action) ? action.GetString() : null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CanarySession));
    }

    /// <summary>
    /// The canary's strike policy. A strike hits only when its roll reaches the chance, which is the
    /// opposite comparison from a roll-under ruleset; its damage is the attacker's might plus any
    /// equipped reach, applied to the vitality track.
    /// </summary>
    private sealed class CanaryStrikeRules(CanarySession session) : IAttackRules<CanaryFact>
    {
        public bool TryPrepare(AttackRequest request, FactBuffer<CanaryFact> facts, out PreparedAttack attack)
        {
            CombatParticipants participants = Participants(request);
            // The canary has no random service; its roll is a deterministic function of the step.
            int roll = checked((int)(request.SimulationStep % 100UL) + 1);
            TryHitEvent hit = session.Combat.TryHit(participants, value =>
            {
                value.Chance = session.Tuning.HitChance;
                value.Roll = roll;
                value.Hit = value.Roll >= value.Chance;
            });
            int damage = hit.Hit
                ? session.Combat.Damage(participants, value =>
                    value.Damage = participants.SourceStats.GetStat(StatId.Parse(session.Might)).ValueInt).Damage
                : 0;
            attack = new CanaryPreparedStrike(session.Tuning.StrikeCooldownSeconds, new AttackOutcome(hit.Hit, true, damage), hit.Roll, hit.Chance);
            return true;
        }

        public void Refused(long attackerId, AttackRefusal reason, FactBuffer<CanaryFact> facts) => facts.Append(new CanaryRefusedFact(reason));

        public void Started(AttackRequest request, PreparedAttack attack, FactBuffer<CanaryFact> facts) { }

        public void Apply(AttackRequest request, PreparedAttack attack, FactBuffer<CanaryFact> facts)
        {
            CanaryPreparedStrike strike = (CanaryPreparedStrike)attack;
            long target = request.TargetId!.Value;
            if (!strike.Outcome.Hit)
            {
                facts.Append(new CanaryMissedFact(request.AttackerId, target, strike.Roll, strike.Chance));
                return;
            }
            CombatParticipants participants = Participants(request);
            ApplyHitEvent applied = session.Combat.ApplyToHealth(participants, strike.Outcome.Damage,
                session.Track(participants.Target, session.Vitality));
            facts.Append(new CanaryStruckFact(request.AttackerId, target, strike.Roll, strike.Chance,
                applied.CalculatedDamage, applied.ActualHealthLost, applied.Defeated));
        }

        private CombatParticipants Participants(AttackRequest request) => new(
            new Actor(session.Actors.Store, session.Actors.Entities.Resolve(ActorsState.Identity(request.AttackerId))),
            new Actor(session.Actors.Store, session.Actors.Entities.Resolve(ActorsState.Identity(request.TargetId!.Value))),
            StrikeCause);
    }

    private sealed class ReachContribution(int reach) : ICombatContribution
    {
        public void Damage(DamageEvent interaction) => interaction.Damage += reach;
    }
}

internal sealed record CanaryScenario(
    IReadOnlyList<string> Statistics,
    IReadOnlyList<string> Resources,
    bool HasCurrency,
    string WeaponSlot,
    string CombatStyle,
    string Room,
    string Actor)
{
    internal static readonly CanaryScenario SingleRoom = new(
        ["might", "finesse"],
        ["vitality", "focus"],
        HasCurrency: false,
        WeaponSlot: "weapon",
        CombatStyle: "reach",
        Room: "observatory",
        Actor: "warden");
}
