using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal static class ExteriorEnvironmentSurfaces
{
    /// <summary>Reconciles cells whose ground is read from the surfaces given, as the session's grounds read theirs.</summary>
    internal static void ReconcileSurfaces(
        this DaggerfallExteriorEnvironment environment,
        IEnumerable<DaggerfallExteriorCellId> cells,
        DaggerfallExteriorWorldOrigin origin,
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaces,
        IReadOnlyDictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations,
        DaggerfallWorldGridsSet grids,
        DaggerfallExteriorCellId? center = null) =>
        environment.Reconcile(cells, origin,
            cell => DaggerfallExteriorEnvironment.Ground(cell, surfaces(cell), locations.GetValueOrDefault(cell), DaggerfallGroundTuning.Default), locations, grids, center);
}
