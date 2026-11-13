using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The dispel choice a Dispel Magic spell on the player opens: which active bundle to end, ended outright when the
/// player cast it and otherwise at the spell's chance. Its pending request is saved; the banishing spells end nearby
/// undead or daedra at theirs.
/// </summary>
internal sealed class DaggerfallDispel(DaggerfallEffectLifecycle effects, IRandomService random, PresentationState presentation,
    DaggerfallDispelRequest? restored)
{
    internal DaggerfallDispelRequest? Pending { get; private set; } = restored;

    internal void Request(DaggerfallDispelRequest request) => Pending = request;

    internal DaggerfallDispelView? View => Pending is { } request
        ? new(request.Instance, DaggerfallMysticismEffects.Dispellable(effects)
            .GroupBy(DaggerfallMysticismEffects.Bundle).Select(group => new DaggerfallDispelOption(group.Key,
                group.First().BundleName ?? group.First().Definition.Key)).ToArray()) : null;

    internal void Choose(string revision, string? bundleId)
    {
        if (Pending is not { } request || request.Instance != revision)
        { presentation.SetOutcome("Dispel choice is no longer current."); return; }
        if (bundleId is null) { Pending = null; presentation.SetOutcome("Dispel cancelled."); return; }
        var selected = DaggerfallMysticismEffects.Dispellable(effects).Where(effect => DaggerfallMysticismEffects.Bundle(effect) == bundleId).ToArray();
        if (selected.Length == 0) { presentation.SetOutcome("That effect bundle is no longer active."); return; }
        bool success = selected.Any(effect => effect.Context.Caster?.Value == DaggerfallActorIdentity.PlayerEntityId)
            || random.DrawKeyed(new(0, "daggerfall.dispel.v1", request.Instance, 1, 100)).Value <= request.Chance;
        if (success) foreach (var effect in selected) effects.Cancel(effect.Context.Instance);
        Pending = null;
        presentation.SetOutcome(success ? "Dispel succeeded." : "Dispel failed.");
    }

    /// <summary>
    /// Banish Undead or Banish Daedra: each live creature of the group within fourteen metres of the player is banished
    /// at the spell's chance. The donor gathers them near the player whatever the spell was delivered to.
    /// </summary>
    /// <param name="nearby">The live actors strictly within a radius of a point, the player excluded.</param>
    /// <param name="liveDefinition">A live, undefeated actor's definition; null for anyone else.</param>
    internal void BanishNearby(DaggerfallActiveEffect effect, bool daedra, WorldPoint player,
        Func<WorldPoint, double, IEnumerable<long>> nearby, Func<long, DaggerfallActorDefinition?> liveDefinition, Action<long> banish)
    {
        var state = DaggerfallMysticismEffects.Read(effect, 6, daedra ? 2 : 1);
        int chance = DaggerfallMagicAdmissionPolicy.CalculateEffectChance(state.Settings, state.CasterLevel);
        int removed = 0;
        foreach (long id in nearby(player, 14d))
        {
            if (liveDefinition(id) is not { } definition) continue;
            if (DaggerfallFormulaPolicy.EnemyGroupFor(definition) != (daedra ? DaggerfallEnemyGroup.Daedra : DaggerfallEnemyGroup.Undead)) continue;
            if (random.DrawKeyed(new(0, "daggerfall.dispel.v1", $"{effect.Context.Instance.Value}:actor:{id}", 1, 100)).Value > chance) continue;
            banish(id); removed++;
        }
        if (removed == 0) effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch;
    }
}
