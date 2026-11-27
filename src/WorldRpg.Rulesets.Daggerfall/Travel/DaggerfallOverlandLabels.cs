using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// Priority-ordered screen-space overlap culling and zoom-tier filtering for the 3D travel map's projected labels
/// (#11116): guarantees party, chasing/clashing bands, cities, towns, dungeons and minor places never stack on top
/// of one another, while projecting high-altitude region and Iliac Bay sea/realm names cleanly at wide strategic zoom.
/// </summary>
internal static class DaggerfallOverlandLabels
{
    /// <summary>View distance (metres) where wide-zoom region and sea/realm names begin fading in.</summary>
    internal const double GeographyFadeStartMetres = 22_000;
    /// <summary>View distance (metres) where wide-zoom region and sea/realm names reach full subtle opacity.</summary>
    internal const double GeographyFadeFullMetres = 36_000;
    /// <summary>Peak opacity of translucent wide-zoom cartographic geography lettering.</summary>
    internal const double GeographyMaxOpacity = .90;

    /// <summary>Maximum non-overlapping foreground pills (party, bands, places) kept per frame.</summary>
    internal const int MaxForegroundLabels = 48;
    /// <summary>Maximum non-overlapping wide-zoom geography labels (regions, seas, realms) kept per frame.</summary>
    internal const int MaxGeographyLabels = 20;

    private const double ReferenceHeightPx = 940.0;
    private const double MarginX = 6.0;
    private const double MarginY = 4.0;

    /// <summary>Whether <paramref name="kind"/> is a background cartographic geography label rather than a foreground pill.</summary>
    internal static bool IsGeography(string kind) => kind is
        "region" or "realm" or "sea" or "bay" or "range" or "desert" or "forest" or "moor" or "island" or "lake" or "river" or "coast";

    /// <summary>
    /// Smooth 0–<see cref="GeographyMaxOpacity"/> fade curve for high-altitude region and sea names:
    /// hidden at close (3 km) and default (12 km) zoom, rising smoothly past 22 km toward strategic views (25–60 km).
    /// </summary>
    internal static double GeographyOpacity(double viewDistanceMetres)
    {
        if (!double.IsFinite(viewDistanceMetres) || viewDistanceMetres <= GeographyFadeStartMetres) return 0;
        double t = Math.Clamp((viewDistanceMetres - GeographyFadeStartMetres) / (GeographyFadeFullMetres - GeographyFadeStartMetres), 0, 1);
        return t * t * (3 - (2 * t)) * GeographyMaxOpacity;
    }

    /// <summary>
    /// Which place kinds earn a label at <paramref name="viewDistanceMetres"/>: every known place close in,
    /// major settlements/temples/dungeons at mid zoom, and cities/towns at wide strategic zoom.
    /// </summary>
    internal static bool PlaceVisibleAtDistance(DaggerfallSiteKind kind, double viewDistanceMetres, double openingDistanceMetres)
    {
        if (viewDistanceMetres <= openingDistanceMetres * .5) return true;
        if (viewDistanceMetres <= GeographyFadeStartMetres)
        {
            return kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage
                or DaggerfallSiteKind.ReligionTemple or DaggerfallSiteKind.DungeonLabyrinth
                or DaggerfallSiteKind.DungeonKeep or DaggerfallSiteKind.DungeonRuin;
        }
        if (viewDistanceMetres < GeographyFadeFullMetres)
        {
            return kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet
                or DaggerfallSiteKind.ReligionTemple or DaggerfallSiteKind.DungeonLabyrinth;
        }
        return kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet;
    }

    /// <summary>
    /// Collision priority for foreground pills (lower wins): party -> clash/chase/hostile bands -> other bands ->
    /// cities -> towns/temples -> villages/taverns -> dungeons -> minor places.
    /// </summary>
    internal static int ForegroundPriority(string kind) => kind switch
    {
        "party" => 0,
        "clash" or "chase" or "hostile" => 1,
        "caravan" or "courier" or "patrol" or "band" or "camp" => 2,
        "City" => 3,
        "Town" or "Temple" => 4,
        "Village" or "Tavern" => 5,
        "Large dungeon" or "Dungeon" or "Small dungeon" => 6,
        _ => 7,
    };

    /// <summary>Collision priority for wide-zoom geography labels (lower wins): sea/bay/realm -> region -> landforms.</summary>
    internal static int GeographyPriority(string kind) => kind switch
    {
        "sea" or "bay" or "realm" => 0,
        "region" => 1,
        _ => 2,
    };

    /// <summary>
    /// Filters and orders projected label candidates with strict screen-space bounding-box overlap rejection.
    /// Foreground pills cull against foreground pills by priority; wide-zoom geography labels cull against other
    /// geography labels so macro sea/region names remain readable without crowding out cities or party tokens.
    /// </summary>
    internal static List<DaggerfallOverlandLabel> Layout(IReadOnlyList<DaggerfallOverlandLabel> candidates, double aspect,
        int maxForeground = MaxForegroundLabels, int maxGeography = MaxGeographyLabels)
    {
        double clampedAspect = double.IsFinite(aspect) ? Math.Clamp(aspect, .8, 3.0) : 16.0 / 9.0;
        double refWidth = ReferenceHeightPx * clampedAspect;

        List<DaggerfallOverlandLabel> geoCandidates = [];
        List<DaggerfallOverlandLabel> fgCandidates = [];
        for (int i = 0; i < candidates.Count; i++)
        {
            DaggerfallOverlandLabel label = candidates[i];
            if (string.IsNullOrWhiteSpace(label.Name) || !double.IsFinite(label.Across) || !double.IsFinite(label.Down)) continue;
            if (IsGeography(label.Kind))
            {
                if (label.Opacity <= .01 || label.Across is < .04 or > .96 || label.Down is < .05 or > .95) continue;
                geoCandidates.Add(label);
            }
            else
            {
                if (label.Across is < .04 or > .96 || label.Down is < .04 or > .97) continue;
                fgCandidates.Add(label);
            }
        }

        geoCandidates.Sort(static (a, b) =>
        {
            int byPriority = GeographyPriority(a.Kind).CompareTo(GeographyPriority(b.Kind));
            return byPriority != 0 ? byPriority : CenterDistanceSq(a).CompareTo(CenterDistanceSq(b));
        });
        fgCandidates.Sort(static (a, b) =>
        {
            int byPriority = ForegroundPriority(a.Kind).CompareTo(ForegroundPriority(b.Kind));
            return byPriority != 0 ? byPriority : CenterDistanceSq(a).CompareTo(CenterDistanceSq(b));
        });

        List<DaggerfallOverlandLabel> accepted = [];
        List<Box> geoBoxes = [];
        for (int i = 0; i < geoCandidates.Count && geoBoxes.Count < maxGeography; i++)
        {
            DaggerfallOverlandLabel candidate = geoCandidates[i];
            Box box = EstimateBox(candidate, refWidth, ReferenceHeightPx);
            if (OverlapsAny(geoBoxes, box)) continue;
            geoBoxes.Add(box);
            accepted.Add(candidate);
        }

        List<Box> fgBoxes = [];
        for (int i = 0; i < fgCandidates.Count && fgBoxes.Count < maxForeground; i++)
        {
            DaggerfallOverlandLabel candidate = fgCandidates[i];
            Box box = EstimateBox(candidate, refWidth, ReferenceHeightPx);
            if (OverlapsAny(fgBoxes, box)) continue;
            fgBoxes.Add(box);
            accepted.Add(candidate);
        }

        return accepted;
    }

    internal readonly record struct Box(double Left, double Top, double Right, double Bottom)
    {
        internal bool Intersects(Box other) => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    }

    /// <summary>Estimates a label's padded 1920x1080-equivalent screen box, matching CSS anchor transforms and typography sizes.</summary>
    internal static Box EstimateBox(DaggerfallOverlandLabel label, double viewWidth, double viewHeight)
    {
        double px = label.Across * viewWidth;
        double py = label.Down * viewHeight;
        int chars = Math.Clamp(label.Name.Length, 2, 42);

        if (IsGeography(label.Kind))
        {
            bool grand = label.Kind is "realm" or "sea" or "bay";
            double charW = grand ? 14.5 : 11.5;
            double h = grand ? 36.0 : 28.0;
            double w = (chars * charW) + 34.0;
            return new(px - (w * .5) - MarginX, py - (h * .5) - MarginY, px + (w * .5) + MarginX, py + (h * .5) + MarginY);
        }

        double pillCharW, padX, pillH;
        switch (label.Kind)
        {
            case "City":
                pillCharW = 9.6; padX = 24.0; pillH = 28.0;
                break;
            case "Town":
            case "Temple":
            case "party":
                pillCharW = 8.4; padX = 18.0; pillH = 24.0;
                break;
            case "clash":
            case "chase":
            case "hostile":
            case "caravan":
            case "courier":
            case "patrol":
            case "band":
            case "camp":
                pillCharW = 7.8; padX = 16.0; pillH = 22.0;
                break;
            default:
                pillCharW = 7.4; padX = 15.0; pillH = 21.0;
                break;
        }

        double pillW = (chars * pillCharW) + padX;
        double left = px - (pillW * .5) - MarginX;
        double right = px + (pillW * .5) + MarginX;

        // Match exact CSS vertical anchor offsets in styles.css (.dagger-expedition-label).
        double top;
        if (label.Kind == "party") top = py + (pillH * .40);
        else if (IsBandKind(label.Kind)) top = py - 66.0 - pillH;
        else top = py - pillH;

        return new(left, top - MarginY, right, top + pillH + MarginY);
    }

    /// <summary>
    /// Chooses the nearest authored water-body name (<c>sea</c> or <c>bay</c>) in <paramref name="worldNames"/> to
    /// world-map pixel (<paramref name="mapX"/>, <paramref name="mapY"/>), so wide-zoom ocean views always identify
    /// Iliac Bay or the Eltheric Ocean even when the single authored anchor sits outside the camera frustum.
    /// </summary>
    internal static DaggerfallWorldName? NearestWaterName(DaggerfallWorldNames worldNames, double mapX, double mapY)
    {
        DaggerfallWorldName? best = null;
        double bestDistSq = double.PositiveInfinity;
        for (int i = 0; i < worldNames.Names.Count; i++)
        {
            DaggerfallWorldName entry = worldNames.Names[i];
            if (entry.Kind is not ("sea" or "bay")) continue;
            double dx = entry.X - mapX, dy = entry.Y - mapY;
            double distSq = (dx * dx) + (dy * dy);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = entry;
            }
        }
        return best;
    }

    private static bool IsBandKind(string kind) => kind is
        "clash" or "chase" or "hostile" or "caravan" or "courier" or "patrol" or "band" or "camp";

    private static double CenterDistanceSq(DaggerfallOverlandLabel label)
    {
        double dx = label.Across - .5, dy = label.Down - .5;
        return (dx * dx) + (dy * dy);
    }

    private static bool OverlapsAny(List<Box> boxes, Box candidate)
    {
        for (int i = 0; i < boxes.Count; i++)
            if (boxes[i].Intersects(candidate)) return true;
        return false;
    }
}
