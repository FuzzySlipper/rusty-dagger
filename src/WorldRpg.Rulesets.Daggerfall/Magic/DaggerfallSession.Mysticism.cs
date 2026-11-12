using WorldRpg.Rulesets.Daggerfall.Content;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallDispelRequest? _pendingDispel;
    internal DaggerfallDispelView? DispelView => _pendingDispel is { } request
        ? new(request.Instance, DaggerfallMysticismEffects.Dispellable(State.Effects)
            .GroupBy(DaggerfallMysticismEffects.Bundle).Select(group => new DaggerfallDispelOption(group.Key,
                group.First().BundleName ?? group.First().Definition.Key)).ToArray()) : null;

    internal void ChooseDispel(string revision, string? bundleId)
    {
        if (_pendingDispel is not { } request || request.Instance != revision)
        { Presentation.SetOutcome("Dispel choice is no longer current."); return; }
        if (bundleId is null) { _pendingDispel = null; Presentation.SetOutcome("Dispel cancelled."); return; }
        var selected = DaggerfallMysticismEffects.Dispellable(State.Effects).Where(effect => DaggerfallMysticismEffects.Bundle(effect) == bundleId).ToArray();
        if (selected.Length == 0) { Presentation.SetOutcome("That effect bundle is no longer active."); return; }
        bool success = selected.Any(effect => effect.Context.Caster?.Value == DaggerfallActorIdentity.PlayerEntityId)
            || _random.DrawKeyed(new(0, "daggerfall.dispel.v1", request.Instance, 1, 100)).Value <= request.Chance;
        if (success) foreach (var effect in selected) State.Effects.Cancel(effect.Context.Instance);
        _pendingDispel = null;
        Presentation.SetOutcome(success ? "Dispel succeeded." : "Dispel failed.");
    }
    private void BanishNearby(DaggerfallActiveEffect effect, bool daedra)
    {
        var state = DaggerfallMysticismEffects.Read(effect, 6, daedra ? 2 : 1);
        // The donor gathers the creatures near the player whatever the spell was delivered to.
        long centerId = DaggerfallActorIdentity.PlayerEntityId;
        var center = State.PlayerControl.Position!.Value;
        int chance = DaggerfallMagicAdmissionPolicy.CalculateEffectChance(state.Settings, state.CasterLevel);
        int removed = 0;
        foreach (long id in AreaSpellTargets(centerId, center.ToVector(), true, 14d, exclusive: true))
        {
            if (!State.Actors.TryGet(id, out var actor) || actor.IsDefeated || !DefinitionsByActor.TryGetValue(id, out var definition)) continue;
            if (DaggerfallFormulaPolicy.EnemyGroupFor(definition) != (daedra ? DaggerfallEnemyGroup.Daedra : DaggerfallEnemyGroup.Undead)) continue;
            if (_random.DrawKeyed(new(0, "daggerfall.dispel.v1", $"{effect.Context.Instance.Value}:actor:{id}", 1, 100)).Value > chance) continue;
            BanishActor(id); removed++;
        }
        if (removed == 0) effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch;
    }
}
