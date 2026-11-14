using Rusty.Engine.Entities;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Loot;

/// <summary>One materialized dropped-item container in the shared Engine inventory store.</summary>
internal sealed record DaggerfallGroundContainer(DaggerfallWorldProfileKey Profile, long Id, EntityId Owner, WorldPoint Position, string? PropertyPlacement = null, long StockedDay = 0, string? TreasurePlacement = null);
