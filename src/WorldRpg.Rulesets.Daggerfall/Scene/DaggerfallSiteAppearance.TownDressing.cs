using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// A town's street dressing (#11169): the flats its region's conditions put out now, standing on the town's dressing
/// spots as <see cref="DaggerfallTownDressingLayout"/> places them. What is out is read every time the location's facts
/// are drawn, so dressing goes up and comes down with its conditions; each spot's flat for a piece is made the first time
/// it stands there and released with the location's decorations.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private readonly List<(DaggerfallSiteDressingSpot Spot, ulong Ordinal)> dressingSpots = [];
    private readonly Dictionary<(int Spot, string Piece), DecorationSprite> dressingFlats = [];
    private IReadOnlyList<DaggerfallTownDressingPiece> dressingPieces = [];
    private Func<int, IReadOnlyList<(DaggerfallTownDressingKind Kind, int Severity)>> dressingOut = _ => [];

    /// <summary>
    /// Stands the art pack's <paramref name="pieces"/> about the town for the dressing <paramref name="dressing"/> says its
    /// classic region puts out now.
    /// </summary>
    internal void UseTownDressing(IReadOnlyList<DaggerfallTownDressingPiece> pieces, Func<int, IReadOnlyList<(DaggerfallTownDressingKind Kind, int Severity)>> dressing)
    {
        dressingPieces = pieces ?? throw new ArgumentNullException(nameof(pieces));
        dressingOut = dressing ?? throw new ArgumentNullException(nameof(dressing));
    }

    private void AdmitTownDressing(DaggerfallSiteProfile inputs)
    {
        foreach (DaggerfallSiteDressingSpot spot in inputs.DressingSpots)
            dressingSpots.Add((spot, NextLocationVisualOrdinal()));
    }

    /// <summary>The pieces standing now, by spot id.</summary>
    internal IReadOnlyList<(string Spot, string Piece)> TownDressingShown => [.. TownDressingNow().Select(entry => (dressingSpots[entry.Spot].Spot.Id, entry.Piece.Id))];

    /// <summary>The layout for what was last out, kept while the session hands back the same answer.</summary>
    private (IReadOnlyList<(DaggerfallTownDressingKind Kind, int Severity)> Out, IReadOnlyList<(int Spot, DaggerfallTownDressingPiece Piece)> Placed)? dressingLaid;

    private IReadOnlyList<(int Spot, DaggerfallTownDressingPiece Piece)> TownDressingNow()
    {
        if (dressingSpots.Count == 0 || dressingPieces.Count == 0 || inputs.Site is not { } site) return [];
        IReadOnlyList<(DaggerfallTownDressingKind Kind, int Severity)> now = dressingOut(site.Region);
        if (dressingLaid is not { } laid || !ReferenceEquals(laid.Out, now))
            dressingLaid = laid = (now, DaggerfallTownDressingLayout.Place([.. dressingSpots.Select(entry => entry.Spot)], dressingPieces, now));
        return laid.Placed;
    }

    private void AppendTownDressingFacts(List<AppearanceFact> facts, int slot)
    {
        foreach ((int spot, DaggerfallTownDressingPiece piece) in TownDressingNow())
        {
            if (!dressingFlats.TryGetValue((spot, piece.Id), out DecorationSprite? flat))
                dressingFlats.Add((spot, piece.Id), flat = CreateRefreshedDecorationSprite(piece.Sprite, baseAligned: true, CastingSprite, givesLight: false));
            (DaggerfallSiteDressingSpot definition, ulong ordinal) = dressingSpots[spot];
            facts.Add(new(DaggerfallPresentationObjectIds.LocationVisual(slot, ordinal), false, 0,
                Compose(worldAppearance.Transform, new Transform(definition.Foot.ToVector(), Quaternion.Identity, Vector3.One)),
                flat.Visual, worldAppearance.Visible, worldAppearance.Layer));
        }
    }

    /// <summary>Lets go of the town's spots; their flats go with the decorations' sprites.</summary>
    private void TakeTownDressing()
    {
        dressingSpots.Clear();
        dressingFlats.Clear();
        dressingLaid = null;
    }
}
