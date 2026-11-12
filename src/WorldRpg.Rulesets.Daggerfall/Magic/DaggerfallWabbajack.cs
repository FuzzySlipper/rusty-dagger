using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Current transformation meaning attached to the canonical replacement actor.</summary>
internal sealed record DaggerfallWabbajackState(string Definition);

internal enum DaggerfallWabbajackOutcome { Transformed, ProtectedQuestTarget, AlreadyTransformed, InvalidTarget, SourceUnavailable, UnavailableAppearance }
internal sealed record DaggerfallWabbajackResult(DaggerfallWabbajackOutcome Outcome, long TargetId, string? Definition = null);

internal static class DaggerfallWabbajack
{
    // WabbajackEffect.careerIDs, in donor draw order. Selection excludes the current mobile.
    internal static readonly int[] MobileIds = [0, 1, 2, 3, 4, 6, 10, 13, 15, 16, 17, 20, 36, 37, 38, 35, 32];

    internal static string? DefinitionOf(Actor actor) =>
        actor.TryGet<DaggerfallWabbajackState>(out var transformation) ? transformation.Definition : null;

    internal static DaggerfallActorDefinition RequireDefinition(DaggerfallDefinitions definitions, string definition)
    {
        DaggerfallActorDefinition actor = definitions.RequireActor(new(definition));
        if (actor.Kind != DaggerfallActorKinds.Monster || actor.MobileId is not int mobile || !MobileIds.Contains(mobile))
            throw new ArgumentException($"Wabbajack replacement '{definition}' is not an admitted transformation monster.");
        return actor;
    }

    internal static void Restore(Actor actor, string? definition)
    {
        if (definition is not null) actor.Add(new DaggerfallWabbajackState(definition));
    }
}
