using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Meaning attached to the copied actor, independent of the source's later lifetime.</summary>
internal sealed record DaggerfallCorruptionOrigin(long ActorId, ulong ItemId)
{
    internal void Validate()
    {
        if (ActorId <= 0 || ItemId == 0) throw new ArgumentException("A corruption copy must name its source actor and item.");
    }
}

/// <summary>
/// The artifacts' powers over creatures: the Sanguine Rose summons an allied Daedroth and the Skull of Corruption an
/// allied copy of the nearest monster, each charging the artifact; the Wabbajack turns the creature it strikes into
/// another; Molag Bal's Mace takes the victim's magicka and strength for its wielder.
/// </summary>
/// <param name="actorOf">The player's or a live site actor's Engine actor.</param>
/// <param name="definitionOf">A site actor's definition, when it has one.</param>
/// <param name="nearbyFoes">
/// The monsters and enemy classes near a point the player can see as foes, nearest first by the query's order, for the
/// eligibility test given (which sees an actor's effective team).
/// </param>
/// <param name="displayable">Whether the site can show a mobile.</param>
/// <param name="spawnPose">A clear pose between two distances of a point, drawn under the operation and random scope.</param>
/// <param name="spawn">Places an actor of a definition at a pose, allied to the player or not; its id.</param>
/// <param name="protectedTarget">Whether a quest protects an actor from being copied or transformed.</param>
/// <param name="transform">Turns a site actor into the mobile given.</param>
internal sealed class DaggerfallArtifactPowers(DaggerfallDefinitions definitions, DaggerfallItemInstances instances,
    MechanicsInventoryCoordinator inventory, DaggerfallItemConditionService itemCondition, DaggerfallEffectLifecycle effects,
    ActorsState actors, IRandomService random, Func<DaggerfallCalendar> calendar, Func<WorldPoint?> playerPosition,
    MechanicsEquipmentCoordinator playerEquipment, DaggerfallActorInventories actorInventories,
    Func<long, Actor?> actorOf, Func<long, DaggerfallActorDefinition?> definitionOf,
    Func<WorldPoint, double, Func<DaggerfallActorDefinition, bool>, IEnumerable<PerceptionPair>> nearbyFoes, Func<int, bool> displayable,
    Func<string, string, WorldPoint, float, float, ActorPose?> spawnPose, Func<string, ActorPose, bool, long> spawn,
    Func<long, bool> protectedTarget, Func<long, int, DaggerfallWabbajackResult> transform)
{
    // SanguineRoseEffect's radius and FoeSpawner's placement envelope, in normalized world metres.
    private const float RoseEnemyRadius = 12f;
    private const float SummonMinimumDistance = 4f;
    private const float SummonMaximumDistance = 20f;

    internal DaggerfallInventoryUseResult UseSanguineRose(KitUniqueInventoryItem source)
    {
        if (playerPosition() is not { } player)
            return new(false, "Sanguine Rose cannot summon without a player position.");
        if (!nearbyFoes(player, RoseEnemyRadius, definition => definition.Team != "player-ally").Any(pair => pair.Distance <= RoseEnemyRadius))
            return new(false, "No monsters nearby.");
        DaggerfallActorDefinition daedroth = definitions.Actors.Values.Single(actor => actor.MobileId == 27);
        if (!displayable(daedroth.MobileId!.Value))
            return new(false, "This site cannot display the Sanguine Rose summon.");
        if (SummonPose(source, player) is not { } pose)
            return new(false, "This site has no clear ground for the Sanguine Rose summon.");
        // The emitted actor belongs to the site, independently of its source item's later lifetime.
        _ = spawn(daedroth.Id.Value, pose, true);
        DaggerfallItemConditionResult charged = itemCondition.Damage(source, 100);
        return new(true, charged.Outcome == DaggerfallItemConditionOutcome.Broken
            ? "Sanguine Rose summoned an allied Daedroth and broke."
            : "Sanguine Rose summoned an allied Daedroth.");
    }

    internal DaggerfallInventoryUseResult UseSkullCorruption(KitUniqueInventoryItem source)
    {
        if (playerPosition() is not { } player)
            return new(false, "Skull of Corruption cannot copy without a player position.");
        var nearest = nearbyFoes(player, 12f,
            definition => definition.Team != "player-ally"
                && definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
            .Where(pair => pair.Distance < 12d)
            .OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target).FirstOrDefault();
        long target = checked((long)nearest.Target);
        if (target == 0 || !actors.TryGet(target, out var original)
            || original.IsDefeated || definitionOf(target) is not { } definition)
            return new(false, "No monsters nearby.");
        if (protectedTarget(target))
            return new(false, "Skull of Corruption cannot copy a protected quest target.");
        if (definition.MobileId is not int mobile || !displayable(mobile))
            return new(false, "This site cannot display the Skull of Corruption copy.");
        if (SummonPose(source, player, "daggerfall.skull-corruption.v1") is not { } pose)
            return new(false, "This site has no clear ground for the Skull of Corruption copy.");
        // The donor creates a fresh allied mobile of the target's type; it does not duplicate
        // the target's wounds, inventory, quest bindings or native handles.
        long copy = spawn(definition.Id.Value, pose, true);
        actors.Get(copy).Actor.Add(new DaggerfallCorruptionOrigin(target,
            inventory.GetDurableItemId(new(source.EntityId)).Value));
        var charged = itemCondition.Damage(source, 100);
        return new(true, charged.Outcome == DaggerfallItemConditionOutcome.Broken
            ? "Skull of Corruption created an allied copy and broke."
            : "Skull of Corruption created an allied copy.");
    }

    private ActorPose? SummonPose(KitUniqueInventoryItem source, WorldPoint player, string randomScope = "daggerfall.sanguine-rose.v1")
    {
        ulong identity = inventory.GetDurableItemId(new(source.EntityId)).Value;
        int condition = instances.RequireUnique(identity).CurrentCondition;
        return spawnPose($"source:{identity}:condition:{condition}", randomScope, player,
            SummonMinimumDistance, SummonMaximumDistance);
    }

    internal DaggerfallWabbajackResult TransformWithWabbajack(long attacker, long target, ulong source, ulong generation, ulong step)
    {
        if (!instances.ContainsUnique(source)
            || instances.RequireUnique(source).CurrentCondition <= 0
            || !actorInventories.EquipmentFor(attacker).Read().Assignments.Any(assignment =>
                actors.Entities.IdentityOf(new EntityId(assignment.Item.EntityId)).Value == source))
            return new(DaggerfallWabbajackOutcome.SourceUnavailable, target);
        if (!actors.TryGet(target, out ActorState? actor) || definitionOf(target) is not { } definition
            || definition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass))
            return new(DaggerfallWabbajackOutcome.InvalidTarget, target);
        if (DaggerfallWabbajack.DefinitionOf(actor.Actor) is not null)
            return new(DaggerfallWabbajackOutcome.AlreadyTransformed, target);
        if (protectedTarget(target)) return new(DaggerfallWabbajackOutcome.ProtectedQuestTarget, target);
        int[] candidates = DaggerfallWabbajack.MobileIds.Where(id => id != definition.MobileId).ToArray();
        int index = checked((int)random.DrawKeyed(new KeyedRngRequest(DaggerfallRandomKey.Seed, "daggerfall.wabbajack.v1",
            $"source:{source}:attacker:{attacker}:target:{target}:generation:{generation}:step:{step}", 0, candidates.Length - 1)).Value);
        return transform(target, candidates[index]);
    }

    /// <summary>Whether a live owner holds Molag Bal's Mace, whole, in either hand.</summary>
    internal bool MolagBalEquipped(long owner, ulong item)
    {
        if (actorOf(owner) is not { } actor || actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current <= 0
            || !instances.ContainsUnique(item) || instances.RequireUnique(item).CurrentCondition <= 0)
            return false;
        var metadata = instances.RequireUnique(item);
        if (metadata.Enchantment is not { } key || !definitions.Magic.TryEnchantments(key, out var payloads)
            || !payloads.Any(payload => payload.Type == 26 && payload.Param == 2)) return false;
        var equipment = owner == actors.Player.DurableId ? playerEquipment : actorInventories.EquipmentFor(owner);
        return equipment.Read().Assignments.Any(assignment => assignment.Slot.Value is "left-hand" or "right-hand"
            && equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == item);
    }

    internal (double Magicka, int Strength) TransferMolagBal(long wielder, long target, ulong item,
        int damage, int strength, ulong generation, ulong step)
    {
        // Combat validates this admitted source before ordinary wear. A break during that
        // accepted strike still drains the victim, but cannot retain a wielder bonus.
        if (actorOf(wielder) is not { } caster || actorOf(target) is not { } victim) return default;
        var stats = caster.Get<StatsComponent>();
        Track sourceMagicka = stats.GetTrack(TrackId.Parse("magicka"));
        Track targetMagicka = victim.Get<StatsComponent>().GetTrack(TrackId.Parse("magicka"));
        // DEC-11: the donor's magicka transfer follows the artifact description, not observed classic fidelity.
        double transferred = Math.Min(damage, Math.Max(0, targetMagicka.Current - targetMagicka.Minimum));
        double overflow = Math.Max(0, sourceMagicka.Current + transferred - sourceMagicka.Maximum.Value);
        string instance = $"molag-bal.{item}.{generation}.{step}.{target}";
        if (strength > 0)
            DaggerfallAttributeDrainEffects.DrainArtifactStrength(effects, instance + ".drain", wielder, target, item, strength);
        var state = new DaggerfallMolagBalState(DaggerfallMolagBalEffects.Minute(calendar()), overflow, strength);
        if (MolagBalEquipped(wielder, item))
            effects.Start(new(instance, DaggerfallMolagBalEffects.Key, DaggerfallMolagBalEffects.Key,
            wielder, wielder, DaggerfallMolagBalEffects.Key, "Magic", item, 1, null, DaggerfallMolagBalEffects.Encode(state))
            { BundleKind = DaggerfallEffectBundleKind.HeldMagicItem });
        targetMagicka.SetCurrent(targetMagicka.Current - transferred, clamp: true);
        sourceMagicka.SetCurrent(sourceMagicka.Current + transferred, clamp: true);
        return (transferred, strength);
    }
}
