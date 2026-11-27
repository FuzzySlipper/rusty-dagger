using System.Globalization;
using System.Text;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Structured heraldry colour set, crest/charge token, and blazon for one guard post or region livery (Den #11165).
/// Derived live from <see cref="DaggerfallRealmArms"/> and <see cref="DaggerfallHeraldry"/> so banners, the Realms
/// map, guard posts, and developer inspection share one source of truth.
/// </summary>
internal sealed record DaggerfallLiveryPalette(
    string ArmsId,
    string Blazon,
    string Division,
    string FieldTincture,
    string PrimaryHex,
    string SecondaryTincture,
    string SecondaryHex,
    string Charge,
    string ChargeTincture,
    string AccentHex,
    string LiveryTincture,
    string LiveryHex);

/// <summary>
/// Resolved watch tier, post role, and active livery/heraldry owner for one guard or watch officer (Den #11167, #11165).
/// Read live from <see cref="DaggerfallRegionLiege"/> and <see cref="DaggerfallRealmArms"/> so political-board
/// allegiance changes update special posts immediately while rank-and-file street watch remain regional.
/// </summary>
internal sealed record DaggerfallGuardLiveryResolution(
    DaggerfallSettlementWatchTier WatchTier,
    string WatchTierLabel,
    DaggerfallGuardPostKind PostKind,
    string PostLabel,
    bool IsOfficer,
    bool UsesLiegeLivery,
    bool IsOccupyingGarrison,
    int LocalRegion,
    string LocalRegionName,
    int LiveryRegion,
    int? LiveryFactionId,
    string LiveryOwnerName,
    string LiverySlug,
    string LiveryKey,
    Color? LiveryTint,
    DaggerfallLiveryPalette? Palette = null);

/// <summary>Site-level watch posture summary surfaced by <c>social.site</c> (Den #11167, #11165).</summary>
internal sealed record DaggerfallSiteWatchSummary(
    DaggerfallSettlementWatchTier WatchTier,
    string WatchTierLabel,
    bool IsWalled,
    int GateCount,
    int Region,
    string RegionName,
    string LocalLiveryOwner,
    string LocalLiverySlug,
    int? LocalLiveryFactionId,
    string LiegeLiveryOwner,
    string LiegeLiverySlug,
    int? LiegeLiveryFactionId,
    bool IsOccupied,
    int OccupationGarrisonCount,
    int PatrolGuardCount,
    int GateOfficerCount,
    DaggerfallLiveryPalette? LocalPalette = null,
    DaggerfallLiveryPalette? LiegePalette = null);

/// <summary>
/// Post-differentiated watch and liege-livery resolution (Den #11167, settled in <c>wiki/rusty-dagger/guards.md</c>):
/// - Rank-and-file street patrols retain their region's local watch identity and livery across ownership changes.
/// - Special posts — palaces, keeps, local crown/governor offices, and one watch officer/knight at each gate of
///   proper walled cities (unwalled hamlets/villages have none) — resolve livery from <see cref="DaggerfallRegionLiege"/>
///   (falling back to the region's own arms when independent).
/// - During a conquest occupation (#11143), liege-livery garrison patrols layer on top of the local watch.
/// </summary>
internal sealed class DaggerfallGuardPosts(
    DaggerfallDefinitions definitions,
    DaggerfallRegionLiege liege,
    DaggerfallOfficeLedger officeLedger,
    DaggerfallGuardTierTuning guardTiers,
    DaggerfallRealmArms realmArms)
{
    private const int PalaceBuildingType = 16;

    /// <summary>Whether <paramref name="region"/> is currently under a conquering liege's occupation/consolidation.</summary>
    internal bool IsRegionOccupied(int region)
    {
        if (liege.NewLiegeOf(region) is not null)
            return true;

        DaggerfallFactionDefinition? currentLiege = liege.LiegeOf(region);
        if (currentLiege is null)
            return false;

        DaggerfallFactionDefinition? province = liege.ProvinceOf(region);
        if (province is null)
            return false;

        string officeKey = DaggerfallOfficeLedger.FactionOfficeKey(province.Id);
        DaggerfallOfficeOverrideSave? ov = officeLedger.TryGetOverride(officeKey);
        return ov is { Vacant: false, Cause: DaggerfallOfficeChangeCause.CardMove };
    }

    /// <summary>
    /// Number of liege-livery occupation garrison patrols layered on top of <paramref name="site"/>'s local watch
    /// while its region is under conquest occupation; zero during normal rule.
    /// </summary>
    internal int OccupationGarrisonCount(DaggerfallSiteRecord? site)
    {
        if (site is null || !IsRegionOccupied(site.Id.Region))
            return 0;

        int raw = guardTiers.ResolveTier(site) switch
        {
            DaggerfallSettlementWatchTier.Capital => 3,
            DaggerfallSettlementWatchTier.Town => 2,
            _ => 1,
        };
        return Math.Min(DaggerfallExteriorPopulation.CrackdownGuardSlots, raw);
    }

    /// <summary>Classifies one exterior population guard placement at <paramref name="site"/>.</summary>
    internal DaggerfallGuardPostKind ClassifyPlacement(DaggerfallSiteRecord? site, DaggerfallPopulationPlacement placement)
    {
        if (placement.GuardPost == DaggerfallGuardPostKind.WalledCityGateOfficer)
            return DaggerfallGuardPostKind.WalledCityGateOfficer;
        if (placement.CrackdownGuard > 0 && site is not null && placement.CrackdownGuard <= OccupationGarrisonCount(site))
            return DaggerfallGuardPostKind.OccupationGarrison;
        return DaggerfallGuardPostKind.StreetWatch;
    }

    /// <summary>Classifies a live guard actor (calm watchman, gate officer, palace/office guard, occupation garrison, or crime-response guard).</summary>
    internal DaggerfallGuardPostKind ClassifyLiveGuard(
        long actorId,
        DaggerfallSiteRecord? site,
        DaggerfallSiteProfile? activeProfile,
        DaggerfallNpc? npc,
        IReadOnlyList<DaggerfallPopulationPlacement> exteriorPlacements)
    {
        if (activeProfile is { ProfileKind: DaggerfallWorldProfileKind.Interior, InteriorBuilding: { } interior })
        {
            if (interior.BuildingType == PalaceBuildingType)
                return DaggerfallGuardPostKind.PalaceGuard;
            if (site is not null && IsCrownOrGovernorOffice(site, interior))
                return DaggerfallGuardPostKind.CrownOfficeGuard;
        }

        if (site?.Kind == DaggerfallSiteKind.DungeonKeep)
            return DaggerfallGuardPostKind.PalaceGuard;

        if (site is not null && exteriorPlacements.Count > 0)
        {
            DaggerfallWorldProfileKey exteriorKey = DaggerfallWorldProfileIds.Exterior(site.Id);
            foreach (DaggerfallPopulationPlacement placement in exteriorPlacements)
            {
                if (!placement.IsGuard)
                    continue;
                long placementId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{exteriorKey.LogicalId}/{placement.Id}");
                if (placementId == actorId
                    || (npc is not null && string.Equals(npc.StableKey, placement.Id, StringComparison.Ordinal)))
                {
                    return ClassifyPlacement(site, placement);
                }
            }
        }

        if (npc is not null && npc.Appearance.FactionId > 0
            && definitions.Factions.Factions.TryGetValue(npc.Appearance.FactionId, out DaggerfallFactionDefinition? faction)
            && faction.Type is 1 or 7 or 14)
        {
            return DaggerfallGuardPostKind.CrownOfficeGuard;
        }

        return DaggerfallGuardPostKind.StreetWatch;
    }

    /// <summary>Resolves watch tier, post metadata, and active livery owner for <paramref name="postKind"/> at <paramref name="site"/> / <paramref name="region"/>.</summary>
    internal DaggerfallGuardLiveryResolution ResolveLivery(
        DaggerfallSiteRecord? site,
        int region,
        DaggerfallGuardPostKind postKind)
    {
        DaggerfallSettlementWatchTier tier = guardTiers.ResolveTier(site);
        DaggerfallGuardTierProfile profile = guardTiers.ProfileFor(tier);
        bool usesLiege = postKind is DaggerfallGuardPostKind.WalledCityGateOfficer
            or DaggerfallGuardPostKind.PalaceGuard
            or DaggerfallGuardPostKind.CrownOfficeGuard
            or DaggerfallGuardPostKind.OccupationGarrison;
        bool isOfficer = postKind is DaggerfallGuardPostKind.WalledCityGateOfficer
            or DaggerfallGuardPostKind.PalaceGuard
            or DaggerfallGuardPostKind.CrownOfficeGuard;
        bool isOccupation = postKind == DaggerfallGuardPostKind.OccupationGarrison;

        DaggerfallFactionDefinition? localProvince = liege.ProvinceOf(region);
        string localRegionName = !string.IsNullOrWhiteSpace(localProvince?.Name)
            ? localProvince.Name
            : definitions.BuildingNames.RegionName(region);
        if (string.IsNullOrWhiteSpace(localRegionName))
            localRegionName = $"Region {region}";

        DaggerfallFactionDefinition? sovereignLiege = usesLiege ? liege.LiegeOf(region) : null;
        DaggerfallFactionDefinition? liveryFaction = usesLiege
            ? liege.SovereignOf(region)
            : localProvince;
        int liveryRegion = liveryFaction is { Region: >= 0 and <= 61 } ? liveryFaction.Region : region;
        string liveryOwnerName = !string.IsNullOrWhiteSpace(liveryFaction?.Name)
            ? liveryFaction.Name
            : localRegionName;
        string liverySlug = Slugify(liveryOwnerName);
        DaggerfallArms? activeArms = usesLiege ? realmArms.Of(region) : realmArms.Own(region);
        (string liveryKey, Color? liveryTint) = usesLiege && activeArms is not null
            ? (activeArms.Seat is { } seat ? $"crown:{seat}" : $"region:{region}", LiveryTint(realmArms.Heraldry.Colour(activeArms.Livery)))
            : ($"watch:region:{region}", (Color?)null);
        DaggerfallLiveryPalette? palette = activeArms is not null ? BuildPalette(activeArms, realmArms.Heraldry) : null;

        return new DaggerfallGuardLiveryResolution(
            WatchTier: tier,
            WatchTierLabel: profile.Label,
            PostKind: postKind,
            PostLabel: FormatPostLabel(postKind),
            IsOfficer: isOfficer,
            UsesLiegeLivery: usesLiege,
            IsOccupyingGarrison: isOccupation,
            LocalRegion: region,
            LocalRegionName: localRegionName,
            LiveryRegion: liveryRegion,
            LiveryFactionId: liveryFaction?.Id,
            LiveryOwnerName: liveryOwnerName,
            LiverySlug: liverySlug,
            LiveryKey: liveryKey,
            LiveryTint: liveryTint,
            Palette: palette);
    }

    internal static DaggerfallLiveryPalette BuildPalette(DaggerfallArms arms, DaggerfallHeraldry heraldry)
    {
        ArgumentNullException.ThrowIfNull(arms);
        ArgumentNullException.ThrowIfNull(heraldry);
        DaggerfallTincture secondary = arms.Second ?? arms.ChargeTincture;
        return new DaggerfallLiveryPalette(
            ArmsId: arms.Id,
            Blazon: arms.Blazon,
            Division: arms.Division.ToString(),
            FieldTincture: DaggerfallArms.Word(arms.Field).ToLowerInvariant(),
            PrimaryHex: DaggerfallHeraldry.Hex(heraldry.Colour(arms.Field)),
            SecondaryTincture: DaggerfallArms.Word(secondary).ToLowerInvariant(),
            SecondaryHex: DaggerfallHeraldry.Hex(heraldry.Colour(secondary)),
            Charge: arms.Charge,
            ChargeTincture: DaggerfallArms.Word(arms.ChargeTincture).ToLowerInvariant(),
            AccentHex: DaggerfallHeraldry.Hex(heraldry.Colour(arms.ChargeTincture)),
            LiveryTincture: DaggerfallArms.Word(arms.Livery).ToLowerInvariant(),
            LiveryHex: DaggerfallHeraldry.Hex(heraldry.Colour(arms.Livery)));
    }

    /// <summary>
    /// The sprite tint a guard in livery wears: the arms' livery colour, paled so it tints the classic sprite rather than
    /// painting over it.
    /// </summary>
    private static Color LiveryTint(Color livery) =>
        new(0.55f + livery.R * 0.45f, 0.55f + livery.G * 0.45f, 0.55f + livery.B * 0.45f, 1f);

    /// <summary>
    /// Resolves canonical guard appearance inputs (livery faction, livery key, and sprite tint) for an
    /// exterior population guard placement at <paramref name="site"/>.
    /// </summary>
    internal DaggerfallGuardAppearanceOverride ResolvePlacementAppearance(
        DaggerfallSiteRecord site,
        DaggerfallPopulationPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(placement);
        DaggerfallGuardPostKind postKind = ClassifyPlacement(site, placement);
        DaggerfallGuardLiveryResolution resolution = ResolveLivery(site, site.Id.Region, postKind);
        int factionId = resolution.UsesLiegeLivery && resolution.LiveryFactionId is int liegeFactionId && liegeFactionId > 0
            ? liegeFactionId
            : placement.FactionId;
        return new DaggerfallGuardAppearanceOverride(
            FactionId: factionId,
            LiveryKey: resolution.LiveryKey,
            Tint: resolution.LiveryTint,
            PostKind: postKind,
            UsesLiegeLivery: resolution.UsesLiegeLivery);
    }

    /// <summary>
    /// Applies resolved regional/liege livery to live guard actors, resolving each guard strictly against its
    /// own owning site/profile/placements (so neighboring resident exterior actors never inherit the active site's
    /// classification or sovereign). Rank-and-file street watch keep their regional appearance untouched.
    /// </summary>
    internal void ApplyLiveGuardAppearances(
        DaggerfallSiteRecord? site,
        DaggerfallSiteProfile? activeProfile,
        IReadOnlyList<DaggerfallPopulationPlacement> exteriorPlacements,
        ActorsState actors,
        DaggerfallNpcRegistry npcs,
        DaggerfallSiteAppearance? siteAppearance,
        Func<DaggerfallWorldProfileKey, (DaggerfallSiteRecord? Site, DaggerfallSiteProfile? Profile, IReadOnlyList<DaggerfallPopulationPlacement> Placements, DaggerfallSiteAppearance? Appearance)?>? resolveProfileContext = null)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(npcs);

        foreach (ActorState actor in actors.All)
        {
            if (actor.DurableId == DaggerfallActorIdentity.PlayerEntityId)
                continue;

            DaggerfallNpc? npc = npcs.TryGet(actor.DurableId, out DaggerfallNpc? foundNpc) ? foundNpc : null;
            bool isGuard = (npc is not null && string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase))
                || (actor.Actor.TryGet<FactionComponent>(out FactionComponent? fc)
                    && string.Equals(fc.CreatureFactionId, "city-watch", StringComparison.OrdinalIgnoreCase));
            if (!isGuard)
                continue;

            // Resolve the guard's own owning site/profile/placements/appearance rather than blindly applying
            // the active site's context to neighboring resident exterior actors (R11167-4).
            DaggerfallSiteRecord? ownerSite = site;
            DaggerfallSiteProfile? ownerProfile = activeProfile;
            IReadOnlyList<DaggerfallPopulationPlacement> ownerPlacements = exteriorPlacements;
            DaggerfallSiteAppearance? ownerAppearance = siteAppearance;

            if (npc?.Profile is { } npcProfile && activeProfile is not null && npcProfile != activeProfile.ProfileKey)
            {
                if (resolveProfileContext?.Invoke(npcProfile) is not { } residentContext)
                    continue;
                (ownerSite, ownerProfile, ownerPlacements, ownerAppearance) = residentContext;
            }
            else if (npc is not null && npc.Profile is null && site is not null
                && (npc.Site.Region != site.Id.Region || !StringComparer.Ordinal.Equals(npc.Site.Location, site.Name)))
            {
                continue;
            }

            int ownerRegion = ownerSite?.Id.Region ?? npc?.Site.Region ?? ownerProfile?.Site?.Region ?? 0;
            int? localRegionalId = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, ownerRegion)?.Id;

            DaggerfallGuardLiveryResolution resolution = ResolveForLiveGuard(
                actor.DurableId, ownerSite, ownerProfile, npc, ownerPlacements);
            int? targetFactionId = resolution.UsesLiegeLivery
                ? resolution.LiveryFactionId ?? localRegionalId
                : localRegionalId ?? resolution.LiveryFactionId;

            if (npc is not null && targetFactionId is int npcFactionId && npcFactionId > 0)
                npcs.RefreshGuardFaction(actor.DurableId, npcFactionId);

            if (actor.Actor.TryGet<FactionComponent>(out FactionComponent? factionComp))
                factionComp.SetPeopleFactions(targetFactionId, localRegionalId);

            if (actor.Actor.TryGet<ActorInstanceIdentity>(out ActorInstanceIdentity? identity) && targetFactionId is int fid)
            {
                string factionText = fid.ToString(CultureInfo.InvariantCulture);
                if (!string.Equals(identity.FactionId, factionText, StringComparison.Ordinal))
                    actor.Actor.Replace(new ActorInstanceIdentity(identity.SpawnerId, identity.TemplateId, identity.Gender, identity.Race, factionText));
            }

            if (actor.Actor.TryGet<ActorRenderComponent>(out ActorRenderComponent? render))
            {
                Color? targetTint = resolution.LiveryTint;
                string targetLiveryKey = resolution.LiveryKey;
                if (render.Spec.Tint != targetTint || !string.Equals(render.Spec.LiveryKey, targetLiveryKey, StringComparison.Ordinal))
                    render.SetVisual(render.Spec with { Tint = targetTint, LiveryKey = targetLiveryKey });
            }

            ownerAppearance?.SetActorTint(actor.DurableId, resolution.LiveryTint);
        }
    }

    /// <summary>Resolves the active livery &amp; post metadata for one live guard actor.</summary>
    internal DaggerfallGuardLiveryResolution ResolveForLiveGuard(
        long actorId,
        DaggerfallSiteRecord? site,
        DaggerfallSiteProfile? activeProfile,
        DaggerfallNpc? npc,
        IReadOnlyList<DaggerfallPopulationPlacement> exteriorPlacements)
    {
        int region = site?.Id.Region ?? npc?.Site.Region ?? activeProfile?.Site?.Region ?? 0;
        DaggerfallGuardPostKind postKind = ClassifyLiveGuard(actorId, site, activeProfile, npc, exteriorPlacements);
        return ResolveLivery(site, region, postKind);
    }

    /// <summary>Summarizes the settlement's watch tier, walls/gates, local vs. liege livery, and occupation garrison for <c>social.site</c>.</summary>
    internal DaggerfallSiteWatchSummary SummarizeSite(
        DaggerfallSiteRecord site,
        IReadOnlyList<DaggerfallCityGateDefinition> cityGates,
        IReadOnlyList<DaggerfallPopulationPlacement> exteriorPlacements)
    {
        ArgumentNullException.ThrowIfNull(site);
        DaggerfallGuardLiveryResolution local = ResolveLivery(site, site.Id.Region, DaggerfallGuardPostKind.StreetWatch);
        DaggerfallGuardLiveryResolution sovereign = ResolveLivery(site, site.Id.Region, DaggerfallGuardPostKind.PalaceGuard);

        int gateOfficerCount = exteriorPlacements.Count(p =>
            p.CrackdownGuard == 0 && p.IsGuard && p.GuardPost == DaggerfallGuardPostKind.WalledCityGateOfficer);
        int patrolGuardCount = exteriorPlacements.Count(p => p.CrackdownGuard == 0 && p.IsGuard);
        int gateCount = Math.Max(cityGates.Count, gateOfficerCount);
        bool isWalled = gateCount > 0;
        bool isOccupied = IsRegionOccupied(site.Id.Region);
        int occupationCount = OccupationGarrisonCount(site);

        return new DaggerfallSiteWatchSummary(
            WatchTier: local.WatchTier,
            WatchTierLabel: local.WatchTierLabel,
            IsWalled: isWalled,
            GateCount: gateCount,
            Region: site.Id.Region,
            RegionName: local.LocalRegionName,
            LocalLiveryOwner: local.LiveryOwnerName,
            LocalLiverySlug: local.LiverySlug,
            LocalLiveryFactionId: local.LiveryFactionId,
            LiegeLiveryOwner: sovereign.LiveryOwnerName,
            LiegeLiverySlug: sovereign.LiverySlug,
            LiegeLiveryFactionId: sovereign.LiveryFactionId,
            IsOccupied: isOccupied,
            OccupationGarrisonCount: occupationCount,
            PatrolGuardCount: patrolGuardCount,
            GateOfficerCount: gateOfficerCount,
            LocalPalette: local.Palette,
            LiegePalette: sovereign.Palette);
    }

    private bool IsCrownOrGovernorOffice(DaggerfallSiteRecord site, DaggerfallInteriorBuilding interior)
    {
        if (interior.BuildingType == PalaceBuildingType)
            return true;
        DaggerfallSiteBuildingId buildingId = new(interior.BlockX, interior.BlockY, interior.Building.Index);
        if (site.Exterior is { } ext
            && ext.Buildings.TryGetValue(buildingId, out DaggerfallSiteBuildingSource? source)
            && source.Source.FactionId is int factionId
            && factionId > 0
            && definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
            && faction.Type is 1 or 7 or 14)
        {
            return true;
        }
        return definitions.ProvincialNobility.IsCapitalSeatProfile(site.Id, buildingId);
    }

    internal static string FormatPostLabel(DaggerfallGuardPostKind kind) => kind switch
    {
        DaggerfallGuardPostKind.WalledCityGateOfficer => "Gate Officer",
        DaggerfallGuardPostKind.PalaceGuard => "Palace Guard",
        DaggerfallGuardPostKind.CrownOfficeGuard => "Crown Office Guard",
        DaggerfallGuardPostKind.OccupationGarrison => "Occupation Garrison",
        _ => "Street Watch",
    };

    private static string Slugify(string value)
    {
        StringBuilder sb = new(value.Length);
        bool lastDash = true;
        foreach (char c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        string slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "regional" : slug;
    }
}
