using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// NPC-to-actor materialization. The registry remains the durable social and appearance record; this owner creates the
/// one canonical Mechanics actor that combat, dialogue, save and corpse policy address by the same durable identity, and
/// retires it again.
/// </summary>
/// <param name="profile">The active site's profile.</param>
/// <param name="appearance">The active site's appearance.</param>
/// <param name="requireSite">A site's record by identity.</param>
/// <param name="profileToLocal">A point in the active profile's frame in the scene's local frame.</param>
/// <param name="materialize">Creates a civilian's actor at a pose (the roster's).</param>
/// <param name="unload">Unloads one live actor without retiring it (the roster's).</param>
/// <param name="retire">Retires one live actor (the roster's).</param>
/// <param name="retireDetached">Retires a civilian an inactive site's delta retains; false when none does.</param>
internal sealed class DaggerfallCivilians(DaggerfallNpcRegistry npcs, ActorsState actors, Func<DaggerfallSiteProfile> profile,
    Func<DaggerfallSiteAppearance> appearance, Func<DaggerfallSiteId, DaggerfallSiteRecord> requireSite, Func<WorldPoint, WorldPoint> profileToLocal,
    Func<DaggerfallNpc, ActorPose, long> materialize, Action<long> unload, Action<long> retire, Func<long, bool> retireDetached)
{
    /// <summary>
    /// Materializes one registered NPC at an explicit world pose. The actor is recorded as a runtime civilian dynamic
    /// actor, so its pose, inventory, corpse and identity ledger take part in the existing save boundary.
    /// </summary>
    internal long Materialize(long npcId, ActorPose pose)
    {
        DaggerfallNpc npc = npcs.Require(npcId);
        if (npc.Kind != DaggerfallNpcKind.Civilian)
            throw new InvalidOperationException($"NPC {npcId} is {npc.Kind}, not a materializable civilian.");
        if (npc.Presence == DaggerfallNpcPresence.Removed)
            throw new InvalidOperationException($"NPC {npcId} has been removed and cannot be materialized.");
        DaggerfallSiteProfile active = profile();
        bool sourcePopulation = IsSourcePopulation(npc);
        if (actors.TryGet(npcId, out _))
        {
            // Site admission can restore a retained actor before the time-of-day population
            // projection runs. Re-admit the source billboard only when the population owner hid
            // that visual for night; the mechanics actor remains the same durable owner.
            if (sourcePopulation) AdmitSourcePopulationAppearance(npc, active, appearance());
            return npcId;
        }

        long actorId = materialize(npc, pose);
        if (sourcePopulation)
        {
            try { AdmitSourcePopulationAppearance(npc, active, appearance()); }
            catch
            {
                unload(actorId);
                throw;
            }
        }
        // The live actor's Transform now owns the pose; the registry's detached placement is dropped.
        npcs.ReleasePose(npcId);
        return actorId;
    }

    /// <summary>
    /// Restores source-population billboards for actors retained by an adjacent exterior. The resident projection owns
    /// the appearance; the NPC registry and this same source lookup stay authoritative, so resident re-entry does not
    /// create a second civilian visual graph.
    /// </summary>
    internal void AdmitResidentAppearances(DaggerfallSiteProfile resident, DaggerfallSiteAppearance residentAppearance)
    {
        ArgumentNullException.ThrowIfNull(resident);
        ArgumentNullException.ThrowIfNull(residentAppearance);
        foreach (DaggerfallNpc npc in npcs.All.Where(npc => IsSourcePopulation(npc)
            && npc.Profile == resident.ProfileKey
            && npc.Presence == DaggerfallNpcPresence.Active
            && actors.TryGet(npc.DurableId, out _)))
        {
            AdmitSourcePopulationAppearance(npc, resident, residentAppearance);
        }
    }

    /// <summary>Materializes every active civilian of the active site, using saved coordinates when present.</summary>
    internal IReadOnlyList<long> MaterializeAll(WorldPoint fallbackPosition)
    {
        DaggerfallSiteProfile active = profile();
        DaggerfallSiteRecord? site = active.Site is { } siteId ? requireSite(siteId) : null;
        List<long> materialized = [];
        foreach (DaggerfallNpc npc in npcs.All)
        {
            // Static and quest NPCs have dialogue/service policy but no generic actor
            // definition yet. Hidden/removed civilians belong to an inactive population
            // and must not be recreated by the next site admission.
            if (npc.Kind != DaggerfallNpcKind.Civilian
                || npc.Presence != DaggerfallNpcPresence.Active
                || site is null
                || (npc.Profile is { } physical ? physical != active.ProfileKey
                    : npc.Site.Region != site.Id.Region || !StringComparer.Ordinal.Equals(npc.Site.Location, site.Name))
                || actors.TryGet(npc.DurableId, out _)) continue;
            WorldPoint position = npc.X is float x && npc.Y is float y && npc.Z is float z
                ? profileToLocal(new WorldPoint(x, y, z))
                : fallbackPosition;
            materialized.Add(Materialize(npc.DurableId, new ActorPose(position, 0F)));
        }
        return materialized;
    }

    /// <summary>Retires a civilian, live or retained by an inactive site, and records the social identity as removed.</summary>
    internal void Retire(long npcId)
    {
        DaggerfallNpc npc = npcs.Require(npcId);
        if (npc.Kind != DaggerfallNpcKind.Civilian)
            throw new InvalidOperationException($"NPC {npcId} is {npc.Kind}, not a materializable civilian.");

        if (actors.TryGet(npcId, out _))
        {
            retire(npcId);
        }
        else if (!retireDetached(npcId))
        {
            // Preserve the roster's diagnostic for an identity that is not a live
            // dynamic actor and is not retained by an inactive site.
            retire(npcId);
        }

        if (npc.Presence != DaggerfallNpcPresence.Removed)
            npcs.SetPresence(npcId, DaggerfallNpcPresence.Removed);
    }

    /// <summary>Whether an NPC is one of a town's source population, placed from the importer's population list.</summary>
    internal static bool IsSourcePopulation(DaggerfallNpc npc) =>
        npc.Kind == DaggerfallNpcKind.Civilian
        && npc.StableKey.StartsWith("population/", StringComparison.Ordinal);

    private static void AdmitSourcePopulationAppearance(DaggerfallNpc npc, DaggerfallSiteProfile profile,
        DaggerfallSiteAppearance appearance)
    {
        if (!profile.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord),
                out NormalizedBillboardSprite? sourceSprite))
        {
            throw new NotSupportedException($"Population NPC {npc.DurableId} has no published billboard {npc.Appearance.BillboardArchive}/{npc.Appearance.BillboardRecord} at '{profile.ProfileKey.LogicalId}'.");
        }
        if (appearance.HasActor(npc.DurableId)) return;
        appearance.AddActor(npc.DurableId, sourceSprite.Standing((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord)));
    }
}
