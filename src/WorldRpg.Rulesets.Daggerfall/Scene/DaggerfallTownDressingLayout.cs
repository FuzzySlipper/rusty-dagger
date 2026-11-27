using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Where a town's street dressing stands (#11169): each kind that is out puts <see cref="PiecesPerSeverity"/> pieces a
/// point of severity, taking its pieces in turn, each on the first free spot of the first of its kinds of spot that has
/// one, until it has put out its number or has nowhere left. Kinds go in their order, and no spot holds two pieces, so
/// one town shows a festival beside a witch hunt without either covering the other.
/// </summary>
internal static class DaggerfallTownDressingLayout
{
    internal const int PiecesPerSeverity = 2;

    /// <summary>Each piece that stands now, by the index of the spot in <paramref name="spots"/> it stands on.</summary>
    internal static IReadOnlyList<(int Spot, DaggerfallTownDressingPiece Piece)> Place(IReadOnlyList<DaggerfallSiteDressingSpot> spots,
        IReadOnlyList<DaggerfallTownDressingPiece> pieces, IReadOnlyList<(DaggerfallTownDressingKind Kind, int Severity)> active)
    {
        bool[] taken = new bool[spots.Count];
        List<(int, DaggerfallTownDressingPiece)> placed = [];
        foreach ((DaggerfallTownDressingKind kind, int severity) in active.OrderBy(entry => entry.Kind))
        {
            DaggerfallTownDressingPiece[] own = [.. pieces.Where(piece => piece.Kind == kind)];
            int wanted = PiecesPerSeverity * severity, put = 0;
            for (bool progressed = true; put < wanted && progressed;)
            {
                progressed = false;
                foreach (DaggerfallTownDressingPiece piece in own)
                {
                    if (put == wanted) break;
                    int spot = piece.Spots.Select(spotKind => Enumerable.Range(0, spots.Count).FirstOrDefault(index => !taken[index] && spots[index].Kind == spotKind, -1))
                        .FirstOrDefault(index => index >= 0, -1);
                    if (spot < 0) continue;
                    taken[spot] = true;
                    placed.Add((spot, piece));
                    put++;
                    progressed = true;
                }
            }
        }
        return placed;
    }
}
