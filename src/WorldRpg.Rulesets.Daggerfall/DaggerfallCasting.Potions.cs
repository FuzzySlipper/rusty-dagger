using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallCasting
{
    /// <summary>Consumes no readiness or spell points. The inventory owner removes the dose after delivery.</summary>
    internal DaggerfallCastResult DrinkPotion(long casterId, int recipeKey)
    {
        var actor = ResolveSource(casterId, null);
        if (actor is null) return Finish(DaggerfallCastOutcome.SourceUnavailable);
        if (!catalog.PotionRecipes.TryGetValue(recipeKey, out var recipe)) return Finish(DaggerfallCastOutcome.UnknownSpell);
        try { recipe.ValidateEffects(effects.Catalog); }
        catch (ArgumentException) { return Finish(DaggerfallCastOutcome.UnsupportedEffect); }
        // A potion's transient delivery is identified by its real recipe; it is never registered in the spellbook/catalog.
        var spell = new DaggerfallSpellDefinition($"potion.{recipe.Key}", -1, false, recipe.Name, 4, 0, 0, 0, recipe.Effects);
        if (!TryDefinitions(spell, out var definitions)) return Finish(DaggerfallCastOutcome.UnsupportedEffect);
        var release = CreateBundle(actor, casterId, new(spell.Key, null, 0, DaggerfallCastSource.Potion), spell, definitions, null, null);
        if (recipe.SpellPointRestore is { } restore)
        {
            var bundle = release.Bundle!;
            int draw = 0;
            int Roll(int low, int high) => checked((int)random.DrawKeyed(new(0, "daggerfall.casting.v1", $"cast:{bundle.Sequence}:draw:{++draw}", low, high)).Value);
            int amount = DaggerfallMagicAdmissionPolicy.RollEffectMagnitude(restore.BaseLow, restore.BaseHigh,
                restore.LevelBase, restore.LevelHigh, restore.PerLevel, bundle.CasterLevel, Roll);
            var admitted = effects.Start(new($"cast.{bundle.Sequence}.{casterId}.0.direct", restore.Effect, spell.Key,
                casterId, casterId, spell.Key, "Magic", null, 1, 1, DaggerfallHealingEffects.SpellPointState(amount))
                { BundleId = $"cast.{bundle.Sequence}", BundleSequence = bundle.Sequence, BundleName = recipe.Name, BundleKind = DaggerfallEffectBundleKind.Potion });
            bundle.Results.Add(new(0, casterId, admitted is DaggerfallEffectAdmissionOutcome.TargetUnavailable or DaggerfallEffectAdmissionOutcome.SourceUnavailable
                ? DaggerfallCastOutcome.SourceUnavailable : DaggerfallCastOutcome.Applied));
        }
        var delivered = Deliver(release.Bundle!, [casterId]);
        return delivered.Bundle is { Results.Count: > 0 } completed
            && completed.Results.All(value => value.Outcome is DaggerfallCastOutcome.SourceUnavailable or DaggerfallCastOutcome.TargetUnavailable)
            ? Finish(DaggerfallCastOutcome.SourceUnavailable, completed) : delivered;
    }
}

internal sealed partial class DaggerfallSession
{
    private DaggerfallInventoryUseResult UsePotion(int recipeKey)
    {
        var result = Casting.DrinkPotion(State.Actors.Player.DurableId, recipeKey);
        return result.Outcome == DaggerfallCastOutcome.DeliveryCompleted
            ? new(true, $"Drank {_definitions.Magic.PotionRecipes[recipeKey].Name}.")
            : new(false, "This potion cannot be used now.");
    }
}
