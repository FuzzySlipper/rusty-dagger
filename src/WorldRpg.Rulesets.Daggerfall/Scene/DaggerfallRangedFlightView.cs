using System.Numerics;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

internal readonly record struct RangedShotIdentity(ulong Generation, long AttackerId, long TargetId, ulong ReleaseStep);

/// <summary>A transient flight's current world pose, read from the canonical combat queue.</summary>
internal readonly record struct DaggerfallRangedFlightView(RangedShotIdentity Identity, WorldPoint Position, Vector3 Direction);
