using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Dialogue's owners without the session, over <see cref="SocietyStage"/>'s player, NPC registry, live civilians,
/// social state and site context: the conversation with its page and outcome lines recorded, and the directions.
/// </summary>
internal sealed class DialogueStage : IDisposable
{
    internal SocietyStage Society { get; } = new();
    internal DaggerfallDialogueView? View { get; private set; }
    internal List<string> Outcomes { get; } = [];

    /// <summary>A person of the site the player stands at who talks.</summary>
    internal long Talker(string role = "baker") => Society.Civilian(role, "talk");

    internal DaggerfallDialogueService Conversation(IRandomService? random = null,
        Func<string?, DaggerfallDialogueDestination?>? resolveDirection = null, Func<string, bool>? disclose = null,
        Func<long, IReadOnlyList<DaggerfallDialogueTopicOption>>? questTopics = null,
        Func<long, string, (string Text, IReadOnlyList<string> Diagnostics)?>? resolveQuestTopic = null) =>
        new(Society.Npcs, Society.Actors, Society.Social, Society.SkillUses, Society.Actors.Player.Stats, Society.Definitions,
            random ?? RandomMinimum.Create(), () => Society.Sites.ActiveSite, () => Society.Character.Identity,
            view => View = view, Outcomes.Add,
            resolveDirection: resolveDirection, discloseDirection: disclose,
            questTopics: questTopics, resolveQuestTopic: resolveQuestTopic);

    internal DaggerfallDialogueDirections Directions(DaggerfallDialogueService? conversation,
        IReadOnlyList<DaggerfallDialogueQuestSubject>? subjects = null) =>
        new(Society.Sites, Society.Npcs, Society.Definitions, Society.Player,
            dialogue: () => conversation,
            activeProfile: () => Society.Active,
            exteriorSitePosition: point => point,
            questSubjects: () => subjects ?? [],
            questClaimsBuilding: (_, _) => false,
            questContacts: _ => []);

    /// <summary>The conversation's activation target for this person.</summary>
    internal static DaggerfallActivationTarget Target(DaggerfallDialogueService conversation, long id) =>
        conversation.NpcTargets().Single(target => target.Identity.Value == (ulong)id);

    public void Dispose() => Society.Dispose();
}
