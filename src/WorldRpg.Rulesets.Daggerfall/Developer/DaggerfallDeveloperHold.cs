using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Why a developer command cannot move the player or change what they have now, or null. A gameplay screen or
/// question that holds the world (a conversation, loot, dungeon text, a pending offer or court) is answered first; the
/// game menu, where the developer console lives, is not one of them.
/// </summary>
/// <param name="mode">The product mode the session runs under.</param>
/// <param name="openScreens">The names of the open interaction screens other than the game menu, in their order.</param>
internal sealed class DaggerfallDeveloperHold(Func<ProductMode> mode, ActorsState actors, PlayerControlState player,
    Func<IEnumerable<string>> openScreens, DaggerfallDaedricSummoning summoning, DaggerfallQuestInstances quests, DaggerfallLaw law,
    DaggerfallDispel dispel, DaggerfallIdentify identify, DaggerfallCreateItem createItem, DaggerfallOpeningCinematics openingCinematics,
    DaggerfallCinematicPresentation? cinematics)
{
    internal string? Refusal()
    {
        if (mode() == ProductMode.Title) return "No game is running.";
        if (actors.Player.IsDefeated) return "The player is dead.";
        if (player.Position is null) return "The player has no position.";
        string[] open = [.. openScreens()];
        if (open.Length > 0) return $"Close the open {string.Join(", ", open)} first.";
        if (summoning.PendingOffer || quests.PendingOffer is not null) return "Answer the pending offer first.";
        // A quest's question (the tutorial's Yes/No, a prompt) holds as it holds a rest.
        if (quests.Messages.Pending is not null) return "Answer the quest's question first.";
        if (law.ModalOpen) return "Answer the court first.";
        if (dispel.Pending is not null || identify.Open || createItem.Pending is not null) return "Answer the pending spell choice first.";
        return openingCinematics.IsActive || cinematics?.ActiveSource is not null ? "A cinematic is playing." : null;
    }
}
