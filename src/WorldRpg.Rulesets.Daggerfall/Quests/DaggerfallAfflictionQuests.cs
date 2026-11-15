using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What lycanthropy and vampirism ask of the quests, answered from the quest instances when they ask: whether a cure
/// or clan quest runs, the clan's ordinary work for the player as they stand, and offering or failing one.
/// </summary>
internal sealed class DaggerfallAfflictionQuests(DaggerfallQuestInstances quests, DaggerfallDefinitions definitions,
    ActorsState actors, DaggerfallSocialState social, DaggerfallCharacterState character) : IDaggerfallAfflictionQuests
{
    public bool Active(string sourceFile) =>
        quests.All.Any(value => value.SourceFile == sourceFile && value.Lifecycle == DaggerfallQuestLifecycle.Active);

    public bool Started(string instanceId) => quests.All.Any(value => value.InstanceId == instanceId);

    public IReadOnlyList<string> ClanWork(int clan) =>
        [.. quests.OrdinaryWorkPool(clan, true, actors.Player.Progression.Level, social.FactionReputation(clan),
            actors.Player.Progression.Level, character.Identity.Gender).Select(row => row.Name)];

    public bool Offer(string instanceId, string sourceFile, int factionId)
    {
        try
        {
            var source = definitions.QuestSources.Resolve(sourceFile);
            quests.Start(new(instanceId, source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []) { FactionId = factionId });
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public void FailActive(string definitionPrefix, string reason)
    {
        foreach (var quest in quests.Capture().Instances.Where(quest => quest.Lifecycle == DaggerfallQuestLifecycle.Active
            && quest.DefinitionName.StartsWith(definitionPrefix, StringComparison.OrdinalIgnoreCase)))
            quests.Fail(quest.InstanceId, reason);
    }
}
