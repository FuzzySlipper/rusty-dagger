using WorldRpg.Kit.Controls;
using Rusty.Engine;
using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>EnemySenses' classic spawn/despawn hysteresis, used by senses rather than a second actor lifetime.</summary>
internal static class DaggerfallClassicSpawnRange
{
    internal static bool Update(DaggerfallEnemyPerceptionMemory memory, WorldPoint enemy, WorldPoint player, bool inside)
    {
        ReadOnlySpan<int> spawnXZ = [1024, 384, 640, 768, 768, 768, 768];
        ReadOnlySpan<int> spawnUpper = [128, 128, 128, 384, 768, 128, 256];
        ReadOnlySpan<int> spawnLower = [0, 0, 0, 0, -128, -768, 0];
        ReadOnlySpan<int> despawnXZ = [1024, 1024, 1024, 1024, 768, 768, 768];
        ReadOnlySpan<int> despawnY = [384, 384, 384, 384, 768, 768, 768];
        int type = memory.SpawnDistance;
        double x = (enemy.X - player.X) / DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
        double y = (enemy.Y - player.Y) / DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
        double z = (enemy.Z - player.Z) / DaggerfallPerceptionQueryDefaults.ClassicGlobalScale;
        double radius = inside ? memory.WithinClassicSpawnRange ? despawnXZ[type] : spawnXZ[type] : 4096;
        double upper = memory.WithinClassicSpawnRange ? despawnY[type] : spawnUpper[type];
        double lower = memory.WithinClassicSpawnRange ? 0 : spawnLower[type];
        return memory.WithinClassicSpawnRange = x * x + y * y + z * z < 1094d * 1094d
            && x * x + z * z <= radius * radius
            && (!inside || (lower == 0 ? Math.Abs(y) <= upper : y >= lower && y <= upper));
    }
}
