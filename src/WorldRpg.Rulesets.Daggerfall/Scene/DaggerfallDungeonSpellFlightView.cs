using System.Numerics;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A flying spell: where it is, which way it flies, and its element (fire, cold, poison, shock, magic), which draws it.</summary>
internal readonly record struct DaggerfallDungeonSpellFlightView(long Sequence, WorldPoint Position, Vector3 Direction, int Element);
