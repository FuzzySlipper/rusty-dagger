namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What lycanthropy and vampirism ask of the quests: whether a cure or clan quest is already running, the clan's
/// ordinary work, and offering or failing one. The session answers; the curses never read the quest state themselves.
/// </summary>
internal interface IDaggerfallAfflictionQuests
{
    /// <summary>A quest from this source file is active.</summary>
    bool Active(string sourceFile);

    /// <summary>A quest with this instance identity was ever started.</summary>
    bool Started(string instanceId);

    /// <summary>The names of the ordinary work the player's vampire clan would offer them now.</summary>
    IReadOnlyList<string> ClanWork(int clan);

    /// <summary>
    /// Starts the quest of <paramref name="sourceFile"/> as active under <paramref name="instanceId"/> for
    /// <paramref name="factionId"/>; false when its source cannot be offered.
    /// </summary>
    bool Offer(string instanceId, string sourceFile, int factionId);

    /// <summary>Fails every active quest whose definition name starts with <paramref name="definitionPrefix"/>.</summary>
    void FailActive(string definitionPrefix, string reason);
}
