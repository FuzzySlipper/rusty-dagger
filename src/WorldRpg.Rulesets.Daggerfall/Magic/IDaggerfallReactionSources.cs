using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Where the held items' reputation powers put their reaction adjustments: one per social group, kept by the source
/// that set it. Society's social state keeps them.
/// </summary>
internal interface IDaggerfallReactionSources
{
    int GroupCount { get; }
    void SetReactionSource(int socialGroup, MechanicsSourceIdentity identity, int amount);
    void RemoveReactionSource(MechanicsSourceIdentity identity);
}
