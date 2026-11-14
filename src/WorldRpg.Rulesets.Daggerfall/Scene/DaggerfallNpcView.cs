using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Where a projected person stands, as the site appearance draws them.</summary>
internal sealed record DaggerfallNpcView(long Id, WorldPoint Position);
